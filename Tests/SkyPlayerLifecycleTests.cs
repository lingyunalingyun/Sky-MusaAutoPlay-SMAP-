using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class SkyPlayerLifecycleTests
{
    sealed class FailingClock : IMonotonicClock
    {
        readonly StopwatchClock _clock = new();
        public int? FailingThread;
        public double Milliseconds => FailingThread == Environment.CurrentManagedThreadId
            ? throw new InvalidOperationException("模拟时钟故障") : _clock.Milliseconds;
    }

    [Fact]
    public void StopAndRestartReuseOneWorkerAndReleaseEachPreviousRun()
    {
        using var player = new SkyPlayer(); using var started = new ManualResetEventSlim();
        var clock = new StopwatchClock(); var input = new RecordingInput(clock); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        var threads = new HashSet<int>(); input.Sent = c => { if (c.IsDown) { threads.Add(Environment.CurrentManagedThreadId); started.Set(); } };
        var sequence = new PlaybackSequence(new[] { PlaybackFixture.Note(0, 0, 10000) }); int completed = 0;
        for (int i = 0; i < 20; i++)
        {
            started.Reset(); player.Play(sequence, 1, () => Interlocked.Increment(ref completed), output);
            Assert.True(started.Wait(TimeSpan.FromSeconds(3))); player.Stop(); Assert.Empty(input.Pressed);
        }
        Assert.Single(threads); Assert.Equal(0, completed);
    }

    [Fact]
    public void CancellationWakesBlockedWorkerAndReleasesWithoutNaturalCompletion()
    {
        using var player = new SkyPlayer(); using var cancel = new CancellationTokenSource();
        using var started = new ManualResetEventSlim(); using var released = new ManualResetEventSlim();
        var input = new RecordingInput(new StopwatchClock()); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        input.Sent = c => { if (c.IsDown) started.Set(); else released.Set(); }; int done = 0;
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 1000000) }), 1, () => done++, output, cancellationToken: cancel.Token);
        Assert.True(started.Wait(TimeSpan.FromSeconds(3))); cancel.Cancel(); Assert.True(released.Wait(TimeSpan.FromSeconds(3)));
        player.Stop(); Assert.Empty(output.PendingKeys); Assert.Equal(0, done);
    }

    [Fact]
    public void AlreadyCanceledRunNeverSendsKeyDown()
    {
        using var player = new SkyPlayer(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var input = new RecordingInput(new StopwatchClock());
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 1000) }), 1, () => { }, new GameNoteOutput(input, SkyPlayer.DefaultVk), cancellationToken: cancel.Token);
        Assert.Empty(input.Events); Assert.False(player.IsPlaying);
    }

    [Fact]
    public void ThreadFailureReleasesInputsAndReportsError()
    {
        using var player = new SkyPlayer(); using var failed = new ManualResetEventSlim();
        var input = new RecordingInput(new StopwatchClock()); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        bool once = false; input.Fail = c => c.IsDown && c.Key == SkyPlayer.DefaultVk[1] && !once && (once = true);
        player.Failed = _ => failed.Set();
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(1, 0, 1000) }), 1, () => { }, output);
        Assert.True(failed.Wait(TimeSpan.FromSeconds(3))); player.Stop();
        Assert.NotNull(player.LastError); Assert.Empty(input.Pressed); Assert.Empty(output.PendingKeys);
    }

    [Fact]
    public void DisposeReleasesActiveInputsAndPreventsRestart()
    {
        var player = new SkyPlayer(); using var started = new ManualResetEventSlim();
        var input = new RecordingInput(new StopwatchClock()); input.Sent = c => { if (c.IsDown) started.Set(); };
        var sequence = new PlaybackSequence(new[] { PlaybackFixture.Note(0, 0, 10000) }); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        player.Play(sequence, 1, () => { }, output); Assert.True(started.Wait(TimeSpan.FromSeconds(3))); player.Dispose();
        Assert.Empty(input.Pressed); Assert.Throws<ObjectDisposedException>(() => player.Play(sequence, 1, () => { }, output));
    }

    [Fact]
    public void ObserverFailureReleasesActiveKeysAndReportsError()
    {
        using var player = new SkyPlayer(); using var failed = new ManualResetEventSlim();
        var input = new RecordingInput(new StopwatchClock()); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        player.NoteFired = _ => throw new InvalidOperationException("模拟观察者故障"); player.Failed = _ => failed.Set();
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 10000) }), 1, () => { }, output);
        Assert.True(failed.Wait(TimeSpan.FromSeconds(3))); player.Stop();
        Assert.NotNull(player.LastError); Assert.Empty(input.Pressed); Assert.Empty(output.PendingKeys);
    }

    [Fact]
    public void StopStillReleasesKeysWhenReadingPositionFails()
    {
        var clock = new FailingClock(); using var player = new SkyPlayer(clock); using var started = new ManualResetEventSlim();
        var input = new RecordingInput(new StopwatchClock()); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        player.NoteFired = _ => started.Set();
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 100000) }), 1, () => { }, output);
        Assert.True(started.Wait(TimeSpan.FromSeconds(3)));
        clock.FailingThread = Environment.CurrentManagedThreadId; Assert.Throws<InvalidOperationException>(player.Stop);
        Assert.Empty(input.Pressed); Assert.Empty(output.PendingKeys); player.Stop();
    }

    [Fact]
    public void InvalidReplacementProjectStopsAndReleasesPreviousSong()
    {
        using var player = new SkyPlayer(); using var started = new ManualResetEventSlim();
        var input = new RecordingInput(new StopwatchClock()); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        player.NoteFired = _ => started.Set();
        player.Play(new(new[] { PlaybackFixture.Note(0, 0, 10000) }), 1, () => { }, output);
        Assert.True(started.Wait(TimeSpan.FromSeconds(3)));
        var invalid = TestProject.ChangeNote(TestProject.Sample(), n => n with { Key = 15 });
        Assert.Throws<System.IO.InvalidDataException>(() => player.Play(invalid, 1, () => { }));
        Assert.False(player.IsPlaying); Assert.Empty(input.Pressed); Assert.Empty(output.PendingKeys);
    }
}
