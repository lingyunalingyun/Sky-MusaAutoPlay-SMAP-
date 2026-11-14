using System.IO;
using System.Text.Json.Serialization;

namespace SMAP_WPF.Core;

public enum PitchVerification { WikiReported, ConflictingSources, UserProvided, GameVerified }
public enum GameInstrumentKind { Diatonic, PitchedPercussion, UnpitchedPercussion }

public sealed record GamePitchProfile
{
    public string Id { get; init; } = "";
    public int Version { get; init; } = 1;
    public string Instrument { get; init; } = "";
    public GameInstrumentKind Kind { get; init; }
    public int Tonic { get; init; }
    public string KeySource { get; init; } = "";
    public string KeySourceUrl { get; init; } = "";
    public List<int?> Pitches { get; init; } = new();
    public List<string> Sources { get; init; } = new();
    public PitchVerification Verification { get; init; }
    public string VerificationNote { get; init; } = "";
    [JsonIgnore] public int KeyCount => Pitches.Count;
    [JsonIgnore] public int? Lowest => Pitches.Where(p => p.HasValue).Min();
    [JsonIgnore] public int? Highest => Pitches.Where(p => p.HasValue).Max();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Version < 1 || string.IsNullOrWhiteSpace(Instrument)
            || Tonic is < 0 or > 11 || !Enum.IsDefined(Kind) || !Enum.IsDefined(Verification)
            || Pitches == null || Pitches.Count is not (4 or 8 or 15) || Pitches.Any(p => p is < 0 or > 127)
            || (Kind == GameInstrumentKind.Diatonic && (Pitches.Count != 15 || Pitches.Any(p => p == null)
                || Pitches.Zip(Pitches.Skip(1)).Any(p => p.First >= p.Second)))
            || (Kind == GameInstrumentKind.PitchedPercussion && Pitches.Any(p => p == null))
            || (Kind == GameInstrumentKind.UnpitchedPercussion && Pitches.Any(p => p != null))
            || Sources == null || Sources.Count == 0 || Sources.Any(s => !Http(s)) || !Http(KeySourceUrl)
            || string.IsNullOrWhiteSpace(KeySource) || VerificationNote == null)
            throw new InvalidDataException("游戏音高配置无效。");
    }
    static bool Http(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
    public GamePitchTarget Target() => new() { InstrumentId = Instrument, ProfileId = Id, ProfileVersion = Version,
        ScoreId = KeySource, KeyMidiPitches = Kind == GameInstrumentKind.UnpitchedPercussion ? new() : Pitches.Select(p => (decimal)p!.Value).ToList() };
}

public static class GamePitchProfiles
{
    public const int CatalogVersion = 1;
    const string InstrumentsUrl = "https://sky-children-of-the-light.fandom.com/wiki/Instruments";
    const string KeyUrl = "https://sky-children-of-the-light.fandom.com/wiki/Music_Key";
    // 游戏资料独立于本地采样和 PitchConfig。未列出的乐器须提供明确的自定义配置。
    static readonly Dictionary<string, int> Roots = new(StringComparer.Ordinal)
    {
        ["Piano"] = 60, ["Flute"] = 60, ["Ocarina"] = 60, ["Bugle"] = 60,
        ["Kalimba"] = 60, ["Violin"] = 60, ["Harmonica"] = 60,
        ["Winter Piano"] = 72, ["Xylophone"] = 72,
        ["Harp"] = 48, ["Guitar"] = 48, ["Ukulele"] = 48, ["Electric Guitar"] = 48, ["Saxophone"] = 48,
        ["Horn"] = 36, ["Cello"] = 36, ["Contrabass"] = 24
    };
    static readonly int[] Diatonic = { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21, 23, 24 };
    public static IReadOnlyList<string> Instruments => Roots.Keys.Concat(new[] { "Handpan", "GoldHandpan", "APBell1", "APBell2", "Dundun", "GoldDundun" }).ToArray();

    public static GamePitchProfile Get(string instrument, int tonic = 0)
    {
        if (tonic is < 0 or > 11) throw new ArgumentOutOfRangeException(nameof(tonic));
        var kind = GameInstrumentKind.Diatonic; var verification = PitchVerification.WikiReported;
        int?[] pitches;
        if (Roots.TryGetValue(instrument, out int root)) pitches = Diatonic.Select(p => (int?)(root + tonic + p)).ToArray();
        else if (instrument is "Handpan" or "GoldHandpan")
        {
            kind = GameInstrumentKind.PitchedPercussion;
            int shift = tonic >= 7 ? tonic - 12 : tonic;
            pitches = new[] { 50, 57, 60, 62, 65, 67, 69, 72 }.Select(p => (int?)(p + shift)).ToArray();
        }
        else if (instrument is "APBell1" or "APBell2")
        {
            kind = GameInstrumentKind.PitchedPercussion; verification = PitchVerification.ConflictingSources;
            int shift = tonic >= 7 ? tonic - 12 : tonic;
            pitches = new[] { 60, 62, 67, 69, 60, 62, 67, 69 }.Select(p => (int?)(p + shift)).ToArray();
        }
        else if (instrument is "Dundun" or "GoldDundun")
        { kind = GameInstrumentKind.UnpitchedPercussion; pitches = new int?[8]; }
        else throw new ArgumentException("尚无该乐器经资料核对的游戏音高配置。", nameof(instrument));
        var result = new GamePitchProfile { Id = $"sky:{instrument}:major:{tonic}", Instrument = instrument, Tonic = tonic,
            Kind = kind, Pitches = pitches.ToList(), Sources = new() { InstrumentsUrl, KeyUrl }, Verification = verification,
            KeySource = tonic == 0 ? "sheet:Bounding About / C major" : tonic == 5 ? "sheet:Looking Up to the Stars / F major" : $"location-guide:major:{tonic}",
            KeySourceUrl = KeyUrl, VerificationNote = verification == PitchVerification.ConflictingSources
                ? "Wiki 正文与表格的钟音区记载冲突，且采样别名对应关系待实机核对。" : "Wiki 资料配置；尚未进行实际游戏音高验证。" };
        result.Validate(); return result;
    }
}
