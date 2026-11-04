using System.IO;

namespace SMAP_WPF.Core;

public enum SapmMode { Melody, Strict, Harmony }
public enum SapmScope { WholeSong, IndependentVoices }
public enum PitchMappingKind { Exact, Octave, Approximate, Unplayable, Excluded }

public sealed record SapmVoiceOptions
{
    public string VoiceId { get; init; } = "";
    public GamePitchProfile Profile { get; init; } = GamePitchProfiles.Get("Piano");
    public bool Included { get; init; } = true;
    public bool AutoTranspose { get; init; } = true;
    public int SemitoneShift { get; init; }
    public int OctaveShift { get; init; }
    public SapmMode Mode { get; init; } = SapmMode.Melody;
    public bool AllowUnverified { get; init; }
    public Dictionary<Guid, int?> ManualKeys { get; init; } = new();
}

public sealed record TonalCandidate(int Tonic, bool Minor, double Score, double InScaleWeight, double TonicWeight);
public sealed record KeyRecommendation(GamePitchProfile Profile, int Transpose, double Cost, string Reason);
public sealed record MappedMidiNote(Guid SourceId, string VoiceId, int OriginalPitch, int RequestedPitch,
    int? Key, int? TargetPitch, PitchMappingKind Kind, bool Manual, string Reason);
public sealed record SapmQuality(int Total, int Exact, int Octave, int Approximate, int Unplayable, int Excluded,
    int? OriginalLow, int? OriginalHigh, int? TargetLow, int? TargetHigh,
    double MeanAbsolutePitchError, double MeanIntervalError, double DirectionPreservation,
    double MelodyFidelity, double HarmonyFidelity, int IntervalPairs, int HarmonyPairs);

public sealed record MidiConversion
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public string Algorithm { get; init; } = "SAPM-1";
    public MidiSource Source { get; init; } = new();
    public SapmScope Scope { get; init; }
    public List<SapmVoiceOptions> Voices { get; init; } = new();
    public Dictionary<string, int> AutoShifts { get; init; } = new(StringComparer.Ordinal);
    public List<MappedMidiNote> Notes { get; init; } = new();
    public List<TonalCandidate> TonalAnalysis { get; init; } = new();
    public SapmQuality? Quality { get; init; }

    public void Validate()
    {
        if (Version != CurrentVersion || Algorithm != "SAPM-1" || !Enum.IsDefined(Scope) || Source == null
            || Voices == null || AutoShifts == null || Notes == null || TonalAnalysis == null || Quality == null)
            throw new InvalidDataException("MIDI 转换记录无效或版本不受支持。");
        Source.Validate();
        var voiceIds = Source.Voices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        if (Voices.Any(v => v == null || v.VoiceId == null || v.Profile == null)
            || Voices.Select(v => v.VoiceId).Distinct(StringComparer.Ordinal).Count() != Voices.Count || !voiceIds.SetEquals(Voices.Select(v => v.VoiceId))
            || !voiceIds.SetEquals(AutoShifts.Keys) || AutoShifts.Values.Any(s => s is < -102 or > 101))
            throw new InvalidDataException("转换声部记录不匹配。");
        foreach (var v in Voices)
        {
            v.Profile.Validate();
            if (!Enum.IsDefined(v.Mode) || v.SemitoneShift is < -48 or > 48 || v.OctaveShift is < -8 or > 8 || v.ManualKeys == null
                || v.ManualKeys.Any(k => k.Value is { } key && (key < 0 || key >= v.Profile.KeyCount)))
                throw new InvalidDataException("声部映射参数无效。");
        }
        var sources = Source.Notes.ToDictionary(n => n.Id);
        var options = Voices.ToDictionary(v => v.VoiceId, StringComparer.Ordinal);
        if (Voices.Any(v => v.ManualKeys.Keys.Any(id => !sources.TryGetValue(id, out var n) || n.VoiceId != v.VoiceId))
            || Notes.Any(n => n == null) || Notes.Count != sources.Count || Notes.Select(n => n.SourceId).Distinct().Count() != Notes.Count)
            throw new InvalidDataException("转换结果必须追踪全部原始音符。");
        foreach (var n in Notes)
            if (!sources.TryGetValue(n.SourceId, out var original) || n.OriginalPitch != original.Pitch || n.VoiceId != original.VoiceId
                || !options.TryGetValue(n.VoiceId, out var v) || !Enum.IsDefined(n.Kind) || n.Reason == null
                || n.RequestedPitch != original.Pitch + v.SemitoneShift + v.OctaveShift * 12 + AutoShifts[v.VoiceId]
                || (n.Key is { } key && (key < 0 || key >= v.Profile.KeyCount || n.TargetPitch != v.Profile.Pitches[key]))
                || (n.Key == null && n.TargetPitch != null) || (!v.Included && (n.Key != null || n.Kind != PitchMappingKind.Excluded))
                || (v.Included && n.Kind == PitchMappingKind.Excluded)
                || ((n.Kind is PitchMappingKind.Unplayable or PitchMappingKind.Excluded) != (n.Key == null))
                || (n.Kind == PitchMappingKind.Exact && n.TargetPitch != n.OriginalPitch)
                || (n.Kind == PitchMappingKind.Octave && (n.TargetPitch == null || n.TargetPitch == n.OriginalPitch || (n.TargetPitch.Value - n.OriginalPitch) % 12 != 0)))
                throw new InvalidDataException("转换音符关联无效。");
        var q = Quality;
        if (q.Total != Notes.Count || q.Exact != Notes.Count(n => n.Kind == PitchMappingKind.Exact) || q.Octave != Notes.Count(n => n.Kind == PitchMappingKind.Octave)
            || q.Approximate != Notes.Count(n => n.Kind == PitchMappingKind.Approximate) || q.Unplayable != Notes.Count(n => n.Kind == PitchMappingKind.Unplayable)
            || q.Excluded != Notes.Count(n => n.Kind == PitchMappingKind.Excluded) || q.IntervalPairs < 0 || q.HarmonyPairs < 0
            || !double.IsFinite(q.MeanAbsolutePitchError) || !double.IsFinite(q.MeanIntervalError) || q.MeanAbsolutePitchError < 0 || q.MeanIntervalError < 0
            || !double.IsFinite(q.DirectionPreservation) || q.DirectionPreservation is < 0 or > 1 || !double.IsFinite(q.MelodyFidelity)
            || q.MelodyFidelity is < 0 or > 100 || !double.IsFinite(q.HarmonyFidelity) || q.HarmonyFidelity is < 0 or > 100
            || TonalAnalysis.Any(t => t == null || t.Tonic is < 0 or > 11 || !double.IsFinite(t.Score) || !double.IsFinite(t.InScaleWeight) || !double.IsFinite(t.TonicWeight)))
            throw new InvalidDataException("MIDI 转换质量记录无效。");
    }
}
