using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class SmapFileTests
{
    [Fact]
    public void EmbeddedArchivesAreReproducibleAndLoadWithoutLosingData()
    {
        var p = TestProject.Sample(); byte[] first = SmapFile.ToBytes(p), second = SmapFile.ToBytes(SmapFile.FromBytes(first));
        Assert.Equal(first, second);
        using var archive = new ZipArchive(new MemoryStream(first));
        Assert.All(archive.Entries, e => Assert.Equal(new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero).DateTime, e.LastWriteTime.DateTime));
        Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(SmapFile.FromBytes(second)));
    }
    [Fact]
    public void RepeatedRoundTripsPreserveIdsNotesMetadataMidiAndHash()
    {
        using var files = new TestProject(); var p = TestProject.Sample() with
        {
            OriginalMidi = new byte[] { 77, 84, 104, 100, 0, 0, 0, 6, 0, 1, 0, 0, 1, 224 }, MidiTicksPerQuarterNote = 480,
            Metadata = new() { Name = "多轨🎵", Cover = "保留封面", Sources = new()
                { new(SourceKind.ExternalLink, "原谱声明", "https://example.com/sheet") } }
        };
        string hash = PerformanceIdentity.ForProject(p); var original = p;
        for (int i = 0; i < 8; i++)
        {
            string path = files.PathFor($"round-{i}.smap"); SmapFile.Save(p, path); p = SmapFile.Load(path);
            Assert.Equal(hash, PerformanceIdentity.ForProject(p)); Assert.Equal(original.ProjectId, p.ProjectId);
            Assert.Equal(original.Tracks[0].Id, p.Tracks[0].Id); Assert.Equal(original.Tracks[0].Notes, p.Tracks[0].Notes);
            Assert.Equal(original.OriginalMidi, p.OriginalMidi); Assert.Equal(original.Metadata.Name, p.Metadata.Name);
            Assert.Equal(original.Metadata.Sources, p.Metadata.Sources); Assert.Equal(original.Metadata.Cover, p.Metadata.Cover);
            Assert.Equal(-12, p.Tracks[0].PreviewSemitoneOffset);
        }
        using var archive = ZipFile.OpenRead(files.PathFor("round-7.smap"));
        Assert.Equal(new[] { "project.json", "source.mid" }, archive.Entries.Select(e => e.FullName));
    }

    [Theory]
    [InlineData("missingAction")] [InlineData("missingId")] [InlineData("missingTracks")]
    [InlineData("futureVersion")] [InlineData("unknownField")] [InlineData("nullNotes")]
    [InlineData("numericAction")] [InlineData("unknownAction")] [InlineData("missingVersion")]
    [InlineData("missingTempoBeat")] [InlineData("missingMeterBeat")] [InlineData("nullTempo")]
    [InlineData("nullTrack")] [InlineData("nullNote")]
    public void RejectsMalformedOrUnknownManifest(string problem)
    {
        using var files = new TestProject(); var root = TestProject.Manifest(TestProject.Sample(), files);
        var note = root["tracks"]![0]!["notes"]![0]!;
        switch (problem)
        {
            case "missingAction": note.AsObject().Remove("action"); break;
            case "missingId": note.AsObject().Remove("id"); break;
            case "missingTracks": root.Remove("tracks"); break;
            case "missingVersion": root.Remove("formatVersion"); break;
            case "futureVersion": root["formatVersion"] = 2; break;
            case "unknownField": root["newData"] = true; break;
            case "nullNotes": root["tracks"]![0]!["notes"] = null; break;
            case "numericAction": note["action"] = 0; break;
            case "unknownAction": note["action"] = "Unknown"; break;
            case "missingTempoBeat": root["tempos"]![0]!.AsObject().Remove("beat"); break;
            case "missingMeterBeat": root["meters"]![0]!.AsObject().Remove("beat"); break;
            case "nullTempo": root["tempos"]![0] = null; break;
            case "nullTrack": root["tracks"]![0] = null; break;
            case "nullNote": root["tracks"]![0]!["notes"]![0] = null; break;
        }
        string path = files.PathFor("bad.smap"); TestProject.Archive(path, ("project.json", Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
    }

    [Theory]
    [InlineData("../escape.txt")] [InlineData("/project.json")] [InlineData("folder/project.json")]
    [InlineData("PROJECT.JSON")] [InlineData("source.mid")]
    public void RejectsUnsafeOrMissingArchivePathsWithoutExtracting(string name)
    {
        using var files = new TestProject(); string path = files.PathFor("bad-path.smap");
        TestProject.Archive(path, (name, "{}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
        Assert.Single(Directory.GetFiles(files.DirectoryPath));
    }

    [Fact]
    public void RejectsDuplicateZipEntriesAndDuplicateJsonProperties()
    {
        using var files = new TestProject(); string path = files.PathFor("duplicates.smap");
        TestProject.Archive(path, ("project.json", "{}"u8.ToArray()), ("project.json", "{}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
        path = files.PathFor("duplicate-json.smap");
        TestProject.Archive(path, ("project.json", "{\"formatVersion\":1,\"formatVersion\":2}"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
    }

    [Fact]
    public void RejectsOversizedDecompression()
    {
        using var files = new TestProject(); string path = files.PathFor("oversized.smap");
        TestProject.Archive(path, ("project.json", new byte[SmapFile.MaxManifestBytes + 1]));
        Assert.True(new FileInfo(path).Length < 100_000);
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
    }

    [Fact]
    public void FailedSavePreservesExistingFileAndLeavesNoTemporaryFiles()
    {
        using var files = new TestProject(); string path = files.PathFor("existing.smap");
        SmapFile.Save(TestProject.Sample(), path); byte[] before = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => SmapFile.Save(TestProject.Sample() with { FormatVersion = 99 }, path));
        Assert.Equal(before, File.ReadAllBytes(path));
        var large = TestProject.Sample() with { Metadata = new() { Description = new string('x', SmapFile.MaxManifestBytes) } };
        Assert.Throws<InvalidDataException>(() => SmapFile.Save(large, path));
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(files.DirectoryPath));
    }

    [Fact]
    public void SuccessfulSaveReplacesExistingProject()
    {
        using var files = new TestProject(); string path = files.PathFor("replace.smap"); var p = TestProject.Sample();
        SmapFile.Save(p, path); SmapFile.Save(p with { Metadata = new() { Name = "替换" } }, path);
        Assert.Equal("替换", SmapFile.Load(path).Metadata.Name); Assert.Single(Directory.GetFiles(files.DirectoryPath));
    }

    [Fact]
    public void LockedDestinationPreservesExistingFileAndCleansTemporaryArchive()
    {
        using var files = new TestProject(); string path = files.PathFor("locked.smap"); var p = TestProject.Sample();
        SmapFile.Save(p, path); byte[] before = File.ReadAllBytes(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Record.Exception(() => SmapFile.Save(p with { Metadata = new() { Name = "不能覆盖" } }, path));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        }
        Assert.Equal(before, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(files.DirectoryPath));
    }

    [Fact]
    public void RejectsInvalidMidiPayload()
    {
        using var files = new TestProject();
        Assert.Throws<InvalidDataException>(() => SmapFile.Save(TestProject.Sample() with { OriginalMidi = new byte[14] }, files.PathFor("bad-midi.smap")));
        var root = TestProject.Manifest(TestProject.Sample(), files); string path = files.PathFor("bad-mid-load.smap");
        TestProject.Archive(path, ("project.json", Encoding.UTF8.GetBytes(root.ToJsonString())), ("source.mid", new byte[14]));
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
    }
}
