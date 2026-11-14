using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

public class LegacyCompatibilityTests(ITestOutputHelper output)
{
    const string Legacy = "{\"name\":\"旧曲谱\",\"author\":\"作者\",\"transcribedBy\":\"谱者\",\"bpm\":120,\"pitchLevel\":3,\"cover\":\"YWJj\",\"custom\":{\"keep\":true},\"songNotes\":[{\"time\":0,\"key\":\"1Key14\"},{\"time\":500,\"key\":2},{\"time\":500,\"key\":\"1Key0\"}]}";

    [Theory]
    [InlineData("utf8", false)] [InlineData("utf8bom", true)] [InlineData("utf16le", true)] [InlineData("utf16be", false)]
    public void PreservesEncodingRootVariantsMetadataKeysAndTapSemantics(string encoding, bool array)
    {
        using var files = new TestProject(); string path = files.PathFor("legacy.txt");
        Encoding enc = encoding switch { "utf8" => new UTF8Encoding(false), "utf8bom" => new UTF8Encoding(true),
            "utf16le" => new UnicodeEncoding(false, true), _ => new UnicodeEncoding(true, true) };
        File.WriteAllText(path, array ? $"[{Legacy}]" : Legacy, enc); byte[] before = File.ReadAllBytes(path);
        var project = LegacyProjectAdapter.Load(path);
        Assert.All(project.Tracks[0].Notes, n => { Assert.Equal(NoteAction.Tap, n.Action); Assert.Equal(0, n.LengthBeats); });
        Assert.Contains(project.Tracks[0].Notes, n => n.Key == 14); Assert.Equal(3, project.Tracks[0].Notes.Count);
        project = project with { GridSubdiv = 12 }; Assert.All(project.Tracks[0].Notes, n => Assert.Equal(NoteAction.Tap, n.Action));
        string native = files.PathFor("saved.smap"); SmapFile.Save(project, native); project = SmapFile.Load(native);
        string exported = files.PathFor("exported.txt"); LegacyProjectAdapter.SaveLegacy(project, exported);
        var root = JsonNode.Parse(File.ReadAllText(exported))![0]!;
        Assert.True(root["custom"]!["keep"]!.GetValue<bool>()); Assert.Equal(3, root["pitchLevel"]!.GetValue<int>());
        Assert.Equal("YWJj", root["cover"]!.GetValue<string>()); Assert.Equal("作者", root["author"]!.GetValue<string>());
        Assert.Equal(before, File.ReadAllBytes(path));
        var old = SongLibrary.LoadDocument(new SongInfo { File = exported });
        Assert.Equal(3, old.Notes.Count); Assert.Contains(old.Notes, n => n.Key == 14);
    }

    [Theory]
    [InlineData("hold")] [InlineData("multi")] [InlineData("tempo")] [InlineData("meter")]
    [InlineData("target")] [InlineData("fractionalTime")]
    public void RefusesLossyLegacyExportWithoutWritingFile(string reason)
    {
        using var files = new TestProject(); var p = TestProject.SingleTap();
        p = reason switch
        {
            "hold" => TestProject.ChangeNote(p, n => n with { Action = NoteAction.Hold, LengthBeats = 2 }),
            "multi" => p with { Tracks = TestProject.Sample().Tracks.Select(t => t with { Notes = new() }).ToList() },
            "tempo" => p with { Tempos = TestProject.Sample().Tempos },
            "meter" => p with { Meters = new() { new(0, 3, 4) } },
            "target" => p with { Tracks = new() { p.Tracks[0] with { GameTarget = new() { InstrumentId = "Flute" } } } },
            "fractionalTime" => TestProject.ChangeNote(p, n => n with { Beat = 0.000001m }),
            _ => throw new ArgumentException(reason)
        };
        string path = files.PathFor("must-stay.txt"); File.WriteAllText(path, "保留");
        Assert.Throws<InvalidOperationException>(() => LegacyProjectAdapter.SaveLegacy(p, path));
        Assert.Equal("保留", File.ReadAllText(path));
    }

    [Fact]
    public void SupportsExplicitSingleTrackExport()
    {
        var p = TestProject.Sample() with { Tempos = new() { new(0, 500000) }, Meters = new() { new(0, 4, 4) },
            Tracks = TestProject.Sample().Tracks.Select(t => t with { Notes = t.Notes.Select(n => n with { Action = NoteAction.Tap, LengthBeats = 0 }).ToList() }).ToList() };
        var document = LegacyProjectAdapter.ToDocument(p, p.Tracks[1].Id);
        Assert.Single(document.Notes); Assert.Equal(2, document.Notes[0].Key);
    }

