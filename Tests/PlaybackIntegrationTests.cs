using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[CollectionDefinition("Playback library", DisableParallelization = true)]
public sealed class PlaybackLibraryCollection;

[Collection("Playback library")]
public class PlaybackIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public void NativeLibraryScanAndPlaybackRetainMultiTrackHoldEnds()
    {
        using var files = new TestProject(); string path = files.PathFor("native.smap"); var project = TestProject.Sample();
        SmapFile.Save(project, path); File.WriteAllText(files.PathFor("corrupted.smap"), "invalid");
        string originalDirectory = SongLibrary.SongsDir;
        try
        {
            SongLibrary.SongsDir = files.DirectoryPath; var info = Assert.Single(SongLibrary.Scan());
            Assert.Equal(project.Metadata.Name, info.Name); Assert.Equal(3, info.NoteCount); Assert.Equal(3000, info.DurationMs);
            byte[] before = SHA256.HashData(File.ReadAllBytes(path));
            var sequence = PlaybackSequence.FromProject(SmapFile.Load(info.File));
            var clock = new ManualClock(); var input = new RecordingInput(clock); var game = new GameNoteOutput(input, SkyPlayer.DefaultVk);
            var session = new PlaybackSession(sequence, clock, new[] { game }); session.Pump();
            clock.Milliseconds = 18; session.Pump(); Assert.Empty(input.Pressed);
            clock.Milliseconds = 1500; session.Pump(); Assert.Single(input.Pressed);
            clock.Milliseconds = 2000; session.Pump(); Assert.Single(input.Pressed);
            clock.Milliseconds = 3000; session.Pump(); Assert.True(session.Completed); Assert.Empty(input.Pressed);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        }
        finally { SongLibrary.SongsDir = originalDirectory; }
    }

    [Fact]
    public void EveryReadableLegacySongKeepsTapSemanticsInPlaybackCompiler()
    {
        string directory = Path.Combine(TestProject.FindRepository(), "曲谱库"); int readable = 0, unreadable = 0, count = 0;
        foreach (string path in Directory.GetFiles(directory).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".json" or ".txt"))
        {
            SongDocument document;
            try { document = SongLibrary.LoadDocument(new SongInfo { File = path }); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
            { unreadable++; continue; }
            var sequence = PlaybackSequence.FromProject(LegacyProjectAdapter.FromDocument(document), useTrackPreview: false);
            Assert.Equal(document.Notes.Count, sequence.Notes.Count); Assert.Empty(sequence.Preview);
            var expected = document.Notes.OrderBy(n => n.Beat).ThenBy(n => n.Key).ToArray();
            for (int i = 0; i < expected.Length; i++)
            {
                var actual = sequence.Notes[i]; Assert.Equal(NoteAction.Tap, actual.Action); Assert.Equal(0, actual.LengthMicroseconds);
                Assert.Equal(expected[i].Key, actual.Key);
                Assert.InRange(Math.Abs((double)(actual.StartMicroseconds / 1000) - expected[i].Beat * document.MsPerBeat), 0, .000001);
            }
            readable++; count += sequence.Notes.Count;
        }
        output.WriteLine($"PLAYBACK_COMPAT readable={readable}; notes={count}; known_unreadable={unreadable}");
        Assert.Equal(1576, readable); Assert.Equal(819975, count); Assert.Equal(15, unreadable);
    }
}
