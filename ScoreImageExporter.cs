using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using SMAP_WPF.Core;

namespace SMAP_WPF;

[Flags]
public enum ScoreImageFormats { Svg = 1, Png = 2, Both = 3 }

public static class ScoreImageExporter
{
    const string Ink = "#171c24", Gray = "#888888";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static string S(string key) => Lang.S("score." + key);

    interface ICanvas
    {
        void Box(double x, double y, double size, string fill, string? stroke = null, double thickness = 0, double radius = 0);
        void Line(double x, double y, double x2, double y2, string color, double thickness);
        void Text(double x, double y, string text, double size, double maxWidth, string anchor = "start", bool bold = false, string color = Ink);
    }

    static FormattedText Measure(string text, double size, bool bold) => new(text, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal,
            bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, Brushes.Black, 1);

    static string Fit(string text, double size, double width, bool bold)
    {
        if (Measure(text, size, bold).Width <= width) return text;
        var elements = StringInfo.GetTextElementEnumerator(text); var values = new List<string>();
        while (elements.MoveNext()) values.Add(elements.GetTextElement());
        int low = 0, high = values.Count;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (Measure(string.Concat(values.Take(middle)) + "…", size, bold).Width <= width) low = middle; else high = middle - 1;
        }
        return string.Concat(values.Take(low)) + "…";
    }

    static void Draw(ScoreExportDocument d, ScorePage page, ICanvas c)
    {
        double w = d.Width, h = d.Height;
        var first = page.Tiles[0]; var track = d.Project.Tracks.Single(t => t.Id == first.TrackId);
        decimal firstBeat = first.Columns[0].Beat;
        var meter = d.Project.Meters.OrderBy(m => m.Beat).Last(m => m.Beat <= firstBeat);
        var tempo = d.Project.Tempos.OrderBy(t => t.Beat).Last(t => t.Beat <= firstBeat);
        c.Text(40, 86, $"{meter.Numerator}/{meter.Denominator}", 42, 300, bold: true);
        c.Text(40, 145, Bpm(tempo) + " BPM", 42, 330, bold: true);
        c.Text(w / 2, 62, "SMAP", 28, 260, "middle", true, "#626873");
        c.Text(w / 2, 142, d.Project.Metadata.Name, 64, w - 760, "middle", true);
        string instrument = d.Project.Tracks.Count > 1 ? S("multiTrack") + $" ({d.Project.Tracks.Count})" : Lang.Instrument(track.GameTarget?.InstrumentId ?? track.PreviewInstrument);
        c.Text(w - 40, 86, instrument, 42, 340, "end", true);
        c.Text(w - 40, 145, track.GameTarget is { } target ? target.ScoreId : S("positionOnly"), 32, 350, "end");
        c.Line(40, 157, w - 40, 157, Ink, 5); c.Line(40, h - 98, w - 40, h - 98, Ink, 5);
        c.Text(40, h - 51, "SHA-256 " + d.PerformanceHash[..12] + "…" + d.PerformanceHash[^8..], w < 2000 ? 26 : 34, w * .39, bold: true);
        c.Text(w / 2, h - 51, $"{page.Index} / {d.Pages.Count}", 36, 160, "middle", true, "#626873");
        c.Text(w - 40, h - 51, S("transcriber") + ": " + (string.IsNullOrWhiteSpace(d.Project.Metadata.TranscribedBy) ? S("notSet") : d.Project.Metadata.TranscribedBy), w < 2000 ? 30 : 36, w * .36, "end", true);
        int columns = d.Options.Orientation == ScoreOrientation.Landscape ? 2 : 1, rows = 8 / columns;
        double tileWidth = (w - 80) / columns, rowHeight = (h - 98 - 175) / rows;
        for (int i = 0; i < page.Tiles.Count; i++)
        {
            var tile = page.Tiles[i]; int row = i / columns, col = i % columns;
            double x = 40 + col * tileWidth, y = 175 + row * rowHeight;
            var ownTrack = d.Project.Tracks.Single(t => t.Id == tile.TrackId);
            string title = tile.BarNumber.ToString("00", Invariant) + (tile.Part > 0 ? $" · {tile.Part + 1}" : "");
            if (d.Project.Tracks.Count > 1) title += "  " + ownTrack.Name;
            c.Text(x, y + 31, title, 34, tileWidth - 50, bold: true);
            if (row > 0 && col == 0) c.Line(40, y - 9, w - 40, y - 9, "#e0e4e9", 2);
            if (col > 0) c.Line(x, y + 5, x, y + rowHeight - 26, "#d2d8df", 2);
            for (int j = 0; j < tile.Columns.Count; j++)
            {
                var cell = tile.Columns[j]; double cx = x + (j + .5) * tileWidth / 4;
                c.Text(cx, y + 67, (cell.Label.Length == 0 ? S("freeRhythm") : cell.Label) + (cell.EventOrder > 0 ? " ↻" : ""), 36, tileWidth / 4 - 12, "middle", color: "#626873");
                if (cell.RestBeats > 0)
                {
                    c.Line(cx - 35, y + 141, cx + 35, y + 141, Ink, 6);
                    string? duration = ScoreExport.RhythmLabel(cell.RestBeats);
                    c.Text(cx, y + 198, duration == null ? S("freeRhythm") : duration + " " + S("beats"), 28, tileWidth / 4 - 15, "middle");
                }
                else for (int key = 0; key < 15; key++)
                    Key(c, cx - 91 + key % 5 * 45.5, y + 95 + key / 5 * 45.5, cell.Keys[key]);
                var change = d.Project.Tempos.SingleOrDefault(t => t.Beat == cell.Beat && t.Beat != 0);
                var meterChange = d.Project.Meters.SingleOrDefault(m => m.Beat == cell.Beat && m.Beat != 0);
                string context = change != null ? Bpm(change) + " BPM" : "";
                if (meterChange != null) context += $" {meterChange.Numerator}/{meterChange.Denominator}";
                if (context.Length > 0) c.Text(cx, y + rowHeight - 13, context, 28, tileWidth / 4 - 15, "middle");
            }
        }
    }
    static string Bpm(TempoPoint t) => (60_000_000m / t.MicrosecondsPerBeat).ToString("0.##", Invariant);
    static void Key(ICanvas c, double x, double y, ScoreKeyState state)
    {
        const double side = 35;
        if (state == ScoreKeyState.Absent) { c.Line(x - 7, y - 7, x + 7, y + 7, Gray, 3.5); return; }
        if (state == ScoreKeyState.Tap) { c.Box(x - side / 2, y - side / 2, side, "#000000", radius: 1.9); return; }
        bool thick = state is ScoreKeyState.HoldStart or ScoreKeyState.HoldEnd;
        double stroke = thick ? 5.25 : 3.5, edge = side - stroke;
        c.Box(x - edge / 2, y - edge / 2, edge, "#ffffff", thick ? "#000000" : Gray, stroke, .9);
        if (state is ScoreKeyState.HoldStart or ScoreKeyState.HoldMiddle) c.Box(x - 7.5, y - 7.5, 15, "#000000", radius: 1.6);
    }

    public static string Svg(ScoreExportDocument document, ScorePage page)
    {
        var output = new StringBuilder();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true });
        writer.WriteStartElement("svg", "http://www.w3.org/2000/svg"); writer.WriteAttributeString("width", document.Width.ToString(Invariant)); writer.WriteAttributeString("height", document.Height.ToString(Invariant));
        writer.WriteAttributeString("viewBox", $"0 0 {document.Width} {document.Height}");
        writer.WriteStartElement("metadata", "http://www.w3.org/2000/svg"); writer.WriteStartElement("smap", "envelope", ScoreImageData.Namespace);
        writer.WriteString(ScoreImageData.Create(document, page.Index)); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("rect", "http://www.w3.org/2000/svg"); writer.WriteAttributeString("width", "100%"); writer.WriteAttributeString("height", "100%"); writer.WriteAttributeString("fill", "white"); writer.WriteEndElement();
        Draw(document, page, new SvgCanvas(writer)); writer.WriteEndElement(); writer.Flush(); return output.ToString();
    }

    public static BitmapSource Preview(ScoreExportDocument document, ScorePage page)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, document.Width, document.Height));
            Draw(document, page, new BitmapCanvas(drawing));
        }
        var bitmap = new RenderTargetBitmap(document.Width, document.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static byte[] Png(ScoreExportDocument document, ScorePage page)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Preview(document, page)));
        using var output = new MemoryStream(); encoder.Save(output);
        return ScoreImageData.EmbedPng(output.ToArray(), ScoreImageData.Create(document, page.Index));
    }

    public static string Export(ScoreExportDocument document, string parent, ScoreImageFormats formats,
        CancellationToken cancellation = default, Action<int, int>? progress = null)
    {
        if (formats is < ScoreImageFormats.Svg or > ScoreImageFormats.Both) throw new ArgumentOutOfRangeException(nameof(formats));
        string root = Path.GetFullPath(parent); Directory.CreateDirectory(root);
        string nonce = Guid.NewGuid().ToString("N"), stage = Path.Combine(root, ".smap-score-" + nonce);
        string destination = Path.Combine(root, SongLibrary.NativeFileStem(document.Project.Metadata.Name) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + nonce[..6]);
        Directory.CreateDirectory(stage);
        try
        {
            File.WriteAllBytes(Path.Combine(stage, "project.smap"), document.Payload);
            for (int i = 0; i < document.Pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested(); var page = document.Pages[i]; string stem = Path.Combine(stage, $"score-p{i + 1:000}");
                if (formats.HasFlag(ScoreImageFormats.Svg)) { string path = stem + ".svg"; File.WriteAllText(path, Svg(document, page), new UTF8Encoding(false)); _ = ScoreImageData.Read(path); }
                if (formats.HasFlag(ScoreImageFormats.Png)) { string path = stem + ".png"; File.WriteAllBytes(path, Png(document, page)); _ = ScoreImageData.Read(path); }
                progress?.Invoke(i + 1, document.Pages.Count);
            }
            cancellation.ThrowIfCancellationRequested(); Directory.Move(stage, destination); return destination;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    sealed class SvgCanvas(XmlWriter writer) : ICanvas
    {
        void Attr(string key, double value) => writer.WriteAttributeString(key, value.ToString("0.###", Invariant));
        public void Box(double x, double y, double size, string fill, string? stroke = null, double thickness = 0, double radius = 0)
        { writer.WriteStartElement("rect", "http://www.w3.org/2000/svg"); Attr("x", x); Attr("y", y); Attr("width", size); Attr("height", size); Attr("rx", radius); writer.WriteAttributeString("fill", fill); if (stroke != null) { writer.WriteAttributeString("stroke", stroke); Attr("stroke-width", thickness); } writer.WriteEndElement(); }
        public void Line(double x, double y, double x2, double y2, string color, double thickness)
        { writer.WriteStartElement("line", "http://www.w3.org/2000/svg"); Attr("x1", x); Attr("y1", y); Attr("x2", x2); Attr("y2", y2); writer.WriteAttributeString("stroke", color); Attr("stroke-width", thickness); writer.WriteEndElement(); }
        public void Text(double x, double y, string text, double size, double maxWidth, string anchor = "start", bool bold = false, string color = Ink)
        { writer.WriteStartElement("text", "http://www.w3.org/2000/svg"); Attr("x", x); Attr("y", y); Attr("font-size", size); writer.WriteAttributeString("font-family", "Microsoft YaHei,sans-serif"); writer.WriteAttributeString("font-weight", bold ? "600" : "400"); writer.WriteAttributeString("text-anchor", anchor); writer.WriteAttributeString("fill", color); writer.WriteString(Fit(text, size, maxWidth, bold)); writer.WriteEndElement(); }
    }
    sealed class BitmapCanvas(DrawingContext drawing) : ICanvas
    {
        static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
        public void Box(double x, double y, double size, string fill, string? stroke = null, double thickness = 0, double radius = 0)
            => drawing.DrawRoundedRectangle(Brush(fill), stroke == null ? null : new Pen(Brush(stroke), thickness), new Rect(x, y, size, size), radius, radius);
        public void Line(double x, double y, double x2, double y2, string color, double thickness) => drawing.DrawLine(new Pen(Brush(color), thickness), new Point(x, y), new Point(x2, y2));
        public void Text(double x, double y, string text, double size, double maxWidth, string anchor = "start", bool bold = false, string color = Ink)
        {
            var formatted = Measure(Fit(text, size, maxWidth, bold), size, bold); formatted.SetForegroundBrush(Brush(color));
            drawing.DrawText(formatted, new Point(x - (anchor == "end" ? formatted.Width : anchor == "middle" ? formatted.Width / 2 : 0), y - formatted.Baseline));
        }
    }
}
