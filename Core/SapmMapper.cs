using System.IO;

namespace SMAP_WPF.Core;

public static class SapmMapper
{
    const int HarmonyContext = 32;
    static int Pc(int n) => (n % 12 + 12) % 12;
    static double PcError(int a, int b) { int d = Math.Abs(Pc(a) - Pc(b)); return Math.Min(d, 12 - d); }
    static int ManualShift(SapmVoiceOptions v) => v.SemitoneShift + v.OctaveShift * 12;

    static Dictionary<Guid, double> Weights(MidiSource source)
    {
        var weights = new Dictionary<Guid, double>(); int meterIndex = 0;
        foreach (var group in source.Notes.OrderBy(n => n.StartTick).GroupBy(n => n.StartTick))
        {
            while (meterIndex + 1 < source.Meters.Count && source.Meters[meterIndex + 1].Tick <= group.Key) meterIndex++;
            var meter = source.Meters[meterIndex];
            decimal beat = source.Beat(group.Key - meter.Tick), bar = meter.Numerator * 4m / meter.Denominator;
            double strong = beat % bar == 0 ? 1.5 : beat % (4m / meter.Denominator) == 0 ? 1.15 : 1;
            var pitched = group.Where(n => n.Channel != 10).ToArray();
            int low = pitched.Length == 0 ? -1 : pitched.Min(n => n.Pitch), high = pitched.Length == 0 ? -1 : pitched.Max(n => n.Pitch);
            foreach (var n in group)
                weights[n.Id] = (0.5 + Math.Min(8, (double)source.Beat(n.EndTick - n.StartTick)))
                    * (0.5 + n.Velocity / 127.0) * strong * (n.Pitch == high ? 1.4 : 1) * (n.Pitch == low ? 1.2 : 1);
        }
        return weights;
    }

