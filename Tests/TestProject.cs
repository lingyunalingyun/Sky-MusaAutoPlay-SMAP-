using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SMAP_WPF.Core;

namespace SMAP.Tests;

internal sealed class TestProject : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(AppContext.BaseDirectory, "TestRuns", Guid.NewGuid().ToString("N"));
    public TestProject() => Directory.CreateDirectory(DirectoryPath);
    public string PathFor(string name) => Path.Combine(DirectoryPath, name);
    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

    public static SmapProject Sample()
    {
        var first = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("10000000-0000-0000-0000-000000000002");
        return new SmapProject
        {
            ProjectId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Metadata = new() { Name = "测试曲", Author = "作者", TranscribedBy = "谱者" },
            Tempos = new() { new(0, 500_000), new(4, 1_000_000) },
            Meters = new() { new(0, 4, 4), new(8, 3, 4) },
            Tracks = new()
            {
                new()
                {
                    Id = first, Name = "旋律", PreviewInstrument = "Flute", PreviewSemitoneOffset = -12,
                    Notes = new()
                    {
                        new() { Id = Guid.Parse("30000000-0000-0000-0000-000000000001"), TrackId = first, Key = 14, Beat = 0, Action = NoteAction.Tap },
                        new() { Id = Guid.Parse("30000000-0000-0000-0000-000000000002"), TrackId = first, Key = 2, Beat = 3, LengthBeats = 2, Action = NoteAction.Hold }
                    }
                },
                new()
                {
                    Id = second, Name = "伴奏", Notes = new()
                    {
                        new() { Id = Guid.Parse("30000000-0000-0000-0000-000000000003"), TrackId = second, Key = 2, Beat = 3, LengthBeats = 1, Action = NoteAction.Hold }
                    }
                }
            }
        };
    }

    public static SmapProject SingleTap() => Sample() with
    {
        Tempos = new() { new(0, 500_000) }, Meters = new() { new(0, 4, 4) },
        Tracks = new() { Sample().Tracks[0] with { Notes = new() { Sample().Tracks[0].Notes[0] } } }
    };

    public static SmapProject ChangeNote(SmapProject project, Func<ProjectNote, ProjectNote> change) => project with
    {
        Tracks = new() { project.Tracks[0] with { Notes = new List<ProjectNote> { change(project.Tracks[0].Notes[0]) }.Concat(project.Tracks[0].Notes.Skip(1)).ToList() } }
    };

    public static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SMAP-WPF.csproj"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到测试仓库。");
    }

    public static JsonObject Manifest(SmapProject project, TestProject files)
    {
        string path = files.PathFor("manifest-source.smap"); SmapFile.Save(project, path);
        using var zip = ZipFile.OpenRead(path);
        using var input = new StreamReader(zip.GetEntry("project.json")!.Open(), Encoding.UTF8);
        return JsonNode.Parse(input.ReadToEnd())!.AsObject();
    }

    public static void Archive(string path, params (string name, byte[] content)[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var output = zip.CreateEntry(name).Open(); output.Write(content);
        }
    }
}
