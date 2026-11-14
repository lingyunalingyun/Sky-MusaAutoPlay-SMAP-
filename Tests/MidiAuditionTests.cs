using System.IO;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

public class MidiAuditionTests(ITestOutputHelper output)
{
    sealed class Voices : IMidiAuditionVoices
    {
        public readonly Dictionary<Guid, (Guid owner, AuditionTone tone)> Active = new();
        public readonly List<AuditionTone> Started = new();
        public void Start(Guid owner, Guid voice, AuditionTone tone, bool sustain) { Active[voice] = (owner, tone); Started.Add(tone); }
        public void Release(Guid voice) => Active.Remove(voice);
        public void StopOwner(Guid owner) { foreach (var id in Active.Where(v => v.Value.owner == owner).Select(v => v.Key).ToArray()) Active.Remove(id); }
    }
    static SmapProject Project()
    {
        var s = new MidiFixture(2).Note(60, 0, 1920, velocity: 80).Note(64, 240, 1200, channel: 2, track: 1).Read();
        return MidiProjectConverter.Create(SapmMapper.Convert(s, MidiFixture.Options(s)));
    }

    [Fact]
    public void OriginalReferencePreservesPitchesVelocityDurationsAndExplicitTimbreLimitation()
    {
        var p = Project(); var plan = MidiAudition.Original(p.MidiConversion!.Source); var voices = new Voices(); var clock = new ManualClock();
        var s = plan.CreateSession(clock, voices); s.Pump(); Assert.Equal(60, Assert.Single(voices.Started).Pitch);
        Assert.Equal(80, voices.Started[0].Velocity); clock.Milliseconds = 250; s.Pump(); Assert.Equal(2, voices.Active.Count);
        clock.Milliseconds = 1250; s.Pump(); Assert.Single(voices.Active); clock.Milliseconds = 2000; s.Pump(); Assert.Empty(voices.Active);
        Assert.Contains("SineReferenceNotOriginalMidiTimbre", plan.Capabilities); Assert.All(plan.Tones.Values, t => Assert.True(t.Reference));
    }

    [Fact]
    public void SameRegionComparisonClipsCrossingHoldsEquallyAndSupportsSingleOrMixedParts()
    {
        var p = Project(); var source = p.MidiConversion!.Source;
        var original = MidiAudition.Original(source, startMs: 500, endMs: 750); var mapped = MidiAudition.Mapped(p, startMs: 500, endMs: 750);
        Assert.Equal(2, original.Sequence.Notes.Count); Assert.Equal(2, mapped.Sequence.Notes.Count);
        Assert.All(original.Sequence.Notes.Concat(mapped.Sequence.Notes), n => { Assert.Equal(0, n.StartMicroseconds); Assert.Equal(250000, n.LengthMicroseconds); });
        Assert.Single(MidiAudition.Mapped(p, new[] { source.Voices[0].Id }).Tones);
        Assert.Throws<ArgumentException>(() => MidiAudition.Original(source, new[] { "missing" }));
    }

    [Fact]
    public void ReferenceAndMappedOwnIndependentVoicesAndPauseSeekStopReleaseState()
    {
        var p = Project(); var source = p.MidiConversion!.Source; var voices = new Voices(); var clock = new ManualClock();
        var a = MidiAudition.Original(source).CreateSession(clock, voices); var b = MidiAudition.Mapped(p).CreateSession(clock, voices);
        a.Pump(); b.Pump(); Assert.Equal(2, voices.Active.Count); a.Stop(); Assert.Single(voices.Active);
        clock.Milliseconds = 100; b.Pause(true); Assert.Empty(voices.Active); b.Pause(false); Assert.Single(voices.Active);
        b.Seek(1000); Assert.Equal(2, voices.Active.Count); b.Stop(); Assert.Empty(voices.Active);
    }

