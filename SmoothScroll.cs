using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SMAP_WPF;

/// <summary>给 ListView/ListBox 加线性平滑滚动: 拦滚轮, 把 VerticalOffset 动画滑到目标(值累加, 快滚更远)。
/// 配合 VirtualizingPanel.ScrollUnit=Pixel 使用(保留虚拟化)。</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));
    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    // 驱动 ScrollToVerticalOffset 的可动画附加属性(WPF 的 VerticalOffset 只读不能直接动画)
    static readonly DependencyProperty OffsetProperty =
        DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(0.0, (d, e) => { if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue); }));

    // 每个 ScrollViewer 记住动画目标(累加用)
    static readonly DependencyProperty TargetProperty =
        DependencyProperty.RegisterAttached("Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0.0));
    // 是否正在滚轮动画中(真实标志, 用来区分动画滚动 vs 用户拖动/键盘滚动)
    static readonly DependencyProperty AnimatingProperty =
        DependencyProperty.RegisterAttached("Animating", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false));

    static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control c || !(bool)e.NewValue) return;
        c.Loaded += (_, __) =>
        {
            if (FindScrollViewer(c) is { } sv) Hook(sv);
        };
    }

    static void Hook(ScrollViewer sv)
    {
        sv.SetValue(TargetProperty, sv.VerticalOffset);
        sv.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            double target = Math.Clamp((double)sv.GetValue(TargetProperty) - e.Delta, 0, sv.ScrollableHeight);
            sv.SetValue(TargetProperty, target);
            sv.SetValue(AnimatingProperty, true);
            var anim = new DoubleAnimation(sv.VerticalOffset, target, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop };
            anim.Completed += (_, __) => { sv.ScrollToVerticalOffset(target); sv.SetValue(AnimatingProperty, false); };
            sv.BeginAnimation(OffsetProperty, anim);
        };
        // 拖滚动条/键盘等非滚轮方式滚动时, 同步目标(免得下次滚轮跳回旧位置)
        sv.ScrollChanged += (_, e) =>
        {
            if (e.VerticalChange != 0 && !(bool)sv.GetValue(AnimatingProperty))
                sv.SetValue(TargetProperty, sv.VerticalOffset);
        };
    }

    static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        if (d is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(d, i)) is { } r) return r;
        return null;
    }
}
