using System.Windows;
using System.Windows.Media;

namespace SMAP_WPF;

public class G2Border : System.Windows.Controls.Border
{
    Size _size;
    CornerRadius _radius;
    Thickness _thickness;
    Geometry? _outer, _ring;

    Geometry Outline
    {
        get
        {
            if (_outer != null && _size == RenderSize && _radius == CornerRadius && _thickness == BorderThickness) return _outer;
            _size = RenderSize; _radius = CornerRadius; _thickness = BorderThickness;
            _outer = G2Geometry.Create(new Rect(RenderSize), CornerRadius); _ring = null;
            if (BorderThickness != default)
            {
                var t = BorderThickness;
                var innerBounds = new Rect(t.Left, t.Top, Math.Max(0, ActualWidth - t.Left - t.Right), Math.Max(0, ActualHeight - t.Top - t.Bottom));
                var innerRadius = new CornerRadius(Math.Max(0, CornerRadius.TopLeft - Math.Max(t.Left, t.Top)),
                    Math.Max(0, CornerRadius.TopRight - Math.Max(t.Right, t.Top)),
                    Math.Max(0, CornerRadius.BottomRight - Math.Max(t.Right, t.Bottom)),
                    Math.Max(0, CornerRadius.BottomLeft - Math.Max(t.Left, t.Bottom)));
                _ring = new CombinedGeometry(GeometryCombineMode.Exclude, _outer, G2Geometry.Create(innerBounds, innerRadius));
                _ring.Freeze();
            }
            return _outer;
        }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var outline = Outline;
        if (Background != null) drawing.DrawGeometry(Background, null, outline);
        if (BorderBrush != null && _ring != null) drawing.DrawGeometry(BorderBrush, null, _ring);
    }

    protected override Geometry? GetLayoutClip(Size layoutSlotSize)
        => ClipToBounds ? Outline : base.GetLayoutClip(layoutSlotSize);
}
