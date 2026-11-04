using System.IO;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class MidiPracticeIntegrationTests
{
    static SmapProject PedalRepeat()
    {
        var source = new MidiFixture().Pedal(0, 127).Note(60, 0, 100).Note(60, 480, 580).Pedal(960, 0).Read();
        return MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source)));
    }

    [Fact]
    public void PedalRepeatedNoteKeepsFreshGameAttackAndOnePhysicalLifetime()
    {
        var p = PedalRepeat(); var sequence = PlaybackSequence.FromProject(p); var clock = new ManualClock();
        Assert.All(sequence.Notes, n => Assert.True(n.RetriggerOnOverlap));
        var input = new RecordingInput(clock); var output = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        var session = new PlaybackSession(sequence, clock, new[] { output }); session.Pump(); clock.Milliseconds = 500; session.Pump();
        Assert.Equal(new[] { true, false, true }, input.Events.Select(e => e.down)); Assert.Single(input.Pressed);
        session.Pause(true); Assert.Empty(input.Pressed); int before = input.Events.Count; session.Pause(false);
        Assert.Equal(before + 1, input.Events.Count); Assert.Single(input.Pressed);
        clock.Milliseconds = 1000; session.Pump(); Assert.True(session.Completed); Assert.Empty(output.PendingKeys);
    }

    [Theory]
    [InlineData(PracticeMode.Follow)] [InlineData(PracticeMode.Rhythm)]
    public void PracticeRequiresFreshPedalRepeatThenFinalRelease(PracticeMode mode)
    {
        var p = PedalRepeat(); var sequence = PlaybackSequence.FromProject(p); var clock = new ManualClock(); var s = new PracticeSession(sequence, clock, mode);
        Assert.Equal(2, s.Steps.Count); s.Press(0); clock.Milliseconds = 500; s.Press(0);
        Assert.Equal(PracticeJudgmentKind.RepeatedPress, s.LastJudgment!.Kind); Assert.False(s.IsCompleted);
        s.Release(0); clock.Milliseconds = 518; s.Press(0); Assert.False(s.IsCompleted);
        clock.Milliseconds = 1000; s.Release(0); Assert.True(s.IsCompleted);
        Assert.All(s.Statuses.Values, status => Assert.Equal(PracticeNoteStatus.Completed, status));
    }

    [Fact]
    public void NestedSameKeyPracticeOwnersReleaseOnlyAfterLastOwnerEnds()
    {
        var first = PlaybackFixture.Note(0, 0, 700) with { RetriggerOnOverlap = true };
        var second = PlaybackFixture.Note(0, 500, 500) with { RetriggerOnOverlap = true };
        var clock = new ManualClock(); var s = new PracticeSession(new(new[] { first, second }), clock);
        s.Press(0); clock.Milliseconds = 500; s.Release(0); s.Press(0);
        clock.Milliseconds = 700; s.Tick(); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[first.NoteId]);
        Assert.Equal(PracticeNoteStatus.Holding, s.Statuses[second.NoteId]); Assert.False(s.IsCompleted);
        clock.Milliseconds = 1000; s.Release(0); Assert.True(s.IsCompleted);
    }

    [Fact]
    public void EarlyReleaseOfSeveralOwnersRewindsToEarliestPendingStep()
    {
        var first = PlaybackFixture.Note(0, 0, 1000) with { RetriggerOnOverlap = true };
        var second = PlaybackFixture.Note(0, 500, 1000) with { RetriggerOnOverlap = true };
        var clock = new ManualClock(); var s = new PracticeSession(new(new[] { first, second }), clock);
        s.Press(0); clock.Milliseconds = 500; s.Release(0); s.Press(0); clock.Milliseconds = 600; s.Release(0);
        Assert.Equal(PracticeJudgmentKind.EarlyRelease, s.LastJudgment!.Kind); Assert.Equal(0, s.CurrentStepIndex);
        Assert.All(s.Statuses.Values, status => Assert.Equal(PracticeNoteStatus.Pending, status));
    }

    [Fact]
    public void RetriggerIdentityUsesVersionTwoWhileOldCanonicalVectorStaysStable()
    {
        using var files = new TestProject(); var p = PedalRepeat(); string hash = PerformanceIdentity.ForProject(p);
        var merged = p with { Tracks = p.Tracks.Select(t => t with { Notes = t.Notes.Select(n => n with { RetriggerOnOverlap = false }).ToList() }).ToList() };
        Assert.NotEqual(hash, PerformanceIdentity.ForProject(merged));
        string path = files.PathFor("retrigger.smap"); SmapFile.Save(p, path); var loaded = SmapFile.Load(path);
        Assert.Equal(hash, PerformanceIdentity.ForProject(loaded)); Assert.All(loaded.Tracks.SelectMany(t => t.Notes), n => Assert.True(n.RetriggerOnOverlap));
        Assert.Equal("160db979d9606069efbce7763c38bb1cc9f85a0465d8c5d83ae3093b7f79e692", PerformanceIdentity.ForProject(TestProject.SingleTap()));
    }

    [Fact]
    public void MixedMergeAndRetriggerHoldsRetainContinuousLegacyRegion()
    {
        var a = PlaybackFixture.Note(0, 0, 1000); var b = PlaybackFixture.Note(0, 500, 200) with { RetriggerOnOverlap = true };
        var c = PlaybackFixture.Note(0, 900, 600); var clock = new ManualClock(); var s = new PracticeSession(new(new[] { a, b, c }), clock);
        Assert.Equal(2, s.Steps.Count); s.Press(0); clock.Milliseconds = 500; s.Release(0); s.Press(0);
        clock.Milliseconds = 700; s.Tick(); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[b.NoteId]);
        clock.Milliseconds = 1500; s.Release(0); Assert.True(s.IsCompleted); Assert.Equal(PracticeNoteStatus.Completed, s.Statuses[c.NoteId]);
    }
}
