using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

internal sealed class ManualClock : IMonotonicClock
{
    public double Milliseconds { get; set; }
}

internal sealed class RecordingInput(IMonotonicClock clock) : IKeyInput
{
    public readonly List<(double time, ushort key, bool down)> Events = new();
    public readonly List<KeyTransition[]> Batches = new();
    public readonly HashSet<ushort> Pressed = new();
    public Func<KeyTransition, bool>? Fail;
    public Action<KeyTransition>? Sent;
    public void Send(IReadOnlyList<KeyTransition> changes)
    {
        Batches.Add(changes.ToArray());
        foreach (var change in changes)
        {
            if (Fail?.Invoke(change) == true) throw new InvalidOperationException("模拟输入故障");
            if (change.IsDown) Pressed.Add(change.Key); else Pressed.Remove(change.Key);
            Events.Add((clock.Milliseconds, change.Key, change.IsDown)); Sent?.Invoke(change);
        }
    }
}

internal static class PlaybackFixture
{
    public static readonly Guid Track = Guid.Parse("55555555-0000-0000-0000-000000000001");
    public static TimedProjectNote Note(int key, decimal startMs, decimal lengthMs = 0) =>
        new(Track, Guid.NewGuid(), key, lengthMs == 0 ? NoteAction.Tap : NoteAction.Hold, startMs * 1000, lengthMs * 1000);
    public static (PlaybackSession session, RecordingInput input, GameNoteOutput output, ManualClock clock) Session(params TimedProjectNote[] notes)
    {
        var clock = new ManualClock(); var input = new RecordingInput(clock); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        return (new(new(notes), clock, new[] { output }), input, output, clock);
    }
}

