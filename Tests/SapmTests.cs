using System.Globalization;
using System.IO;
using System.Text.Json;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

public class SapmTests(ITestOutputHelper output)
{
    static MidiSource Melody(params int[] pitches)
    { var f = new MidiFixture(); for (int i = 0; i < pitches.Length; i++) f.Note(pitches[i], i * 480, (i + 1) * 480); return f.Read(); }

    [Fact]
    public void DetectsDKeyFromWeightedPitchesInsteadOfCWhiteKeyMembership()
    {
        var s = Melody(62, 66, 69, 74, 73, 69, 66, 62, 62); var keys = SapmMapper.AnalyzeKey(s);
        Assert.Equal(2, keys[0].Tonic); Assert.False(keys[0].Minor);
        Assert.True(keys[0].Score > keys.Single(k => k.Tonic == 0 && !k.Minor).Score);
        var recommended = SapmMapper.Recommend(s, "Piano")[0];
        Assert.Equal(2, recommended.Profile.Tonic); Assert.Contains("WeightedKeyFit", recommended.Reason);
        Assert.Contains("Source=", recommended.Reason);
    }

    [Fact]
    public void KeyAnalysisIncludesDurationsVelocityStrongBeatsAndBassEvidence()
    {
        var f = new MidiFixture(); f.Note(62, 0, 3840, velocity: 127).Note(69, 3840, 7680, velocity: 127);
        for (int i = 0; i < 20; i++) f.Note(61, i * 10 + 1, i * 10 + 2, channel: 2, velocity: 1);
        var tonal = SapmMapper.AnalyzeKey(f.Read()); Assert.True(tonal[0].InScaleWeight > .9);
        Assert.True(tonal[0].Score > tonal.Single(k => k.Tonic == 1 && !k.Minor).Score);
    }

    [Fact]
    public void StrictModeLeavesUnavailableChromaticNotesUnmappedAndTraceable()
    {
        var s = Melody(60, 61, 62); var r = SapmMapper.Convert(s, MidiFixture.Options(s, mode: SapmMode.Strict));
        Assert.Equal(2, r.Quality!.Exact); Assert.Equal(1, r.Quality.Unplayable); Assert.Equal(0, r.Quality.Approximate);
        var missing = r.Notes.Single(n => n.OriginalPitch == 61); Assert.Null(missing.Key); Assert.Contains("Unplayable", missing.Reason);
    }

    [Fact]
    public void RegisterOptimizationMovesWholePhraseWithoutIndependentOctaveFolds()
    {
        var s = Melody(83, 84, 86, 88, 89, 91); var r = SapmMapper.Convert(s, MidiFixture.Options(s, auto: true));
        var pitches = r.Notes.Select(n => n.TargetPitch!.Value).ToArray();
        Assert.Equal(s.Notes.Select(n => n.Pitch - 12), pitches); Assert.Equal(-12, Assert.Single(r.AutoShifts).Value);
        Assert.Equal(6, r.Quality!.Octave); Assert.Equal(1, r.Quality.DirectionPreservation); Assert.Equal(0, r.Quality.MeanIntervalError);
    }

    [Theory]
    [InlineData(SapmMode.Melody)] [InlineData(SapmMode.Harmony)]
    public void ModesPreserveMajorTriadQualityAndBassOrdering(SapmMode mode)
    {
        var f = new MidiFixture(2); for (int i = 0; i < 4; i++)
        { f.Note(60, i * 480, i * 480 + 470).Note(64, i * 480, i * 480 + 470).Note(67, i * 480, i * 480 + 470).Note(48, i * 480, i * 480 + 470, channel: 2, track: 1); }
        var s = f.Read(); var v = MidiFixture.Options(s, mode: mode);
        v[Array.FindIndex(v, o => s.Voices.Single(x => x.Id == o.VoiceId).Track == 1)] = v.Single(o => s.Voices.Single(x => x.Id == o.VoiceId).Track == 1) with { Profile = GamePitchProfiles.Get("Harp") };
        var r = SapmMapper.Convert(s, v); Assert.Equal(16, r.Quality!.Exact); Assert.Equal(100, r.Quality.HarmonyFidelity);
        Assert.True(r.Quality.HarmonyPairs > 0);
    }

