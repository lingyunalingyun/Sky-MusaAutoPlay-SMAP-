using System.Globalization;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class PerformanceIdentityTests
{
    [Fact]
    public void CanonicalVersionOneHasStableIndependentHashVector()
    {
        // [1,[[0,500000]],[[0,4,4]],[[null,[[14,0,0,0]]]]]
        Assert.Equal("160db979d9606069efbce7763c38bb1cc9f85a0465d8c5d83ae3093b7f79e692",
            PerformanceIdentity.ForProject(TestProject.SingleTap()));
    }

    [Fact]
    public void SmallLengthEditStillChangesHashAtVeryLargeBeat()
    {
        var p = TestProject.ChangeNote(TestProject.SingleTap(), n => n with
            { Beat = 1_000_000_000m, Action = NoteAction.Hold, LengthBeats = 0.00000000000000000001m });
        var changed = TestProject.ChangeNote(p, n => n with { LengthBeats = 0.00000000000000000002m });
        Assert.NotEqual(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(changed));
    }

    [Fact]
    public void IdentityExcludesMetadataEditingIdsGridAndPreviewSettings()
    {
        var p = TestProject.Sample(); string before = PerformanceIdentity.ForProject(p);
        var tracks = p.Tracks.Select(t =>
        {
            var id = Guid.NewGuid();
            return t with { Id = id, Name = "改名", PreviewInstrument = "Cello", PreviewSemitoneOffset = 24,
                Notes = t.Notes.Select(n => n with { Id = Guid.NewGuid(), TrackId = id,
                    MidiOrigin = new(1, 2, 60, 100, 0, 480) }).Reverse().ToList() };
        }).Reverse().ToList();
        var changed = p with { ProjectId = Guid.NewGuid(), GridSubdiv = 3, Tracks = tracks,
            Metadata = new() { Name = "新名称", Author = "另一位作者", Cover = "封面", Description = "描述",
                Sources = new() { new(SourceKind.Attribution, "声明") } } };
        Assert.Equal(before, PerformanceIdentity.ForProject(changed));
        Assert.Equal(64, before.Length);
    }

    [Theory]
    [InlineData("key")] [InlineData("start")] [InlineData("end")] [InlineData("action")]
    [InlineData("tempo")] [InlineData("meter")] [InlineData("target")] [InlineData("deleted")]
    public void EachPerformanceChangeChangesTrackVersion(string change)
    {
        var p = TestProject.SingleTap(); var id = p.Tracks[0].Id;
        var changed = change switch
        {
            "key" => TestProject.ChangeNote(p, n => n with { Key = 0 }),
            "start" => TestProject.ChangeNote(p, n => n with { Beat = 0.00000000000000000001m }),
            "end" => TestProject.ChangeNote(p, n => n with { Action = NoteAction.Hold, LengthBeats = 2 }),
            "action" => TestProject.ChangeNote(p, n => n with { Action = NoteAction.Hold, LengthBeats = 0.25m }),
            "tempo" => p with { Tempos = new() { new(0, 600000) } },
            "meter" => p with { Meters = new() { new(0, 3, 4) } },
            "target" => p with { Tracks = new() { p.Tracks[0] with { GameTarget = new() { InstrumentId = "Harp" } } } },
            "deleted" => p with { Tracks = new() { p.Tracks[0] with { Notes = new() } } },
            _ => throw new ArgumentException(change)
        };
        Assert.NotEqual(PerformanceIdentity.ForTrack(p, id), PerformanceIdentity.ForTrack(changed, id));
    }

    [Fact]
    public void SeparateTrackVersionsIgnoreOtherTracksAndLocalMute()
    {
        var p = TestProject.Sample(); var track = p.Tracks[0];
        var changed = p with { Tracks = new() { track with { Muted = true }, p.Tracks[1] with { Notes = new() } } };
        Assert.Equal(PerformanceIdentity.ForTrack(p, track.Id), PerformanceIdentity.ForTrack(changed, track.Id));
        Assert.NotEqual(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(changed));
    }

    [Fact]
    public void DecimalScaleAndCultureDoNotChangeIdentity()
    {
        var p = TestProject.SingleTap(); var scaled = p with { Tempos = new() { new(0.000m, 500000.000m) } };
        string hash = PerformanceIdentity.ForProject(p); var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new("fr-FR"); Assert.Equal(hash, PerformanceIdentity.ForProject(scaled)); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void RejectsUnknownTrackInsteadOfHashingEmptyContent()
    {
        Assert.Throws<ArgumentException>(() => PerformanceIdentity.ForTrack(TestProject.Sample(), Guid.NewGuid()));
    }
}
