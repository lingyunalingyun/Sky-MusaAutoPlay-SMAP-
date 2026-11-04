using System.IO;
using System.Reflection;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

[Collection("Editor model")]
public class EditorBridgeTests
{
    sealed class Voices : IMidiAuditionVoices
    {
        public readonly HashSet<Guid> Active = new();
        public readonly List<AuditionTone> Tones = new();
        public void Start(Guid owner, Guid id, AuditionTone tone, bool sustain) { Active.Add(id); Tones.Add(tone); }
        public void Release(Guid id) => Active.Remove(id);
        public void StopOwner(Guid owner) => Active.Clear();
    }
    [Fact]
    public void MixedNativeAndMappedAudioOwnSeparateVoicesAndCleanUpOnPauseSeekStop()
    {
        var p = TestProject.Sample(); var mapped = p.Tracks[0] with { GameTarget = GamePitchProfiles.Get("Flute").Target(), PreviewInstrument = "Flute", PreviewSemitoneOffset = 0 };
        p = p with { Tracks = new() { mapped, p.Tracks[1] } }; var source = PlaybackSequence.FromProject(p);
        var midi = new Voices(); var samples = new FakeAudioVoices(); var clock = new ManualClock(); var output = new EditorAudioOutput(p, samples, midi);
        var playback = new PlaybackSession(source, clock, new[] { output }); playback.Pump(); Assert.Single(midi.Active); Assert.Empty(samples.Voices);
        clock.Milliseconds = 1500; playback.Pump(); Assert.NotEmpty(midi.Active); Assert.NotEmpty(samples.Voices);
        playback.Pause(true); Assert.Empty(midi.Active); Assert.Empty(samples.Voices); playback.Pause(false); playback.Pump(); Assert.NotEmpty(midi.Active);
        playback.Seek(0); playback.Stop(); Assert.Empty(midi.Active); Assert.Empty(samples.Voices);
    }
    [Fact]
    public void ManualTempoMeterAndPreviewChangesSurviveProtectedRemap()
    {
        var source = new MidiFixture().Note(60, 0, 480).Read(); var options = MidiFixture.Options(source); var p = MidiProjectConverter.Create(SapmMapper.Convert(source, options));
        var s = new EditorSession(p); s.UpdateTrack(s.ActiveTrack with { PreviewInstrument = "Harp", PreviewSemitoneOffset = 3 });
        s.ReplaceProject(s.Project with { Tempos = new() { new(0, 750000) }, Meters = new() { new(0, 3, 4) } });
        var raw = MidiProjectConverter.Remap(s.Project, options); var protectedPlan = s.PreviewRemap(raw);
        Assert.Equal(750000, protectedPlan.Project.Tempos[0].MicrosecondsPerBeat); Assert.Equal(3, protectedPlan.Project.Meters[0].Numerator);
        Assert.Equal("Harp", protectedPlan.Project.Tracks[0].PreviewInstrument); Assert.Equal(3, protectedPlan.Project.Tracks[0].PreviewSemitoneOffset);
        Assert.Equal(500000, s.PreviewRemap(raw, false).Project.Tempos[0].MicrosecondsPerBeat);
    }
    [Fact]
    public void GridChoiceSurvivesUndoAndRedoOfContent()
    {
        var s = new EditorSession(TestProject.SingleTap()); s.Create(2, 2); s.SetGrid(3); s.Undo(); Assert.Equal(3, s.Project.GridSubdiv); s.Redo(); Assert.Equal(3, s.Project.GridSubdiv);
    }
    [Fact]
    public void ClipboardKeepsFractionalTimingAndOriginButCopiesAreNotRemapTargets()
    {
        var source = new MidiFixture().Note(60, 7, 493).Read(); var s = new EditorSession(MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source))));
        var n = s.ActiveTrack.Notes[0]; s.Select(new[] { n.Id }); s.Copy(); s.Paste(3.125m); var copy = Assert.Single(s.SelectedNotes());
        Assert.Equal(n.LengthBeats, copy.LengthBeats); Assert.Equal(n.MidiOrigin, copy.MidiOrigin); Assert.Null(copy.SourceMidiNoteId); Assert.Equal(3.125m, copy.Beat);
    }
    [Theory]
    [InlineData("Dundun", 8)]
    [InlineData("Handpan", 8)]
    [InlineData("Piano", 15)]
    public void TargetKeyCountConstrainsCreationAndPaste(string instrument, int count)
    {
        var p = TestProject.SingleTap(); p = p with { Tracks = new() { p.Tracks[0] with { Notes = new(), GameTarget = GamePitchProfiles.Get(instrument).Target() } } };
        var s = new EditorSession(p); Assert.Equal(count, s.KeyCount); s.Create(count - 1, 0); Assert.ThrowsAny<Exception>(() => s.Create(count, 0));
        Assert.Single(s.ActiveTrack.Notes);
    }
    [Fact]
    public void QuantizationIsExplicitUndoableAndDoesNotChangeTapAction()
    {
        var s = new EditorSession(TestProject.SingleTap()); var n = s.ActiveTrack.Notes[0]; s.PutNotes(new[] { n with { Beat = .13m } }); s.Select(new[] { n.Id }); s.QuantizeSelected();
        Assert.Equal(.25m, s.Find(n.Id)!.Beat); Assert.Equal(NoteAction.Tap, s.Find(n.Id)!.Action); s.Undo(); Assert.Equal(.13m, s.Find(n.Id)!.Beat);
    }
    [Fact]
    public void VeryLongHoldUsesAnIntervalInsteadOfGridAllocation()
    {
        var p = TestProject.Sample(); var n = p.Tracks[0].Notes[1] with { LengthBeats = 100000000000000000m };
        var s = new EditorSession(p); s.PutNotes(new[] { n }); var index = new NoteViewportIndex(s.ActiveTrack.Notes);
        Assert.Contains(n, index.Query(10000000000000000m, 10000000000000001m)); Assert.InRange(index.LastVisitedNodes, 1, 10);
    }
    [Fact]
    public void NewInterfaceStringsHaveFourTranslationsAndValidFormats()
    {
        _ = Lang.S("ed.title"); var entries = (Dictionary<string, string[]>)typeof(Lang).GetField("T", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var editor = entries.Where(e => e.Key.StartsWith("ed.", StringComparison.Ordinal)).ToArray(); Assert.True(editor.Length > 80);
        foreach (var entry in editor)
        {
            Assert.Equal(4, entry.Value.Length);
            foreach (string text in entry.Value) { Assert.False(string.IsNullOrWhiteSpace(text)); Assert.DoesNotContain("???", text); _ = string.Format(text, Enumerable.Repeat<object>(1, 10).ToArray()); }
        }
    }
    [Fact]
    public void NewManualTrackAndOriginalMidiBytesSurviveSaveAndRemapRoundTrip()
    {
        using var files = new TestProject(); var source = new MidiFixture().Note(60, 0, 480).Read(); var options = MidiFixture.Options(source);
        var s = new EditorSession(MidiProjectConverter.Create(SapmMapper.Convert(source, options))); Guid added = s.AddTrack("Manual"); s.Create(4, 8);
        var raw = MidiProjectConverter.Remap(s.Project, options); s.ReplaceProject(s.PreviewRemap(raw).Project); string path = files.PathFor("edited.smap"); SmapFile.Save(s.Project, path);
        var saved = SmapFile.Load(path); Assert.Contains(saved.Tracks, t => t.Id == added && t.Notes.Count == 1); Assert.Equal(source.Bytes, saved.OriginalMidi);
    }
    [Fact]
    public void InvalidPatchesLeaveContentHistoryAndRedoIntact()
    {
        var s = new EditorSession(TestProject.SingleTap()); var n = s.ActiveTrack.Notes[0]; s.Create(2, 2); s.Undo(); string identity = PerformanceIdentity.ForProject(s.Project); int history = s.UndoCount;
        Assert.ThrowsAny<Exception>(() => s.PutNotes(new[] { n, n }));
        Assert.ThrowsAny<Exception>(() => s.PutNotes(new[] { n with { SourceMidiNoteId = Guid.NewGuid() } }));
        Assert.ThrowsAny<Exception>(() => s.PutNotes(new[] { n with { Beat = -1 } }));
        Assert.ThrowsAny<Exception>(() => s.PutNotes(new[] { n with { TrackId = Guid.NewGuid() } }));
        Assert.Equal(identity, PerformanceIdentity.ForProject(s.Project)); Assert.Equal(history, s.UndoCount); Assert.True(s.CanRedo); s.Redo(); Assert.Equal(2, s.ActiveTrack.Notes.Count);
    }
    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("LPT9.mid", "_LPT9.mid")]
    [InlineData("a:b?c.", "abc")]
    public void SaveDialogReusesNativeFileNameRules(string name, string expected) => Assert.Equal(expected, SongLibrary.NativeFileStem(name));
}