    public static IReadOnlyList<TonalCandidate> AnalyzeKey(MidiSource source)
    {
        source.Validate(); var weight = Weights(source); var histogram = new double[12]; var bass = new double[12];
        foreach (var n in source.Notes.Where(n => n.Channel != 10)) histogram[Pc(n.Pitch)] += weight[n.Id];
        foreach (var group in source.Notes.Where(n => n.Channel != 10).GroupBy(n => n.StartTick))
        { var n = group.OrderBy(n => n.Pitch).ThenBy(n => n.Id).First(); bass[Pc(n.Pitch)] += weight[n.Id]; }
        double total = histogram.Sum(); var results = new List<TonalCandidate>();
        foreach (bool minor in new[] { false, true }) for (int tonic = 0; tonic < 12; tonic++)
        {
            var scale = (minor ? new[] { 0, 2, 3, 5, 7, 8, 10 } : new[] { 0, 2, 4, 5, 7, 9, 11 }).Select(p => Pc(p + tonic)).ToHashSet();
            double inside = Enumerable.Range(0, 12).Where(scale.Contains).Sum(p => histogram[p]);
            double tonicWeight = histogram[tonic] + bass[tonic] * .5;
            // 权重包含次数、时值、力度、重拍、最高旋律音和低音；主音/属音额外参与排序。
            double score = total == 0 ? 0 : (inside - (total - inside) * 1.5 + tonicWeight * .2
                + histogram[Pc(tonic + 7)] * .08) / total;
            results.Add(new(tonic, minor, score, total == 0 ? 0 : inside / total, tonicWeight));
        }
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Minor).ThenBy(r => r.Tonic).ToArray();
    }

    public static IReadOnlyList<KeyRecommendation> Recommend(MidiSource source, string instrument, CancellationToken cancellationToken = default)
    {
        source.Validate(); var weight = Weights(source); var tonal = AnalyzeKey(source);
        var pitchedNotes = source.Notes.Where(n => n.Channel != 10).ToArray();
        return Enumerable.Range(0, 12).Select(tonic =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profile = GamePitchProfiles.Get(instrument, tonic);
            if (profile.Kind == GameInstrumentKind.UnpitchedPercussion || profile.Verification == PitchVerification.ConflictingSources)
                return new KeyRecommendation(profile, 0, double.MaxValue, "RequiresManualVerification");
            var option = new SapmVoiceOptions { Profile = profile };
            var best = Enumerable.Range(-6, 12).Select(s => Register(pitchedNotes, option, weight, s)).OrderBy(s => s.cost).ThenBy(s => Math.Abs(s.shift)).ThenBy(s => s.shift).First();
            double keyFit = tonal.First(t => t.Tonic == tonic && !t.Minor).Score;
            return new KeyRecommendation(profile, best.shift, best.cost - keyFit * .3,
                FormattableString.Invariant($"WeightedKeyFit={keyFit:R};RegisterCost={best.cost:R};Transpose={best.shift};Source={profile.KeySource}"));
        }).OrderBy(r => r.Cost).ThenBy(r => Math.Abs(r.Transpose)).ThenBy(r => r.Profile.Tonic).ToArray();
    }

    static (int shift, double cost) Register(IReadOnlyList<MidiSourceNote> notes, SapmVoiceOptions option, Dictionary<Guid, double> weight, int semitone)
    {
        var pitched = option.Profile.Pitches.Where(p => p.HasValue).Select(p => p!.Value).ToArray();
        if (pitched.Length == 0 || notes.Count == 0) return (0, 0);
        var histogram = new double[128]; foreach (var n in notes) histogram[n.Pitch] += weight[n.Id];
        double total = histogram.Sum(), center = Enumerable.Range(0, 128).Sum(p => p * histogram[p]) / total;
        int oct = Math.Clamp((int)Math.Round(((pitched.Min() + pitched.Max()) / 2.0 - center - ManualShift(option) - semitone) / 12, MidpointRounding.ToEven), -8, 8);
        return Enumerable.Range(Math.Max(-8, oct - 1), Math.Min(8, oct + 1) - Math.Max(-8, oct - 1) + 1).Select(o =>
        {
            int shift = semitone + o * 12; double cost = 0;
            for (int p = 0; p < 128; p++) if (histogram[p] > 0)
            {
                int requested = p + shift + ManualShift(option);
                cost += histogram[p] * pitched.Min(t => Math.Abs(t - requested) * .8 + PcError(t, requested) * 2.5);
            }
            return (shift, cost: cost / total + Math.Abs(semitone) * .04 + Math.Abs(o) * .03);
        }).OrderBy(r => r.cost).ThenBy(r => Math.Abs(r.shift)).ThenBy(r => r.shift).First();
    }

    public static MidiConversion Convert(MidiSource source, IEnumerable<SapmVoiceOptions> settings, SapmScope scope = SapmScope.WholeSong, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source.Validate(); if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        var supplied = settings.ToDictionary(v => v.VoiceId, StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        if (supplied.Keys.Except(source.Voices.Select(v => v.Id), StringComparer.Ordinal).Any()) throw new ArgumentException("映射声部不存在。", nameof(settings));
        var options = source.Voices.Select(v => supplied.TryGetValue(v.Id, out var option) ? option
            : new SapmVoiceOptions { VoiceId = v.Id, Included = false }).ToDictionary(v => v.VoiceId, StringComparer.Ordinal);
        foreach (var v in options.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            v.Profile.Validate();
            if (!Enum.IsDefined(v.Mode) || v.SemitoneShift is < -48 or > 48 || v.OctaveShift is < -8 or > 8 || v.ManualKeys == null)
                throw new ArgumentException("映射参数超出范围。", nameof(settings));
            if (v.Included && v.Profile.Verification == PitchVerification.ConflictingSources && !v.AllowUnverified)
                throw new InvalidDataException("目标音高资料存在冲突，请先核对或明确采用该配置。");
            var memberIds = source.Notes.Where(n => n.VoiceId == v.VoiceId).Select(n => n.Id).ToHashSet();
            if (v.ManualKeys.Any(k => !memberIds.Contains(k.Key) || (k.Value is { } key && (key < 0 || key >= v.Profile.KeyCount))))
                throw new ArgumentException("手动映射的音符或琴键无效。", nameof(settings));
        }
        var weight = Weights(source); var byVoice = source.Notes.GroupBy(n => n.VoiceId).ToDictionary(g => g.Key, g => (IReadOnlyList<MidiSourceNote>)g.ToArray(), StringComparer.Ordinal);
        var melodicVoices = source.Voices.Where(v => v.Channel != 10).Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var automatic = options.Values.Where(v => v.Included && v.AutoTranspose && melodicVoices.Contains(v.VoiceId) && v.Profile.Kind != GameInstrumentKind.UnpitchedPercussion).ToArray();
        var shifts = options.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        if (automatic.Length > 0)
        {
            int common = Enumerable.Range(-6, 12).Select(s => (s, cost: automatic.Sum(v => Register(byVoice[v.VoiceId], v, weight, s).cost * byVoice[v.VoiceId].Count)))
                .OrderBy(r => r.cost).ThenBy(r => Math.Abs(r.s)).ThenBy(r => r.s).First().s;
            foreach (var v in automatic)
                shifts[v.VoiceId] = scope == SapmScope.WholeSong ? Register(byVoice[v.VoiceId], v, weight, common).shift
                    : Enumerable.Range(-6, 12).Select(s => Register(byVoice[v.VoiceId], v, weight, s)).OrderBy(s => s.cost).ThenBy(s => Math.Abs(s.shift)).ThenBy(s => s.shift).First().shift;
        }
        var keys = new Dictionary<Guid, int?>(); var strands = new List<MidiSourceNote[]>();
        foreach (var pair in byVoice.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var v = options[pair.Key];
            var parts = new List<List<MidiSourceNote>>();
            foreach (var chord in pair.Value.OrderBy(n => n.StartTick).GroupBy(n => n.StartTick))
            {
                int i = 0;
                foreach (var n in chord.OrderByDescending(n => n.Pitch).ThenBy(n => n.Id))
                {
                    if (i == parts.Count) parts.Add(new()); parts[i++].Add(n);
                    keys[n.Id] = null;
                }
            }
            foreach (var part in parts)
            {
                var strand = part.ToArray(); strands.Add(strand);
                if (!v.Included) continue;
                int shift = ManualShift(v) + shifts[v.VoiceId];
                var candidates = strand.Select(n => Candidates(n, v, shift)).ToArray();
                Optimize(strand, candidates, v, shift, weight, keys, cancellationToken);
            }
        }
        RefineHarmony(source, options, shifts, weight, strands, keys, cancellationToken);
        var mapped = source.Notes.Select(n =>
        {
            var v = options[n.VoiceId]; int requested = n.Pitch + ManualShift(v) + shifts[v.VoiceId];
            int? key = keys[n.Id], target = key is { } k ? v.Profile.Pitches[k] : null;
            var kind = !v.Included ? PitchMappingKind.Excluded : key == null ? PitchMappingKind.Unplayable
                : n.Channel == 10 ? PitchMappingKind.Approximate : target == n.Pitch ? PitchMappingKind.Exact : target.HasValue && Pc(target.Value) == Pc(n.Pitch) ? PitchMappingKind.Octave : PitchMappingKind.Approximate;
            string cause = !v.Included ? "Excluded" : v.ManualKeys.ContainsKey(n.Id) ? "Manual" : n.Channel == 10 || v.Profile.Kind == GameInstrumentKind.UnpitchedPercussion ? "Percussion"
                : key == null ? "Unavailable" : kind == PitchMappingKind.Exact ? "Exact"
                : target == requested ? "Transpose" : requested < v.Profile.Lowest || requested > v.Profile.Highest ? "Range"
                : !v.Profile.Pitches.Any(p => p.HasValue && Pc(p.Value) == Pc(requested)) ? "PitchClass" : "Context";
            string reason = $"{cause};kind={kind};original={n.Pitch};requested={requested};target={target};mode={v.Mode};auto={shifts[v.VoiceId]};manual={v.ManualKeys.ContainsKey(n.Id)}";
            if (n.Incomplete) reason += ";IncompleteSourceNote";
            if (n.Channel == 10 && !v.ManualKeys.ContainsKey(n.Id)) reason += ";PercussionNeedsManualMapping";
            return new MappedMidiNote(n.Id, n.VoiceId, n.Pitch, requested, key, target, kind, v.ManualKeys.ContainsKey(n.Id), reason);
        }).ToList();
        var result = new MidiConversion { Source = source, Scope = scope, Voices = options.Values.ToList(), AutoShifts = shifts,
            Notes = mapped, TonalAnalysis = AnalyzeKey(source).ToList(), Quality = Quality(source, mapped, strands, weight, options) };
        result.Validate(); return result;
    }

    static int[] Candidates(MidiSourceNote n, SapmVoiceOptions v, int shift)
    {
        if (v.ManualKeys.TryGetValue(n.Id, out int? key)) return key is { } k ? new[] { k } : Array.Empty<int>();
        if (n.Channel == 10 || v.Profile.Kind == GameInstrumentKind.UnpitchedPercussion) return Array.Empty<int>();
        return Enumerable.Range(0, v.Profile.KeyCount).Where(k => v.Mode != SapmMode.Strict || v.Profile.Pitches[k] == n.Pitch + shift).ToArray();
    }
    static double Node(MidiSourceNote n, int pitch, SapmVoiceOptions v, int shift, double weight) =>
        (Math.Abs(pitch - n.Pitch - shift) * (v.Mode == SapmMode.Harmony ? .65 : .8) + PcError(pitch, n.Pitch + shift) * 2.5) * weight;
    static double Transition(int original, int mapped, SapmMode mode)
    {
        double direction = Math.Sign(original) == Math.Sign(mapped) ? 0 : 6;
        return (Math.Abs(original - mapped) * 1.5 + direction + Math.Max(0, Math.Abs(mapped) - Math.Max(12, Math.Abs(original))) * 2)
            * (mode == SapmMode.Harmony ? .5 : 1);
    }

    static void Optimize(MidiSourceNote[] strand, int[][] candidates, SapmVoiceOptions v, int shift,
        Dictionary<Guid, double> weights, Dictionary<Guid, int?> keys, CancellationToken cancellationToken)
    {
        var valid = Enumerable.Range(0, strand.Length).Where(i => candidates[i].Length > 0).ToArray();
        if (valid.Length == 0) return;
        if (v.Profile.Kind == GameInstrumentKind.UnpitchedPercussion)
        { foreach (int i in valid) keys[strand[i].Id] = candidates[i][0]; return; }
        int count = v.Profile.KeyCount; var previous = new double[count]; Array.Fill(previous, double.PositiveInfinity);
        var back = new byte[checked(valid.Length * count)];
        for (int i = 0; i < valid.Length; i++)
        {
            if ((i & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            int index = valid[i]; var note = strand[index]; var current = new double[count]; Array.Fill(current, double.PositiveInfinity);
            foreach (int key in candidates[index])
            {
                int pitch = v.Profile.Pitches[key]!.Value; double best = i == 0 ? 0 : double.PositiveInfinity; int from = 0;
                if (i > 0) for (int prior = 0; prior < count; prior++)
                {
                    double cost = previous[prior] + Transition(note.Pitch - strand[valid[i - 1]].Pitch, pitch - v.Profile.Pitches[prior]!.Value, v.Mode);
                    if (cost < best) { best = cost; from = prior; }
                }
                current[key] = best + Node(note, pitch, v, shift, weights[note.Id]); back[i * count + key] = (byte)from;
            }
            previous = current;
        }
        int chosen = Enumerable.Range(0, count).OrderBy(k => previous[k]).ThenBy(k => k).First();
        for (int i = valid.Length - 1; i >= 0; i--) { keys[strand[valid[i]].Id] = chosen; chosen = back[i * count + chosen]; }
    }

    static double HarmonyCost(MidiSourceNote n, int pitch, MidiSourceNote peer, int other)
    {
        int original = n.Pitch - peer.Pitch, mapped = pitch - other;
        return PcError(mapped, original) + (Math.Sign(original) * Math.Sign(mapped) < 0 ? 4 : 0)
            + (original != 0 && mapped == 0 ? 4 : 0) + Math.Abs(original - mapped) * .1;
    }

    static void VisitHarmony(MidiSource source, Dictionary<Guid, double> weights, Action<MidiSourceNote[], MidiSourceNote[]> visit, Func<MidiSourceNote, bool> eligible)
    {
        var active = new SortedSet<(double priority, Guid id)>(); var expiry = new PriorityQueue<MidiSourceNote, long>();
        var notes = source.Notes.Where(n => n.Channel != 10 && eligible(n)).ToArray(); var lookup = notes.ToDictionary(n => n.Id);
        foreach (var onset in notes.OrderBy(n => n.StartTick).GroupBy(n => n.StartTick))
        {
            while (expiry.TryPeek(out var note, out long end) && end <= onset.Key)
            { expiry.Dequeue(); active.Remove((-weights[note.Id], note.Id)); }
            var group = onset.OrderBy(n => n.Pitch).ThenBy(n => n.Id).ToArray();
            var context = active.Take(HarmonyContext).Select(p => lookup[p.id]).Concat(group)
                .OrderByDescending(n => weights[n.Id]).ThenBy(n => n.Id).Take(HarmonyContext).ToArray();
            visit(group, context);
            foreach (var n in group) if (n.EndTick > n.StartTick) { active.Add((-weights[n.Id], n.Id)); expiry.Enqueue(n, n.EndTick); }
        }
    }

    static void RefineHarmony(MidiSource source, Dictionary<string, SapmVoiceOptions> options, Dictionary<string, int> shifts,
        Dictionary<Guid, double> weight, List<MidiSourceNote[]> strands, Dictionary<Guid, int?> keys, CancellationToken cancellationToken)
    {
        var neighbors = new Dictionary<Guid, (MidiSourceNote? before, MidiSourceNote? after)>();
        foreach (var strand in strands) for (int i = 0; i < strand.Length; i++) neighbors[strand[i].Id] = (i > 0 ? strand[i - 1] : null, i + 1 < strand.Length ? strand[i + 1] : null);
        int? Pitch(MidiSourceNote n) => keys[n.Id] is { } k ? options[n.VoiceId].Profile.Pitches[k] : null;
        for (int pass = 0; pass < 2; pass++) VisitHarmony(source, weight, (group, context) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var n in group)
            {
                var v = options[n.VoiceId];
                if (!v.Included || n.Channel == 10 || keys[n.Id] == null || v.Mode == SapmMode.Strict || v.ManualKeys.ContainsKey(n.Id)) continue;
                int shift = ManualShift(v) + shifts[v.VoiceId]; var near = neighbors[n.Id];
                double Cost(int key)
                {
                    int p = v.Profile.Pitches[key]!.Value; double cost = Node(n, p, v, shift, weight[n.Id]);
                    if (near.before is { } before && Pitch(before) is { } b) cost += Transition(n.Pitch - before.Pitch, p - b, v.Mode);
                    if (near.after is { } after && Pitch(after) is { } a) cost += Transition(after.Pitch - n.Pitch, a - p, v.Mode);
                    foreach (var peer in context) if (peer.Id != n.Id && peer.Channel != 10 && options[peer.VoiceId].Included && Pitch(peer) is { } other)
                        cost += HarmonyCost(n, p, peer, other) * (v.Mode == SapmMode.Harmony ? 3 : .6);
                    return cost;
                }
                keys[n.Id] = Candidates(n, v, shift).OrderBy(Cost).ThenBy(k => k).First();
            }
        }, n => options[n.VoiceId].Included && Pitch(n).HasValue);
    }

    static SapmQuality Quality(MidiSource source, List<MappedMidiNote> mapped, List<MidiSourceNote[]> strands,
        Dictionary<Guid, double> weights, Dictionary<string, SapmVoiceOptions> options)
    {
        var lookup = mapped.ToDictionary(n => n.SourceId); int intervals = 0, directions = 0, harmonyPairs = 0;
        double intervalError = 0, harmonyError = 0;
        foreach (var strand in strands) for (int i = 1; i < strand.Length; i++)
        {
            if (strand[i].Channel == 10) continue;
            var a = lookup[strand[i - 1].Id]; var b = lookup[strand[i].Id];
            if (a.TargetPitch is not { } p || b.TargetPitch is not { } q) continue;
            int original = b.OriginalPitch - a.OriginalPitch, final = q - p; intervals++;
            intervalError += Math.Abs(original - final); if (Math.Sign(original) == Math.Sign(final)) directions++;
        }
        VisitHarmony(source, weights, (group, peers) =>
        {
            foreach (var n in group) if (n.Channel != 10 && lookup[n.Id].TargetPitch is { } pitch)
                foreach (var peer in peers) if (peer.Id != n.Id && (peer.StartTick < n.StartTick || peer.Id.CompareTo(n.Id) < 0)
                    && peer.Channel != 10 && lookup[peer.Id].TargetPitch is { } other)
                { harmonyPairs++; harmonyError += HarmonyCost(n, pitch, peer, other); }
        }, n => lookup[n.Id].TargetPitch.HasValue);
        var melodicIds = source.Notes.Where(n => n.Channel != 10).Select(n => n.Id).ToHashSet();
        var included = mapped.Where(n => n.Kind != PitchMappingKind.Excluded && melodicIds.Contains(n.SourceId)).ToArray();
        double coverage = included.Length == 0 ? 0 : included.Count(n => n.TargetPitch.HasValue) / (double)included.Length;
        double meanInterval = intervals == 0 ? 0 : intervalError / intervals, direction = intervals == 0 ? 1 : directions / (double)intervals;
        // 评分范围 0..100；旋律 = 覆盖率×(0.5×exp(-平均音程误差/6)+0.5×方向保持率)。
        // 和声 = 覆盖率×exp(-平均和声代价/3)。无比较对时需结合 pair 数量阅读，不能解释为实测保真。
        return new(mapped.Count, mapped.Count(n => n.Kind == PitchMappingKind.Exact), mapped.Count(n => n.Kind == PitchMappingKind.Octave),
            mapped.Count(n => n.Kind == PitchMappingKind.Approximate), mapped.Count(n => n.Kind == PitchMappingKind.Unplayable), mapped.Count(n => n.Kind == PitchMappingKind.Excluded),
            source.Notes.Where(n => n.Channel != 10).Select(n => (int?)n.Pitch).Min(), source.Notes.Where(n => n.Channel != 10).Select(n => (int?)n.Pitch).Max(),
            options.Values.Where(v => v.Included).Select(v => v.Profile.Lowest).Min(), options.Values.Where(v => v.Included).Select(v => v.Profile.Highest).Max(),
            included.Where(n => n.TargetPitch.HasValue).Select(n => Math.Abs(n.TargetPitch!.Value - n.OriginalPitch)).DefaultIfEmpty().Average(),
            meanInterval, direction, coverage * (.5 * Math.Exp(-meanInterval / 6) + .5 * direction) * 100,
            coverage * Math.Exp(-(harmonyPairs == 0 ? 0 : harmonyError / harmonyPairs) / 3) * 100, intervals, harmonyPairs);
    }
}
