namespace SMAP_WPF.Core;

public sealed class PlaybackSession
{
    readonly record struct Scheduled(decimal Time, TimedProjectNote Note, bool On);
    readonly Scheduled[] _events;
    readonly PlaybackSequence _sequence;
    readonly IMonotonicClock _clock;
    readonly INoteOutput[] _outputs;
    readonly Dictionary<Guid, TimedProjectNote> _active = new();
    readonly PriorityQueue<Guid, decimal> _tapEnds = new();
    readonly double _tapMilliseconds;
    double _speed;
    decimal _lastClock;
    decimal _position;
    int _index;

    public bool Paused { get; private set; }
    public bool Stopped { get; private set; }
    public bool Completed { get; private set; }
    decimal CurrentPosition => !Paused && !Stopped && !Completed
        ? _position + ((decimal)_clock.Milliseconds - _lastClock) * 1000 * (decimal)_speed : _position;
    public double PositionMs => (double)(Math.Min(CurrentPosition, _sequence.DurationMicroseconds) / 1000);
    public double Speed => _speed;
    public int ActiveNoteCount => _active.Count;
    public event Action<int>? NoteStarted;

    public PlaybackSession(PlaybackSequence sequence, IMonotonicClock clock, IEnumerable<INoteOutput> outputs,
        double speed = 1, double startMs = 0, double tapMilliseconds = 18)
    {
        ValidateSpeed(speed);
        if (!double.IsFinite(tapMilliseconds) || tapMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(tapMilliseconds));
        _sequence = sequence; _clock = clock; _outputs = outputs.ToArray();
        _tapMilliseconds = tapMilliseconds; _speed = speed;
        _events = sequence.Notes.SelectMany(n => n.Action == NoteAction.Tap
            ? new[] { new Scheduled(n.StartMicroseconds, n, true) }
            : new[] { new Scheduled(n.StartMicroseconds, n, true), new Scheduled(n.StartMicroseconds + n.LengthMicroseconds, n, false) })
            .OrderBy(e => e.Time).ThenBy(e => e.On).ThenBy(e => e.Note.Key).ThenBy(e => e.Note.NoteId).ToArray();
        Seek(startMs);
    }

    void Advance()
    {
        double raw = _clock.Milliseconds;
        if (!double.IsFinite(raw)) throw new InvalidOperationException("演奏时钟必须单调递增。");
        decimal now = (decimal)raw;
        if (now < _lastClock) throw new InvalidOperationException("演奏时钟必须单调递增。");
        if (!Paused && !Stopped && !Completed) _position = checked(_position + (now - _lastClock) * 1000 * (decimal)_speed);
        _lastClock = now;
    }

    public void SetSpeed(double speed)
    {
        ValidateSpeed(speed);
        try { Advance(); _speed = speed; }
        catch (Exception error) { Abort(error); throw; }
    }

    static void ValidateSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0 || speed > 100) throw new ArgumentOutOfRangeException(nameof(speed));
    }

    public void Pump()
    {
        if (Stopped || Completed || Paused) return;
        try
        {
            Advance();
            ReleaseExpiredTaps();
            while (_index < _events.Length && _events[_index].Time <= _position)
            {
                decimal time = _events[_index].Time;
                decimal dispatchTime = (decimal)_clock.Milliseconds;
                var changes = new List<NoteOutputEvent>();
                while (_index < _events.Length && _events[_index].Time == time)
                {
                    var e = _events[_index++];
                    if (e.On)
                    {
                        _active.Add(e.Note.NoteId, e.Note);
                        if (e.Note.Action == NoteAction.Tap) _tapEnds.Enqueue(e.Note.NoteId, dispatchTime + (decimal)_tapMilliseconds);
                        changes.Add(new(e.Note, true));
                    }
                    else if (_active.Remove(e.Note.NoteId)) changes.Add(new(e.Note, false));
                }
                Send(changes);
            }
            if (_index == _events.Length && _active.Count == 0 && _position >= _sequence.DurationMicroseconds)
            {
                ReleaseOutputs(true); Completed = true;
            }
        }
        catch (Exception error)
        {
            try { Stop(); } catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
    }

    void ReleaseExpiredTaps()
    {
        var changes = new List<NoteOutputEvent>();
        while (_tapEnds.TryPeek(out var id, out var end) && end <= (decimal)_clock.Milliseconds)
        {
            _tapEnds.Dequeue();
            if (_active.Remove(id, out var note)) changes.Add(new(note, false));
        }
        Send(changes);
    }

    void Send(List<NoteOutputEvent> changes)
    {
        if (changes.Count == 0) return;
        foreach (var output in _outputs) output.Apply(changes);
        foreach (var change in changes) if (change.IsOn) NoteStarted?.Invoke(change.Note.Key);
    }

    public void Pause(bool paused)
    {
        if (Stopped || Completed || Paused == paused) return;
        try
        {
            Advance(); Paused = paused;
            if (paused) ReleaseAll(true);
            else { ResetIndex(); RebuildHolds(); }
        }
        catch (Exception error) { Abort(error); throw; }
    }

    public void Seek(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0) throw new ArgumentOutOfRangeException(nameof(ms));
        if (Stopped) return;
        try
        {
            ReleaseAll(); _position = Math.Min((decimal)ms * 1000, _sequence.DurationMicroseconds);
            _lastClock = (decimal)_clock.Milliseconds; Completed = false;
            ResetIndex();
            if (!Paused) RebuildHolds();
        }
        catch (Exception error) { Abort(error); throw; }
    }

    void ResetIndex()
    {
        int lo = 0, hi = _events.Length;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_events[mid].Time < _position) lo = mid + 1; else hi = mid; }
        _index = lo;
    }

    void RebuildHolds()
    {
        var changes = new List<NoteOutputEvent>();
        foreach (var note in _sequence.Notes)
            if (note.Action == NoteAction.Hold && note.StartMicroseconds < _position
                && note.StartMicroseconds + note.LengthMicroseconds > _position)
            {
                _active.Add(note.NoteId, note); changes.Add(new(note, true, true));
            }
        Send(changes);
    }

    public double NextDelayMilliseconds
    {
        get
        {
            if (Paused || Stopped || Completed) return double.PositiveInfinity;
            decimal projected = CurrentPosition;
            double delay = _index < _events.Length ? Math.Max(0, (double)((_events[_index].Time - projected) / 1000) / _speed) : double.PositiveInfinity;
            if (_tapEnds.TryPeek(out _, out var end)) delay = Math.Min(delay, Math.Max(0, (double)(end - (decimal)_clock.Milliseconds)));
            if (_index == _events.Length && _active.Count == 0)
                delay = Math.Min(delay, Math.Max(0, (double)((_sequence.DurationMicroseconds - projected) / 1000) / _speed));
            return delay;
        }
    }

    public void Stop()
    {
        Stopped = true; ReleaseAll();
    }

    void Abort(Exception error)
    {
        try { Stop(); } catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
    }

    void ReleaseAll(bool natural = false)
    {
        _active.Clear(); _tapEnds.Clear(); ReleaseOutputs(natural);
    }

    void ReleaseOutputs(bool natural = false)
    {
        List<Exception>? failures = null;
        foreach (var output in _outputs)
            try { if (natural) output.ReleaseAll(); else output.StopAll(); } catch (Exception error) { (failures ??= new()).Add(error); }
        if (failures != null) throw new AggregateException("演奏输出清理失败。", failures);
    }
}
