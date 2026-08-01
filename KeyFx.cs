using System.Windows;
using System.Windows.Controls;

namespace SMAP_WPF;

/// <summary>让 Border.CornerRadius 可被 double 动画驱动(WPF 原生不能动画 CornerRadius)。
/// 翻转时圆角 3(尖=菱形) → 15(满=圆) → 3, 配合旋转还原光遇按键翻转。</summary>
public static class KeyFx
{
    public static readonly DependencyProperty RoundProperty =
        DependencyProperty.RegisterAttached("Round", typeof(double), typeof(KeyFx),
            new PropertyMetadata(3.0, (d, e) =>
            {
                if (d is Border b) b.CornerRadius = new CornerRadius((double)e.NewValue);
            }));

    public static void SetRound(DependencyObject o, double v) => o.SetValue(RoundProperty, v);
    public static double GetRound(DependencyObject o) => (double)o.GetValue(RoundProperty);
}
