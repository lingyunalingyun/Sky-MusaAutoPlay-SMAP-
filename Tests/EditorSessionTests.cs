using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

[CollectionDefinition("Editor model", DisableParallelization = true)]
public sealed class EditorModelCollection;

[Collection("Editor model")]
public class EditorSessionTests
{
    [Fact]
    public void ClipboardAndOneTransactionPreserveCompleteHoldAndIndependentIds()
    {
        var session = new EditorSession(TestProject.Sample()); var hold = session.ActiveTrack.Notes[1];
        session.Select(new[] { hold.Id }); session.Copy(); session.Paste(8);
        var copy = Assert.Single(session.SelectedNotes()); Assert.NotEqual(hold.Id, copy.Id); Assert.Equal(2, copy.LengthBeats);
        Assert.Equal(NoteAction.Hold, copy.Action); Assert.Equal(8, copy.Beat); Assert.Equal(1, session.UndoCount);
        session.Undo(); Assert.Equal(2, session.ActiveTrack.Notes.Count); Assert.False(session.IsDirty);
        session.Redo(); Assert.Equal(3, session.ActiveTrack.Notes.Count); Assert.True(session.IsDirty);
        session.MarkSaved(); session.MoveSelected(1, 0); session.Undo(); Assert.False(session.IsDirty);
    }
    [Fact]
    public void PreviewNeverMutatesAndCommitIsOneHistoryEntry()
    {
        var session = new EditorSession(TestProject.Sample()); var n = session.ActiveTrack.Notes[1]; session.Select(new[] { n.Id });
        for (int i = 0; i < 200; i++) { var preview = session.MovePreview(session.SelectedNotes(), i / 100m, 0); Assert.Equal(3, session.Find(n.Id)!.Beat); }
        Assert.Equal(0, session.UndoCount); session.PutNotes(session.MovePreview(session.SelectedNotes(), .75m, 2));
        Assert.Equal(1, session.UndoCount); session.Undo(); Assert.Equal(n, session.Find(n.Id));
    }
    [Fact]
    public void GroupBoundariesPreserveRelativeIntervalsAndOffsets()
    {
        var s = new EditorSession(TestProject.Sample()); s.Select(s.ActiveTrack.Notes.Select(n => n.Id)); var before = s.SelectedNotes();
        s.MoveSelected(-100, 100); var after = s.SelectedNotes();
        Assert.Equal(before[1].Beat - before[0].Beat, after[1].Beat - after[0].Beat);
        Assert.Equal(before[1].Key - before[0].Key, after[1].Key - after[0].Key);
        Assert.Equal(0, after.Min(n => n.Beat)); Assert.Equal(14, after.Max(n => n.Key));
    }
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(3)]
    public void ChangingGridNeverMovesOrConvertsNotesOrChangesPerformance(int grid)
    {
        var s = new EditorSession(TestProject.Sample()); var original = s.ActiveTrack.Notes.ToArray(); string hash = PerformanceIdentity.ForProject(s.Project);
        s.SetGrid(grid); Assert.Equal(original, s.ActiveTrack.Notes); Assert.Equal(hash, PerformanceIdentity.ForProject(s.Project)); Assert.Equal(0, s.UndoCount);
    }
    [Fact]
    public void ResizeDistinguishesTapShortImportedHoldAndExplicitReturnToTap()
    {
        var s = new EditorSession(TestProject.SingleTap()); var tap = s.ActiveTrack.Notes[0];
        var hold = Assert.Single(s.ResizePreview(new[] { tap }, 2)); Assert.Equal(NoteAction.Hold, hold.Action); Assert.Equal(2.25m, hold.LengthBeats);
        Assert.Equal(NoteAction.Tap, s.ResizePreview(new[] { hold }, -2)[0].Action);
        var shortHold = hold with { LengthBeats = .125m, RetriggerOnOverlap = true };
        Assert.Equal(NoteAction.Hold, s.ResizePreview(new[] { shortHold }, .125m)[0].Action);
        Assert.Equal(NoteAction.Hold, s.ResizePreview(new[] { hold }, -2, free: true)[0].Action);
        Assert.Throws<InvalidOperationException>(() => s.ResizePreview(new[] { tap, hold }, 1));
    }
    [Fact]
    public void InvalidEditIsAtomicAndDoesNotDestroyRedo()
    {
        var s = new EditorSession(TestProject.SingleTap()); var note = s.ActiveTrack.Notes[0];
        s.Create(2, 2); s.Undo(); var original = s.Project;
        Assert.ThrowsAny<Exception>(() => s.PutNotes(new[] { note with { Key = 20 } }));
        Assert.Same(original, s.Project); Assert.True(s.CanRedo); Assert.Equal(0, s.UndoCount);
    }
    [Fact]
    public void SplitPreservesTotalDurationAndRearticulatesWithUndo()
    {
        var s = new EditorSession(TestProject.Sample()); var note = s.ActiveTrack.Notes[1]; s.Select(new[] { note.Id }); s.SplitSelected(4);
        var fragments = s.SelectedNotes().OrderBy(n => n.Beat).ToArray(); Assert.Equal(2, fragments.Length);
        Assert.Equal(note.LengthBeats, fragments.Sum(n => n.LengthBeats)); Assert.True(fragments[1].RetriggerOnOverlap);
        s.Undo(); Assert.Equal(note, s.Find(note.Id)); Assert.Equal(2, s.ActiveTrack.Notes.Count);
    }
    [Fact]
    public void TrackDeletionAndMetadataAreUndoableAndKeepOtherTracks()
    {
        var s = new EditorSession(TestProject.Sample()); Guid first = s.ActiveTrackId; Guid second = s.AddTrack("New");
        Assert.Equal(second, s.ActiveTrackId); s.UpdateTrack(s.ActiveTrack with { Name = "Renamed", Muted = true, Solo = true });
        s.DeleteTrack(first); Assert.DoesNotContain(s.Project.Tracks, t => t.Id == first); s.Undo(); Assert.Equal(2, s.Project.Tracks.Single(t => t.Id == first).Notes.Count);
        s.Undo(); Assert.Equal("New", s.ActiveTrack.Name); Assert.False(s.ActiveTrack.Muted);
    }
    [Fact]
    public void HistoryStoresDeltasInsteadOfRepeatedWholeTracksAndIsBounded()
    {
        var p = TestProject.SingleTap(); var t = p.Tracks[0];
        p = p with { Tracks = new() { t with { Notes = Enumerable.Range(0, 10000).Select(i => t.Notes[0] with { Id = Guid.NewGuid(), Beat = i }).ToList() } } };
        var s = new EditorSession(p); s.Select(new[] { s.ActiveTrack.Notes[0].Id });
        for (int i = 0; i < 140; i++) s.MoveSelected(.25m, 0);
        Assert.Equal(128, s.UndoCount); Assert.Equal(256, s.HistoryNoteVersions);
    }
    [Fact]
    public void RemapPreservesManualMovesAdditionsAndDeletionsByDefaultAndCanUndoReplacement()
    {
        var source = new MidiFixture().Note(60, 0, 480).Note(64, 480, 960).Read(); var options = MidiFixture.Options(source);
        var project = MidiProjectConverter.Create(SapmMapper.Convert(source, options)); var s = new EditorSession(project);
        var first = s.ActiveTrack.Notes[0]; var second = s.ActiveTrack.Notes[1]; s.PutNotes(new[] { first with { Beat = 5 } }, new[] { second.Id });
        var added = s.Create(3, 8); var changed = options.Select(v => v with { SemitoneShift = 12 }).ToArray();
        var remapped = MidiProjectConverter.Remap(s.Project, changed);
        var preserved = s.PreviewRemap(remapped); Assert.Equal(5, preserved.Project.Tracks[0].Notes.Single(n => n.Id == first.Id).Beat);
        Assert.DoesNotContain(preserved.Project.Tracks[0].Notes, n => n.Id == second.Id); Assert.Contains(added, preserved.Project.Tracks[0].Notes);
        var replace = s.PreviewRemap(remapped, false); Assert.NotEmpty(replace.Differences); var before = s.Project;
        s.ReplaceProject(replace.Project); Assert.Contains(added, s.ActiveTrack.Notes); s.Undo(); Assert.Equal(before.Tracks[0].Notes, s.ActiveTrack.Notes);
    }
    [Fact]
    public void MappedAuditionIncludesNewAndCopiedNotesWithoutChangingOriginalMidi()
    {
        var source = new MidiFixture().Note(60, 0, 480).Read(); var p = MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source)));
        var s = new EditorSession(p); var n = s.Create(4, 3); var plan = MidiAudition.Mapped(s.Project);
        Assert.Contains(n.Id, plan.Tones.Keys); Assert.Equal(67, plan.Tones[n.Id].Pitch); Assert.Equal(100, plan.Tones[n.Id].Velocity);
        Assert.Equal(source.Bytes, s.Project.OriginalMidi); Assert.Single(s.Project.MidiConversion!.Source.Notes);
    }
    [Fact]
    public void NativeEditsRoundTripWithoutLosingSourceOrHoldSemantics()
    {
        using var files = new TestProject(); var s = new EditorSession(TestProject.Sample()); s.Select(new[] { s.ActiveTrack.Notes[1].Id });
        s.PutNotes(s.ResizePreview(s.SelectedNotes(), .375m)); string path = files.PathFor("edited.smap"); SmapFile.Save(s.Project, path);
        Assert.Equal(PerformanceIdentity.ForProject(s.Project), PerformanceIdentity.ForProject(SmapFile.Load(path)));
    }
}