    [Fact]
    public void CollectionRequiresExplicitSongSelection()
    {
        using var files = new TestProject(); string path = files.PathFor("pack.txt");
        File.WriteAllText(path, $"[{Legacy},{Legacy.Replace("旧曲谱", "第二曲")}]", new UTF8Encoding(false));
        Assert.Throws<InvalidOperationException>(() => LegacyProjectAdapter.Load(path));
        Assert.Equal("第二曲", LegacyProjectAdapter.Load(path, 1).Metadata.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => LegacyProjectAdapter.Load(path, 2));
    }

    [Fact]
    public void RepositoryCollectionsAllowEverySongToBeSelectedWithoutMerging()
    {
        string directory = Path.Combine(TestProject.FindRepository(), "曲谱库"); int collections = 0, songs = 0;
        foreach (string path in Directory.GetFiles(directory))
        {
            JsonArray? array;
            try
            {
                using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                array = JsonNode.Parse(reader.ReadToEnd()) as JsonArray;
            }
            catch (JsonException) { continue; }
            if (array == null || array.Count <= 1) continue;
            collections++; Assert.Throws<InvalidOperationException>(() => LegacyProjectAdapter.Load(path));
            for (int i = 0; i < array.Count; i++)
            {
                var p = LegacyProjectAdapter.Load(path, i); songs++;
                Assert.Single(p.Tracks); Assert.Equal(array[i]!["name"]!.GetValue<string>(), p.Metadata.Name);
                Assert.Equal(array[i]!["songNotes"]!.AsArray().Count, p.Tracks[0].Notes.Count);
            }
        }
        output.WriteLine($"Collections: files={collections}; individually selected songs={songs}");
        Assert.True(collections > 0);
    }

    [Fact]
    public void RepositoryCorpusPreservesEveryReadableV22FirstSongThroughNativeRoundTrip()
    {
        using var files = new TestProject(); string directory = Path.Combine(TestProject.FindRepository(), "曲谱库");
        var paths = Directory.GetFiles(directory).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".txt" or ".json").ToArray();
        Assert.NotEmpty(paths); int readable = 0, count = 0; var failures = new List<string>();
        foreach (string path in paths)
        {
            SongDocument old;
            try { old = SongLibrary.LoadDocument(new SongInfo { File = path }); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or NullReferenceException)
            { failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}"); continue; }
            byte[] before = SHA256.HashData(File.ReadAllBytes(path));
            var project = LegacyProjectAdapter.FromDocument(old);
            string native = files.PathFor("corpus.smap"); SmapFile.Save(project, native); var restored = SmapFile.Load(native);
            Assert.Equal(old.Notes.Count, restored.Tracks[0].Notes.Count);
            Assert.Equal(old.Name, restored.Metadata.Name); Assert.Equal(old.Author, restored.Metadata.Author);
            var times = new ProjectTimeline(restored.Tempos).GetNotes(restored.Tracks[0]).ToArray();
            var expected = old.Notes.OrderBy(n => n.Beat).ThenBy(n => n.Key).ToArray();
            for (int i = 0; i < times.Length; i++)
            {
                Assert.Equal(expected[i].Key, times[i].Key); Assert.Equal(NoteAction.Tap, times[i].Action);
                Assert.InRange(Math.Abs((double)(times[i].StartMicroseconds / 1000) - expected[i].Beat * old.MsPerBeat), 0, 0.000001);
            }
            string exported = files.PathFor("corpus.txt"); LegacyProjectAdapter.SaveLegacy(restored, exported);
            using var exportedJson = JsonDocument.Parse(File.ReadAllText(exported));
            var exportedNotes = exportedJson.RootElement[0].GetProperty("songNotes").EnumerateArray().ToArray();
            var expectedTimes = old.Notes.OrderBy(n => n.Beat).Select(n => (long)Math.Round(n.Beat * old.MsPerBeat)).ToArray();
            Assert.Equal(expectedTimes, exportedNotes.Select(n => n.GetProperty("time").GetInt64()));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
            readable++; count += old.Notes.Count;
        }
        output.WriteLine($"Corpus: files={paths.Length}; readable={readable}; notes={count}; pre-existing unreadable={failures.Count}");
        foreach (string failure in failures) output.WriteLine(failure);
        Assert.Equal(paths.Length, readable + failures.Count);
        string[] expectedUnreadable =
        {
            "null.txt", "old money.txt", "《悦神》何以解忧.txt", "《撒野》-何以解忧.txt", "伯虎说.txt",
            "你离开的事实《The Truth That You Le.txt", "光遇本本.txt", "奕的扒谱2（稻香).txt", "开心往前飞.txt",
            "慢慢喜欢你 安迪Zoey.txt", "所念皆星河（修）.txt", "明明就(1).txt", "星辰大海(慢).txt", "是但求其爱..txt",
            "犬夜叉《穿越时空的思念》FutariNo.txt"
        };
        Assert.Equal(expectedUnreadable.OrderBy(s => s, StringComparer.Ordinal),
            failures.Select(f => f[..f.LastIndexOf(": ", StringComparison.Ordinal)]).OrderBy(s => s, StringComparer.Ordinal));
    }
}
