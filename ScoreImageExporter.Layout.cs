using System.Globalization;
using System.IO;
using System.Text;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public static partial class ScoreImageExporter
{
    static string[] Elements(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text); var result = new List<string>();
        while (e.MoveNext()) result.Add(e.GetTextElement());
        return result.ToArray();
    }
    static IReadOnlyList<string> VerticalRuns(string text)
    {
        var runs = new List<string>(); var latin = new StringBuilder();
        foreach (string e in Elements(text))
        {
            if (e.All(c => c is >= ' ' and <= '~')) latin.Append(e);
            else { if (latin.Length > 0) { runs.Add(latin.ToString()); latin.Clear(); } runs.Add(e); }
        }
        if (latin.Length > 0) runs.Add(latin.ToString()); return runs;
    }
    static bool Latin(string text) => text.All(c => c is >= ' ' and <= '~');
    static double Extent(string text, double size, bool vertical, bool bold)
        => vertical ? VerticalRuns(text).Sum(r => Latin(r) ? Measure(r, size, bold).WidthIncludingTrailingWhitespace : Measure(r, size, bold).Height)
            : Measure(text, size, bold).Width;

    static (string[] Lines, double Size) TitleLines(string text, double budget, bool vertical)
    {
        if (text.Length > 4096) throw new InvalidDataException("ScoreTitleTooLong");
        var elements = Elements(text);
        foreach (double size in vertical ? new double[] { 52, 46, 40, 36, 32, 28, 24 } : new double[] { 60, 54, 48, 42, 36, 32, 28, 24 })
        {
            if (Extent(text, size, vertical, true) <= budget) return (new[] { text }, size);
            if (size > 36) continue;
            if (Extent(text, size, vertical, true) > budget * 2) continue;
            (string Left, string Right, double Difference)? best = null;
            for (int i = 1; i < elements.Length; i++)
            {
                if (elements[i - 1].All(char.IsAsciiLetterOrDigit) && elements[i].All(char.IsAsciiLetterOrDigit)) continue;
                string left = string.Concat(elements.Take(i)).TrimEnd(), right = string.Concat(elements.Skip(i)).TrimStart();
                double a = Extent(left, size, vertical, true), b = Extent(right, size, vertical, true);
                if (a <= budget && b <= budget && (best == null || Math.Abs(a - b) < best.Value.Difference)) best = (left, right, Math.Abs(a - b));
            }
            if (best is { } fit) return (new[] { fit.Left, fit.Right }, size);
        }
        throw new InvalidDataException("ScoreTitleTooLong");
    }
    static void Vertical(ICanvas c, double x, double y, string text, double size, double maxHeight,
        string anchor = "start", bool bold = false, string color = Ink, bool complete = false)
    {
        if (!complete)
        {
            var parts = Elements(text).ToList(); bool clipped = false;
            while (parts.Count > 0 && Extent(string.Concat(parts) + (clipped ? "…" : ""), size, true, bold) > maxHeight)
            { parts.RemoveAt(parts.Count - 1); clipped = true; }
            text = string.Concat(parts) + (clipped ? "…" : "");
        }
        double height = Extent(text, size, true, bold), cursor = y - (anchor == "end" ? height : anchor == "middle" ? height / 2 : 0);
        foreach (string run in VerticalRuns(text))
        {
            var measured = Measure(run, size, bold);
            if (string.IsNullOrWhiteSpace(run))
            { cursor += Latin(run) ? measured.WidthIncludingTrailingWhitespace : measured.Height; continue; }
            if (Latin(run))
            {
                double origin = x - measured.Baseline + measured.Height / 2;
                c.Text(origin, cursor, run, size, measured.WidthIncludingTrailingWhitespace + 2, bold: bold, color: color, rotation: 90);
                cursor += measured.WidthIncludingTrailingWhitespace;
            }
            else
            {
                c.Text(x, cursor + measured.Baseline, run, size, measured.Width + 2, "middle", bold, color);
                cursor += measured.Height;
            }
        }
    }
    static string Reference(ScoreExportDocument d, decimal beat)
    {
        var r = ScoreLayout.Reference(d.Project, beat, d.DisplayFactor);
        return r.Symbol + " " + r.Number;
    }
    static void Frame(ScoreExportDocument d, ScorePage page, ICanvas c)
    {
        var first = page.Rows[0].Slots[0].Cell; var p = d.Project;
        var track = p.Tracks.Single(t => t.Id == first.TrackId);
        decimal beat = first.Column.Beat; var meter = ScoreLayout.Meter(p, beat);
        string signature = $"{meter.Numerator}/{meter.Denominator * d.DisplayFactor}";
        string reference = S("reference") + " " + Reference(d, beat);
        string title = string.IsNullOrWhiteSpace(p.Metadata.Name) ? S("untitled") : p.Metadata.Name;
        string id = track.GameTarget?.InstrumentId ?? track.Capability?.InstrumentId ?? track.PreviewInstrument;
        string instrument = p.Tracks.Count > 1 ? S("multiTrack") + $" ({p.Tracks.Count})" : Lang.Instrument(id);
        if (p.Tracks.Count == 1 && InstrumentCapabilities.ForTrack(track) is { } capability)
            instrument += " · " + string.Format(S("keys"), capability.PhysicalKeyCount);
        string pitch = p.Tracks.Count > 1 ? S("perTrack") : track.GameTarget?.ScoreId ?? S("positionOnly");
        string person = S("transcriber") + ": " + (string.IsNullOrWhiteSpace(p.Metadata.TranscribedBy) ? S("notSet") : p.Metadata.TranscribedBy);
        string number = $"{page.Index} / {d.Pages.Count}";
        if (d.Options.Orientation == ScoreOrientation.Landscape)
        {
            c.Line(40, 157, 2200, 157, Ink, 5); c.Line(40, 1162, 2200, 1162, Ink, 5);
            c.Text(40, 84, signature, 40, 310, bold: true); c.Text(40, 140, reference, 38, 355, bold: true);
            var t = TitleLines(title, 1440, false);
            c.Text(1120, t.Lines.Length > 1 ? 32 : 49, "SMAP", t.Lines.Length > 1 ? 22 : 27, 260, "middle", true, "#626873");
            for (int i = 0; i < t.Lines.Length; i++) c.Text(1120, (t.Lines.Length == 1 ? 135 : 78) + i * t.Size * 1.6, t.Lines[i], t.Size, 1441, "middle", true);
            c.Text(2200, 84, instrument, 38, 340, "end", true); c.Text(2200, 140, pitch, 30, 355, "end");
            c.Text(1120, 1213, number, 34, 180, "middle", true, "#626873");
            c.Text(2200, 1213, person, 30, 640, "end", true);
            if (d.VerifiedShareCode is { } code) c.Text(40, 1213, code, 32, 900, bold: true);
        }
        else
        {
            // Counterclockwise frame conversion: x'=y, y'=2240-x; the body stays upright.
            c.Line(157, 40, 157, 2200, Ink, 5); c.Line(1162, 40, 1162, 2200, Ink, 5);
            Vertical(c, 48, 45, instrument, 32, 320, bold: true); Vertical(c, 111, 45, pitch, 26, 340);
            c.Text(32, 2050, signature, 34, 120, bold: true);
            var r = ScoreLayout.Reference(p, beat, d.DisplayFactor);
            c.Text(26, 2113, S("reference") + " " + r.Symbol, 25, 125, bold: true); c.Text(40, 2168, r.Number, 40, 110, bold: true);
            var t = TitleLines(title, 1360, true);
            Vertical(c, 22, 1120, "SMAP", 24, 200, "middle", true, "#626873");
            for (int i = 0; i < t.Lines.Length; i++) Vertical(c, t.Lines.Length == 1 ? 95 : i == 0 ? 124 : 70, 1120, t.Lines[i], t.Size, 1360, "middle", true, complete: true);
            Vertical(c, 1212, 45, person, 28, 740, bold: true);
            Vertical(c, 1212, 1120, number, 30, 180, "middle", true, "#626873");
            if (d.VerifiedShareCode is { } code) Vertical(c, 1212, 2190, code, 30, 800, "end", true);
        }
    }
    static string Rest(ScoreExportDocument d, ScoreCell cell)
    {
        var c = cell.Column;
        if (c.Beat == cell.StartBeat && c.DurationBeats == cell.EndBeat - cell.StartBeat) return S("barRest");
        decimal q = c.DurationBeats / ScoreLayout.Reference(d.Project, c.Beat, d.DisplayFactor).BeatsPerPulse;
        decimal whole = decimal.Round(q, 0, MidpointRounding.ToEven);
        if (whole > 0 && Math.Abs(q - whole) < .01m) return whole.ToString(Invariant) + " " + S("beats");
        return Math.Abs(q - .5m) < .01m ? S("halfBeat") : S("rest");
    }
    static void Draw(ScoreExportDocument d, ScorePage page, ICanvas c)
    {
        Frame(d, page, c);
        var body = ScoreLayout.Body(d.Options.Orientation); bool compact = d.Options.Spacing == ScoreSpacing.Compact;
        double rh = ScoreLayout.RowHeight(d.Options.Spacing);
        for (int r = 0; r < page.Rows.Count; r++)
        {
            var row = page.Rows[r]; double yy = body.Y + r * rh, xx = body.X;
            bool last = page.Index == d.Pages.Count && r == page.Rows.Count - 1;
            double extra = row.Slots.Count > 1 && !last ? (body.Width - row.Slots.Sum(s => s.Width)) / (row.Slots.Count - 1) : 0;
            long priorBar = -1;
            for (int j = 0; j < row.Slots.Count; j++)
            {
                var slot = row.Slots[j]; var cell = slot.Cell; var column = cell.Column;
                double cx = xx + 108.5 + (last ? (slot.Width - 217) / 2 : 0);
                var meter = ScoreLayout.Meter(d.Project, column.Beat);
                string caption = "";
                if (cell.BarNumber != priorBar)
                {
                    caption = cell.BarNumber.ToString("00", Invariant);
                    if (column.Beat - cell.StartBeat > 4m / meter.Denominator / 96) caption += " " + S("continued");
                    if (meter.Beat == column.Beat && column.Beat != 0) caption += $" · {meter.Numerator}/{meter.Denominator * d.DisplayFactor}";
                    if (j > 0) c.Line(xx - 8, yy + 30, xx - 8, yy + rh - 8, "#d1d5da", 1.8);
                }
                if (j == 0 && d.Project.Tracks.Count > 1) caption += " " + d.Project.Tracks.Single(t => t.Id == cell.TrackId).Name;
                if (column.Beat != 0 && d.Project.Tempos.Any(t => t.Beat == column.Beat)) caption += " " + Reference(d, column.Beat);
                if (caption.Length > 0) c.Text(xx, yy + (compact ? 19 : 25), caption, compact ? 18 : 22, slot.Width - 5, bold: true);
                c.Text(cx, yy + (compact ? 49 : 66), column.Label + (column.EventOrder > 0 ? " ↻" : ""), compact ? 22 : 28, slot.Width - 10, "middle", color: "#626873");
                if (column.RestBeats > 0)
                {
                    c.Line(cx - 30, yy + 108, cx + 30, yy + 108, Ink, 5);
                    c.Text(cx, yy + 153, Rest(d, cell), compact ? 24 : 27, slot.Width - 10, "middle");
                }
                else for (int key = 0; key < 15; key++) Key(c, cx - 91 + key % 5 * ScoreLayout.KeyStep, yy + (compact ? 76 : 93) + key / 5 * ScoreLayout.KeyStep, column.Keys[key]);
                priorBar = cell.BarNumber; xx += slot.Width + extra;
            }
            if (r > 0) c.Line(body.X, yy - 6, body.X + body.Width, yy - 6, "#e3e5e8", 1.5);
        }
    }
}
