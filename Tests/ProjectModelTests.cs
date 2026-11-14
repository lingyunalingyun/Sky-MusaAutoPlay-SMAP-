using System.IO;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class ProjectModelTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(3, 1500000)] [InlineData(4, 2000000)]
    [InlineData(5, 3000000)] [InlineData(8, 6000000)] [InlineData(1000000, 999998000000)]
    public void TempoMapConvertsBothDirections(decimal beat, decimal microseconds)
    {
        var timeline = new ProjectTimeline(TestProject.Sample().Tempos.AsEnumerable().Reverse());
        Assert.Equal(microseconds, timeline.BeatToMicroseconds(beat));
        Assert.Equal(beat, timeline.MicrosecondsToBeat(microseconds));
    }

    [Fact]
    public void HoldCrossesTempoBoundaryAndTapHasNoGridDuration()
    {
        var project = TestProject.Sample(); var notes = new ProjectTimeline(project.Tempos).GetNotes(project.Tracks[0]).ToArray();
        Assert.Equal(0, notes[0].LengthMicroseconds);
        Assert.Equal(1_500_000, notes[1].StartMicroseconds);
        Assert.Equal(1_500_000, notes[1].LengthMicroseconds);
        Assert.Equal(project.Tracks[0].Id, notes[1].TrackId);
    }

    [Fact]
    public void FractionalAndVeryLongNotesRemainExactWithoutGridAllocation()
    {
        var project = TestProject.ChangeNote(TestProject.SingleTap(), n => n with
        { Beat = 0.3333333333333333333333333333m, LengthBeats = 1_000_000_000m, Action = NoteAction.Hold });
        ProjectValidation.Validate(project);
        Assert.Single(new ProjectTimeline(project.Tempos).GetNotes(project.Tracks[0]));
        Assert.Equal(500_000_000_000_000m,
            new ProjectTimeline(project.Tempos).GetNotes(project.Tracks[0]).Single().LengthMicroseconds);
    }

    [Theory]
    [InlineData("future")] [InlineData("trackId")] [InlineData("noteId")] [InlineData("trackMismatch")]
    [InlineData("key")] [InlineData("negativeBeat")] [InlineData("tapLength")] [InlineData("holdLength")]
    [InlineData("action")] [InlineData("tempoDuplicate")] [InlineData("tempoMissingZero")]
    [InlineData("tempoZero")] [InlineData("meterDuplicate")] [InlineData("meterDenominator")]
    [InlineData("emptyTracks")] [InlineData("tooManyTracks")] [InlineData("badTarget")]
    [InlineData("nullNotes")] [InlineData("nullMetadata")] [InlineData("overflow")]
    [InlineData("midiOrigin")] [InlineData("badSource")]
    public void RejectsInvalidModels(string problem)
    {
        var p = TestProject.Sample();
        p = problem switch
        {
            "future" => p with { FormatVersion = 99 },
            "trackId" => p with { Tracks = new() { p.Tracks[0], p.Tracks[0] } },
            "noteId" => p with { Tracks = new() { p.Tracks[0] with { Notes = new() { p.Tracks[0].Notes[0], p.Tracks[0].Notes[0] } } } },
            "trackMismatch" => TestProject.ChangeNote(p, n => n with { TrackId = Guid.NewGuid() }),
            "key" => TestProject.ChangeNote(p, n => n with { Key = 15 }),
            "negativeBeat" => TestProject.ChangeNote(p, n => n with { Beat = -1 }),
            "tapLength" => TestProject.ChangeNote(p, n => n with { LengthBeats = 1 }),
            "holdLength" => TestProject.ChangeNote(p, n => n with { Action = NoteAction.Hold }),
            "action" => TestProject.ChangeNote(p, n => n with { Action = (NoteAction)99 }),
            "tempoDuplicate" => p with { Tempos = new() { new(0, 500000), new(0, 600000) } },
            "tempoMissingZero" => p with { Tempos = new() { new(1, 500000) } },
            "tempoZero" => p with { Tempos = new() { new(0, 0) } },
            "meterDuplicate" => p with { Meters = new() { new(0, 4, 4), new(0, 3, 4) } },
            "meterDenominator" => p with { Meters = new() { new(0, 4, 3) } },
            "emptyTracks" => p with { Tracks = new() },
            "tooManyTracks" => p with { Tracks = Enumerable.Range(0, ProjectValidation.MaxTracks + 1).Select(_ => new ProjectTrack()).ToList() },
            "badTarget" => p with { Tracks = new() { p.Tracks[0] with { GameTarget = new() { KeyMidiPitches = new() { 60 } } } } },
            "nullNotes" => p with { Tracks = new() { p.Tracks[0] with { Notes = null! } } },
            "nullMetadata" => p with { Metadata = null! },
            "overflow" => TestProject.ChangeNote(p, n => n with { Beat = decimal.MaxValue, LengthBeats = 1, Action = NoteAction.Hold }),
            "midiOrigin" => TestProject.ChangeNote(p, n => n with { MidiOrigin = new(0, 0, 60, 100, 20, 10) }),
            "badSource" => p with { Metadata = new() { Sources = new() { new(SourceKind.InternalVersion, "", ContentHash: "short") } } },
            _ => throw new ArgumentException(problem)
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidation.Validate(p));
    }

    [Fact]
    public void MuteAndSoloSelectOnlyAudibleTracks()
    {
        var p = TestProject.Sample(); Assert.Equal(2, p.AudibleTracks().Count());
        var solo = p with { Tracks = new() { p.Tracks[0] with { Solo = true }, p.Tracks[1] } };
        Assert.Equal(p.Tracks[0].Id, Assert.Single(solo.AudibleTracks()).Id);
        var mutedSolo = p with { Tracks = new() { p.Tracks[0] with { Solo = true, Muted = true }, p.Tracks[1] } };
        Assert.Equal(p.Tracks[1].Id, Assert.Single(mutedSolo.AudibleTracks()).Id);
    }
}
