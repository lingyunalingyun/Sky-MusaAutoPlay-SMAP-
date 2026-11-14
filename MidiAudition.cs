using System.IO;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed record AuditionTone(int Pitch, int Velocity, string Instrument, int SampleKey, int SampleShift, bool Reference);
public interface IMidiAuditionVoices
{
    void Start(Guid owner, Guid voice, AuditionTone tone, bool sustain);
    void Release(Guid voice);
    void StopOwner(Guid owner);
}

public sealed class MidiAuditionPlan
{
    internal PlaybackSequence Sequence { get; }
    public IReadOnlyDictionary<Guid, AuditionTone> Tones { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public double DurationMs => Sequence.DurationMs;
    readonly Lazy<AuditionSampleBank> _bank;
    public long PreparedSampleBytes => _bank.IsValueCreated ? _bank.Value.Bytes : 0;
    public int PreparedSampleCount => _bank.IsValueCreated ? _bank.Value.Count : 0;
    internal MidiAuditionPlan(PlaybackSequence sequence, Dictionary<Guid, AuditionTone> tones, IReadOnlyList<string> capabilities)
    { Sequence = sequence; Tones = tones; Capabilities = capabilities; _bank = new(() => new(tones.Values)); }
    public void PrepareSamples() => _ = _bank.Value;
    public PlaybackSession CreateSession(IMonotonicClock clock, IMidiAuditionVoices voices, double speed = 1) =>
        new(Sequence, clock, new[] { CreateOutput(voices) }, speed);
    public void Play(SkyPlayer player, double speed, Action done, IMidiAuditionVoices? voices = null)
    {
        player.Stop(); player.Play(Sequence, speed, done, CreateOutput(voices));
    }
    internal INoteOutput CreateOutput(IMidiAuditionVoices? voices = null) => new AuditionOutput(Tones, voices ?? new PreparedMidiVoices(_bank.Value));

    sealed class AuditionOutput(IReadOnlyDictionary<Guid, AuditionTone> tones, IMidiAuditionVoices voices) : INoteOutput
    {
        readonly Guid _owner = Guid.NewGuid();
        readonly Dictionary<Guid, Guid> _ids = new();
        public void Apply(IReadOnlyList<NoteOutputEvent> changes)
        {
            foreach (var change in changes)
                if (change.IsOn)
                {
                    if (!tones.TryGetValue(change.Note.NoteId, out var tone)) continue;
                    var id = Guid.NewGuid(); _ids[change.Note.NoteId] = id;
                    voices.Start(_owner, id, tone, change.Note.Action == NoteAction.Hold);
                }
                else if (_ids.Remove(change.Note.NoteId, out var id)) voices.Release(id);
        }
        public void ReleaseAll() { foreach (var id in _ids.Values) voices.Release(id); _ids.Clear(); }
        public void StopAll() { voices.StopOwner(_owner); _ids.Clear(); }
    }
}

public static class MidiAudition
{
    public static MidiAuditionPlan Original(MidiSource source, IEnumerable<string>? voiceIds = null, double startMs = 0, double? endMs = null)
    {
        source.Validate(); var selection = Selection(source, voiceIds); var tones = new Dictionary<Guid, AuditionTone>();
        var timeline = source.Timeline(); var notes = new List<TimedProjectNote>(); ValidateRegion(startMs, endMs);
        bool unpitched = source.Notes.Any(n => selection.Contains(n.VoiceId) && n.Channel == 10);
        foreach (var n in source.Notes.Where(n => selection.Contains(n.VoiceId) && n.Channel != 10))
        {
            // 参考事件的 Key 是内部占位；此时间线只交给音频输出，不作为游戏演奏曲谱暴露。
            if (!Clip(timeline.BeatToMicroseconds(source.Beat(n.StartTick)), timeline.BeatToMicroseconds(source.Beat(n.EndTick)), startMs, endMs, out var start, out var length)) continue;
            notes.Add(new(MidiSource.StableId($"reference:{n.VoiceId}"), n.Id, 0, length > 0 ? NoteAction.Hold : NoteAction.Tap, start, length));
            tones[n.Id] = new(n.Pitch, n.Velocity, "SineReference", 0, 0, true);
        }
        return new(new(notes), tones, unpitched
            ? new[] { "SineReferenceNotOriginalMidiTimbre", "UnsupportedControllersNotRendered", "UnpitchedPercussionPreviewUnavailable" }
            : new[] { "SineReferenceNotOriginalMidiTimbre", "UnsupportedControllersNotRendered" });
    }

