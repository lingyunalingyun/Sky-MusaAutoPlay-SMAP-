using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NAudio.Midi;

namespace SMAP_WPF.Core;

public static class MidiSourceReader
{
    public const int MaxEvents = 1_000_000;
    sealed record Event(int Track, int Order, MidiEvent Value);
    sealed class Pending(MidiSourceNote note)
    {
        public MidiSourceNote Note = note;
    }

    public static MidiSource Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > SmapFile.MaxMidiBytes) throw new InvalidDataException("MIDI 文件过大。");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return Read(bytes);
    }

    public static MidiSource Read(byte[] bytes)
    {
        try { return Parse(bytes); }
        catch (Exception error) when (error is FormatException or EndOfStreamException or OverflowException or ArgumentException)
        { throw new InvalidDataException("MIDI 文件结构或事件无效。", error); }
    }

    static MidiSource Parse(byte[] bytes)
    {
        if (bytes.Length is < 14 or > SmapFile.MaxMidiBytes || !bytes.AsSpan(0, 4).SequenceEqual("MThd"u8)
            || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, 4)) != 6) throw new InvalidDataException("MIDI 文件头无效。");
        int format = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8, 2));
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10, 2));
        int resolution = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12, 2));
        if (format is < 0 or > 1) throw new InvalidDataException("不支持异步格式 2 或未知 MIDI 格式。");
        if (resolution == 0 || (resolution & 0x8000) != 0) throw new InvalidDataException("不支持 SMPTE 时间分辨率或零分辨率。");
        if (count is < 1 or > 128 || (format == 0 && count != 1)) throw new InvalidDataException("MIDI 音轨数量无效。");
        var source = new MidiSource { Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)), Format = format,
            Resolution = resolution, Bytes = bytes.ToArray() };
        var issues = new Dictionary<(string, int, int), MidiIssue>();
        void Issue(string code, int track, int channel, long tick, bool informational = false)
        {
            var key = (code, track, channel);
            if (issues.TryGetValue(key, out var old)) issues[key] = old with { Count = old.Count + 1 };
            else issues[key] = new(code, informational ? MidiIssueSeverity.Information : MidiIssueSeverity.Warning, track, channel, tick);
        }
        var all = new List<Event>();
        using var input = new MemoryStream(bytes, writable: false); input.Position = 14;
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        long endTick = 0;
        for (int track = 0; track < count; track++)
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MTrk") throw new InvalidDataException("缺少 MIDI 音轨块。");
            int length = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());
            if (length < 0 || length > input.Length - input.Position) throw new InvalidDataException("MIDI 音轨块长度无效。");
            long end = input.Position + length, tick = 0; int order = 0; MidiEvent? previous = null;
            string name = $"Track {track + 1}"; bool ended = false;
            while (input.Position < end)
            {
                long eventStart = input.Position;
                // 使用事件解析器严格读取，不使用容错文件读取器静默忽略损坏事件。
                MidiEvent ev = MidiEvent.ReadNextEvent(reader, previous);
                if (input.Position > end || ev.DeltaTime < 0) throw new InvalidDataException("MIDI 事件跨越音轨边界。");
                tick = checked(tick + ev.DeltaTime); ev.AbsoluteTime = tick; previous = ev;
                if (ended) Issue("EventsAfterEndTrack", track, 0, tick);
                if (ev is MetaEvent { MetaEventType: MetaEventType.EndTrack }) ended = true;
                if (ev is TextEvent text)
                {
                    var raw = bytes.AsSpan((int)eventStart, (int)(input.Position - eventStart)); int offset = 0;
                    while ((raw[offset++] & 0x80) != 0) { }
                    offset += 2;
                    while ((raw[offset++] & 0x80) != 0) { }
                    string decoded;
                    try { decoded = new UTF8Encoding(false, true).GetString(raw[offset..]); }
                    catch (DecoderFallbackException) { Issue("TextEncodingUncertain", track, 0, tick); decoded = Encoding.UTF8.GetString(raw[offset..]); }
                    if (text.MetaEventType == MetaEventType.SequenceTrackName && !string.IsNullOrWhiteSpace(decoded)) name = decoded.Trim();
                }
                if (all.Count >= MaxEvents) throw new InvalidDataException("MIDI 事件过多。");
                all.Add(new(track, order++, ev)); endTick = Math.Max(endTick, tick);
            }
            if (!ended) Issue("MissingEndTrack", track, 0, tick);
            source.Tracks.Add(new(track, name));
        }
        if (input.Position != input.Length) throw new InvalidDataException("MIDI 音轨块之后存在未知数据。");
        source = source with { EndTick = endTick };
        var active = new Dictionary<(int channel, int pitch), Queue<Pending>>();
        var deferred = Enumerable.Range(0, 17).Select(_ => new List<Pending>()).ToArray();
        var pedal = new bool[17]; var program = new int[17]; var bankMsb = new int[17]; var bankLsb = new int[17];
        var segment = new int[17]; var voices = new Dictionary<string, MidiVoice>(StringComparer.Ordinal);
        var notes = new List<MidiSourceNote>(); int noteIndex = 0;
        var tempos = new SortedDictionary<long, MidiTempo> { [0] = new(0, 500000) };
        var meters = new SortedDictionary<long, MidiMeter> { [0] = new(0, 4, 4) };
        var explicitTempos = new HashSet<long>(); var explicitMeters = new HashSet<long>();
        void Finish(Pending p, long tick)
        {
            long end = Math.Max(tick, p.Note.KeyReleaseTick);
            if (end == p.Note.StartTick) Issue("ZeroLengthNoteUsesTap", p.Note.Track, p.Note.Channel, end);
            notes.Add(p.Note with { EndTick = end });
        }
        foreach (var item in all.OrderBy(e => e.Value.AbsoluteTime).ThenBy(e => e.Track).ThenBy(e => e.Order))
        {
            MidiEvent ev = item.Value; long tick = ev.AbsoluteTime; int ch = ev.Channel;
            if (ev is TempoEvent tempo)
            {
                if (tempo.MicrosecondsPerQuarterNote <= 0) throw new InvalidDataException("MIDI 速度值无效。");
                if (!explicitTempos.Add(tick) && tempos[tick].MicrosecondsPerQuarter != tempo.MicrosecondsPerQuarterNote) Issue("ConflictingTempo", item.Track, 0, tick);
                tempos[tick] = new(tick, tempo.MicrosecondsPerQuarterNote);
            }
            else if (ev is TimeSignatureEvent meter)
            {
                if (meter.Numerator <= 0 || meter.Denominator is < 0 or > 10) throw new InvalidDataException("MIDI 拍号无效。");
                if (!explicitMeters.Add(tick) && (meters[tick].Numerator != meter.Numerator || meters[tick].Denominator != 1 << meter.Denominator)) Issue("ConflictingMeter", item.Track, 0, tick);
                meters[tick] = new(tick, meter.Numerator, 1 << meter.Denominator);
            }
            else if (ev is PatchChangeEvent patch)
            {
                if (program[ch] != patch.Patch) { program[ch] = patch.Patch; segment[ch]++; }
                source.Events.Add(new(item.Track, item.Order, tick, ch, "Program", patch.Patch, 0));
            }
            else if (ev is NoteOnEvent { Velocity: > 0 } on)
            {
                string voice = $"{item.Track}:{ch}:{program[ch]}:{bankMsb[ch]}:{bankLsb[ch]}:{segment[ch]}";
                if (!voices.ContainsKey(voice)) voices.Add(voice, new(voice, item.Track, ch, program[ch], bankMsb[ch], bankLsb[ch], source.Tracks[item.Track].Name));
                var key = (ch, on.NoteNumber);
                if (!active.TryGetValue(key, out var queue)) active[key] = queue = new();
                if (queue.Count > 0) Issue("OverlapPairedFifo", item.Track, ch, tick, true);
                queue.Enqueue(new(new(MidiSource.StableId($"{source.Sha256}:note:{noteIndex++}"), voice, item.Track, ch,
                    program[ch], on.NoteNumber, on.Velocity, tick, tick, tick)));
                if (noteIndex > ProjectValidation.MaxNotes) throw new InvalidDataException("MIDI 音符过多。");
            }
            else if (ev is NoteEvent off && (ev.CommandCode == MidiCommandCode.NoteOff || (ev.CommandCode == MidiCommandCode.NoteOn && off.Velocity == 0)))
            {
                if (active.TryGetValue((ch, off.NoteNumber), out var queue) && queue.TryDequeue(out var pending))
                {
                    pending.Note = pending.Note with { KeyReleaseTick = tick };
                    if (pedal[ch]) deferred[ch].Add(pending); else Finish(pending, tick);
                }
                else Issue("UnmatchedNoteOff", item.Track, ch, tick);
            }
            else if (ev is ControlChangeEvent cc)
            {
                int controller = (int)cc.Controller, value = cc.ControllerValue;
                source.Events.Add(new(item.Track, item.Order, tick, ch, "Controller", controller, value));
                if (controller == 64)
                {
                    bool down = value >= 64;
                    if (pedal[ch] && !down) { foreach (var p in deferred[ch]) Finish(p, tick); deferred[ch].Clear(); }
                    pedal[ch] = down;
                }
                else if (controller == 0) { if (bankMsb[ch] != value) { bankMsb[ch] = value; segment[ch]++; } }
                else if (controller == 32) { if (bankLsb[ch] != value) { bankLsb[ch] = value; segment[ch]++; } }
                else Issue("ControllerNotRendered", item.Track, ch, tick);
            }
            else if (ev is PitchWheelChangeEvent bend)
            {
                source.Events.Add(new(item.Track, item.Order, tick, ch, "PitchBend", bend.Pitch, 0));
                Issue("PitchBendNotMapped", item.Track, ch, tick);
            }
            else if (ev is not MetaEvent { MetaEventType: MetaEventType.EndTrack } && ev is not TextEvent)
            {
                source.Events.Add(new(item.Track, item.Order, tick, ch, ev.GetType().Name, (int)ev.CommandCode, 0));
                Issue("EventNotRendered", item.Track, ch, tick);
            }
        }
        foreach (var queue in active.Values) foreach (var p in queue)
        {
            Issue("MissingNoteOff", p.Note.Track, p.Note.Channel, p.Note.StartTick);
            p.Note = p.Note with { KeyReleaseTick = endTick, Incomplete = true }; Finish(p, endTick);
        }
        for (int ch = 1; ch <= 16; ch++) foreach (var p in deferred[ch])
        { Issue("PedalNotReleased", p.Note.Track, ch, endTick); Finish(p, endTick); }
        source = source with { Notes = notes.OrderBy(n => n.StartTick).ThenBy(n => n.Id).ToList(), Voices = voices.Values.OrderBy(v => v.Id, StringComparer.Ordinal).ToList(),
            Tempos = tempos.Values.ToList(), Meters = meters.Values.ToList(), Issues = issues.Values.OrderBy(i => i.Tick).ThenBy(i => i.Code, StringComparer.Ordinal).ToList() };
        source.Validate(); return source;
    }
}
