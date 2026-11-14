using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;
using System.IO;

namespace SMAP.Tests;

internal sealed class FakeAudioVoices : IAudioVoices
{
    public readonly Dictionary<Guid, (Guid owner, bool sustain)> Voices = new();
    public readonly List<PreviewSettings?> Settings = new();
    public void Start(Guid owner, Guid voice, int key, bool sustain, PreviewSettings? settings)
    { Voices[voice] = (owner, sustain); Settings.Add(settings); }
    public void Release(Guid voice) => Voices.Remove(voice);
    public void StopOwner(Guid owner)
    { foreach (var id in Voices.Where(v => v.Value.owner == owner).Select(v => v.Key).ToArray()) Voices.Remove(id); }
}

public class AudioLifecycleTests
{
    sealed class Samples(int count = int.MaxValue) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public int Read(float[] buffer, int offset, int requested)
        { int n = Math.Min(count, requested); count -= n; Array.Fill(buffer, 0.25f, offset, n); return n; }
    }

    [Fact]
    public void RealMixerVoiceRegistryPrunesCompletedSamplesAndStopsAllVoices()
    {
        var mixer = new MixingSampleProvider(new Samples().WaveFormat) { ReadFully = true }; var voices = new AudioVoiceMixer(mixer);
        var owner = Guid.NewGuid(); voices.Start(Guid.NewGuid(), owner, new Samples(2));
        var buffer = new float[64]; mixer.Read(buffer, 0, 64); mixer.Read(buffer, 0, 64); Assert.Equal(0, voices.Count);
        bool released = false; var id = Guid.NewGuid(); voices.Start(id, owner, new Samples(), () => released = true);
        voices.Release(id); Assert.True(released); Assert.Equal(1, voices.Count);
        voices.StopAll(); Assert.Equal(0, voices.Count); Array.Fill(buffer, 1f); mixer.Read(buffer, 0, buffer.Length);
        Assert.All(buffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public void StoppingAnOwnerPreservesOtherVoicesAndIndependentDryMetronome()
    {
        var mixer = new MixingSampleProvider(new Samples().WaveFormat) { ReadFully = true }; var voices = new AudioVoiceMixer(mixer);
        Guid one = Guid.NewGuid(), two = Guid.NewGuid();
        voices.Start(Guid.NewGuid(), one, new Samples()); voices.Start(Guid.NewGuid(), two, new Samples());
        voices.StopOwner(one); Assert.Equal(1, voices.Count);
        var dry = new MixingSampleProvider(new Samples().WaveFormat) { ReadFully = true };
        dry.AddMixerInput(mixer); dry.AddMixerInput(new Samples()); voices.StopAll();
        var buffer = new float[16]; dry.Read(buffer, 0, buffer.Length); Assert.All(buffer, n => Assert.Equal(0.25f, n));
    }

    [Fact]
    public void TapReleaseDoesNotCutOneShotAndVoiceIdReplacementRemovesOldSource()
    {
        var mixer = new MixingSampleProvider(new Samples().WaveFormat) { ReadFully = true }; var voices = new AudioVoiceMixer(mixer);
        var id = Guid.NewGuid(); var owner = Guid.NewGuid(); voices.Start(id, owner, new Samples()); voices.Release(id);
        Assert.Equal(1, voices.Count); voices.Start(id, owner, new Samples()); Assert.Equal(1, voices.Count);
        var buffer = new float[16]; mixer.Read(buffer, 0, 16); Assert.All(buffer, n => Assert.Equal(0.25f, n)); voices.StopAll();
    }

    [Fact]
    public void GameAndPreviewUseSeparateOutputsAndStoppingPreviewDoesNotSendKeys()
    {
        var sequence = new PlaybackSequence(new[] { PlaybackFixture.Note(0, 0, 1000) });
        var clock = new ManualClock(); var input = new RecordingInput(clock); var game = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        var audio = new FakeAudioVoices(); var preview = new AudioNoteOutput(sequence, audio);
        var session = new PlaybackSession(sequence, clock, new INoteOutput[] { game, preview }); session.Pump();
        Assert.Single(input.Pressed); Assert.Single(audio.Voices); preview.StopAll();
        Assert.Single(input.Pressed); Assert.Empty(audio.Voices); session.Stop(); Assert.Empty(input.Pressed);
        int count = input.Events.Count;
        session = new(sequence, clock, new[] { new AudioNoteOutput(sequence, audio) }); session.Pump(); session.Stop();
        Assert.Equal(count, input.Events.Count); Assert.Empty(audio.Voices);
    }

    [Fact]
    public void PerTrackPreviewPitchIsPassedWithoutChangingGameKeys()
    {
        var sequence = PlaybackSequence.FromProject(TestProject.Sample()); var audio = new FakeAudioVoices();
        var preview = new AudioNoteOutput(sequence, audio); var clock = new ManualClock();
        var session = new PlaybackSession(sequence, clock, new[] { preview }); session.Pump();
        Assert.Equal(new PreviewSettings("Flute", -12), Assert.Single(audio.Settings)); session.Stop();
    }

    [Fact]
    public void TwoPreviewOutputsOfSameProjectOwnDifferentVoices()
    {
        var sequence = new PlaybackSequence(new[] { PlaybackFixture.Note(0, 0, 1000) }); var audio = new FakeAudioVoices();
        var first = new AudioNoteOutput(sequence, audio); var second = new AudioNoteOutput(sequence, audio);
        var changes = new[] { new NoteOutputEvent(sequence.Notes[0], true) }; first.Apply(changes); second.Apply(changes);
        Assert.Equal(2, audio.Voices.Count); first.StopAll(); Assert.Single(audio.Voices);
        second.StopAll(); Assert.Empty(audio.Voices);
    }

    [Fact]
    public void RealLoopProviderSustainsThenFinishesItsReleaseAndHandlesEmptySample()
    {
        using var files = new TestProject(); string path = files.PathFor("sample.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)))
            writer.WriteSamples(Enumerable.Repeat(0.25f, 4000).ToArray(), 0, 4000);
        var loop = new LoopProvider(new CachedSound(path)); var buffer = new float[128];
        for (int i = 0; i < 100; i++) Assert.Equal(128, loop.Read(buffer, 0, buffer.Length));
        loop.Stop = true; int read = 128;
        for (int i = 0; i < 40 && read > 0; i++) read = loop.Read(buffer, 0, buffer.Length);
        Assert.Equal(0, read);
        string empty = files.PathFor("empty.wav");
        using (var writer = new WaveFileWriter(empty, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))) { }
        Assert.Equal(0, new LoopProvider(new CachedSound(empty)).Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public void FailedGameReleaseDoesNotSkipAudioCleanup()
    {
        var sequence = new PlaybackSequence(new[] { PlaybackFixture.Note(0, 0, 1000) }); var clock = new ManualClock();
        var input = new RecordingInput(clock); var game = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        var audio = new FakeAudioVoices(); var preview = new AudioNoteOutput(sequence, audio);
        var session = new PlaybackSession(sequence, clock, new INoteOutput[] { game, preview }); session.Pump();
        input.Fail = c => !c.IsDown; Assert.Throws<AggregateException>(session.Stop); Assert.Empty(audio.Voices);
        Assert.Single(game.PendingKeys); input.Fail = null; session.Stop(); Assert.Empty(input.Pressed);
    }
}
