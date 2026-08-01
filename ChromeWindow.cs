using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>统一窗口外壳: 圆角无边框 + 深蓝紫标题栏(标题+三色圆点) + 内容区。其余对话框继承它保持风格一致, 跟随主题。</summary>
public class ChromeWindow : Window
{
    readonly ContentControl _host = new();

    public ChromeWindow(string title, double width, bool resizable = false)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = resizable ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")); } catch { }

        var root = new Border { Background = B("WindowBg"), CornerRadius = new CornerRadius(14), BorderBrush = B("WindowBorder"), BorderThickness = new Thickness(1), Margin = new Thickness(6) };
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = TitleBar(title);
        Grid.SetRow(bar, 0); grid.Children.Add(bar);
        Grid.SetRow(_host, 1); grid.Children.Add(_host);
        root.Child = grid;
        Content = root;
    }

    /// <summary>设置内容区(标题栏下方)。</summary>
    public void SetBody(UIElement body) => _host.Content = body;

    protected Brush B(string k) => (Brush)Application.Current.Resources[k];

    UIElement TitleBar(string title)
    {
        var bar = new Border { Height = 34, CornerRadius = new CornerRadius(9), Background = B("TitleGrad") };
        bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var g = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        left.Children.Add(new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xf5)) });
        left.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        dots.Children.Add(Dot(0xc9c9c9, () => WindowState = WindowState.Minimized));
        dots.Children.Add(Dot(0xe6b52a, () => WindowState = WindowState.Minimized));
        dots.Children.Add(Dot(0xe0483b, Close));
        g.Children.Add(left); g.Children.Add(dots);
        bar.Child = g;
        return bar;
    }

    System.Windows.Shapes.Ellipse Dot(int rgb, Action onClick)
    {
        var e = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Margin = new Thickness(5, 0, 0, 0), Cursor = Cursors.Hand, Fill = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) };
        e.MouseLeftButtonDown += (_, __) => onClick();
        return e;
    }

    // 供子类构造圆角按钮/输入框
    protected Button Neutral(string text)
    {
        return new Button
        {
            Content = text, Height = 44, FontSize = 14, Cursor = Cursors.Hand,
            Foreground = B("NeutralBtnFg"), Background = B("NeutralBtnBg"),
            BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1),
            Template = BtnTpl()
        };
    }

    public static ControlTemplate BtnTpl() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        @"<ControlTemplate TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
            <Border x:Name='bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' Padding='10,0'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate>");
}
