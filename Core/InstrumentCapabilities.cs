using System.IO;
using System.Text.Json.Serialization;
using System.Runtime.CompilerServices;

namespace SMAP_WPF.Core;

public enum KeyAvailability { Unknown, Available, Unavailable }
public enum NoteKeyDomain { LegacyOrdinal, StableLogical }
public sealed record InstrumentKey(string Id, int LogicalKey, int LegacyKey, int? MidiPitch,
    string SoundId, int PreviewSample, KeyAvailability Availability = KeyAvailability.Unknown);
public sealed record InstrumentSlot(int Row, int Column, string? KeyId);

public sealed record InstrumentCapability
{
    public string Id { get; init; } = "";
    public int Version { get; init; } = 1;
    public string InstrumentId { get; init; } = "";
    public string LayoutId { get; init; } = "";
    public GameInstrumentKind Kind { get; init; }
    public PitchVerification Verification { get; init; }
    public List<string> Sources { get; init; } = new();
    public int Rows { get; init; }
    public int Columns { get; init; }
    public List<InstrumentKey> Keys { get; init; } = new();
    public List<InstrumentSlot> Slots { get; init; } = new();
    [JsonIgnore] public int PhysicalKeyCount => Keys.Count;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Version < 1 || string.IsNullOrWhiteSpace(InstrumentId)
            || string.IsNullOrWhiteSpace(LayoutId) || Rows is < 1 or > 15 || Columns is < 1 or > 15
            || Keys == null || Slots == null || Sources == null || Keys.Count is < 1 or > 15 || Slots.Count != Rows * Columns || Sources.Count == 0
            || !Enum.IsDefined(Kind) || !Enum.IsDefined(Verification)
            || Keys.Any(k => k==null || string.IsNullOrWhiteSpace(k.Id) || k.LogicalKey is < 0 or >= 15
                || k.LegacyKey is < 0 or >= 15 || k.PreviewSample is < 0 or >= 15
                || k.MidiPitch is < 0 or > 127 || k.SoundId == null || !Enum.IsDefined(k.Availability))
            || Keys.Select(k => k.Id).Distinct().Count() != Keys.Count
            || Keys.Select(k => k.LogicalKey).Distinct().Count() != Keys.Count
            || Keys.Select(k => k.LegacyKey).Distinct().Count() != Keys.Count
            || Slots.Any(s => s==null || s.Row < 0 || s.Row >= Rows || s.Column < 0 || s.Column >= Columns
                || s.KeyId != null && !Keys.Any(k => k.Id == s.KeyId))
            || Slots.Select(s => (s.Row, s.Column)).Distinct().Count() != Slots.Count
            || Slots.Where(s => s.KeyId != null).Select(s => s.KeyId).Distinct().Count() != Keys.Count
            || Slots.Count(s => s.KeyId != null) != Keys.Count)
            throw new InvalidDataException("InvalidInstrumentCapability");
    }
    public InstrumentKey? Resolve(int key, NoteKeyDomain domain) => Keys.FirstOrDefault(k =>
        domain == NoteKeyDomain.LegacyOrdinal ? k.LegacyKey == key : k.LogicalKey == key);
    public int NoteKey(InstrumentKey key, NoteKeyDomain domain) => domain == NoteKeyDomain.LegacyOrdinal ? key.LegacyKey : key.LogicalKey;
}

