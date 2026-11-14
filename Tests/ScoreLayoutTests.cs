using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class ScoreLayoutTests
{
    static ScoreExportOptions Options(SmapProject p, ScoreOrientation direction = ScoreOrientation.Portrait, ScoreSpacing spacing = ScoreSpacing.Standard)
        => new() { TrackIds = p.Tracks.Select(t => t.Id).ToArray(), Orientation = direction, Spacing = spacing };
    static IEnumerable<ScoreCell> Cells(ScoreExportDocument d) => d.Pages.SelectMany(p => p.Rows).SelectMany(r => r.Slots).Select(s => s.Cell);
    static T Sta<T>(Func<T> action)
    {
        T? value = default; Exception? error = null;
        var t = new Thread(() => { try { value = action(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); return value!;
    }
    static SmapProject LongHold()
    {
        var p = TestProject.SingleTap(); var t = p.Tracks[0];
        var notes = Enumerable.Range(0, 120).Select(i => new ProjectNote { TrackId = t.Id, Key = i % 2 == 0 ? 5 : 13, Beat = i / 2m }).ToList();
        notes.Add(new() { TrackId = t.Id, Key = 14, Beat = 0, LengthBeats = 60, Action = NoteAction.Hold });
        return p with { Tracks = new() { t with { Notes = notes } } };
    }
    [Fact]
    public void AllLayoutSettingsKeepIdenticalNativeBytesAndSource()
    {
        var p = TestProject.Sample(); byte[] before = SmapFile.ToBytes(p); byte[]? payload = null;
        foreach (var orientation in Enum.GetValues<ScoreOrientation>()) foreach (var spacing in Enum.GetValues<ScoreSpacing>()) foreach (bool speed in new[] { false, true })
        {
            var d = ScoreExport.Prepare(p, Options(p, orientation, spacing) with { SimplifySpeed = speed });
            payload ??= d.Payload; Assert.Equal(payload, d.Payload);
            Assert.Equal(PerformanceIdentity.ForProject(p), d.PerformanceHash);
            Assert.Equal(before, SmapFile.ToBytes(p));
        }
        using var archive = new ZipArchive(new MemoryStream(payload!));
        Assert.All(archive.Entries, e => Assert.Equal(1980, e.LastWriteTime.Year));
    }
    [Theory]
    [InlineData(400, 4, "100")]
    [InlineData(327.87, 2, "164")]
    public void HighTempoChangesDisplayPulseValueWithoutChangingActualTiming(double speed, int factor, string reference)
    {
        var p = LongHold() with { Tempos = new() { new(0, 60_000_000m / (decimal)speed) } };
        var d = ScoreExport.Prepare(p, Options(p));
        Assert.Equal(factor, d.DisplayFactor); Assert.Equal(reference, ScoreLayout.Reference(p, 0, factor).Number);
        var source = new ProjectTimeline(p.Tempos);
        var display = new ProjectTimeline(p.Tempos.Select(t => new TempoPoint(t.Beat / factor, t.MicrosecondsPerBeat * factor)));
        foreach (var note in p.Tracks.SelectMany(t => t.Notes)) foreach (decimal b in new[] { note.Beat, note.Beat + note.LengthBeats })
            Assert.Equal(source.BeatToMicroseconds(b), display.BeatToMicroseconds(b / factor));
        Assert.Equal(1, ScoreExport.Prepare(p, Options(p) with { SimplifySpeed = false }).DisplayFactor);
        Assert.Equal(p.Tempos, d.Project.Tempos); Assert.Equal(p.Meters, d.Project.Meters);
    }
    [Fact]
    public void NearBarEndDoesNotInventAnAdditionalBeat()
    {
        var p = TestProject.SingleTap();
        Assert.Equal("·", ScoreLayout.BeatLabel(p, 0, 4, 3.99999m, 1));
        Assert.Equal("|", ScoreLayout.BeatLabel(p, 0, 4, 4, 1));
        Assert.Equal("3", ScoreLayout.BeatLabel(p, 0, 2.5m, 2, 1));
    }
    [Fact]
    public void ChangedMeterHasRealEventsAndTheCorrectReferencePulse()
    {
        var p = LongHold() with { Meters = new() { new(0, 4, 4), new(4, 6, 8) }, Tempos = new() { new(0, 500000), new(4, 600000) } };
        var d = ScoreExport.Prepare(p, Options(p)); var reference = ScoreLayout.Reference(p, 4, d.DisplayFactor);
        Assert.Equal(1, d.DisplayFactor); Assert.Equal("♩·", reference.Symbol); Assert.Equal("67", reference.Number);
        Assert.Equal(1.5m, reference.BeatsPerPulse);
        Assert.Contains(Cells(d), c => c.Column.Beat == 4.5m && c.Column.Keys[13] == ScoreKeyState.Tap);
        Assert.Equal("1", ScoreLayout.BeatLabel(p, 4, 7, 4, 1)); Assert.Equal("2", ScoreLayout.BeatLabel(p, 4, 7, 5.5m, 1));
        Assert.Contains("6/8", Sta(() => ScoreImageExporter.Svg(d, d.Pages[0])));
        Assert.Contains("♩· 67", Sta(() => ScoreImageExporter.Svg(d, d.Pages[0])));
    }
    [Theory]
    [InlineData(ScoreOrientation.Landscape, ScoreSpacing.Standard)]
    [InlineData(ScoreOrientation.Landscape, ScoreSpacing.Compact)]
    [InlineData(ScoreOrientation.Portrait, ScoreSpacing.Standard)]
    [InlineData(ScoreOrientation.Portrait, ScoreSpacing.Compact)]
    public void CrossPageHoldContinuesAndTerminalReleaseHasContext(ScoreOrientation direction, ScoreSpacing spacing)
    {
        var p = LongHold(); var d = ScoreExport.Prepare(p, Options(p, direction, spacing));
        Assert.True(d.Pages.Count > 1);
        foreach (var page in d.Pages.Skip(1)) Assert.Equal(ScoreKeyState.HoldMiddle, page.Rows[0].Slots[0].Cell.Column.Keys[14]);
        Assert.Single(Cells(d), c => c.Column.Keys[14] == ScoreKeyState.HoldStart);
        Assert.Single(Cells(d), c => c.Column.Keys[14] == ScoreKeyState.HoldEnd);
        Assert.True(d.Pages[^1].Rows.Count > 1);
        foreach (var row in d.Pages.SelectMany(p => p.Rows))
            Assert.True(row.Slots.Sum(s => s.Width) <= ScoreLayout.Body(direction).Width);
    }
    [Fact]
    public void CompactModeOnlyCollapsesSilentAuxiliaryCellsAndKeepsTracksAligned()
    {
        var p = TestProject.Sample();
        var standard = ScoreExport.Prepare(p, Options(p)); var compact = ScoreExport.Prepare(p, Options(p) with { Spacing = ScoreSpacing.Compact });
        foreach (var track in p.Tracks)
        {
            string[] Events(ScoreExportDocument d) => Cells(d).Where(c => c.TrackId == track.Id && (c.Column.HasEvent || c.Column.Keys.Any(k => k is ScoreKeyState.HoldStart or ScoreKeyState.HoldMiddle or ScoreKeyState.HoldEnd or ScoreKeyState.Tap)))
                .Select(c => $"{c.Column.Beat}:{c.Column.EventOrder}:{string.Join(',', c.Column.Keys)}").ToArray();
            Assert.Equal(Events(standard), Events(compact));
        }
        foreach (var group in compact.Pages.SelectMany(p => p.Rows).Chunk(p.Tracks.Count))
        {
            Assert.Equal(p.Tracks.Count, group.Length);
            var first = group[0].Slots.Select(s => (s.Cell.Column.Beat, s.Cell.Column.EventOrder, s.Width)).ToArray();
            Assert.All(group.Skip(1), r => Assert.Equal(first, r.Slots.Select(s => (s.Cell.Column.Beat, s.Cell.Column.EventOrder, s.Width)).ToArray()));
        }
    }
    [Theory]
    [InlineData("风从很远的云端吹来以后我们仍然记得那首关于天空海洋和再次相遇的长篇协奏曲")]
    [InlineData("Howl's Moving Castle — A Long Journey Across the Clouds, the Sea and the Returning Light")]
    [InlineData("哈尔的移动城堡 · Howl's Moving Castle — Across the Clouds and Back Again / 天空之城改编练习篇")]
    public void LongTitlesAndRecoverableImagesWorkInBothOrientations(string title)
    {
        var p = TestProject.SingleTap() with { Metadata = new() { Name = title } };
        foreach (var direction in Enum.GetValues<ScoreOrientation>()) Sta(() =>
        {
            var d = ScoreExport.Prepare(p, Options(p, direction)); string svg = ScoreImageExporter.Svg(d, d.Pages[0]);
            var xml = XDocument.Parse(svg); string text = string.Concat(xml.Descendants(XName.Get("text", "http://www.w3.org/2000/svg")).Select(e => e.Value));
            string Normalize(string s) => Regex.Replace(s, @"\s", "");
            Assert.Contains(Normalize(title), Normalize(text));
            Assert.DoesNotContain("≈", text); Assert.DoesNotContain("自由节奏", text);
            byte[] png = ScoreImageExporter.Png(d, d.Pages[0]);
            Assert.Equal(d.PerformanceHash, PerformanceIdentity.ForProject(ScoreImageData.Recover(ScoreImageData.ReadPng(png))));
            return true;
        });
    }
}
