using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class PracticeSessionTests
{
    static (PracticeSession session, ManualClock clock, TimedProjectNote note) Hold(PracticeMode mode = PracticeMode.Follow)
    {
        var clock = new ManualClock(); var note = PlaybackFixture.Note(0, 0, 1000);
        return (new(new(new[] { note }), clock, mode), clock, note);
    }

    [Theory]
    [InlineData(PracticeMode.Follow)] [InlineData(PracticeMode.Rhythm)]
    public void HoldNeedsBothOnsetAndCorrectRelease(PracticeMode mode)
    {
        var (s, clock, note) = Hold(mode); s.Press(0);
        Assert.Equal(PracticeNoteStatus.Holding, s.Statuses[note.NoteId]); Assert.False(s.IsCompleted);
        clock.Milliseconds = 700; s.Tick(); Assert.Contains(0, s.HoldingKeys);
        clock.Milliseconds = 1000; s.Release(0); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[note.NoteId]);
        Assert.Equal(PracticeJudgmentKind.Completed, s.LastJudgment!.Kind);
    }

    [Theory]
    [InlineData(PracticeMode.Follow)] [InlineData(PracticeMode.Rhythm)]
    public void EarlyReleaseFailsInsteadOfCompletingHold(PracticeMode mode)
    {
        var (s, clock, note) = Hold(mode); s.Press(0); clock.Milliseconds = 500; s.Release(0);
        Assert.Equal(PracticeJudgmentKind.EarlyRelease, s.LastJudgment!.Kind);
        Assert.NotEqual(PracticeNoteStatus.Completed, s.Statuses[note.NoteId]);
        if (mode == PracticeMode.Follow)
        {
            Assert.Equal(0, s.CurrentStepIndex); s.Press(0); clock.Milliseconds = 1500; s.Release(0);
            Assert.True(s.IsCompleted);
        }
    }

    [Theory]
    [InlineData(PracticeMode.Follow)] [InlineData(PracticeMode.Rhythm)]
    public void FailingToReleaseIsNotACompletedSustain(PracticeMode mode)
    {
        var (s, clock, note) = Hold(mode); s.Press(0); clock.Milliseconds = 1081; s.Tick();
        Assert.Equal(PracticeJudgmentKind.LateRelease, s.LastJudgment!.Kind);
        Assert.NotEqual(PracticeNoteStatus.Completed, s.Statuses[note.NoteId]);
    }

    [Fact]
    public void ChordNeedsSimultaneousKeysThenIndependentHoldReleases()
    {
        var clock = new ManualClock(); var first = PlaybackFixture.Note(0, 0, 500); var second = PlaybackFixture.Note(1, 0, 1000);
        var s = new PracticeSession(new(new[] { first, second }), clock);
        s.Press(0); s.Release(0); s.Press(1); Assert.Equal(PracticeNoteStatus.Pending, s.Statuses[first.NoteId]);
        s.Press(0); Assert.Equal(2, s.HoldingKeys.Count()); Assert.False(s.IsCompleted);
        clock.Milliseconds = 500; s.Release(0); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[first.NoteId]);
        Assert.False(s.IsCompleted); clock.Milliseconds = 1000; s.Release(1); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void OverlappingDifferentKeysCanStartBeforeEarlierHoldEnds()
    {
        var clock = new ManualClock(); var first = PlaybackFixture.Note(0, 0, 1000); var next = PlaybackFixture.Note(1, 500, 1000);
        var s = new PracticeSession(new(new[] { first, next }), clock);
        s.Press(0); clock.Milliseconds = 500; s.Press(1); Assert.Equal(2, s.HoldingKeys.Count());
        clock.Milliseconds = 1000; s.Release(0); clock.Milliseconds = 1500; s.Release(1); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void SameKeyOverlapUsesOneContinuousPhysicalHoldAndCompletesBothNotes()
    {
        var clock = new ManualClock(); var first = PlaybackFixture.Note(0, 0, 1000); var next = PlaybackFixture.Note(0, 500, 1000);
        var s = new PracticeSession(new(new[] { first, next }), clock);
        Assert.Single(s.Steps); s.Press(0); clock.Milliseconds = 1000; s.Tick(); Assert.False(s.IsCompleted);
        clock.Milliseconds = 1500; s.Release(0); Assert.True(s.IsCompleted);
        Assert.All(s.Statuses.Values, state => Assert.Equal(PracticeNoteStatus.Completed, state));
    }

    [Fact]
    public void AutorepeatDoesNotCompleteNextTapButReleaseAndRepressDoes()
    {
        var clock = new ManualClock(); var s = new PracticeSession(new(new[] { PlaybackFixture.Note(0, 0), PlaybackFixture.Note(0, 5) }), clock, groupingToleranceMs: 20);
        Assert.Equal(2, s.Steps.Count); s.Press(0); Assert.Equal(1, s.CurrentStepIndex);
        s.Press(0); Assert.Equal(PracticeJudgmentKind.RepeatedPress, s.LastJudgment!.Kind); Assert.False(s.IsCompleted);
        s.Release(0); s.Press(0); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void ExpectedTapInsideHoldAllowsShortRearticulationWithoutLosingSustain()
    {
        var clock = new ManualClock(); var hold = PlaybackFixture.Note(0, 0, 1000); var tap = PlaybackFixture.Note(0, 500);
        var s = new PracticeSession(new(new[] { hold, tap }), clock); s.Press(0);
        clock.Milliseconds = 500; s.Release(0); clock.Milliseconds = 518; s.Press(0);
        Assert.Equal(PracticeNoteStatus.Holding, s.Statuses[hold.NoteId]); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[tap.NoteId]);
        clock.Milliseconds = 1000; s.Release(0); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void LongGapWhileRetriggeringIsEarlyRelease()
    {
        var clock = new ManualClock(); var hold = PlaybackFixture.Note(0, 0, 1000);
        var s = new PracticeSession(new(new[] { hold, PlaybackFixture.Note(0, 500) }), clock); s.Press(0);
        clock.Milliseconds = 500; s.Release(0); clock.Milliseconds = 551; s.Tick();
        Assert.Equal(PracticeJudgmentKind.EarlyRelease, s.LastJudgment!.Kind); Assert.False(s.IsCompleted);
    }

    [Fact]
    public void RhythmAdvancesWithoutInputAndRecordsMissedOnsets()
    {
        var clock = new ManualClock(); var s = new PracticeSession(new(new[] { PlaybackFixture.Note(0, 0), PlaybackFixture.Note(1, 500) }), clock, PracticeMode.Rhythm);
        clock.Milliseconds = 500; s.Tick(); Assert.Equal(1, s.CurrentStepIndex);
        Assert.Contains(PracticeNoteStatus.Missed, s.Statuses.Values);
        s.Press(1); Assert.Contains(PracticeNoteStatus.Completed, s.Statuses.Values);
        clock.Milliseconds = 700; s.Tick(); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void RhythmRejectsOutOfWindowAndUnexpectedKeys()
    {
        var clock = new ManualClock(); var note = PlaybackFixture.Note(0, 500);
        var s = new PracticeSession(new(new[] { PlaybackFixture.Note(1, 0), note }), clock, PracticeMode.Rhythm);
        s.Press(0); Assert.Equal(PracticeJudgmentKind.UnexpectedKey, s.LastJudgment!.Kind);
        clock.Milliseconds = 700; s.Tick(); Assert.Equal(PracticeNoteStatus.Missed, s.Statuses[note.NoteId]);
    }

    [Fact]
    public void PauseFreezesFollowHoldAndRequiresFreshPressOnResume()
    {
        var (s, clock, note) = Hold(); s.Press(0); clock.Milliseconds = 300; s.Pause(true);
        Assert.Empty(s.HoldingKeys); clock.Milliseconds = 5000; s.Pause(false);
        clock.Milliseconds = 6000; s.Tick(); Assert.Equal(PracticeNoteStatus.Holding, s.Statuses[note.NoteId]);
        s.Press(0); clock.Milliseconds = 6700; s.Release(0); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void SeekIntoHoldRequiresPressAndOnlyRemainingDuration()
    {
        var (s, clock, note) = Hold(); s.Seek(600); Assert.False(s.IsCompleted); Assert.Empty(s.HoldingKeys);
        s.Press(0); clock.Milliseconds = 400; s.Release(0); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[note.NoteId]);
    }

    [Fact]
    public void PracticeRateChangePreservesElapsedHoldTime()
    {
        var (s, clock, _) = Hold(); s.Press(0); clock.Milliseconds = 250; s.Speed = 2;
        clock.Milliseconds = 625; s.Release(0); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void RhythmPreservesLeadingRestAndDoesNotAcceptItsFirstNoteEarly()
    {
        var clock = new ManualClock(); var note = PlaybackFixture.Note(0, 500);
        var s = new PracticeSession(new(new[] { note }), clock, PracticeMode.Rhythm);
        Assert.Equal(0, s.PositionMs); s.Press(0); Assert.Equal(PracticeJudgmentKind.UnexpectedKey, s.LastJudgment!.Kind);
        s.Release(0); clock.Milliseconds = 500; s.Press(0); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[note.NoteId]);
    }

    [Theory]
    [InlineData(PracticeMode.Follow)] [InlineData(PracticeMode.Rhythm)]
    public void VeryShortHoldCannotPassAsAnImmediateTap(PracticeMode mode)
    {
        var clock = new ManualClock(); var note = PlaybackFixture.Note(0, 0, 50);
        var s = new PracticeSession(new(new[] { note }), clock, mode); s.Press(0); s.Release(0);
        Assert.Equal(PracticeJudgmentKind.EarlyRelease, s.LastJudgment!.Kind);
        Assert.NotEqual(PracticeNoteStatus.Completed, s.Statuses[note.NoteId]);
    }
}