    public static MidiAuditionPlan Mapped(SmapProject project, IEnumerable<string>? voiceIds = null, double startMs = 0, double? endMs = null)
    {
        ProjectValidation.Validate(project);
        var conversion = project.MidiConversion ?? throw new InvalidDataException("工程没有 MIDI 转换数据。");
        var selection = voiceIds == null ? null : Selection(conversion.Source, voiceIds)
            .Select(id => MidiSource.StableId($"{conversion.Source.Sha256}:track:{id}")).ToHashSet();
        return BuildMapped(project, selection, startMs, endMs);
    }

    public static MidiAuditionPlan Edited(SmapProject project, double startMs = 0, double? endMs = null)
    {
        ProjectValidation.Validate(project); return BuildMapped(project, null, startMs, endMs);
    }

    static MidiAuditionPlan BuildMapped(SmapProject project, HashSet<Guid>? selection, double startMs, double? endMs)
    {
        ValidateRegion(startMs, endMs); var tones = new Dictionary<Guid, AuditionTone>();
        var timeline = new ProjectTimeline(project.Tempos); var notes = new List<TimedProjectNote>(); var capabilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in project.AudibleTracks()) foreach (var n in track.Notes)
        {
            if (selection != null && !selection.Contains(track.Id)) continue;
            if (!Clip(timeline.BeatToMicroseconds(n.Beat), timeline.BeatToMicroseconds(n.Beat + n.LengthBeats), startMs, endMs, out var start, out var length)) continue;
            if (track.GameTarget == null && track.Capability == null) continue;
            var resolved = PerformanceCompatibility.Evaluate(track, n);
            if (resolved.Availability == KeyAvailability.Unavailable || resolved.Key == null) throw new InvalidDataException("PerformanceCompatibility: K" + n.Key);
            if (resolved.Key.MidiPitch is not { } target)
            { capabilities.Add("UnpitchedPercussionPreviewUnavailable"); continue; }
            int sampleKey = resolved.Key.PreviewSample;
            int preview = (track.PreviewSemitoneOffset ?? PitchConfig.Get(track.PreviewInstrument)) - PitchConfig.DefaultOffset(track.PreviewInstrument);
            if (preview is < -48 or > 48) throw new InvalidDataException("试听移调超出支持范围。");
            bool sample = AuditionSampleBank.SamplePitch(track.PreviewInstrument, sampleKey) is { };
            int shift = sample ? target - AuditionSampleBank.SamplePitch(track.PreviewInstrument, sampleKey)!.Value + preview : 0;
            if (target + preview is < 0 or > 127) throw new InvalidDataException("试听音高超出 MIDI 范围。");
            tones[n.Id] = new(target + preview, n.MidiOrigin?.Velocity ?? 100, track.PreviewInstrument, sampleKey, shift, !sample);
            capabilities.Add(sample ? "InstrumentSamplesWithIndependentPreviewTranspose" : "SineFallbackForUncalibratedSamples");
            notes.Add(new(track.Id, n.Id, n.Key, length > 0 ? NoteAction.Hold : NoteAction.Tap, start, length));
        }
        return new(new(notes), tones, capabilities.Order(StringComparer.Ordinal).ToArray());
    }

    static HashSet<string> Selection(MidiSource source, IEnumerable<string>? selection)
    {
        var all = source.Voices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var chosen = selection?.ToHashSet(StringComparer.Ordinal) ?? all;
        if (chosen.Except(all, StringComparer.Ordinal).Any()) throw new ArgumentException("试听声部不存在。", nameof(selection));
        return chosen;
    }
    static void ValidateRegion(double start, double? end)
    {
        if (!double.IsFinite(start) || start < 0 || end is { } value && (!double.IsFinite(value) || value <= start)) throw new ArgumentOutOfRangeException(nameof(start));
    }
    static bool Clip(decimal start, decimal end, double from, double? to, out decimal relativeStart, out decimal length)
    {
        decimal first = (decimal)from * 1000, last = to is { } t ? (decimal)t * 1000 : decimal.MaxValue;
        relativeStart = Math.Max(start, first) - first; length = Math.Max(0, Math.Min(end, last) - Math.Max(start, first));
        return start < last && (end > first || start == first);
    }
}
