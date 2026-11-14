using System.Threading;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class SkyPlayer : IDisposable
{
    public static readonly ushort[] DefaultVk =
    {
        0x59, 0x55, 0x49, 0x4F, 0x50,
        0x48, 0x4A, 0x4B, 0x4C, 0xBA,
        0x4E, 0x4D, 0xBC, 0xBE, 0xBF
    };
    public ushort[] Vk = (ushort[])DefaultVk.Clone();
    public Action<int>? NoteFired;
    public Action<Exception>? Failed;
    public bool RandomSpeed;
    public int HoldMs = 18;
    readonly object _gate = new();
    readonly AutoResetEvent _wake = new(false);
    readonly IMonotonicClock _clock;
    Thread? _worker;
    PlaybackSession? _session;
    Action? _onDone;
    CancellationTokenRegistration _cancellation;
    CancellationToken _token;
    bool _disposed;
    long _generation;
    int _randCountdown;
    double _speed = 1, _position, _total;
    public Exception? LastError { get; private set; }
    internal bool HighResolutionTimer { get; private set; }

    public SkyPlayer(IMonotonicClock? clock = null) => _clock = clock ?? new StopwatchClock();
    public bool IsPlaying { get { lock (_gate) return _session is { Stopped: false, Completed: false }; } }
    public double PositionMs { get { lock (_gate) return _session?.PositionMs ?? _position; } }
    public double TotalMs { get { lock (_gate) return _total; } }
    public double SpeedFactor
    {
        get { lock (_gate) return _speed; }
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_gate) { _session?.SetSpeed(value); _speed = value; Wake(); }
        }
    }

    public void Play(SmapProject project, double speed, Action onDone, bool preview = false, double startMs = 0,
        CancellationToken cancellationToken = default)
    {
        Stop();
        var sequence = PlaybackSequence.FromProject(project);
        Play(sequence, speed, onDone, preview ? new AudioNoteOutput(sequence) : new GameNoteOutput(new WindowsKeyInput(), Vk), startMs, cancellationToken);
    }

    public void Play(List<(int key, double ms)> notes, double speed, Action onDone, Action<int>? noteAction = null, double startMs = 0)
    {
        Stop();
        var sequence = PlaybackSequence.FromLegacy(notes);
        INoteOutput output = noteAction == null ? new GameNoteOutput(new WindowsKeyInput(), Vk)
            : noteAction == AudioEngine.Play ? new AudioNoteOutput(sequence) : new CallbackOutput(noteAction);
        Play(sequence, speed, onDone, output, startMs);
    }

    public void Play(PlaybackSequence sequence, double speed, Action onDone, INoteOutput output, double startMs = 0,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StopCore(); LastError = null; _speed = speed; _total = sequence.DurationMs; _position = startMs;
            _token = cancellationToken; _onDone = onDone; _randCountdown = 0;
            if (cancellationToken.IsCancellationRequested) return;
            _session = new(sequence, _clock, new[] { output }, speed, startMs, HoldMs);
            _session.NoteStarted += key => _fired.Add(key);
            _cancellation = cancellationToken.UnsafeRegister(_ => _wake.Set(), null);
            _worker ??= StartWorker(); Wake();
        }
    }

    Thread StartWorker()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "SMAP playback" };
        thread.Start(); return thread;
    }

    readonly List<int> _fired = new();
    void Run()
    {
        try
        {
            using var waiter = new PlaybackWaiter(_wake); HighResolutionTimer = waiter.HighResolution;
            RunLoop(waiter);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                LastError = error; _onDone = null;
                try { _session?.Stop(); } catch (Exception cleanup) { LastError = new AggregateException(error, cleanup); }
                if (_worker == Thread.CurrentThread) _worker = null;
            }
            try { Failed?.Invoke(LastError!); } catch { }
        }
        finally { lock (_gate) { if (_worker == Thread.CurrentThread) _worker = null; } }
    }

    void RunLoop(PlaybackWaiter waiter)
    {
        while (true)
        {
            double delay = double.PositiveInfinity; int[] fired; Action? done = null; Exception? failure = null; long generation;
            lock (_gate)
            {
                if (_disposed) return;
                generation = _generation;
                try
                {
                    if (_token.IsCancellationRequested) StopCore();
                    if (_session is { Stopped: false, Completed: false } session)
                    {
                        session.Pump(); _position = session.PositionMs;
                        if (session.Completed) { done = _onDone; _onDone = null; }
                        if (RandomSpeed && _fired.Count > 0 && (_randCountdown -= _fired.Count) <= 0)
                        {
                            _speed = 0.5 + Random.Shared.NextDouble() * 0.8; session.SetSpeed(_speed);
                            _randCountdown = Random.Shared.Next(2, 6);
                        }
                        delay = session.NextDelayMilliseconds;
                    }
                }
                catch (Exception error)
                {
                    failure = error; LastError = error; _onDone = null;
                    try { _session?.Stop(); } catch (Exception cleanup) { failure = LastError = new AggregateException(error, cleanup); }
                }
                fired = _fired.ToArray(); _fired.Clear();
            }
            try
            {
                foreach (int key in fired) NoteFired?.Invoke(key);
                if (failure != null) Failed?.Invoke(failure);
                lock (_gate) { if (_generation != generation || _disposed) done = null; }
                done?.Invoke();
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    LastError = error;
                    if (_generation == generation) try { StopCore(); } catch (Exception cleanup) { LastError = new AggregateException(error, cleanup); }
                }
                if (failure == null) try { Failed?.Invoke(LastError!); } catch { }
            }
            // 每个播放器只保留一个可唤醒的阻塞工作线程，不为音符创建线程或计时器。
            lock (_gate) { if (_disposed) return; delay = _session?.NextDelayMilliseconds ?? double.PositiveInfinity; }
            waiter.Wait(_wake, delay);
        }
    }

    public void Pause(bool paused) { lock (_gate) { _session?.Pause(paused); Wake(); } }
    public void Seek(double ms) { lock (_gate) { _session?.Seek(Math.Max(0, ms)); Wake(); } }
    public void Stop() { lock (_gate) { StopCore(); Wake(); } }
    void StopCore()
    {
        _generation++; _onDone = null; _fired.Clear();
        _cancellation.Dispose();
        if (_session != null)
        {
            try { _position = _session.PositionMs; }
            finally { _session.Stop(); }
            _session = null;
        }
    }
    void Wake() { if (!_disposed) _wake.Set(); }

    public void Dispose()
    {
        Exception? failure = null;
        lock (_gate)
        {
            if (_disposed) return;
            try { StopCore(); } catch (Exception error) { failure = error; }
            finally { _disposed = true; _wake.Set(); }
        }
        if (_worker != Thread.CurrentThread) _worker?.Join();
        _wake.Dispose();
        if (failure != null) throw failure;
    }

    sealed class CallbackOutput(Action<int> callback) : INoteOutput
    {
        public void Apply(IReadOnlyList<NoteOutputEvent> changes)
        { foreach (var change in changes) if (change.IsOn) callback(change.Note.Key); }
        public void StopAll() { }
    }
}