    [Fact]
    public void ChromaticSubstitutionsHavePerNoteSourceAndReason()
    {
        var s = Melody(60, 61, 62, 63, 64, 65, 66, 67); var r = SapmMapper.Convert(s, MidiFixture.Options(s));
        Assert.Equal(s.Notes.Count, r.Notes.Count); Assert.True(r.Quality!.Approximate > 0);
        Assert.All(r.Notes, n => { Assert.Contains(s.Notes, o => o.Id == n.SourceId && o.Pitch == n.OriginalPitch); Assert.NotEmpty(n.Reason); Assert.InRange(n.Key!.Value, 0, 14); });
    }

    [Fact]
    public void ManualMappingOverridesOneOriginalNoteAndRecomputesQuality()
    {
        var s = Melody(60, 62); var v = MidiFixture.Options(s, mode: SapmMode.Strict); v[0].ManualKeys[s.Notes[0].Id] = 14;
        var r = SapmMapper.Convert(s, v); Assert.Equal(84, r.Notes[0].TargetPitch); Assert.True(r.Notes[0].Manual);
        Assert.Equal(1, r.Quality!.Octave); Assert.Equal(1, r.Quality.Exact);
        v[0].ManualKeys[s.Notes[0].Id] = null; r = SapmMapper.Convert(s, v); Assert.Equal(1, r.Quality!.Unplayable);
    }

    [Fact]
    public void ExportSelectionAndIndependentVoiceParametersAreRetained()
    {
        var s = new MidiFixture(2).Note(60, 0, 480).Note(48, 0, 480, channel: 2, track: 1).Read(); var v = MidiFixture.Options(s);
        v[1] = v[1] with { Included = false }; var r = SapmMapper.Convert(s, v, SapmScope.IndependentVoices);
        Assert.Equal(1, r.Quality!.Excluded); Assert.Null(r.Notes.Single(n => n.Kind == PitchMappingKind.Excluded).Key);
        v[1] = v[1] with { Included = true, SemitoneShift = 2, OctaveShift = 1, Profile = GamePitchProfiles.Get("Harp", 2), Mode = SapmMode.Strict };
        r = SapmMapper.Convert(s, v); Assert.Equal(62, r.Notes.Single(n => n.OriginalPitch == 48).TargetPitch);
    }

    [Fact]
    public void WholeSongCoordinatesChromaticTransposeWhileIndependentVoicesCanDiffer()
    {
        var f = new MidiFixture(2); for (int i = 0; i < 4; i++) { f.Note(61, i * 480, i * 480 + 240).Note(66, i * 480, i * 480 + 240, channel: 2, track: 1); }
        var s = f.Read(); var v = MidiFixture.Options(s, auto: true);
        var joint = SapmMapper.Convert(s, v); var independent = SapmMapper.Convert(s, v, SapmScope.IndependentVoices);
        Assert.Single(joint.AutoShifts.Values.Select(x => (x % 12 + 12) % 12).Distinct());
        Assert.Equal(2, independent.AutoShifts.Count);
        Assert.Equal(SapmScope.IndependentVoices, independent.Scope);
    }

    [Theory]
    [InlineData("Piano", 60)] [InlineData("Harp", 48)] [InlineData("Cello", 36)] [InlineData("Contrabass", 24)] [InlineData("Winter Piano", 72)]
    public void ProfilesHaveInstrumentSpecificOctavesIndependentOfPreviewPitch(string instrument, int low)
    {
        var profile = GamePitchProfiles.Get(instrument); profile.Validate(); Assert.Equal(low, profile.Lowest);
        Assert.Equal(low + 24, profile.Highest); Assert.Equal(15, profile.KeyCount); Assert.Equal(PitchVerification.WikiReported, profile.Verification);
        Assert.Equal(low, profile.Target().KeyMidiPitches[0]); Assert.Equal(1, profile.Version); Assert.NotEmpty(profile.Sources);
    }

