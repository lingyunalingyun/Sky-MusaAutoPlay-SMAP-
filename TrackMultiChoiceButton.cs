using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace SMAP_WPF;

public sealed class TrackChoice(Guid id, string label, bool selected) : INotifyPropertyChanged
{
    public Guid Id { get; } = id;
    public string Label { get; } = label;
    bool _selected = selected;
    public bool IsSelected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TrackMultiChoiceButton : JoinedChoiceButton
{
    readonly List<TrackChoice> _tracks;
    readonly StackPanel _body = new() { Margin = new Thickness(7) };
    readonly ICollectionView _view;
    readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    bool _batch;
    public event EventHandler? SelectionChanged;
    public IReadOnlyList<TrackChoice> Tracks => _tracks;
    public Guid[] SelectedIds => _tracks.Where(t => t.IsSelected).Select(t => t.Id).ToArray();
    static string S(string key) => Lang.Sharing(key);

    public TrackMultiChoiceButton(IEnumerable<TrackChoice> tracks)
    {
        _tracks = tracks.ToList(); Height = 38; FontSize = 14;
        _view = CollectionViewSource.GetDefaultView(_tracks);
        var search = new TextBox { Height = 34, Template = ChromeWindow.TextBoxTpl(), ToolTip = S("trackSearch") };
        foreach (var pair in new[] { (Control.BackgroundProperty, "PanelBg"), (Control.ForegroundProperty, "TextFg"), (Control.BorderBrushProperty, "BoxBorder") }) search.SetResourceReference(pair.Item1, pair.Item2);
        search.BorderThickness = new Thickness(1);
        var searchHost = new Grid { Margin = new Thickness(2, 2, 2, 7) }; searchHost.Children.Add(search);
        var hint = new TextBlock { Text = S("trackSearch") + "…", Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }; hint.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); searchHost.Children.Add(hint); _body.Children.Add(searchHost);
        search.TextChanged += (_, _) => { string filter = search.Text.Trim(); hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _view.Filter = item => filter.Length == 0 || ((TrackChoice)item).Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase); };
        var commands = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var pair in new[] { (S("tracksAll"), true), (S("tracksClear"), false) })
        {
            var button = new Button { Content = pair.Item1, Height = 28, Margin = new Thickness(0, 0, 5, 0), Template = ChromeWindow.BtnTpl(), Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
            button.SetResourceReference(Control.ForegroundProperty, "TextFg");
            button.Click += (_, e) => { SelectAll(pair.Item2); e.Handled = true; }; commands.Children.Add(button);
        }
        _count.HorizontalAlignment = HorizontalAlignment.Right; _count.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); commands.Children.Add(_count); _body.Children.Add(commands);
        var resources = new ResourceDictionary { Source = new Uri("/SMAP;component/ChoicePopupStyles.xaml", UriKind.Relative) };
        var itemStyle = new Style(typeof(ListBoxItem), (Style)resources["PopupOptionItem"]);
        itemStyle.Setters.Add(new Setter(ListBoxItem.IsSelectedProperty, new Binding(nameof(TrackChoice.IsSelected)) { Mode = BindingMode.TwoWay }));
        var list = new ListBox { ItemsSource = _view, MaxHeight = 220, MinHeight = 36, SelectionMode = SelectionMode.Multiple, Background = Brushes.Transparent, BorderThickness = new Thickness(0), ItemContainerStyle = itemStyle };
        list.SetResourceReference(Control.ForegroundProperty, "TextFg");
        list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        list.SetValue(ScrollViewer.CanContentScrollProperty, true);
        list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        list.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        list.SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
        list.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><CheckBox IsChecked='{Binding IsSelected,Mode=TwoWay}' Foreground='{DynamicResource TextFg}' VerticalContentAlignment='Center'><TextBlock Text='{Binding Label}' ToolTip='{Binding Label}' TextTrimming='CharacterEllipsis'/></CheckBox></DataTemplate>");
        _body.Children.Add(list);
        foreach (var track in _tracks) track.PropertyChanged += (_, _) => { if (!_batch) Changed(); };
        Click += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, this)) return;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer viewport)
                {
                    double available = viewport.ActualHeight - TranslatePoint(new Point(0, ActualHeight), viewport).Y;
                    _body.Measure(new Size(Math.Max(1, ActualWidth - 2), double.PositiveInfinity));
                    double overhead = _body.DesiredSize.Height - list.DesiredSize.Height + 2;
                    list.MaxHeight = Math.Clamp(available - overhead, 36, 220); break;
                }
            Panel.SetZIndex(this, 30); ToggleContent(_body);
        };
        UpdateSummary();
    }

    public void SelectAll(bool selected)
    {
        _batch = true;
        try { foreach (var track in _tracks) track.IsSelected = selected; }
        finally { _batch = false; }
        Changed();
    }
    void Changed() { UpdateSummary(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    void UpdateSummary()
    {
        int count = _tracks.Count(t => t.IsSelected);
        string summary = string.Format(S("tracksSelected"), count, _tracks.Count);
        Content = summary; _count.Text = summary;
    }
}
