using System.IO;

namespace SMAP_WPF.Core;

public static class ProjectValidation
{
    public const int MaxTracks = 128;
    public const int MaxNotes = 1_000_000;
    public const int MaxTimingPoints = 10_000;

    public static void Validate(SmapProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Require(project.FormatVersion is SmapProject.CurrentFormatVersion or SmapProject.CapabilityFormatVersion, "不支持的工程格式版本。");
        Require(project.FormatVersion == 1 ? project.KeyInterpretationVersion == 0 : project.KeyInterpretationVersion == 1, "UnsupportedKeyInterpretationVersion");
        Require(project.ProjectId != Guid.Empty, "工程 ID 不能为空。");
        Require(project.Metadata != null && project.Metadata.Sources != null, "工程元信息不能为空。");
        Require(project.Metadata!.Name != null && project.Metadata.Author != null
            && project.Metadata.TranscribedBy != null && project.Metadata.Description != null, "工程文本信息不能为 null。");
        Require(project.Tracks != null && project.Tracks.Count is > 0 and <= MaxTracks, "音轨数量超出范围。");
        Require(project.GridSubdiv > 0, "网格细分必须为正数。");
        Require(project.MidiTicksPerQuarterNote is null or > 0, "MIDI 分辨率无效。");
        Require(project.Tempos != null && project.Tempos.Count is > 0 and <= MaxTimingPoints, "速度事件数量无效。");
        Require(project.Meters != null && project.Meters.Count is > 0 and <= MaxTimingPoints, "拍号事件数量无效。");
        var tempoBeats = new HashSet<decimal>();
        foreach (var point in project.Tempos!)
        {
            Require(point != null && point.Beat >= 0 && point.MicrosecondsPerBeat > 0, "速度事件无效。");
            Require(tempoBeats.Add(point!.Beat), "同一拍位存在多个速度事件。");
        }
        Require(tempoBeats.Contains(0), "速度事件必须从第零拍开始。");
        var meterBeats = new HashSet<decimal>();
        foreach (var point in project.Meters!)
        {
            Require(point != null && point.Beat >= 0 && point.Numerator > 0 && point.Denominator > 0
                && (point.Denominator & (point.Denominator - 1)) == 0, "拍号事件无效。");
            Require(meterBeats.Add(point!.Beat), "同一拍位存在多个拍号事件。");
        }
        Require(meterBeats.Contains(0), "拍号事件必须从第零拍开始。");
        var trackIds = new HashSet<Guid>();
        var noteIds = new HashSet<Guid>();
        int noteCount = 0;
        foreach (var track in project.Tracks!)
        {
            Require(track != null && track.Id != Guid.Empty && trackIds.Add(track.Id), "音轨 ID 无效或重复。");
            Require(track!.Notes != null, "音符列表不能为空。");
            Require(track.Notes!.Count <= MaxNotes - noteCount, "音符数量超出范围。");
            noteCount += track.Notes.Count;
            Require(Enum.IsDefined(track.KeyDomain), "InvalidKeyDomain");
            Require(project.FormatVersion >= 2 || track.Capability == null && track.KeyDomain == NoteKeyDomain.LegacyOrdinal, "CapabilityRequiresFormat2");
            Require(track.KeyDomain != NoteKeyDomain.StableLogical || track.Capability != null, "MissingCapabilitySnapshot");
            track.Capability?.Validate();
            if (track.GameTarget is { } target)
            {
                Require(target.InstrumentId != null && target.ScoreId != null && target.ProfileId != null
                    && target.ProfileVersion >= 0, "目标游戏配置标识无效。");
                Require(target.KeyMidiPitches != null && target.KeyMidiPitches.Count is 0 or 4 or 8 or 15
                    && target.KeyMidiPitches.All(p => p is >= 0 and <= 127), "目标游戏音高配置无效。");
            }
            foreach (var note in track.Notes!)
            {
                Require(note != null && note.Id != Guid.Empty && noteIds.Add(note.Id), "音符 ID 无效或重复。");
                Require(note!.TrackId == track.Id, "音符所属音轨不匹配。");
                Require(note.Key is >= 0 and < 15 && note.Beat >= 0, "音符键位或起点无效。");
                Require(project.FormatVersion >= 2 || track.GameTarget is not { KeyMidiPitches.Count: > 0 } configured || note.Key < configured.KeyMidiPitches.Count, "音符超出目标乐器琴键范围。");
                Require((note.Action == NoteAction.Tap && note.LengthBeats == 0)
                    || (note.Action == NoteAction.Hold && note.LengthBeats > 0), "音符动作与长度不匹配。");
                Require(!note.RetriggerOnOverlap || note.Action == NoteAction.Hold, "重叠重触发标记仅用于持续音符。");
                try { _ = checked(note.Beat + note.LengthBeats); }
                catch (OverflowException ex) { throw new InvalidDataException("音符结束拍位超出数值范围。", ex); }
                if (note.MidiOrigin is { } midi)
                    Require(midi.Track >= 0 && midi.Channel is >= 1 and <= 16 && midi.Pitch is >= 0 and <= 127
                        && midi.Velocity is > 0 and <= 127 && midi.StartTick >= 0 && midi.EndTick >= midi.StartTick,
                        "原 MIDI 音符信息无效。");
            }
        }
        foreach (var source in project.Metadata!.Sources!)
        {
            Require(source != null && Enum.IsDefined(source.Kind), "来源类型无效。");
            if (source!.ContentHash is { } hash)
                Require(hash.Length == 64 && hash.All(Uri.IsHexDigit), "来源哈希必须为完整 SHA-256。");
            if (source.Url is { } url)
                Require(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http",
                    "来源链接必须为 HTTP 或 HTTPS 地址。");
            Require(source.Kind != SourceKind.InternalVersion || source.ContentHash != null, "内部来源缺少内容哈希。");
            Require(source.Kind != SourceKind.ExternalLink || source.Url != null, "外部来源缺少链接。");
        }
        // 验证实际时间可表示；不按音符时长分配逐帧或逐格数组。
        var timeline = new ProjectTimeline(project.Tempos!);
        foreach (var track in project.Tracks!)
            foreach (var note in track.Notes)
            {
                _ = timeline.BeatToMicroseconds(note.Beat);
                _ = timeline.BeatToMicroseconds(note.Beat + note.LengthBeats);
                _ = timeline.LengthToMicroseconds(note.Beat, note.LengthBeats);
            }
        if (project.MidiConversion is { } conversion)
        {
            conversion.Validate();
            Require(project.OriginalMidi != null, "MIDI 转换工程必须保留原始文件。");
            var sourceIds = conversion.Source.Notes.Select(n => n.Id).ToHashSet();
            Require(project.Tracks.SelectMany(t => t.Notes).All(n => n.SourceMidiNoteId is not { } id || sourceIds.Contains(id)), "演奏音符的 MIDI 来源关联无效。");
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