    [Fact]
    public void PercussionIsDistinctAndAmbiguousBellDataRequiresExplicitConsent()
    {
        var handpan = GamePitchProfiles.Get("Handpan"); Assert.Equal(8, handpan.KeyCount); Assert.Equal(50, handpan.Lowest);
        Assert.Equal(45, GamePitchProfiles.Get("Handpan", 7).Lowest);
        var drums = GamePitchProfiles.Get("Dundun"); Assert.Equal(GameInstrumentKind.UnpitchedPercussion, drums.Kind); Assert.Null(drums.Lowest);
        var s = Melody(60); var v = MidiFixture.Options(s, "APBell1"); Assert.Throws<InvalidDataException>(() => SapmMapper.Convert(s, v));
        v[0] = v[0] with { AllowUnverified = true }; Assert.NotNull(SapmMapper.Convert(s, v));
        Assert.Throws<ArgumentException>(() => GamePitchProfiles.Get("Unknown instrument"));
    }

    [Fact]
    public void MidiDrumChannelRequiresExplicitPadMappingInsteadOfTreatingDrumNumbersAsPitches()
    {
        var s = new MidiFixture().Note(38, 0, 100, channel: 10).Read(); var v = MidiFixture.Options(s, "Dundun");
        var r = SapmMapper.Convert(s, v); Assert.Equal(1, r.Quality!.Unplayable);
        v[0].ManualKeys[s.Notes[0].Id] = 2; r = SapmMapper.Convert(s, v);
        Assert.Equal(2, r.Notes[0].Key); Assert.Null(r.Notes[0].TargetPitch); Assert.True(r.Notes[0].Manual);
    }

