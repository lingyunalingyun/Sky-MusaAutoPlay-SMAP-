using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[CollectionDefinition("Playback metrics", DisableParallelization = true)]
public sealed class PlaybackMetricsCollection;

[Collection("Playback metrics")]
public class SchedulerMetricsTests(ITestOutputHelper output)
{
    sealed class MeasuredClock : IMonotonicClock
    {
        readonly Stopwatch _watch = Stopwatch.StartNew();
        public double? Anchor;
        public double Milliseconds
        {
            get { double now = _watch.Elapsed.TotalMilliseconds; Anchor ??= now; return now; }
        }
    }
    sealed class MeasuredOutput(MeasuredClock clock, GameNoteOutput game) : INoteOutput
    {
        public readonly List<double> Errors = new();
        public readonly HashSet<int> WorkerThreads = new();
        public uint NativeThread;
        public void Apply(IReadOnlyList<NoteOutputEvent> changes)
        {
            game.Apply(changes);
            double now = clock.Milliseconds - clock.Anchor!.Value;
            foreach (var change in changes)
            {
                double expected = (double)((change.Note.StartMicroseconds + (change.IsOn ? 0 : change.Note.LengthMicroseconds)) / 1000);
                Errors.Add(now - expected);
            }
            WorkerThreads.Add(Environment.CurrentManagedThreadId);
            if (NativeThread == 0) NativeThread = GetCurrentThreadId();
        }
        public void StopAll() => game.StopAll();
    }

    [Fact]
    public void MeasureRealWorkerDenseChordsAndExceptionCleanup()
    {
        var notes = Enumerable.Range(0, 100).SelectMany(i => Enumerable.Range(0, 15)
            .Select(key => PlaybackFixture.Note(key, i * 20, 10))).ToArray();
        var sequence = new PlaybackSequence(notes); var clock = new MeasuredClock();
        var input = new RecordingInput(clock); var game = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        var measured = new MeasuredOutput(clock, game);
        using var done = new ManualResetEventSlim(); using var player = new SkyPlayer(clock);
        player.Play(new(notes.Take(30)), 1, done.Set, measured);
        Assert.True(done.Wait(TimeSpan.FromSeconds(3))); player.Stop(); done.Reset();
        measured.Errors.Clear(); measured.WorkerThreads.Clear(); clock.Anchor = null;
        using var process = Process.GetCurrentProcess();
        int baselineThreads = process.Threads.Count;
        using var workerThread = process.Threads.Cast<ProcessThread>().Single(t => t.Id == measured.NativeThread);
        TimeSpan workerStart = workerThread.TotalProcessorTime;
        TimeSpan cpuStart = process.TotalProcessorTime; var elapsed = Stopwatch.StartNew();
        player.Play(sequence, 1, done.Set, measured);
        // 进程线程枚举开销较大，放在观察线程采样，不能放入每个输出批次。
        process.Refresh(); int sampledPeakThreads = Math.Max(baselineThreads, process.Threads.Count);
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "调度未在规定时间内结束。");
        elapsed.Stop(); TimeSpan cpu = process.TotalProcessorTime - cpuStart;
        TimeSpan workerCpu = workerThread.TotalProcessorTime - workerStart;
        player.Stop(); Assert.Empty(input.Pressed); Assert.Empty(game.PendingKeys);
        Assert.Equal(3000, measured.Errors.Count); Assert.Single(measured.WorkerThreads);
        double[] errors = measured.Errors.Order().ToArray();
        double rms = Math.Sqrt(errors.Average(e => e * e)); double coreCpu = cpu.TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds * 100;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"REAL_WORKER notes={notes.Length}; events={errors.Length}; wall_ms={elapsed.Elapsed.TotalMilliseconds:F3}; cpu_ms={cpu.TotalMilliseconds:F3}; cpu_one_core_pct={coreCpu:F3}; cpu_machine_pct={coreCpu / Environment.ProcessorCount:F3}; scheduler_cpu_ms={workerCpu.TotalMilliseconds:F3}; scheduler_one_core_pct={workerCpu.TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds * 100:F3}; logical_processors={Environment.ProcessorCount}; process_threads_before={baselineThreads}; process_threads_sampled_peak={sampledPeakThreads}; scheduler_threads={measured.WorkerThreads.Count}; high_resolution_timer={player.HighResolutionTimer}; error_min_ms={errors[0]:F6}; error_mean_ms={errors.Average():F6}; error_p95_ms={errors[(int)(errors.Length * .95)]:F6}; error_max_ms={errors[^1]:F6}; error_rms_ms={rms:F6}; pending_keys={game.PendingKeys.Count}"));
        Assert.InRange(errors[0], -0.1, 250); Assert.InRange(errors[^1], 0, 250);
        Assert.True(workerCpu.TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds < 0.2,
            "调度工作线程不应持续占满一个处理器核心。");

        using var failed = new ManualResetEventSlim(); double failureTime = 0, lastRelease = 0; bool once = false;
        var failureClock = new StopwatchClock(); var faulty = new RecordingInput(failureClock);
        faulty.Fail = change =>
        {
            if (!change.IsDown || change.Key != SkyPlayer.DefaultVk[1] || once) return false;
            once = true; failureTime = failureClock.Milliseconds; return true;
        };
        faulty.Sent = change => { if (!change.IsDown) lastRelease = failureClock.Milliseconds; };
        var failureOutput = new GameNoteOutput(faulty, SkyPlayer.DefaultVk); player.Failed = _ => failed.Set();
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(1, 0, 1000) }), 1, () => { }, failureOutput);
        Assert.True(failed.Wait(TimeSpan.FromSeconds(3))); player.Stop();
        Assert.Empty(faulty.Pressed); Assert.Empty(failureOutput.PendingKeys);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"EXCEPTION_CLEANUP elapsed_ms={lastRelease - failureTime:F6}; pending_keys={failureOutput.PendingKeys.Count}; error_reported={player.LastError != null}"));
    }

    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
