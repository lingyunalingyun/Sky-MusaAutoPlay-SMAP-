using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class CapabilityTests
{
    public static InstrumentCapability Four() => new() { Id = "test:drum", InstrumentId = "TestDrum", LayoutId = "grid:2x2:1",
        Rows = 2, Columns = 2, Kind = GameInstrumentKind.UnpitchedPercussion, Sources = new() { "test fixture" },
        Keys = new() { new("a", 0, 0, null, "a", 0, KeyAvailability.Available), new("b", 4, 1, null, "b", 4),
            new("c", 10, 2, null, "c", 10, KeyAvailability.Unavailable), new("d", 14, 3, null, "d", 14) },
        Slots = new() { new(0, 0, "a"), new(0, 1, "b"), new(1, 0, "c"), new(1, 1, "d") } };
    [Theory] [InlineData("Piano", 15, 3, 5)] [InlineData("Handpan", 8, 2, 4)]
    public void CatalogHasExplicitPhysicalKeysAndSymmetricSlots(string instrument, int count, int rows, int columns)
    {
        var c = InstrumentCapabilities.ForInstrument(instrument)!; c.Validate();
        Assert.Equal(count, c.PhysicalKeyCount); Assert.Equal(rows, c.Rows); Assert.Equal(columns, c.Columns);
        Assert.Equal(count, c.Slots.Select(s => s.KeyId).Distinct().Count());
    }
    [Fact] public void EightLogicalKeysAreSparseAndOldOrdinalsStayDistinct()
    {
        var c = InstrumentCapabilities.ForInstrument("Handpan")!;
        Assert.Equal(new[] { 1, 5, 7, 8, 10, 11, 12, 14 }, c.Keys.Select(k => k.LogicalKey));
        Assert.Equal("key-0", c.Resolve(0, NoteKeyDomain.LegacyOrdinal)!.Id);
        Assert.Null(c.Resolve(0, NoteKeyDomain.StableLogical));
        Assert.Equal("key-7", c.Resolve(14, NoteKeyDomain.StableLogical)!.Id);
    }
    [Fact] public void FourKeyDisabledSlotDoesNotCollapseAndUnknownDoesNotBecomeDisabled()
    {
        var c = Four(); c.Validate(); Assert.Equal(4, c.Slots.Count);
        Assert.Equal(KeyAvailability.Unknown, c.Keys[1].Availability);
        Assert.Equal(KeyAvailability.Unavailable, c.Resolve(10, NoteKeyDomain.StableLogical)!.Availability);
        Assert.Equal("c", c.Slots.Single(s => s.Row == 1 && s.Column == 0).KeyId);
    }
    [Fact] public void DuplicatePitchesAndUnpitchedSoundsRetainPhysicalIdentity()
    {
        var bells = InstrumentCapabilities.ForInstrument("APBell1")!;
        Assert.Equal(8, bells.Keys.Count); Assert.Equal(4, bells.Keys.Select(k => k.MidiPitch).Distinct().Count());
        var drums = InstrumentCapabilities.ForInstrument("Dundun")!;
        Assert.All(drums.Keys, k => Assert.Null(k.MidiPitch)); Assert.Equal(8, drums.Keys.Select(k => k.SoundId).Distinct().Count());
    }
    [Fact] public void UnknownInstrumentDoesNotInventFifteenGameKeys() => Assert.Null(InstrumentCapabilities.ForInstrument("Unknown"));
    [Fact] public void NewFormatCanEditGreyNotesAndUndoWithoutChangingOtherTrack()
    {
        var p = TestProject.Sample(); var t = p.Tracks[0];
        p = p with { FormatVersion = 2, KeyInterpretationVersion = 1, Tracks = new() { t with { Capability = Four(), KeyDomain = NoteKeyDomain.StableLogical }, p.Tracks[1] } };
        var s = new EditorSession(p); var note = s.ActiveTrack.Notes[0];
        s.Select(new[] { note.Id }); s.MoveSelected(2, -4);
        Assert.Equal(KeyAvailability.Unavailable, PerformanceCompatibility.Evaluate(s.ActiveTrack, s.Find(note.Id)!).Availability);
        Assert.Equal(p.Tracks[1].Notes, s.Project.Tracks[1].Notes); s.Undo(); Assert.Equal(note, s.Find(note.Id));
    }
    [Fact] public void CapabilitySnapshotRoundTripsAndIdentityIgnoresPreviewAndSources()
    {
        var p = TestProject.SingleTap(); p = p with { FormatVersion = 2, KeyInterpretationVersion = 1, Tracks = new() { p.Tracks[0] with { Capability = Four(), KeyDomain = NoteKeyDomain.StableLogical } } };
        using var files = new TestProject(); string file = files.PathFor("capability.smap"); SmapFile.Save(p, file);
        var loaded = SmapFile.Load(file); Assert.Equal(4, loaded.Tracks[0].Capability!.Keys.Count);
        Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(loaded));
        var changed = p with { Tracks = new() { p.Tracks[0] with { PreviewInstrument = "Cello", Capability = Four() with { Sources = new() { "changed" } } } } };
        Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(changed));
    }
    [Fact] public void LegacyHashStillHasExactVector() => Assert.Equal("160db979d9606069efbce7763c38bb1cc9f85a0465d8c5d83ae3093b7f79e692", PerformanceIdentity.ForProject(TestProject.SingleTap()));
    [Fact] public void UnknownRequiresExplicitAcceptanceButCannotBypassMissingKey()
    {
        var p = TestProject.SingleTap(); p = p with { FormatVersion = 2, KeyInterpretationVersion = 1, Tracks = new() { p.Tracks[0] with { Capability = Four(), KeyDomain = NoteKeyDomain.StableLogical } } };
        Assert.Throws<InvalidOperationException>(() => PerformanceCompatibility.RequirePlayable(p));
        PerformanceCompatibility.RequirePlayable(p, allowUnknown: true);
        var missing = TestProject.ChangeNote(p, n => n with { Key = 3 });
        Assert.Throws<InvalidOperationException>(() => PerformanceCompatibility.RequirePlayable(missing, allowUnknown: true));
    }
}