    [Fact]
    public void RepeatedMappingHasIdenticalSerializedResultsAcrossCultures()
    {
        var s = Melody(60, 61, 62, 67, 66); var options = MidiFixture.Options(s, auto: true);
        string a = JsonSerializer.Serialize(SapmMapper.Convert(s, options)); string recommendation = JsonSerializer.Serialize(SapmMapper.Recommend(s, "Piano")); var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(a, JsonSerializer.Serialize(SapmMapper.Convert(s, options)));
            Assert.Equal(recommendation, JsonSerializer.Serialize(SapmMapper.Recommend(s, "Piano")));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void QualityFormulaIsReproducibleAndDoesNotCallTranspositionLossless()
    {
        var s = Melody(48, 50, 52, 53); var r = SapmMapper.Convert(s, MidiFixture.Options(s, auto: true)); var q = r.Quality!;
        Assert.Equal(0, q.Exact); Assert.Equal(4, q.Octave); Assert.Equal(12, q.MeanAbsolutePitchError);
        double coverage = (q.Total - q.Unplayable - q.Excluded) / (double)(q.Total - q.Excluded);
        Assert.Equal(100 * coverage * (.5 * Math.Exp(-q.MeanIntervalError / 6) + .5 * q.DirectionPreservation), q.MelodyFidelity, 8);
        Assert.Equal(q.Total, q.Exact + q.Octave + q.Approximate + q.Unplayable + q.Excluded);
    }

    [Fact]
    public void SyntheticQualityComparisonRecordsActualValuesAgainstIndependentOctaveFolding()
    {
        var s = Melody(82, 84, 86, 88, 89, 91, 89, 88, 86, 84); var v = MidiFixture.Options(s, auto: true);
        var optimized = SapmMapper.Convert(s, v); var profile = GamePitchProfiles.Get("Piano"); var baseline = MidiFixture.Options(s);
        foreach (var n in s.Notes)
        {
            int pitch = n.Pitch; while (pitch > 84) pitch -= 12; while (pitch < 60) pitch += 12;
            baseline[0].ManualKeys[n.Id] = Enumerable.Range(0, 15).OrderBy(k => Math.Abs(profile.Pitches[k]!.Value - pitch)).ThenBy(k => k).First();
        }
        var naive = SapmMapper.Convert(s, baseline);
        Assert.True(optimized.Quality!.MelodyFidelity > naive.Quality!.MelodyFidelity);
        output.WriteLine(FormattableString.Invariant($"QUALITY fixture=upper_register; optimized_melody={optimized.Quality.MelodyFidelity:F6}; baseline_melody={naive.Quality.MelodyFidelity:F6}; optimized_interval_error={optimized.Quality.MeanIntervalError:F6}; baseline_interval_error={naive.Quality.MeanIntervalError:F6}; optimized_direction={optimized.Quality.DirectionPreservation:F6}; baseline_direction={naive.Quality.DirectionPreservation:F6}"));
    }

    [Fact]
    public void ChromaticMajorTriadKeepsItsStructureThroughCoordinatedTranspose()
    {
        var s = new MidiFixture().Note(61, 0, 480).Note(65, 0, 480).Note(68, 0, 480).Read();
        var optimized = SapmMapper.Convert(s, MidiFixture.Options(s, mode: SapmMode.Harmony, auto: true));
        var baseline = MidiFixture.Options(s); var pitches = baseline[0].Profile.Pitches;
        foreach (var n in s.Notes) baseline[0].ManualKeys[n.Id] = Enumerable.Range(0, 15).OrderBy(k => Math.Abs(pitches[k]!.Value - n.Pitch)).ThenBy(k => k).First();
        var naive = SapmMapper.Convert(s, baseline); var mapped = optimized.Notes.Select(n => n.TargetPitch!.Value).Order().ToArray();
        Assert.Equal(4, mapped[1] - mapped[0]); Assert.Equal(7, mapped[2] - mapped[0]);
        Assert.True(optimized.Quality!.HarmonyFidelity > naive.Quality!.HarmonyFidelity); Assert.Equal(3, optimized.Quality.Approximate);
        output.WriteLine(FormattableString.Invariant($"QUALITY fixture=chromatic_major_triad; optimized_harmony={optimized.Quality.HarmonyFidelity:F6}; baseline_harmony={naive.Quality.HarmonyFidelity:F6}; exact={optimized.Quality.Exact}; approximate={optimized.Quality.Approximate}; lossless=False"));
    }

    [Fact]
    public void DrumNumbersDoNotInfluenceMusicalKeyRegisterOrFidelityMetrics()
    {
        var source = new MidiFixture(2).Note(60, 0, 480).Note(64, 480, 960).Note(60, 0, 960, channel: 10, track: 1).Read();
        var options = MidiFixture.Options(source, auto: true); var drum = source.Voices.Single(v => v.Channel == 10);
        var option = options.Single(v => v.VoiceId == drum.Id); option.ManualKeys[source.Notes.Single(n => n.Channel == 10).Id] = 0;
        var result = SapmMapper.Convert(source, options); var mappedDrum = result.Notes.Single(n => n.VoiceId == drum.Id);
        Assert.Equal(PitchMappingKind.Approximate, mappedDrum.Kind); Assert.Equal(0, result.AutoShifts[drum.Id]);
        Assert.Equal(1, result.Quality!.IntervalPairs); Assert.Equal(60, result.Quality.OriginalLow); Assert.Equal(64, result.Quality.OriginalHigh);
    }

    [Fact]
    public void CanceledConversionNeverReturnsAPartialResultOrChangesOriginalNotes()
    {
        var source = Melody(60, 62, 64); string before = JsonSerializer.Serialize(source); using var cancellation = new CancellationTokenSource();
        var options = MidiFixture.Options(source);
        IEnumerable<SapmVoiceOptions> CancelWhileSelecting() { foreach (var v in options) { yield return v; cancellation.Cancel(); } }
        Assert.Throws<OperationCanceledException>(() => SapmMapper.Convert(source, CancelWhileSelecting(), cancellationToken: cancellation.Token));
        Assert.Equal(before, JsonSerializer.Serialize(source));
        Assert.Throws<OperationCanceledException>(() => SapmMapper.Recommend(source, "Piano", cancellation.Token));
    }

    [Fact]
    public void DrumDensityDoesNotChangeTargetKeyRecommendation()
    {
        var melody = new MidiFixture(2).Note(62, 0, 480).Note(66, 480, 960).Note(69, 960, 1440);
        var original = SapmMapper.Recommend(melody.Read(), "Piano");
        for (int i = 0; i < 120; i++) melody.Note(35, i * 10, i * 10 + 5, channel: 10, track: 1);
        var withDrums = SapmMapper.Recommend(melody.Read(), "Piano");
        Assert.Equal(original.Select(r => (r.Profile.Id, r.Transpose, r.Cost)), withDrums.Select(r => (r.Profile.Id, r.Transpose, r.Cost)));
    }
}
