using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SMAP_WPF.Core;

public enum NoteAction { Tap, Hold }
public enum SourceKind { Attribution, ExternalLink, InternalVersion }

public sealed record ProjectNote
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    [JsonRequired] public Guid TrackId { get; init; }
    [JsonRequired] public int Key { get; init; }
    [JsonRequired] public decimal Beat { get; init; }
    // 点按的长度为零；显示宽度由网格决定，播放时仍使用短按语义。
    [JsonRequired] public decimal LengthBeats { get; init; }
    [JsonRequired] public NoteAction Action { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool RetriggerOnOverlap { get; init; }
    public MidiNoteOrigin? MidiOrigin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Guid? SourceMidiNoteId { get; init; }
}

public sealed record MidiNoteOrigin(int Track, int Channel, int Pitch, int Velocity,
    long StartTick, long EndTick);

// 四分音符为一拍，与既有 Beat、Subdiv 和 60000 / BPM 公式一致。
public sealed record TempoPoint([property: JsonRequired] decimal Beat, [property: JsonRequired] decimal MicrosecondsPerBeat);
public sealed record MeterPoint([property: JsonRequired] decimal Beat, [property: JsonRequired] int Numerator,
    [property: JsonRequired] int Denominator);

public sealed record GamePitchTarget
{
    public string InstrumentId { get; init; } = "";
    public string ScoreId { get; init; } = "";
    public string ProfileId { get; init; } = "";
    public int ProfileVersion { get; init; }
    // 尚未核实的资料留空；不以试听偏移推断游戏音高。
    public List<decimal> KeyMidiPitches { get; init; } = new();
}

public sealed record ProjectTrack
{
    [JsonRequired] public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string PreviewInstrument { get; init; } = "Piano";
    // null 表示继续使用 PitchConfig 中该乐器的设置。
    public int? PreviewSemitoneOffset { get; init; }
    public GamePitchTarget? GameTarget { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public InstrumentCapability? Capability { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public NoteKeyDomain KeyDomain { get; init; }
    public bool Muted { get; init; }
    public bool Solo { get; init; }
    [JsonRequired] public List<ProjectNote> Notes { get; init; } = new();
    public JsonObject? LegacyFields { get; init; }
}

public sealed record ProjectSource(SourceKind Kind, string Attribution,
    string? Url = null, string? ContentHash = null);

public sealed record ProjectMetadata
{
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string TranscribedBy { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Cover { get; init; }
    public List<ProjectSource> Sources { get; init; } = new();
}

public sealed record SmapProject
{
    public const int CurrentFormatVersion = 1;
    public const int CapabilityFormatVersion = 2;
    [JsonRequired] public int FormatVersion { get; init; } = CurrentFormatVersion;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int KeyInterpretationVersion { get; init; }
    [JsonRequired] public Guid ProjectId { get; init; } = Guid.NewGuid();
    public ProjectMetadata Metadata { get; init; } = new();
    [JsonRequired] public List<TempoPoint> Tempos { get; init; } = new() { new(0, 500_000) };
    [JsonRequired] public List<MeterPoint> Meters { get; init; } = new() { new(0, 4, 4) };
    [JsonRequired] public List<ProjectTrack> Tracks { get; init; } = new();
    public int GridSubdiv { get; init; } = 4;
    public int? MidiTicksPerQuarterNote { get; init; }
    [JsonIgnore] public byte[]? OriginalMidi { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MidiConversion? MidiConversion { get; init; }

    public IEnumerable<ProjectTrack> AudibleTracks() =>
        Tracks.Where(t => !t.Muted && (!Tracks.Any(s => s.Solo && !s.Muted) || t.Solo));
}