    [Fact]
    public void MappedPreviewRetargetsSamplesWithoutChangingGamePitchOrPerformanceIdentity()
    {
        var p = Project(); var voice = p.MidiConversion!.Voices.Select(v => v with { Profile = GamePitchProfiles.Get("Violin", 2) }).ToArray();
        p = MidiProjectConverter.Remap(p, voice); var originalHash = PerformanceIdentity.ForProject(p);
        var normal = MidiAudition.Mapped(p); var changed = p with { Tracks = p.Tracks.Select(t => t with { PreviewSemitoneOffset = 12 }).ToList() };
        var transposed = MidiAudition.Mapped(changed); Assert.Equal(originalHash, PerformanceIdentity.ForProject(changed));
        foreach (var pair in normal.Tones) Assert.Equal(pair.Value.Pitch + 12, transposed.Tones[pair.Key].Pitch);
        Assert.All(normal.Tones.Values, t => Assert.False(t.Reference)); Assert.All(normal.Tones.Values, t => Assert.True(t.SampleShift > 0));
    }

    [Fact]
    public void SamplePreparationIsReusedAndBoundedWithoutOpeningAnAudioDevice()
    {
        var p = Project(); var plan = MidiAudition.Mapped(p); plan.PrepareSamples(); long bytes = plan.PreparedSampleBytes; int count = plan.PreparedSampleCount;
        plan.PrepareSamples(); Assert.Equal(bytes, plan.PreparedSampleBytes); Assert.Equal(count, plan.PreparedSampleCount);
        Assert.InRange(bytes, 1, AuditionSampleBank.MaxBytes); Assert.Equal(2, count);
        output.WriteLine($"PREVIEW_CACHE sounds={count}; bytes={bytes}; reused=True; limit_bytes={AuditionSampleBank.MaxBytes}");
        var tooMany = Enumerable.Range(0, 300).Select(i => new AuditionTone(60, 100, "Piano", i % 15, i / 15, false));
        Assert.Throws<InvalidDataException>(() => new AuditionSampleBank(tooMany));
    }

    [Fact]
    public void ReferenceProviderEmitsActualRequestedPitchAndFinishesRelease()
    {
        var provider = new ReferenceToneProvider(69, 100); var samples = new float[44100 * 2];
        Assert.Equal(samples.Length, provider.Read(samples, 0, samples.Length)); int crossings = 0;
        for (int i = 442; i < samples.Length; i += 2) if (samples[i - 2] <= 0 && samples[i] > 0) crossings++;
        double hz = crossings / ((samples.Length - 442) / 2.0 / 44100);
        Assert.InRange(hz, 438, 442); provider.Stop = true; Assert.InRange(provider.Read(samples, 0, samples.Length), 1, 1800);
        Assert.Equal(0, provider.Read(samples, 0, samples.Length));
    }

    [Fact]
    public void UnsupportedSampleTimbreAndUnpitchedPreviewAreExplicitCapabilities()
    {
        var p = Project(); var options = p.MidiConversion!.Voices.Select(v => v with { Profile = GamePitchProfiles.Get("Harmonica") }).ToArray();
        var fallback = MidiAudition.Mapped(MidiProjectConverter.Remap(p, options));
        Assert.Contains("SineFallbackForUncalibratedSamples", fallback.Capabilities); Assert.All(fallback.Tones.Values, t => Assert.True(t.Reference));
        var source = new MidiFixture().Note(38, 0, 480, channel: 10).Read(); var drums = MidiFixture.Options(source, "Dundun"); drums[0].ManualKeys[source.Notes[0].Id] = 1;
        var plan = MidiAudition.Mapped(MidiProjectConverter.Create(SapmMapper.Convert(source, drums)));
        Assert.Empty(plan.Tones); Assert.Contains("UnpitchedPercussionPreviewUnavailable", plan.Capabilities);
        var original = MidiAudition.Original(source); Assert.Empty(original.Tones); Assert.Contains("UnpitchedPercussionPreviewUnavailable", original.Capabilities);
    }

    [Fact]
    public void InvalidComparisonRegionFailsBeforeAnyOutput()
    {
        var p = Project(); Assert.Throws<ArgumentOutOfRangeException>(() => MidiAudition.Original(p.MidiConversion!.Source, startMs: 1, endMs: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MidiAudition.Mapped(p, startMs: double.NaN));
    }
}