public class PlaybackTests
{
    [Theory]
    [InlineData(0.5)] [InlineData(1)] [InlineData(2)]
    public void TapAlwaysUsesEighteenRealMilliseconds(double speed)
    {
        var (s, input, output, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0));
        s.SetSpeed(speed); s.Pump(); Assert.Single(input.Pressed);
        clock.Milliseconds = 17.999; s.Pump(); Assert.Single(input.Pressed);
        clock.Milliseconds = 18; s.Pump(); Assert.Empty(input.Pressed); Assert.Empty(output.PendingKeys); Assert.True(s.Completed);
        Assert.Equal(new[] { (0d, SkyPlayer.DefaultVk[0], true), (18d, SkyPlayer.DefaultVk[0], false) }, input.Events);
    }

    [Fact]
    public void ChordStartsAsOneBatchAndNotesReleaseIndependently()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 250), PlaybackFixture.Note(1, 0, 1000));
        s.Pump(); Assert.Equal(2, Assert.Single(input.Batches).Length);
        clock.Milliseconds = 250; s.Pump(); Assert.Equal(new[] { SkyPlayer.DefaultVk[1] }, input.Pressed);
        clock.Milliseconds = 1000; s.Pump(); Assert.Empty(input.Pressed); Assert.True(s.Completed);
    }

    [Fact]
    public void LongHoldDoesNotBlockAnotherNoteOrFollowGridBoundaries()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 100_000), PlaybackFixture.Note(1, 125.125m));
        s.Pump(); clock.Milliseconds = 125.125; s.Pump(); Assert.Equal(2, input.Pressed.Count);
        clock.Milliseconds = 143.125; s.Pump(); Assert.Single(input.Pressed);
        s.Stop(); Assert.Empty(input.Pressed);
    }

    [Fact]
    public void RepeatedTapRetriggersAndEarlierReleaseDoesNotReleaseLaterOwner()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0), PlaybackFixture.Note(0, 5));
        s.Pump(); clock.Milliseconds = 5; s.Pump();
        Assert.Equal(new[] { true, false, true }, input.Events.Select(e => e.down));
        clock.Milliseconds = 18; s.Pump(); Assert.Single(input.Pressed); Assert.Equal(3, input.Events.Count);
        clock.Milliseconds = 23; s.Pump(); Assert.Empty(input.Pressed); Assert.Equal(4, input.Events.Count);
    }

    [Fact]
    public void OverlappingSameKeyHoldsMergePhysicalOwnership()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(0, 500, 1000));
        s.Pump(); clock.Milliseconds = 500; s.Pump(); Assert.Single(input.Events);
        clock.Milliseconds = 1000; s.Pump(); Assert.Single(input.Pressed); Assert.Single(input.Events);
        clock.Milliseconds = 1500; s.Pump(); Assert.Equal(2, input.Events.Count); Assert.Empty(input.Pressed);
    }

    [Fact]
    public void EqualTimeReleaseAndRestartHasStableReleaseBeforePressOrder()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 100), PlaybackFixture.Note(1, 0, 100),
            PlaybackFixture.Note(0, 100), PlaybackFixture.Note(1, 100));
        s.Pump(); clock.Milliseconds = 100; s.Pump();
        Assert.Equal(new[] { false, false, true, true }, input.Batches[1].Select(e => e.IsDown));
        s.Stop(); Assert.Empty(input.Pressed);
    }

    [Fact]
    public void ChangingSpeedAccumulatesOldRateBeforeApplyingNewRate()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000));
        s.Pump(); clock.Milliseconds = 250; s.SetSpeed(2);
        clock.Milliseconds = 624.999; s.Pump(); Assert.Single(input.Pressed);
        clock.Milliseconds = 625; s.Pump(); Assert.Empty(input.Pressed); Assert.Equal(1000, s.PositionMs);
    }

    [Fact]
    public void PausingReleasesAndResumingRebuildsOnlyRemainingHold()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(1, 0));
        s.Pump(); clock.Milliseconds = 250; s.Pause(true); Assert.Empty(input.Pressed);
        clock.Milliseconds = 1000; s.Pump(); Assert.Equal(250, s.PositionMs);
        s.Pause(false); Assert.Equal(new[] { SkyPlayer.DefaultVk[0] }, input.Pressed);
        clock.Milliseconds = 1750; s.Pump(); Assert.Empty(input.Pressed); Assert.True(s.Completed);
    }

    [Fact]
    public void SeekIntoAndOutOfHoldReleasesThenReconstructs()
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(1, 1500, 500));
        s.Pump(); clock.Milliseconds = 10; s.Seek(500);
        Assert.Equal(new[] { true, false, true }, input.Events.Select(e => e.down));
        s.Seek(1200); Assert.Empty(input.Pressed);
        s.Pause(true); s.Seek(1700); Assert.Empty(input.Pressed);
        clock.Milliseconds = 200; s.Pause(false); Assert.Equal(new[] { SkyPlayer.DefaultVk[1] }, input.Pressed);
        s.Stop(); Assert.Empty(input.Pressed);
    }

    [Fact]
    public void StopIsIdempotentAndReleasesEveryKey()
    {
        var (s, input, output, _) = PlaybackFixture.Session(Enumerable.Range(0, 15).Select(k => PlaybackFixture.Note(k, 0, 1000)).ToArray());
        s.Pump(); Assert.Equal(15, input.Pressed.Count); s.Stop(); s.Stop();
        Assert.Empty(output.PendingKeys); Assert.Empty(input.Pressed); Assert.Equal(0, s.ActiveNoteCount);
    }

    [Fact]
    public void PartiallyFailedBatchStillCleansEveryPossiblyPressedKey()
    {
        var (s, input, output, _) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(1, 0, 1000));
        bool failed = false; input.Fail = c => c.IsDown && c.Key == SkyPlayer.DefaultVk[1] && !failed && (failed = true);
        Assert.Throws<InvalidOperationException>(s.Pump);
        Assert.True(s.Stopped); Assert.Empty(input.Pressed); Assert.Empty(output.PendingKeys);
        Assert.Contains(input.Events, e => e.key == SkyPlayer.DefaultVk[1] && !e.down);
    }

    [Fact]
    public void CleanupRetriesTransientFailureAndContinuesAfterPermanentFailure()
    {
        var (s, input, output, _) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000), PlaybackFixture.Note(1, 0, 1000));
        s.Pump(); input.Fail = c => !c.IsDown && c.Key == SkyPlayer.DefaultVk[0];
        Assert.Throws<AggregateException>(s.Stop);
        Assert.Equal(new[] { SkyPlayer.DefaultVk[0] }, output.PendingKeys);
        Assert.DoesNotContain(SkyPlayer.DefaultVk[1], input.Pressed);
        input.Fail = null; s.Stop(); Assert.Empty(output.PendingKeys); Assert.Empty(input.Pressed);
        var another = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000)); another.session.Pump();
        int failures = 0; another.input.Fail = c => !c.IsDown && failures++ < 2;
        another.session.Stop(); Assert.Empty(another.output.PendingKeys); Assert.Equal(3, failures);
    }

    [Fact]
    public void FailingObserverAndClockBothReleaseActiveState()
    {
        var fixture = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000));
        fixture.session.NoteStarted += _ => throw new InvalidOperationException("模拟观察者故障");
        Assert.Throws<InvalidOperationException>(fixture.session.Pump); Assert.Empty(fixture.input.Pressed);
        fixture = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000)); fixture.session.Pump();
        fixture.clock.Milliseconds = -1; Assert.Throws<InvalidOperationException>(fixture.session.Pump); Assert.Empty(fixture.input.Pressed);
    }

    [Fact]
    public void ProjectCompilationRetainsHoldDurationAndTrackSelection()
    {
        var project = TestProject.Sample(); var sequence = PlaybackSequence.FromProject(project);
        Assert.Equal(3, sequence.Notes.Count);
        Assert.Equal(1_500_000, sequence.Notes.Single(n => n.TrackId == project.Tracks[0].Id && n.Action == NoteAction.Hold).LengthMicroseconds);
        Assert.Equal(2, PlaybackSequence.FromProject(project, trackId: project.Tracks[0].Id).Notes.Count);
        Assert.All(PlaybackSequence.FromLegacy(new[] { (0, 0d), (14, 100d) }).Notes, n =>
        { Assert.Equal(NoteAction.Tap, n.Action); Assert.Equal(0, n.LengthMicroseconds); });
    }

    [Fact]
    public void LongHoldPositionIsReadableWhileWorkerWaitsForDistantRelease()
    {
        var (s, _, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 100000)); s.Pump();
        clock.Milliseconds = 1234.5; Assert.Equal(1234.5, s.PositionMs); Assert.InRange(s.NextDelayMilliseconds, 98765.49, 98765.51);
        s.Stop();
    }

    [Fact]
    public void DenseChordsHaveZeroErrorWithInjectedClockAndBoundedPhysicalState()
    {
        var notes = Enumerable.Range(0, 500).SelectMany(i => Enumerable.Range(0, 15)
            .Select(k => PlaybackFixture.Note(k, i * 20, 10))).ToArray();
        var (s, input, output, clock) = PlaybackFixture.Session(notes);
        for (int i = 0; i < 500; i++)
        {
            clock.Milliseconds = i * 20; s.Pump(); Assert.Equal(15, input.Pressed.Count);
            clock.Milliseconds += 10; s.Pump(); Assert.Empty(input.Pressed);
        }
        Assert.True(s.Completed); Assert.Empty(output.PendingKeys); Assert.Equal(15000, input.Events.Count);
        Assert.All(input.Events, e => Assert.Equal(e.down ? 0 : 10, e.time % 20));
    }

    [Fact]
    public void SeveralSkyKeysBoundToOnePhysicalKeyStillHaveOneLifetime()
    {
        var clock = new ManualClock(); var input = new RecordingInput(clock); var keys = (ushort[])SkyPlayer.DefaultVk.Clone(); keys[1] = keys[0];
        var game = new GameNoteOutput(input, keys);
        var s = new PlaybackSession(new(new[] { PlaybackFixture.Note(0, 0, 500), PlaybackFixture.Note(1, 0, 1000) }), clock, new[] { game });
        s.Pump(); Assert.Single(input.Events); clock.Milliseconds = 500; s.Pump(); Assert.Single(input.Pressed);
        clock.Milliseconds = 1000; s.Pump(); Assert.Empty(input.Pressed); Assert.Equal(2, input.Events.Count);
    }

    [Theory]
    [InlineData("pause")] [InlineData("speed")]
    public void ControlClockFailureAlsoReleasesExistingState(string operation)
    {
        var (s, input, _, clock) = PlaybackFixture.Session(PlaybackFixture.Note(0, 0, 1000)); s.Pump();
        clock.Milliseconds = -1;
        Assert.Throws<InvalidOperationException>(() => { if (operation == "pause") s.Pause(true); else s.SetSpeed(2); });
        Assert.Empty(input.Pressed); Assert.True(s.Stopped);
    }
}
