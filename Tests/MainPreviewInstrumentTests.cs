using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class MainPreviewInstrumentTests
{
    [Fact]public void LegacyPlaybackUsesCurrentPreviewInstrumentInsteadOfAdapterPiano()
    {
        var project=TestProject.SingleTap();var sequence=PlaybackSequence.FromProject(project,useTrackPreview:false);var voices=new FakeAudioVoices();
        var output=MainWindow.CreatePreviewOutput(sequence,project,voices);
        output.Apply(new[]{new NoteOutputEvent(sequence.Notes[0],true)});
        Assert.Null(Assert.Single(voices.Settings));output.StopAll();Assert.Empty(voices.Voices);
    }
    [Fact]public void NativePlaybackRetainsIndependentTrackPreviewAndOriginalIdentity()
    {
        var project=TestProject.SingleTap();project=project with{Tracks=new(){project.Tracks[0] with{PreviewInstrument="Flute",PreviewSemitoneOffset=-12}}};
        string identity=PerformanceIdentity.ForProject(project);var sequence=PlaybackSequence.FromProject(project);var voices=new FakeAudioVoices();
        var output=MainWindow.CreatePreviewOutput(sequence,project,voices);
        output.Apply(new[]{new NoteOutputEvent(sequence.Notes[0],true)});Assert.Equal(new PreviewSettings("Flute",-12),Assert.Single(voices.Settings));
        output.StopAll();Assert.Equal(identity,PerformanceIdentity.ForProject(project));Assert.Equal("Flute",project.Tracks[0].PreviewInstrument);
    }
}
