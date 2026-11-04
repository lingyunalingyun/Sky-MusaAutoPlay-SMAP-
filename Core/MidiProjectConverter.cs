using System.IO;
using System.Text.Json;
using NAudio.Midi;

namespace SMAP_WPF.Core;

public static class MidiProjectConverter
{
    public static SmapProject Create(MidiConversion conversion, string name = "", SmapProject? previous = null)
    {
        conversion.Validate(); var source = conversion.Source;
        if (source.Bytes == null) throw new InvalidDataException("缺少原始 MIDI 文件，不能建立可重新映射的工程。");
        var originals = source.Notes.ToDictionary(n => n.Id);
        var byVoice = conversion.Notes.GroupBy(n => n.VoiceId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var tracks = new List<ProjectTrack>();
        foreach (var option in conversion.Voices)
        {
            var voice = source.Voices.Single(v => v.Id == option.VoiceId);
            Guid id = MidiSource.StableId($"{source.Sha256}:track:{voice.Id}");
            var oldTrack = previous?.Tracks.SingleOrDefault(t => t.Id == id);
            tracks.Add(new() { Id = id, Name = oldTrack?.Name ?? $"{voice.Name} · Ch {voice.Channel} · Program {voice.Program}", Solo = oldTrack?.Solo ?? false,
                PreviewInstrument = option.Profile.Instrument, Muted = !option.Included, GameTarget = option.Profile.Target(),
                PreviewSemitoneOffset = oldTrack?.PreviewInstrument == option.Profile.Instrument ? oldTrack.PreviewSemitoneOffset : null,
                Notes = byVoice[voice.Id].Where(n => n.Key.HasValue).Select(n =>
                {
                    var origin = originals[n.SourceId]; decimal duration = source.Beat(origin.EndTick - origin.StartTick);
                    return new ProjectNote { Id = MidiSource.StableId($"{n.SourceId}:playable"), TrackId = id,
                        Key = n.Key!.Value, Beat = source.Beat(origin.StartTick), LengthBeats = duration,
                        Action = duration > 0 ? NoteAction.Hold : NoteAction.Tap, SourceMidiNoteId = n.SourceId,
                        RetriggerOnOverlap = duration > 0,
                        MidiOrigin = new(origin.Track, origin.Channel, origin.Pitch, origin.Velocity, origin.StartTick, origin.EndTick) };
                }).ToList() });
        }
        if (tracks.Count == 0) throw new InvalidDataException("MIDI 文件没有可转换的声部。");
        var project = new SmapProject { ProjectId = previous?.ProjectId ?? MidiSource.StableId($"{source.Sha256}:project"),
            Metadata = previous == null ? new() { Name = name } : previous.Metadata with { Name = string.IsNullOrWhiteSpace(name) ? previous.Metadata.Name : name }, Tracks = tracks, MidiConversion = conversion,
            Tempos = source.Tempos.Select(t => new TempoPoint(source.Beat(t.Tick), t.MicrosecondsPerQuarter)).ToList(),
            Meters = source.Meters.Select(m => new MeterPoint(source.Beat(m.Tick), m.Numerator, m.Denominator)).ToList(),
            MidiTicksPerQuarterNote = source.Resolution, OriginalMidi = source.Bytes.ToArray(), GridSubdiv = previous?.GridSubdiv ?? 4 };
        ProjectValidation.Validate(project); ValidateOriginal(project); return project;
    }

    public static SmapProject Remap(SmapProject project, IEnumerable<SapmVoiceOptions> settings, SapmScope? scope = null)
    {
        ProjectValidation.Validate(project); ValidateOriginal(project);
        var old = project.MidiConversion ?? throw new InvalidDataException("此工程没有保留 MIDI 转换数据。");
        var source = old.Source with { Bytes = project.OriginalMidi!.ToArray() };
        return Create(SapmMapper.Convert(source, settings, scope ?? old.Scope), previous: project);
    }

    internal static void ValidateOriginal(SmapProject project)
    {
        if (project.MidiConversion is not { } conversion) return;
        if (project.OriginalMidi == null) throw new InvalidDataException("缺少 MIDI 转换所需的原始文件。");
        var parsed = MidiSourceReader.Read(project.OriginalMidi);
        if (!JsonSerializer.SerializeToUtf8Bytes(parsed).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(conversion.Source)))
            throw new InvalidDataException("保存的 MIDI 解析数据与原文件不一致。");
    }

    public static void ExportOriginal(SmapProject project, string path)
    {
        ProjectValidation.Validate(project); ValidateOriginal(project);
        if (project.OriginalMidi == null) throw new InvalidDataException("工程没有原始 MIDI 文件。");
        WriteAtomically(path, project.OriginalMidi);
    }

