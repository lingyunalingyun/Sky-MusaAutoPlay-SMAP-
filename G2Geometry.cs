using System.Windows;
using System.Windows.Media;

namespace SMAP_WPF;

public static class G2Geometry
{
    public static Geometry Create(Rect bounds, CornerRadius radius)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return Geometry.Empty;
        double scale = 1;
        void Fit(double length, double sum) { if (sum > length) scale = Math.Min(scale, length / sum); }
        Fit(bounds.Width, radius.TopLeft + radius.TopRight);
        Fit(bounds.Width, radius.BottomLeft + radius.BottomRight);
        Fit(bounds.Height, radius.TopLeft + radius.BottomLeft);
        Fit(bounds.Height, radius.TopRight + radius.BottomRight);
        double tl = radius.TopLeft * scale, tr = radius.TopRight * scale;
        double br = radius.BottomRight * scale, bl = radius.BottomLeft * scale;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new(bounds.Left + tl, bounds.Top), true, true);
            Corner(context, new(bounds.Right - tr, bounds.Top), new(1, 0), new(0, 1), tr);
            Corner(context, new(bounds.Right, bounds.Bottom - br), new(0, 1), new(-1, 0), br);
            Corner(context, new(bounds.Left + bl, bounds.Bottom), new(-1, 0), new(0, -1), bl);
            Corner(context, new(bounds.Left, bounds.Top + tl), new(0, -1), new(1, 0), tl);
        }
        geometry.Freeze(); return geometry;
    }

    static void Corner(StreamGeometryContext context, Point origin, Vector along, Vector inward, double radius)
    {
        Point At(double x, double y) => origin + radius * (x * along + y * inward);
        context.LineTo(origin, true, false);
        if (radius <= 0) return;
        // Each join has matching tangent and curvature; curvature at the straight edges is zero.
        context.BezierTo(At(.28, 0), At(.64, 0), At(.82, .18), true, false);
        context.BezierTo(At(1, .36), At(1, .72), At(1, 1), true, false);
    }

    public static Geometry Create(Rect bounds, double radiusX, double radiusY)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return Geometry.Empty;
        if (radiusX == radiusY) return Create(bounds, new CornerRadius(Math.Max(0, radiusX)));
        double rx = Math.Clamp(radiusX, 0, bounds.Width / 2), ry = Math.Clamp(radiusY, 0, bounds.Height / 2);
        if (rx == 0 || ry == 0) return Create(bounds, new CornerRadius());
        var geometry = Create(new Rect(0, 0, bounds.Width / rx, bounds.Height / ry), new CornerRadius(1)).Clone();
        geometry.Transform = new MatrixTransform(rx, 0, 0, ry, bounds.X, bounds.Y);
        geometry.Freeze(); return geometry;
    }

    public static void Draw(DrawingContext drawing, Brush? fill, Pen? stroke, Rect bounds, double radiusX, double radiusY)
        => drawing.DrawGeometry(fill, stroke, Create(bounds, radiusX, radiusY));
}
