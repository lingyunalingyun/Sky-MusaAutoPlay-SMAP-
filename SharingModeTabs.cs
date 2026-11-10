using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SMAP_WPF;

public sealed class SharingModeTabs : UserControl
{
    readonly Border _track, _selection;
    readonly Grid _layout = new();
    readonly TranslateTransform _offset = new();
    readonly Button[] _buttons;
    int _selected;
    public event Action<int>? SelectionChanged;
    public int SelectedIndex => _selected;
    public bool OfflineEnabled { get => _buttons[1].IsEnabled; set => _buttons[1].IsEnabled = value; }

    public SharingModeTabs()
    {
        Width = 270; Height = 40;
        _layout.ColumnDefinitions.Add(new ColumnDefinition()); _layout.ColumnDefinitions.Add(new ColumnDefinition());
        _selection = new Border { CornerRadius = new CornerRadius(7), HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = _offset, IsHitTestVisible = false, Effect = new DropShadowEffect { BlurRadius = 5, ShadowDepth = 1, Opacity = .13 } };
        Grid.SetColumnSpan(_selection, 2); _layout.Children.Add(_selection);
        _buttons = [CreateButton(Lang.Sharing("onlineMode"), 0), CreateButton(Lang.Sharing("offlineMode"), 1)];
        _track = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Child = _layout }; Content = _track;
        _layout.SizeChanged += (_, _) => Move(false);
        Loaded += (_, _) => { Theme.Changed += Palette; Palette(); };
        Unloaded += (_, _) => Theme.Changed -= Palette;
        Palette();
    }
    Button CreateButton(string label, int index)
    {
        var button = new Button { Content = label, FontSize = 14, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), Cursor = Cursors.Hand, Template = ChromeWindow.BtnTpl() };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => Select(index);
        button.PreviewKeyDown += (_, e) =>
        {
            int next = e.Key switch { Key.Left or Key.Right => 1 - _selected, Key.Home => 0, Key.End => 1, _ => -1 };
            if (next >= 0 && _buttons[next].IsEnabled) { e.Handled = true; Select(next); _buttons[next].Focus(); }
        };
        Grid.SetColumn(button, index); _layout.Children.Add(button); return button;
    }
    public void Select(int index)
    {
        if (index is < 0 or > 1 || !_buttons[index].IsEnabled || index == _selected) return;
        _selected = index; Move(SystemParameters.ClientAreaAnimation); UpdateLabels(true); SelectionChanged?.Invoke(index); _buttons[index].Focus();
    }
    void Move(bool animate)
    {
        double from = _offset.X, target = _selected * _layout.ActualWidth / 2;
        _selection.Width = Math.Max(0, _layout.ActualWidth / 2); _offset.BeginAnimation(TranslateTransform.XProperty, null); _offset.X = target;
        if (animate) _offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    void Palette()
    {
        var colors = Theme.Current switch { AppTheme.Light => ("#e8e8ed", "#ffffff"), AppTheme.SunsetPink => ("#efdfed", "#fff8fd"), _ => ("#28282d", "#55555f") };
        _track.Background = (Brush)new BrushConverter().ConvertFromString(colors.Item1)!;
        _selection.Background = (Brush)new BrushConverter().ConvertFromString(colors.Item2)!;
        UpdateLabels(false);
    }
    void UpdateLabels(bool animate)
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            var button = _buttons[i];var target = ((SolidColorBrush)FindResource(i == _selected ? "TextFg" : "SubTextFg")).Color;
            var color = new SolidColorBrush(target);var previous = (button.Foreground as SolidColorBrush)?.Color ?? target;button.Foreground = color;
            if (animate && SystemParameters.ClientAreaAnimation) color.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(previous, target, TimeSpan.FromMilliseconds(180)));
            button.FontWeight = i == _selected ? FontWeights.SemiBold : FontWeights.Normal;button.IsTabStop = i == _selected;
        }
    }
}
