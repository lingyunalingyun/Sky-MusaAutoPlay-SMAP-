using System.IO;
using System.Text.Json.Nodes;

namespace SMAP_WPF.Core;

public static class LegacyProjectAdapter
{
    public static SmapProject Load(string path, int? songIndex = null)
    {
        if (new FileInfo(path).Length > SmapFile.MaxManifestBytes) throw new InvalidDataException("旧曲谱文件过大。");
        var node = JsonNode.Parse(SongLibrary.ReadText(path));
        JsonObject root;
        if (node is JsonArray array)
        {
            if (array.Count != 1 && songIndex == null)
                throw new InvalidOperationException("文件包含多个曲谱，请明确选择曲谱索引。");
            int index = songIndex ?? 0;
            if (index < 0 || index >= array.Count) throw new ArgumentOutOfRangeException(nameof(songIndex));
            root = array[index] as JsonObject ?? throw new InvalidDataException("旧曲谱对象无效。");
        }
        else
        {
            if (songIndex is not (null or 0)) throw new ArgumentOutOfRangeException(nameof(songIndex));
            root = node as JsonObject ?? throw new InvalidDataException("旧曲谱对象无效。");
        }
        return FromDocument(SongLibrary.LoadDocument(root, path));
    }

    public static SmapProject FromDocument(SongDocument document)
    {
        if (!double.IsFinite(document.MsPerBeat) || document.MsPerBeat <= 0)
            throw new InvalidDataException("旧曲谱拍长无效。");
        var trackId = Guid.NewGuid();
        var fields = document.Root.DeepClone().AsObject();
        fields.Remove("songNotes");
        var project = new SmapProject
        {
            Metadata = new ProjectMetadata
            {
                Name = document.Name, Author = document.Author, TranscribedBy = document.TranscribedBy,
                Cover = fields["cover"] is JsonValue cover && cover.TryGetValue<string>(out var text) ? text : null
            },
            Tempos = new() { new(0, (decimal)document.MsPerBeat * 1000) },
            Tracks = new()
            {
                new ProjectTrack
                {
                    Id = trackId, Name = document.Name, LegacyFields = fields,
                    Notes = document.Notes.Select(n => new ProjectNote
                    {
                        TrackId = trackId, Key = n.Key, Beat = (decimal)n.Beat,
                        Action = NoteAction.Tap, LengthBeats = 0
                    }).ToList()
                }
            }
        };
        ProjectValidation.Validate(project);
        return project;
    }

    // 仅导出旧格式能准确表达的演奏数据；多轨必须由调用方显式指定单轨。
    public static SongDocument ToDocument(SmapProject project, Guid? trackId = null)
    {
        ProjectValidation.Validate(project);
        if (trackId == null && project.Tracks.Count != 1)
            throw new InvalidOperationException("旧格式不支持多轨，请明确选择单轨导出。");
        var track = trackId is { } id ? project.Tracks.SingleOrDefault(t => t.Id == id) : project.Tracks[0];
        if (track == null) throw new ArgumentException("音轨不存在。", nameof(trackId));
        if (track.Notes.Any(n => n.Action != NoteAction.Tap))
            throw new InvalidOperationException("旧格式不支持长按，不能丢弃持续时间。");
        if (project.Tempos.Count != 1 || project.Meters.Count != 1
            || project.Meters[0].Numerator != 4 || project.Meters[0].Denominator != 4 || track.GameTarget != null
            || track.Capability!=null || track.KeyDomain!=NoteKeyDomain.LegacyOrdinal)
            throw new InvalidOperationException("旧格式无法保留变速、拍号或目标游戏音高配置。");
        var timeline = new ProjectTimeline(project.Tempos);
        foreach (var note in track.Notes)
        {
            decimal ms = timeline.BeatToMicroseconds(note.Beat) / 1000;
            if (Math.Abs(ms - decimal.Round(ms)) > 0.000001m || ms > long.MaxValue)
                throw new InvalidOperationException("旧格式无法精确保留音符时间，请使用 .smap。");
        }
        decimal bpm = 60_000_000m / project.Tempos[0].MicrosecondsPerBeat;
        if (bpm < 1 || bpm > int.MaxValue) throw new InvalidOperationException("旧格式无法表达此 BPM。");
        var document = new SongDocument
        {
            Root = track.LegacyFields?.DeepClone().AsObject() ?? new JsonObject(),
            Name = project.Metadata.Name, Author = project.Metadata.Author, TranscribedBy = project.Metadata.TranscribedBy,
            MsPerBeat = (double)(project.Tempos[0].MicrosecondsPerBeat / 1000), Bpm = (int)decimal.Round(bpm)
        };
        if (project.Metadata.Cover is { } cover) document.Root["cover"] = cover;
        document.Notes.AddRange(track.Notes.Select(n => new Note { Key = n.Key, Beat = (double)n.Beat }));
        document.TotalBeats = Math.Max(16, Math.Ceiling(document.Notes.Select(n => n.Beat).DefaultIfEmpty().Max()) + 4);
        return document;
    }

    public static void SaveLegacy(SmapProject project, string path, Guid? trackId = null)
    {
        var document = ToDocument(project, trackId);
        SongLibrary.Save(document, document.Notes, path);
    }
}
