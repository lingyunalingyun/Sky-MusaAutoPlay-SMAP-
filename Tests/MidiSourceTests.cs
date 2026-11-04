using System.IO;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class MidiSourceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)]
    public void ReadsSingleTrackWithNameChannelProgramAndExactUnquantizedTimes(int format)
    {
        var f = new MidiFixture { Format = format }; f.Event(0, 0, 0xff, 3, 4, 108, 101, 97, 100).Event(0, 0, 0xc2, 40).Note(61, 7, 493, channel: 3);
        var s = f.Read(); var n = Assert.Single(s.Notes); var voice = Assert.Single(s.Voices);
        Assert.Equal("lead", s.Tracks[0].Name); Assert.Equal(3, n.Channel); Assert.Equal(40, n.Program); Assert.Equal(61, n.Pitch);
        Assert.Equal(7, n.StartTick); Assert.Equal(493, n.EndTick); Assert.Equal(493, n.KeyReleaseTick);
        Assert.Equal(s.Beat(7), new ProjectTimeline(s.Tempos.Select(t => new TempoPoint(s.Beat(t.Tick), t.MicrosecondsPerQuarter))).MicrosecondsToBeat(s.Time(7)));
        Assert.Equal(n.VoiceId, voice.Id); Assert.Empty(s.Issues);
    }

    [Fact]
    public void DistinguishesPhysicalTrackChannelAndProgramSegments()
    {
        var f = new MidiFixture(2); f.Event(0, 0, 0xc0, 73).Note(60, 0, 100).Note(64, 0, 100, channel: 2)
            .Event(0, 120, 0xc0, 40).Note(67, 120, 220).Note(72, 240, 340, channel: 1, track: 1);
        var s = f.Read(); Assert.Equal(2, s.Tracks.Count); Assert.Equal(4, s.Voices.Count);
        Assert.Equal(40, s.Notes.Single(n => n.Track == 1).Program);
        Assert.Equal(new[] { 1, 2 }, s.Notes.Select(n => n.Channel).Distinct().Order());
    }

    [Fact]
    public void ZeroVelocityAndCrossTrackNoteOffPairWithoutUsingFileReaderPairing()
    {
        var f = new MidiFixture(2); f.Event(0, 11, 0x90, 60, 100).Event(1, 112, 0x90, 60, 0);
        var n = Assert.Single(f.Read().Notes); Assert.Equal(0, n.Track); Assert.Equal(112, n.EndTick); Assert.Equal(100, n.Velocity);
    }

    [Fact]
    public void SamePitchOverlapsPairFifoAndRetainBothOnsets()
    {
        var s = new MidiFixture().Note(60, 0, 100).Note(60, 25, 150).Read();
        var notes = s.Notes.OrderBy(n => n.StartTick).ToArray(); Assert.Equal(2, notes.Length);
        Assert.Equal(100, notes[0].EndTick); Assert.Equal(150, notes[1].EndTick);
        Assert.Contains(s.Issues, i => i.Code == "OverlapPairedFifo" && i.Severity == MidiIssueSeverity.Information);
    }

    [Fact]
    public void SustainRetainsPhysicalReleaseAndExtendsAudibleEndForWholeChannel()
    {
        var f = new MidiFixture(2); f.Pedal(0, 127, track: 1).Note(60, 10, 100).Note(64, 30, 80, channel: 2)
            .Note(60, 120, 200).Pedal(350, 0, track: 1);
        var s = f.Read(); Assert.Equal(3, s.Notes.Count);
        Assert.All(s.Notes.Where(n => n.Channel == 1), n => Assert.Equal(350, n.EndTick));
        Assert.Equal(80, s.Notes.Single(n => n.Channel == 2).EndTick);
        Assert.Equal(100, s.Notes.Single(n => n.StartTick == 10).KeyReleaseTick);
        Assert.Equal(2, s.Events.Count(e => e.Kind == "Controller"));
    }

    [Theory]
    [InlineData(63, false)] [InlineData(64, true)]
    public void SustainThresholdMatchesControllerSemantics(int value, bool extended)
    {
        var s = new MidiFixture().Pedal(0, value).Note(60, 0, 100).Pedal(300, 0).Read();
        Assert.Equal(extended ? 300 : 100, s.Notes[0].EndTick);
    }

    [Fact]
    public void TempoAndMeterChangesPreserveExactTicksAndDurationAcrossBoundary()
    {
        var s = new MidiFixture(2).Tempo(0, 500000).Tempo(480, 1000000, track: 1)
            .Event(1, 0, 0xff, 0x58, 4, 4, 2, 24, 8).Event(1, 960, 0xff, 0x58, 4, 3, 3, 24, 8).Note(60, 240, 960).Read();
        Assert.Equal(250000, s.Time(240)); Assert.Equal(1500000, s.Time(960));
        Assert.Equal(2, s.Tempos.Count); Assert.Equal(3, s.Meters[1].Numerator); Assert.Equal(8, s.Meters[1].Denominator);
        Assert.Equal(960, s.Meters[1].Tick);
    }

    [Fact]
    public void IncompleteNotesPedalAndUnsupportedEventsAreExplicitDiagnostics()
    {
        var f = new MidiFixture(); f.Pedal(0, 127).Note(60, 0, 100).Event(0, 10, 0x90, 64, 100)
            .Event(0, 30, 0xe0, 0, 100).Event(0, 200, 0xb0, 1, 80).Event(0, 300, 0x80, 70, 0);
        var s = MidiSourceReader.Read(f.Bytes(endTrack: false));
        foreach (string code in new[] { "MissingNoteOff", "PedalNotReleased", "PitchBendNotMapped", "ControllerNotRendered", "UnmatchedNoteOff", "MissingEndTrack" })
            Assert.Contains(s.Issues, i => i.Code == code);
        Assert.True(s.Notes.Single(n => n.Pitch == 64).Incomplete); Assert.All(s.Notes, n => Assert.Equal(300, n.EndTick));
        Assert.Contains(s.Events, e => e.Kind == "PitchBend" && e.Data1 == 12800);
    }

    [Fact]
    public void BankSelectIsRetainedAndSeparatesInstrumentVoices()
    {
        var s = new MidiFixture().Note(60, 0, 100).Event(0, 101, 0xb0, 0, 4).Event(0, 101, 0xb0, 32, 2).Note(62, 120, 200).Read();
        Assert.Equal(2, s.Voices.Count); Assert.Contains(s.Voices, v => v.BankMsb == 4 && v.BankLsb == 2);
    }

    [Theory]
    [InlineData("format2")] [InlineData("smpte")] [InlineData("zeroResolution")]
    [InlineData("truncated")] [InlineData("badHeader")] [InlineData("badEvent")] [InlineData("trailing")]
    public void RejectsUnsupportedOrDamagedInputWithoutSilentSuccess(string problem)
    {
        var bytes = new MidiFixture().Note(60, 0, 100).Bytes();
        switch (problem)
        {
            case "format2": bytes[9] = 2; break;
            case "smpte": bytes[12] = 0xe7; break;
            case "zeroResolution": bytes[12] = bytes[13] = 0; break;
            case "truncated": bytes = bytes[..^2]; break;
            case "badHeader": bytes[0] = 0; break;
            case "badEvent": bytes[23] = 0xff; break;
            case "trailing": bytes = bytes.Concat(new byte[] { 1 }).ToArray(); break;
        }
        Assert.ThrowsAny<InvalidDataException>(() => MidiSourceReader.Read(bytes));
    }

    [Fact]
    public void CompleteImportEntryAndSourceIdentityAreDeterministicAndIndependentOfCallerBuffer()
    {
        using var files = new TestProject(); var bytes = new MidiFixture().Note(60, 0, 100).Bytes(); string path = files.PathFor("complete.mid"); File.WriteAllBytes(path, bytes);
        var a = MidiImporter.ReadComplete(path); var b = MidiSourceReader.Read(bytes);
        Assert.Equal(a.Sha256, b.Sha256); Assert.Equal(a.Notes, b.Notes); bytes[0] = 0; a.Validate(); b.Validate();
    }

    [Fact]
    public void RepeatedIdenticalProgramAndBankEventsDoNotInventInstrumentParts()
    {
        var f = new MidiFixture(); for (int i = 0; i < 140; i++) f.Event(0, i * 10, 0xc0, 0).Event(0, i * 10, 0xb0, 0, 0).Note(60, i * 10, i * 10 + 5);
        var s = f.Read(); Assert.Single(s.Voices); Assert.Equal(140, s.Notes.Count); Assert.Equal(280, s.Events.Count);
    }

    [Fact]
    public void ZeroDurationNoteRetainsZeroTicksAndReportsItsCompatibleTapPlayback()
    {
        var source = new MidiFixture().Note(60, 10, 10).Read(); Assert.Equal(0, source.Notes[0].EndTick - source.Notes[0].StartTick);
        Assert.Contains(source.Issues, i => i.Code == "ZeroLengthNoteUsesTap" && i.Severity == MidiIssueSeverity.Warning);
        var p = MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source)));
        Assert.Equal(NoteAction.Tap, p.Tracks[0].Notes[0].Action); Assert.False(p.Tracks[0].Notes[0].RetriggerOnOverlap);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Utf8TrackNamesAreDecodedAndUncertainLegacyEncodingsAreReported(bool utf8)
    {
        byte[] title = utf8 ? System.Text.Encoding.UTF8.GetBytes("旋律 キミ") : new byte[] { 0x82, 0xa0 };
        var f = new MidiFixture(); f.Event(0, 0, new byte[] { 0xff, 3, (byte)title.Length }.Concat(title).ToArray()).Note(60, 0, 100);
        var source = f.Read();
        if (utf8) { Assert.Equal("旋律 キミ", source.Tracks[0].Name); Assert.DoesNotContain(source.Issues, i => i.Code == "TextEncodingUncertain"); }
        else Assert.Contains(source.Issues, i => i.Code == "TextEncodingUncertain");
        Assert.Equal(f.Bytes(), source.Bytes);
    }
}
