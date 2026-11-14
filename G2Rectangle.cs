using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SMAP_WPF;

public sealed class G2Rectangle : Shape
{
    public static readonly DependencyProperty RadiusXProperty = DependencyProperty.Register(nameof(RadiusX), typeof(double), typeof(G2Rectangle), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RadiusYProperty = DependencyProperty.Register(nameof(RadiusY), typeof(double), typeof(G2Rectangle), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double RadiusX { get => (double)GetValue(RadiusXProperty); set => SetValue(RadiusXProperty, value); }
    public double RadiusY { get => (double)GetValue(RadiusYProperty); set => SetValue(RadiusYProperty, value); }
    protected override Geometry DefiningGeometry
    {
        get
        {
            double inset = Stroke == null ? 0 : StrokeThickness / 2;
            return G2Geometry.Create(new Rect(inset, inset, Math.Max(0, ActualWidth - 2 * inset), Math.Max(0, ActualHeight - 2 * inset)), RadiusX, RadiusY);
        }
    }
}
