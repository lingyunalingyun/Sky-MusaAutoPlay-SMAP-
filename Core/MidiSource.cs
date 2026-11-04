using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace SMAP_WPF.Core;

public enum MidiIssueSeverity { Information, Warning }
public sealed record MidiIssue(string Code, MidiIssueSeverity Severity, int Track, int Channel, long Tick, int Count = 1);
public sealed record MidiPhysicalTrack(int Index, string Name);
public sealed record MidiVoice(string Id, int Track, int Channel, int Program, int BankMsb, int BankLsb, string Name);
public sealed record MidiSourceNote(Guid Id, string VoiceId, int Track, int Channel, int Program, int Pitch, int Velocity,
    long StartTick, long KeyReleaseTick, long EndTick, bool Incomplete = false);
public sealed record MidiSourceEvent(int Track, int Order, long Tick, int Channel, string Kind, int Data1, int Data2);
public sealed record MidiTempo(long Tick, int MicrosecondsPerQuarter);
public sealed record MidiMeter(long Tick, int Numerator, int Denominator);

public sealed record MidiSource
{
    public string Sha256 { get; init; } = "";
    public int Format { get; init; }
    public int Resolution { get; init; }
    public long EndTick { get; init; }
    public List<MidiPhysicalTrack> Tracks { get; init; } = new();
    public List<MidiVoice> Voices { get; init; } = new();
    public List<MidiSourceNote> Notes { get; init; } = new();
    public List<MidiSourceEvent> Events { get; init; } = new();
    public List<MidiTempo> Tempos { get; init; } = new();
    public List<MidiMeter> Meters { get; init; } = new();
    public List<MidiIssue> Issues { get; init; } = new();
    [JsonIgnore] public byte[]? Bytes { get; init; }
    public decimal Beat(long tick) => tick / (decimal)Resolution;
    public ProjectTimeline Timeline() => new(Tempos.Select(t => new TempoPoint(Beat(t.Tick), t.MicrosecondsPerQuarter)));
    public decimal Time(long tick) => Timeline().BeatToMicroseconds(Beat(tick));

    internal static Guid StableId(string scope) => new(SHA256.HashData(Encoding.UTF8.GetBytes(scope)).AsSpan(0, 16));

    public void Validate()
    {
        if (Sha256 == null || Tracks == null || Voices == null || Notes == null || Events == null || Tempos == null || Meters == null || Issues == null
            || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit) || Resolution is < 1 or > 32767 || Format is < 0 or > 1
            || EndTick < 0 || Tracks.Count is < 1 or > 128 || Voices.Count > ProjectValidation.MaxTracks
            || Notes.Count > ProjectValidation.MaxNotes || Events.Count > MidiSourceReader.MaxEvents
            || Tempos.Count is < 1 or > ProjectValidation.MaxTimingPoints || Meters.Count is < 1 or > ProjectValidation.MaxTimingPoints)
            throw new InvalidDataException("MIDI 原始数据无效或超出支持范围。");
        if (Tracks.Any(t => t == null || t.Index < 0 || t.Index >= Tracks.Count || t.Name == null) || Tracks.Select(t => t.Index).Distinct().Count() != Tracks.Count
            || Voices.Any(v => v == null || string.IsNullOrWhiteSpace(v.Id) || v.Name == null || v.Track < 0 || v.Track >= Tracks.Count || v.Channel is < 1 or > 16
                || v.Program is < 0 or > 127 || v.BankMsb is < 0 or > 127 || v.BankLsb is < 0 or > 127)
            || Voices.Select(v => v.Id).Distinct(StringComparer.Ordinal).Count() != Voices.Count)
            throw new InvalidDataException("MIDI 音轨或声部无效。");
        var voices = Voices.ToDictionary(v => v.Id, StringComparer.Ordinal);
        var ids = new HashSet<Guid>();
        foreach (var n in Notes)
            if (n == null || n.VoiceId == null || !ids.Add(n.Id) || n.Id == Guid.Empty || !voices.TryGetValue(n.VoiceId, out var voice)
                || n.Track != voice.Track || n.Channel != voice.Channel || n.Program != voice.Program
                || n.Pitch is < 0 or > 127 || n.Velocity is < 1 or > 127
                || n.StartTick < 0 || n.KeyReleaseTick < n.StartTick || n.EndTick < n.KeyReleaseTick || n.EndTick > EndTick)
                throw new InvalidDataException("MIDI 原始音符无效。");
        if (!Voices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal).SetEquals(Notes.Select(n => n.VoiceId))
            || Events.Any(e => e == null || e.Track < 0 || e.Track >= Tracks.Count || e.Order < 0 || e.Tick < 0 || e.Tick > EndTick || e.Channel is < 0 or > 16 || string.IsNullOrWhiteSpace(e.Kind))
            || Issues.Any(i => i == null || i.Count <= 0 || i.Track < 0 || i.Track >= Tracks.Count || i.Channel is < 0 or > 16 || i.Tick < 0 || i.Tick > EndTick || !Enum.IsDefined(i.Severity) || string.IsNullOrWhiteSpace(i.Code))
            || Tempos.Any(t => t == null || t.Tick < 0 || t.Tick > EndTick || t.MicrosecondsPerQuarter is < 1 or > 0xffffff))
            throw new InvalidDataException("MIDI 事件或速度数据无效。");
        _ = Timeline();
        if (Meters.Any(m => m == null || m.Tick < 0 || m.Tick > EndTick || m.Numerator <= 0 || m.Denominator <= 0
            || (m.Denominator & (m.Denominator - 1)) != 0) || Meters[0].Tick != 0 || Meters.Select(m => m.Tick).Distinct().Count() != Meters.Count)
            throw new InvalidDataException("MIDI 拍号无效。");
        if (Bytes != null && Convert.ToHexStringLower(SHA256.HashData(Bytes)) != Sha256)
            throw new InvalidDataException("MIDI 原始内容身份不匹配。");
    }
}