public static class InstrumentCapabilities
{
    static readonly int[] SparseEight = { 1, 5, 7, 8, 10, 11, 12, 14 };
    sealed record Resolved(InstrumentCapability? Capability);
    static readonly ConditionalWeakTable<ProjectTrack, Resolved> Cache = new();
    public static InstrumentCapability FromProfile(GamePitchProfile profile)
    {
        profile.Validate(); int count = profile.KeyCount;
        var keys = profile.Pitches.Select((pitch, i) => new InstrumentKey($"key-{i}", count == 8 ? SparseEight[i] : i,
            i, pitch, profile.Kind == GameInstrumentKind.UnpitchedPercussion ? $"drum-{i}" : "", count == 8 ? SparseEight[i] : i,
            profile.Verification == PitchVerification.GameVerified ? KeyAvailability.Available : KeyAvailability.Unknown)).ToList();
        int columns = count == 15 ? 5 : count == 8 ? 4 : 2;
        var result = new InstrumentCapability { Id = profile.Id, Version = profile.Version, InstrumentId = profile.Instrument,
            LayoutId = $"grid:{count / columns}x{columns}:1", Rows = count / columns, Columns = columns,
            Keys = keys, Slots = keys.Select((k, i) => new InstrumentSlot(i / columns, i % columns, k.Id)).ToList(),
            Kind = profile.Kind, Sources = profile.Sources.ToList(), Verification = profile.Verification };
        result.Validate(); return result;
    }
    public static InstrumentCapability? ForInstrument(string instrument)
        => instrument == "Custom4" ? CustomFour() : GamePitchProfiles.Instruments.Contains(instrument, StringComparer.Ordinal) ? FromProfile(GamePitchProfiles.Get(instrument)) : null;
    public static InstrumentCapability CustomFour()
    {
        int[] logical={0,4,10,14};
        return new() { Id="user:four:1",InstrumentId="Custom4",LayoutId="grid:2x2:1",Kind=GameInstrumentKind.UnpitchedPercussion,
            Verification=PitchVerification.UserProvided,Rows=2,Columns=2,Sources=new(){"user-defined; unverified"},
            Keys=logical.Select((k,i)=>new InstrumentKey($"key-{i}",k,i,null,$"sound-{i}",k)).ToList(),
            Slots=Enumerable.Range(0,4).Select(i=>new InstrumentSlot(i/2,i%2,$"key-{i}")).ToList() };
    }
    public static InstrumentCapability? ForTrack(ProjectTrack track) => track.Capability ?? Cache.GetValue(track, t => new(BuildLegacy(t))).Capability;
    static InstrumentCapability? BuildLegacy(ProjectTrack track)
    {
        if (track.Capability != null) return track.Capability;
        if (track.GameTarget is not { } target) return null;
        var profile = ForInstrument(target.InstrumentId);
        if (profile == null && target.KeyMidiPitches.Count is 4 or 8 or 15)
        {
            int count = target.KeyMidiPitches.Count, cols = count == 15 ? 5 : count == 8 ? 4 : 2;
            profile = new() { Id = string.IsNullOrEmpty(target.ProfileId) ? "project-pitches" : target.ProfileId,
                InstrumentId = target.InstrumentId, Rows = count / cols, Columns = cols, LayoutId = $"project:grid:{count / cols}x{cols}",
                Sources = new() { "project" }, Keys = target.KeyMidiPitches.Select((p, i) => new InstrumentKey($"key-{i}", i, i, (int)p, "", i)).ToList(),
                Slots = Enumerable.Range(0, count).Select(i => new InstrumentSlot(i / cols, i % cols, $"key-{i}")).ToList() };
        }
        if (profile == null) return null;
        // 旧工程的局部编号和音高快照保持原义，不能被目录的后续音高更新改写。
        if (target.KeyMidiPitches.Count > 0)
            profile = profile with { Keys = profile.Keys.Select(k => k with { MidiPitch = k.LegacyKey < target.KeyMidiPitches.Count
                ? (int)target.KeyMidiPitches[k.LegacyKey] : null }).ToList() };
        return profile;
    }
    public static ProjectTrack WithTarget(ProjectTrack track, GamePitchProfile profile)
    {
        bool oldLocal = track.KeyDomain == NoteKeyDomain.LegacyOrdinal && track.Capability == null
            && track.GameTarget is { } target && (target.KeyMidiPitches.Count is 4 or 8
                || target.KeyMidiPitches.Count == 0 && ForTrack(track)?.PhysicalKeyCount is 4 or 8);
        return track with { GameTarget = profile.Target(), Capability = FromProfile(profile),
            KeyDomain = oldLocal ? NoteKeyDomain.LegacyOrdinal : track.Capability != null ? track.KeyDomain : NoteKeyDomain.StableLogical };
    }
}

public sealed record NoteCompatibility(KeyAvailability Availability, InstrumentKey? Key, bool Approximate, string Reason);
public static class PerformanceCompatibility
{
    public static NoteCompatibility Evaluate(ProjectTrack track, ProjectNote note)
    {
        var capability = InstrumentCapabilities.ForTrack(track);
        var key = capability?.Resolve(note.Key, track.KeyDomain);
        if (capability == null) return new(KeyAvailability.Unknown, null, false, "UnknownCapability");
        if (key == null) return new(KeyAvailability.Unavailable, null, false, "KeyNotPresent");
        bool approximate = note.MidiOrigin is { } origin && key.MidiPitch is { } pitch && origin.Pitch != pitch;
        return new(key.Availability, key, approximate, key.Availability.ToString());
    }
    public static void RequirePlayable(SmapProject project, Guid? trackId = null, bool allowUnknown = false)
    {
        var tracks = trackId is { } id ? project.Tracks.Where(t => t.Id == id) : project.AudibleTracks();
        var failed = tracks.SelectMany(t => t.Notes.Select(n => (t, n, state: Evaluate(t, n))))
            .Where(x => x.state.Availability == KeyAvailability.Unavailable || x.state.Key == null
                || !allowUnknown && x.state.Availability == KeyAvailability.Unknown).Take(20).ToArray();
        if (failed.Length > 0) throw new InvalidOperationException("PerformanceCompatibility: " + string.Join("; ",
            failed.Select(x => $"{x.t.Name} K{x.n.Key}: {x.state.Reason}")));
    }
    public static int ReferenceKey(ProjectTrack source, ProjectNote note, ProjectTrack target)
    {
        var from = InstrumentCapabilities.ForTrack(source); var to = InstrumentCapabilities.ForTrack(target);
        var key = from?.Resolve(note.Key, source.KeyDomain);
        if (key == null || to == null) return -1;
        var match = to.Keys.Where(k => key.MidiPitch != null ? k.MidiPitch == key.MidiPitch
            : from!.InstrumentId == to.InstrumentId && k.SoundId == key.SoundId)
            .OrderBy(k => k.Id == key.Id ? 0 : 1).ThenBy(k => Math.Abs(k.LogicalKey - key.LogicalKey)).FirstOrDefault();
        return match == null ? -1 : to.NoteKey(match, target.KeyDomain);
    }
}