    public static void ExportPerformance(SmapProject project, string path, Guid? trackId = null)
    {
        ProjectValidation.Validate(project);
        int resolution = project.MidiTicksPerQuarterNote ?? 960;
        if (resolution is < 1 or > 32767) throw new InvalidDataException("MIDI 导出分辨率无效。");
        var tracks = trackId is { } id ? project.Tracks.Where(t => t.Id == id).ToArray() : project.AudibleTracks().ToArray();
        if (tracks.Length == 0) throw new InvalidDataException("没有选中的导出音轨。");
        long Tick(decimal beat, long? originalTick = null)
        {
            if (originalTick is { } original && beat == original / (decimal)resolution) return original;
            decimal tick = beat * resolution, rounded = decimal.Round(tick, 0, MidpointRounding.ToEven);
            if (tick != rounded || rounded > long.MaxValue) throw new InvalidDataException("当前拍位无法由 MIDI 分辨率精确表示。");
            return (long)rounded;
        }
        var source = project.MidiConversion?.Source;
        var originalTempos = source?.Tempos.ToDictionary(t => source.Beat(t.Tick), t => (long?)t.Tick) ?? new();
        var originalMeters = source?.Meters.ToDictionary(m => source.Beat(m.Tick), m => (long?)m.Tick) ?? new();
        var events = new MidiEventCollection(1, resolution); events.AddTrack();
        foreach (var t in project.Tempos)
        {
            if (t.MicrosecondsPerBeat != decimal.Truncate(t.MicrosecondsPerBeat) || t.MicrosecondsPerBeat > 0xffffff)
                throw new InvalidDataException("当前速度无法由 MIDI 整数微秒格式精确表示。");
            events.AddEvent(new TempoEvent((int)t.MicrosecondsPerBeat, Tick(t.Beat, originalTempos.GetValueOrDefault(t.Beat))), 0);
        }
        foreach (var m in project.Meters)
        {
            if (m.Numerator > 255 || m.Denominator > 1024) throw new InvalidDataException("当前拍号超出 MIDI 导出支持范围。");
            events.AddEvent(new TimeSignatureEvent(Tick(m.Beat, originalMeters.GetValueOrDefault(m.Beat)), m.Numerator, (int)Math.Log2(m.Denominator), 24, 8), 0);
        }
        events.AddEvent(new MetaEvent(MetaEventType.EndTrack, 0, events[0].Max(e => e.AbsoluteTime)), 0);
        int index = 1; var scheduled = new List<(int track, ProjectNote note, int pitch, long start, long end)>();
        foreach (var track in tracks)
        {
            if (track.GameTarget is not { KeyMidiPitches.Count: > 0 } target) throw new InvalidDataException("导出 MIDI 需要明确的目标琴键音高。");
            events.AddTrack(); events.AddEvent(new TextEvent(track.Name, MetaEventType.SequenceTrackName, 0), index);
            foreach (var n in track.Notes)
            {
                if(track.Capability!=null&&PerformanceCompatibility.Evaluate(track,n).Availability==KeyAvailability.Unavailable)
                    throw new InvalidDataException("UnavailablePerformanceKey");
                if (track.Capability==null && n.Key >= target.KeyMidiPitches.Count) throw new InvalidDataException("琴键没有目标音高。");
                decimal pitch = track.Capability!=null ? InstrumentCapabilities.ForTrack(track)?.Resolve(n.Key,track.KeyDomain)?.MidiPitch
                    ?? throw new InvalidDataException("MissingTargetPitch") : target.KeyMidiPitches[n.Key];
                if (pitch != decimal.Truncate(pitch)) throw new InvalidDataException("目标音高不是 MIDI 整数音高。");
                long start = Tick(n.Beat, n.MidiOrigin?.StartTick);
                long end = n.MidiOrigin is { } origin && n.Beat == origin.StartTick / (decimal)resolution
                    && n.LengthBeats == (origin.EndTick - origin.StartTick) / (decimal)resolution ? origin.EndTick : Tick(n.Beat + n.LengthBeats);
                if (n.Action == NoteAction.Tap) throw new InvalidDataException("点按没有音乐时值，请明确时值后导出 MIDI。");
                if (end <= start) throw new InvalidDataException("音符时值不能由 MIDI 分辨率表示。");
                scheduled.Add((index, n, (int)pitch, start, end));
            }
            index++;
        }
        // 为重叠同音分配不同通道，避免 MIDI 1.0 关音配对改变原有时值。
        var channels = Enumerable.Range(1, 16).Where(c => c != 10).ToArray(); var occupied = new Dictionary<(int channel, int pitch), long>();
        foreach (var n in scheduled.OrderBy(n => n.start).ThenBy(n => n.end).ThenBy(n => n.track).ThenBy(n => n.note.Id))
        {
            int preferred = (n.track - 1) % channels.Length;
            int channel = Enumerable.Range(0, channels.Length).Select(i => channels[(preferred + i) % channels.Length])
                .FirstOrDefault(c => !occupied.TryGetValue((c, n.pitch), out long until) || until <= n.start);
            if (channel == 0) throw new InvalidDataException("同时重叠的同音超过 MIDI 可独立配对的通道数量。");
            occupied[(channel, n.pitch)] = n.end;
            events.AddEvent(new NoteOnEvent(n.start, channel, n.pitch, n.note.MidiOrigin?.Velocity ?? 100, 0), n.track);
            events.AddEvent(new NoteEvent(n.end, channel, MidiCommandCode.NoteOff, n.pitch, 0), n.track);
        }
        for (int t = 1; t < events.Tracks; t++) events.AddEvent(new MetaEvent(MetaEventType.EndTrack, 0, events[t].Max(e => e.AbsoluteTime)), t);
        for (int t = 0; t < events.Tracks; t++)
        {
            long previousTick = 0;
            foreach (var ev in events[t].OrderBy(e => e.AbsoluteTime))
            {
                if (ev.AbsoluteTime - previousTick > 0x0fffffff) throw new InvalidDataException("MIDI 事件间隔超出标准变长整数范围。");
                previousTick = ev.AbsoluteTime;
            }
        }
        string destination = Path.GetFullPath(path), temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try { MidiFile.Export(temporary, events); _ = MidiSourceReader.Read(temporary); File.Move(temporary, destination, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static void WriteAtomically(string path, byte[] bytes)
    {
        string destination = Path.GetFullPath(path), temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, destination, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
