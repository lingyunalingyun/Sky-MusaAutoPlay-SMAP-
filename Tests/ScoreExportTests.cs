using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class ScoreExportTests
{
    static ScoreExportOptions Options(SmapProject p, ScoreOrientation orientation = ScoreOrientation.Portrait)
        => new() { TrackIds = p.Tracks.Select(t => t.Id).ToArray(), Orientation = orientation };
    static ScoreExportDocument Prepare(SmapProject p) => ScoreExport.Prepare(p, Options(p));
    static ScoreColumn[] Cells(ScoreExportDocument d) => d.Pages.SelectMany(p => p.Tiles).SelectMany(t => t.Columns).ToArray();
    static SmapProject Holds(params (decimal beat, decimal length, bool retrigger)[] values)
    {
        var p = TestProject.SingleTap(); var t = p.Tracks[0];
        return p with { Tracks = new() { t with { Notes = values.Select(v => new ProjectNote { TrackId = t.Id, Key = 7,
            Beat = v.beat, LengthBeats = v.length, Action = NoteAction.Hold, RetriggerOnOverlap = v.retrigger }).ToList() } } };
    }
    static T Sta<T>(Func<T> action)
    {
        T? result = default; Exception? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result!;
    }

    [Fact]
    public void ExactHoldReleaseIsNotRoundedToGrid()
    {
        var cells = Cells(Prepare(Holds((0, 2.375m, false))));
        Assert.Equal(ScoreKeyState.HoldStart, cells.Single(c => c.Beat == 0).Keys[7]);
        Assert.Equal(ScoreKeyState.HoldMiddle, cells.Single(c => c.Beat == 1).Keys[7]);
        Assert.Equal(ScoreKeyState.HoldEnd, cells.Single(c => c.Beat == 2.375m).Keys[7]);
        Assert.Equal("3+3/8", cells.Single(c => c.Beat == 2.375m).Label);
        Assert.Equal(ScoreKeyState.Idle, cells.Single(c => c.Beat == 3).Keys[7]);
    }
    [Fact]
    public void NearGridEventsKeepExactTimesWithoutRedundantRestPulse()
    {
        var p = TestProject.SingleTap(); var track = p.Tracks[0];
        p = p with { Tracks = new() { track with { Notes = new() {
            new() { TrackId = track.Id, Key = 2, Beat = 1.00008m },
            new() { TrackId = track.Id, Key = 3, Beat = 1.00009m }
        } } } };
        var d = Prepare(p); var cells = Cells(d);
        Assert.DoesNotContain(cells, c => c.Beat == 1);
        Assert.Equal(ScoreKeyState.Tap, cells.Single(c => c.Beat == 1.00008m).Keys[2]);
        Assert.Equal(ScoreKeyState.Tap, cells.Single(c => c.Beat == 1.00009m).Keys[3]);
        // Close but distinct events must not masquerade as a single quantized attack.
        Assert.Equal("", cells.Single(c => c.Beat == 1.00008m).Label);
        Assert.Equal("", cells.Single(c => c.Beat == 1.00009m).Label);
        Assert.Contains("ScoreFreeRhythm", d.Notices);
        Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(ScoreImageData.Recover(ScoreImageData.Create(d, 1))));
    }
    [Fact]
    public void UnrecognizedRhythmIsMarkedWithoutChangingPerformance()
    {
        var p = Holds((.29m, .41m, false)); var d = Prepare(p);
        Assert.Equal("", Cells(d).Single(c => c.Beat == .29m).Label);
        Assert.Contains("ScoreFreeRhythm", d.Notices);
        Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(ScoreImageData.Recover(ScoreImageData.Create(d, 1))));
    }
    [Theory]
    [InlineData("2.50008", "2+1/2")]
    [InlineData("1.33333333333333", "1+1/3")]
    [InlineData("1.375", "1+3/8")]
    [InlineData("1.0625", "1+1/16")]
    [InlineData("1.9999", "2")]
    [InlineData("0.00008", null)]
    [InlineData("1.29", null)]
    public void RhythmicLabelsUseCommonFractionsInsteadOfDecimalApproximations(string value, string? expected)
    {
        Assert.Equal(expected, ScoreExport.RhythmLabel(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }
    [Fact]
    public void SustainedKeyAcrossPageIsNotPressedAgain()
    {
        var doc = Prepare(Holds((0, 40, false)));
        Assert.True(doc.Pages.Count > 1);
        var next = doc.Pages[1].Tiles[0].Columns[0];
        Assert.Equal(ScoreKeyState.HoldMiddle, next.Keys[7]); Assert.Equal(0, next.RestBeats);
        Assert.Single(Cells(doc), c => c.Keys[7] == ScoreKeyState.HoldStart);
        Assert.Single(Cells(doc), c => c.Keys[7] == ScoreKeyState.HoldEnd);
    }
    [Fact]
    public void SameBeatReleaseAndRestartHaveSeparateOrderedColumns()
    {
        var cells = Cells(Prepare(Holds((0, 1, false), (1, 1, false)))).Where(c => c.Beat == 1).ToArray();
        Assert.Equal(new[] { ScoreKeyState.HoldEnd, ScoreKeyState.HoldStart }, cells.Select(c => c.Keys[7]));
        Assert.Equal(new[] { 0, 1 }, cells.Select(c => c.EventOrder));
    }
    [Fact]
    public void OverlapWithoutRetriggerReleasesOnlyLastOwner()
    {
        var cells = Cells(Prepare(Holds((0, 2, false), (1, 2, false))));
        Assert.Equal(ScoreKeyState.HoldMiddle, cells.Single(c => c.Beat == 1).Keys[7]);
        Assert.Equal(ScoreKeyState.HoldMiddle, cells.Single(c => c.Beat == 2).Keys[7]);
        Assert.Equal(ScoreKeyState.HoldEnd, cells.Single(c => c.Beat == 3).Keys[7]);
    }
    [Fact]
    public void RetriggerOverlapIncludesEndBeforeNewPress()
    {
        var cells = Cells(Prepare(Holds((0, 3, false), (1, 2, true)))).Where(c => c.Beat == 1).ToArray();
        Assert.Equal(new[] { ScoreKeyState.HoldEnd, ScoreKeyState.HoldStart }, cells.Select(c => c.Keys[7]));
    }
    [Fact]
    public void ExtraColumnInOtherTrackDoesNotInventHoldForTap()
    {
        var p = Holds((0, 1, false), (1, 1, false)); var id = Guid.NewGuid();
        p = p with { Tracks = p.Tracks.Append(new ProjectTrack { Id = id, Notes = new() { new() { TrackId = id, Key = 3, Beat = 1 } } }).ToList() };
        var cells = Prepare(p).Pages.SelectMany(p => p.Tiles).Where(t => t.TrackId == id).SelectMany(t => t.Columns).Where(c => c.Beat == 1).ToArray();
        Assert.Equal(new[] { ScoreKeyState.Tap, ScoreKeyState.Idle }, cells.Select(c => c.Keys[3]));
    }
    [Fact]
    public void ExplicitSelectionIgnoresLocalMuteAndCopiesSnapshot()
    {
        var p = TestProject.Sample(); var ids = p.Tracks.Select(t => t.Id).ToArray();
        p = p with { Tracks = p.Tracks.Select(t => t with { Muted = true }).ToList() };
        var d = Prepare(p);
        Assert.Equal(PerformanceIdentity.ForTracks(p, ids), d.PerformanceHash);
        Assert.All(d.Project.Tracks, t => Assert.False(t.Muted)); Assert.All(p.Tracks, t => Assert.True(t.Muted));
        int count = d.Project.Tracks[0].Notes.Count; p.Tracks[0].Notes.Clear(); Assert.Equal(count, d.Project.Tracks[0].Notes.Count);
    }
    [Fact]
    public void SourceMidiIsOptInWithoutChangingPerformance()
    {
        var p = TestProject.SingleTap() with { OriginalMidi = new byte[] { 77,84,104,100,0,0,0,6,0,0,0,1,0,96,77,84,114,107,0,0,0,4,0,255,47,0 } };
        var excluded = Prepare(p); var included = ScoreExport.Prepare(p, Options(p) with { IncludeOriginalMidi = true });
        Assert.Null(excluded.Project.OriginalMidi); Assert.Equal(p.OriginalMidi, included.Project.OriginalMidi);
        Assert.Equal(excluded.PerformanceHash, included.PerformanceHash);
    }
    [Fact]
    public void LegacyEightKeysResolveToStablePositions()
    {
        var p = TestProject.SingleTap();
        p = p with { Tracks = new() { p.Tracks[0] with { GameTarget = GamePitchProfiles.Get("Handpan").Target(),
            Notes = p.Tracks[0].Notes.Select(n => n with { Key = 0 }).ToList() } } };
        var c = Cells(Prepare(p))[0]; Assert.Equal(ScoreKeyState.Absent, c.Keys[0]); Assert.Equal(ScoreKeyState.Tap, c.Keys[1]);
    }
    [Fact]
    public void FourKeySnapshotDoesNotInventOtherKeys()
    {
        var p = TestProject.SingleTap();
        p = p with { FormatVersion = 2, KeyInterpretationVersion = 1, Tracks = new() { p.Tracks[0] with { Capability = InstrumentCapabilities.CustomFour(), KeyDomain = NoteKeyDomain.StableLogical } } };
        var c = Cells(Prepare(p))[0]; Assert.Equal(ScoreKeyState.Tap, c.Keys[14]); Assert.Equal(11, c.Keys.Count(k => k == ScoreKeyState.Absent));
    }
    [Fact]
    public void UnknownTrackSelectionAndMissingPhysicalKeyAreRejected()
    {
        var p = TestProject.SingleTap();
        Assert.Throws<InvalidDataException>(() => ScoreExport.Prepare(p, Options(p) with { TrackIds = new[] { Guid.NewGuid() } }));
        p = p with { FormatVersion = 2, KeyInterpretationVersion = 1, Tracks = new() { p.Tracks[0] with { Capability = InstrumentCapabilities.CustomFour(), KeyDomain = NoteKeyDomain.StableLogical,
            Notes = p.Tracks[0].Notes.Select(n => n with { Key = 2 }).ToList() } } };
        Assert.Throws<InvalidDataException>(() => Prepare(p));
    }
    [Fact]
    public void DenseRhythmsKeepEveryExactOnsetAndPaginate()
    {
        var p = TestProject.SingleTap(); var t = p.Tracks[0];
        p = p with { Tracks = new() { t with { Notes = Enumerable.Range(0, 100).Select(i => new ProjectNote { TrackId = t.Id, Key = i % 15, Beat = i / 16m }).ToList() } } };
        var doc = Prepare(p); Assert.All(p.Tracks[0].Notes, n => Assert.Contains(Cells(doc), c => c.Beat == n.Beat && c.Keys[n.Key] == ScoreKeyState.Tap));
        Assert.All(doc.Pages.SelectMany(p => p.Tiles), t => Assert.InRange(t.Columns.Count, 1, 4)); Assert.True(doc.Pages.Count > 1);
    }
    [Fact]
    public void RestDurationUsesTheCurrentMeterDenominatorPulse()
    {
        var p = TestProject.SingleTap() with { Meters = new() { new(0, 6, 8) } };
        Assert.Equal(1, Cells(Prepare(p)).Single(c => c.Beat == .5m).RestBeats);
    }
    [Fact]
    public void MeterChangesAndTempoEventsAreShownAtOriginalBeat()
    {
        var p = Holds((0, 7, false)) with { Meters = new() { new(0, 4, 4), new(2.5m, 6, 8) }, Tempos = new() { new(0, 500000), new(1.125m, 700000) } };
        var d = Prepare(p); Assert.Contains(Cells(d), c => c.Beat == 1.125m); Assert.Contains(d.Pages.SelectMany(p => p.Tiles), t => t.StartBeat == 2.5m);
    }
    [Fact]
    public void PngAndSvgRecoverSameNativeProjectAndHash()
    {
        using var files = new TestProject(); var p = TestProject.Sample(); var d = Prepare(p);
        Sta(() =>
        {
            string svg = files.PathFor("score.svg"), png = files.PathFor("score.png");
            File.WriteAllText(svg, ScoreImageExporter.Svg(d, d.Pages[0])); File.WriteAllBytes(png, ScoreImageExporter.Png(d, d.Pages[0]));
            foreach (string path in new[] { svg, png }) Assert.Equal(d.PerformanceHash, PerformanceIdentity.ForProject(ScoreImageData.Read(path)));
            var frame = BitmapDecoder.Create(new Uri(png), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(1260, frame.PixelWidth); Assert.Equal(2240, frame.PixelHeight);
            var xml = XDocument.Load(svg); Assert.DoesNotContain(xml.Descendants(), e => e.Name.LocalName == "rect" && e.Name.NamespaceName.Length == 0);
            return true;
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReencodedOrDamagedPngNeverInventsNotes(bool damaged)
    {
        var d = Prepare(TestProject.SingleTap());
        var bytes = Sta(() => ScoreImageExporter.Png(d, d.Pages[0]));
        if (damaged)
        {
            bytes[^5] ^= 1;
            Assert.Throws<InvalidDataException>(() => ScoreImageData.ReadPng(bytes));
        }
        else
        {
            var stripped = Sta(() =>
            {
                using var input = new MemoryStream(bytes);
                var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(new WriteableBitmap(frame)));
                using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
            });
            Assert.Equal("ScoreNoData", Assert.Throws<InvalidDataException>(() => ScoreImageData.ReadPng(stripped)).Message);
        }
    }
    [Theory]
    [InlineData("payloadSha256", "bad")]
    [InlineData("performanceSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("encoding", "other")]
    public void TamperedOrUnknownEnvelopeIsRejected(string key, string value)
    {
        var d = Prepare(TestProject.SingleTap()); var json = JsonNode.Parse(ScoreImageData.Create(d, 1))!.AsObject(); json[key] = value;
        Assert.Throws<InvalidDataException>(() => ScoreImageData.Recover(json.ToJsonString()));
    }
    [Fact]
    public void DuplicatePayloadAndDtdAreRejectedWithoutRenderingSvg()
    {
        using var files = new TestProject(); var d = Prepare(TestProject.SingleTap());
        string text = Sta(() => ScoreImageExporter.Svg(d, d.Pages[0])); var xml = XDocument.Parse(text); var metadata = xml.Root!.Element(XName.Get("metadata", "http://www.w3.org/2000/svg"))!;
        metadata.Add(new XElement(metadata.Elements().Single())); string path = files.PathFor("duplicate.svg"); xml.Save(path);
        Assert.Throws<InvalidDataException>(() => ScoreImageData.Read(path));
        File.WriteAllText(path, "<!DOCTYPE svg [<!ENTITY bad SYSTEM 'file:///secret'>]><svg/>");
        Assert.ThrowsAny<Exception>(() => ScoreImageData.Read(path));
    }
    [Fact]
    public void DuplicateJsonAndUnknownMajorVersionFail()
    {
        var d = Prepare(TestProject.SingleTap()); string json = ScoreImageData.Create(d, 1);
        Assert.Throws<InvalidDataException>(() => ScoreImageData.Recover(json.Replace("\"envelopeVersion\":1", "\"envelopeVersion\":1,\"envelopeVersion\":1")));
        var node = JsonNode.Parse(json)!; node["envelopeVersion"] = 2;
        Assert.Throws<InvalidDataException>(() => ScoreImageData.Recover(node.ToJsonString()));
    }
    [Fact]
    public void CanceledExportLeavesExistingFilesAndNoPartialFolder()
    {
        using var files = new TestProject(); File.WriteAllText(files.PathFor("keep.txt"), "unchanged"); var d = Prepare(TestProject.SingleTap());
        Assert.Throws<OperationCanceledException>(() => Sta(() => ScoreImageExporter.Export(d, files.DirectoryPath, ScoreImageFormats.Both, new CancellationToken(true))));
        Assert.Equal("unchanged", File.ReadAllText(files.PathFor("keep.txt"))); Assert.Empty(Directory.GetDirectories(files.DirectoryPath));
    }
    [Fact]
    public void OrientationAndMetadataNeverChangePerformanceIdentity()
    {
        var p = TestProject.SingleTap(); var a = Prepare(p); var b = ScoreExport.Prepare(p with { Metadata = p.Metadata with { Name = "renamed", TranscribedBy = "different" } }, Options(p, ScoreOrientation.Landscape));
        Assert.Equal(a.PerformanceHash, b.PerformanceHash); Assert.Equal((2240,1260), (b.Width,b.Height));
    }
}
