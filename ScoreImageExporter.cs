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

public static partial class ScoreImageExporter
{
    const string Ink = "#171c24", Gray = "#888888";
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    static string S(string key) => Lang.S("score." + key);

    interface ICanvas
    {
        void Box(double x, double y, double size, string fill, string? stroke = null, double thickness = 0, double radius = 0);
        void Line(double x, double y, double x2, double y2, string color, double thickness);
        void Text(double x, double y, string text, double size, double maxWidth, string anchor = "start", bool bold = false, string color = Ink, double rotation = 0);
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

    static void Key(ICanvas c, double x, double y, ScoreKeyState state)
    {
        const double side = ScoreLayout.KeySide;
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
        public void Text(double x, double y, string text, double size, double maxWidth, string anchor = "start", bool bold = false, string color = Ink, double rotation = 0)
        { writer.WriteStartElement("text", "http://www.w3.org/2000/svg"); Attr("x", x); Attr("y", y); Attr("font-size", size); writer.WriteAttributeString("font-family", "Microsoft YaHei,sans-serif"); writer.WriteAttributeString("font-weight", bold ? "600" : "400"); writer.WriteAttributeString("text-anchor", anchor); writer.WriteAttributeString("fill", color); if (rotation != 0) writer.WriteAttributeString("transform", FormattableString.Invariant($"rotate({rotation} {x} {y})")); writer.WriteString(Fit(text, size, maxWidth, bold)); writer.WriteEndElement(); }
    }
    sealed class BitmapCanvas(DrawingContext drawing) : ICanvas
    {
        static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
        public void Box(double x, double y, double size, string fill, string? stroke = null, double thickness = 0, double radius = 0)
            => drawing.DrawRoundedRectangle(Brush(fill), stroke == null ? null : new Pen(Brush(stroke), thickness), new Rect(x, y, size, size), radius, radius);
        public void Line(double x, double y, double x2, double y2, string color, double thickness) => drawing.DrawLine(new Pen(Brush(color), thickness), new Point(x, y), new Point(x2, y2));
        public void Text(double x, double y, string text, double size, double maxWidth, string anchor = "start", bool bold = false, string color = Ink, double rotation = 0)
        {
            var formatted = Measure(Fit(text, size, maxWidth, bold), size, bold); formatted.SetForegroundBrush(Brush(color));
            if (rotation != 0) drawing.PushTransform(new RotateTransform(rotation, x, y));
            drawing.DrawText(formatted, new Point(x - (anchor == "end" ? formatted.Width : anchor == "middle" ? formatted.Width / 2 : 0), y - formatted.Baseline));
            if (rotation != 0) drawing.Pop();
        }
    }
}
