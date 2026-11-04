using System.IO;
using System.Text.Json.Nodes;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class MidiProjectTests
{
    static SmapProject Project()
    {
        var source = new MidiFixture(2).Tempo(0, 500000).Tempo(480, 1000000).Pedal(0, 127)
            .Note(60, 7, 100).Pedal(960, 0).Note(64, 241, 1001, channel: 2, track: 1).Read();
        return MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source)), "Synthetic");
    }

    [Fact]
    public void NativeRoundTripPreservesRawDataMappingParametersAndIndependentIdentities()
    {
        using var files = new TestProject(); var p = Project(); string path = files.PathFor("midi.smap"); string hash = PerformanceIdentity.ForProject(p);
        for (int i = 0; i < 3; i++)
        {
            SmapFile.Save(p, path); var loaded = SmapFile.Load(path);
            Assert.Equal(p.OriginalMidi, loaded.OriginalMidi); Assert.Equal(p.MidiConversion!.Source.Sha256, loaded.MidiConversion!.Source.Sha256);
            Assert.Equal(p.MidiConversion.Notes, loaded.MidiConversion.Notes); Assert.Equal(hash, PerformanceIdentity.ForProject(loaded));
            Assert.Equal(p.MidiConversion.Source.Events, loaded.MidiConversion.Source.Events); p = loaded;
        }
        Assert.All(p.Tracks.SelectMany(t => t.Notes), n => Assert.NotNull(n.SourceMidiNoteId));
        Assert.All(p.Tracks.SelectMany(t => t.Notes), n => Assert.NotEqual(n.Id, n.SourceMidiNoteId));
    }

    [Fact]
    public void RemappingAlwaysUsesOriginalPitchesAndCanReturnToInitialResultAfterReload()
    {
        using var files = new TestProject(); var p = Project(); var original = PerformanceIdentity.ForProject(p);
        var options = p.MidiConversion!.Voices.Select(v => v with { SemitoneShift = 2, Profile = GamePitchProfiles.Get("Piano", 2) }).ToArray();
        var first = MidiProjectConverter.Remap(p, options); Assert.NotEqual(original, PerformanceIdentity.ForProject(first));
        string path = files.PathFor("remap.smap"); SmapFile.Save(first, path); var loaded = SmapFile.Load(path);
        var restored = MidiProjectConverter.Remap(loaded, p.MidiConversion.Voices);
        Assert.Equal(original, PerformanceIdentity.ForProject(restored)); Assert.Equal(p.MidiConversion.Source.Notes, restored.MidiConversion!.Source.Notes);
        Assert.Equal(p.OriginalMidi, restored.OriginalMidi); Assert.Equal(p.ProjectId, restored.ProjectId);
    }

    [Fact]
    public void ProjectRetainsSustainDurationsTempoAndPhaseTwoSchedulingWithoutFlattening()
    {
        var p = Project(); var source = p.MidiConversion!.Source; var sequence = PlaybackSequence.FromProject(p);
        Assert.Equal(2, sequence.Notes.Count); Assert.All(sequence.Notes, n => Assert.Equal(NoteAction.Hold, n.Action));
        foreach (var note in p.Tracks.SelectMany(t => t.Notes))
        {
            var origin = source.Notes.Single(n => n.Id == note.SourceMidiNoteId);
            var timed = sequence.Notes.Single(n => n.NoteId == note.Id);
            Assert.Equal(source.Time(origin.StartTick), timed.StartMicroseconds);
            Assert.InRange(Math.Abs(source.Time(origin.EndTick) - timed.StartMicroseconds - timed.LengthMicroseconds), 0, .000000001m);
        }
        var clock = new ManualClock(); var input = new RecordingInput(clock); var game = new GameNoteOutput(input, SkyPlayer.DefaultVk);
        var session = new PlaybackSession(sequence, clock, new[] { game }); clock.Milliseconds = 10; session.Pump(); Assert.Single(input.Pressed);
        session.Seek(750); Assert.NotEmpty(input.Pressed); session.Pause(true); Assert.Empty(input.Pressed); session.Pause(false);
        clock.Milliseconds = 5000; session.Pump(); Assert.True(session.Completed); Assert.Empty(game.PendingKeys);
    }

    [Fact]
    public void SourceMetadataAndPreviewChangesDoNotChangePerformanceIdentity()
    {
        var p = Project(); var changed = p with { Metadata = p.Metadata with { Name = "Renamed" },
            MidiConversion = p.MidiConversion! with { TonalAnalysis = new() },
            Tracks = p.Tracks.Select(t => t with { PreviewSemitoneOffset = 12 }).ToList() };
        Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(changed));
        var notes = p.Tracks[0].Notes; changed = p with { Tracks = p.Tracks.Select(t => t with { Notes = t.Notes.Select(n => n with { Key = (n.Key + 1) % 15 }).ToList() }).ToList() };
        Assert.NotEqual(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(changed));
    }

    [Fact]
    public void ProfileSnapshotDoesNotFollowLaterCatalogMutation()
    {
        var p = Project(); string hash = PerformanceIdentity.ForProject(p); var unrelated = GamePitchProfiles.Get("Piano"); unrelated.Pitches[0] = 59;
        Assert.Equal(60, p.MidiConversion!.Voices[0].Profile.Pitches[0]); Assert.Equal(hash, PerformanceIdentity.ForProject(p));
        Assert.Equal(60, GamePitchProfiles.Get("Piano").Pitches[0]);
    }

    [Fact]
    public void OriginalMidiExportIsByteIdenticalAndMappedMidiCanBeReimported()
    {
        using var files = new TestProject(); var p = Project(); string raw = files.PathFor("original.mid"), mapped = files.PathFor("mapped.mid");
        MidiProjectConverter.ExportOriginal(p, raw); Assert.Equal(p.OriginalMidi, File.ReadAllBytes(raw));
        MidiProjectConverter.ExportPerformance(p, mapped); var parsed = MidiSourceReader.Read(mapped);
        Assert.Equal(2, parsed.Notes.Count); Assert.Equal(p.MidiConversion!.Source.Notes.Select(n => n.StartTick).Order(), parsed.Notes.Select(n => n.StartTick).Order());
        Assert.Equal(p.MidiConversion.Source.Notes.Select(n => n.EndTick).Order(), parsed.Notes.Select(n => n.EndTick).Order());
        Assert.Equal(2, parsed.Tempos.Count); Assert.Equal(p.MidiConversion.Notes.Select(n => n.TargetPitch!.Value).Order(), parsed.Notes.Select(n => n.Pitch).Order());
    }

    [Theory]
    [InlineData("sourcePitch")] [InlineData("mappingKey")] [InlineData("missingRaw")] [InlineData("conversionVersion")]
    public void RejectsInconsistentNativeMidiPayloadWithoutOverwritingExistingFile(string problem)
    {
        using var files = new TestProject(); var p = Project(); string path = files.PathFor("valid.smap"); SmapFile.Save(p, path); var bytes = File.ReadAllBytes(path);
        switch (problem)
        {
            case "sourcePitch": p.MidiConversion!.Source.Notes[0] = p.MidiConversion.Source.Notes[0] with { Pitch = 61 }; break;
            case "mappingKey": p.MidiConversion!.Notes[0] = p.MidiConversion.Notes[0] with { Key = 15 }; break;
            case "missingRaw": p = p with { OriginalMidi = null }; break;
            case "conversionVersion": p = p with { MidiConversion = p.MidiConversion! with { Version = 99 } }; break;
        }
        Assert.ThrowsAny<InvalidDataException>(() => SmapFile.Save(p, path)); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void LegacyExportStillRefusesLongMultiTrackConversionInsteadOfDiscardingData()
    {
        using var files = new TestProject(); Assert.ThrowsAny<InvalidOperationException>(() => LegacyProjectAdapter.SaveLegacy(Project(), files.PathFor("lossy.json")));
    }

    [Fact]
    public void MidiExportUsesIndependentChannelsForNestedSamePitchReleases()
    {
        using var files = new TestProject(); var source = new MidiFixture().Note(60, 0, 960).Note(61, 240, 480).Read();
        var options = MidiFixture.Options(source); foreach (var n in source.Notes) options[0].ManualKeys[n.Id] = 0;
        var p = MidiProjectConverter.Create(SapmMapper.Convert(source, options)); string path = files.PathFor("overlap.mid");
        MidiProjectConverter.ExportPerformance(p, path); var result = MidiSourceReader.Read(path);
        Assert.Equal(new[] { (0L, 960L), (240L, 480L) }, result.Notes.OrderBy(n => n.StartTick).Select(n => (n.StartTick, n.EndTick)));
        Assert.Equal(2, result.Notes.Select(n => n.Channel).Distinct().Count());
    }

    [Fact]
    public void NonDivisiblePpqnRetainsOriginalTicksOnMidiReexport()
    {
        using var files = new TestProject(); var fixture = new MidiFixture { Resolution = 7 }; fixture.Note(60, 1, 13).Tempo(3, 600001);
        var source = fixture.Read(); var p = MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source)));
        string path = files.PathFor("rational.mid"); MidiProjectConverter.ExportPerformance(p, path); var parsed = MidiSourceReader.Read(path);
        Assert.Equal(1, parsed.Notes[0].StartTick); Assert.Equal(13, parsed.Notes[0].EndTick); Assert.Equal(3, parsed.Tempos[1].Tick);
    }

    [Theory]
    [InlineData("tempoFraction")] [InlineData("tinyStart")] [InlineData("tinyHold")]
    public void MidiExportRejectsUnrepresentableTimingInsteadOfSilentlyRounding(string problem)
    {
        using var files = new TestProject(); var p = Project();
        p = problem == "tempoFraction" ? p with { Tempos = new() { new(0, 500000.5m) } }
            : p with { Tracks = p.Tracks.Select(t => t with { Notes = t.Notes.Select(n => problem == "tinyStart"
                ? n with { Beat = .00000000000000000001m } : n with { LengthBeats = .00000000000000000001m }).ToList() }).ToList() };
        string path = files.PathFor("existing.mid"); File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidDataException>(() => MidiProjectConverter.ExportPerformance(p, path)); Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("nullSource")] [InlineData("nullTracks")] [InlineData("nullNote")] [InlineData("foreignManual")] [InlineData("badQuality")]
    public void InvalidConversionManifestIsRejectedAsDataError(string problem)
    {
        using var files = new TestProject(); var p = Project(); JsonObject root = TestProject.Manifest(p, files); var conversion = root["midiConversion"]!.AsObject();
        switch (problem)
        {
            case "nullSource": conversion["source"] = null; break;
            case "nullTracks": conversion["source"]!["tracks"] = null; break;
            case "nullNote": conversion["source"]!["notes"]![0] = null; break;
            case "foreignManual": conversion["voices"]![0]!["manualKeys"]![Guid.Empty.ToString()] = 2; break;
            case "badQuality": conversion["quality"]!["exact"] = 999; break;
        }
        string path = files.PathFor("bad-conversion.smap"); TestProject.Archive(path, ("project.json", System.Text.Encoding.UTF8.GetBytes(root.ToJsonString())), ("source.mid", p.OriginalMidi!));
        Assert.Throws<InvalidDataException>(() => SmapFile.Load(path));
    }

    [Fact]
    public void RemapPreservesTrackNamesPreviewOverridesAndOtherProjectMetadata()
    {
        var p = Project(); p = p with { Metadata = p.Metadata with { Author = "Author", Description = "Description" },
            Tracks = p.Tracks.Select(t => t with { Name = "Renamed", PreviewSemitoneOffset = 12 }).ToList() };
        var changed = MidiProjectConverter.Remap(p, p.MidiConversion!.Voices.Select(v => v with { SemitoneShift = 2 }));
        Assert.Equal(p.Metadata, changed.Metadata); Assert.All(changed.Tracks, t => { Assert.Equal("Renamed", t.Name); Assert.Equal(12, t.PreviewSemitoneOffset); });
        var named = MidiProjectConverter.Create(changed.MidiConversion!, "New version", changed);
        Assert.Equal("New version", named.Metadata.Name); Assert.Equal("Author", named.Metadata.Author); Assert.Equal("Description", named.Metadata.Description);
    }
}
