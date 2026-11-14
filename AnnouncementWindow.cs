using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMAP_WPF.Announcements;

namespace SMAP_WPF;

public sealed class AnnouncementWindow : ChromeWindow
{
    readonly AnnouncementService _service;
    readonly ListBox _list = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    readonly StackPanel _detail = new();
    readonly ScrollViewer _detailScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    readonly Button _refresh, _more;
    readonly CancellationTokenSource _lifetime = new();
    CancellationTokenSource? _detailRequest;
    long? _selectedId;
    readonly long? _initialId;
    bool _rebuilding, _loaded, _closed;
    int _generation;
    static string S(string key) => Lang.Announcement(key);

    public AnnouncementWindow(Window owner, AnnouncementService service, long? initialId = null) : base(S("title"), Math.Min(1060, Math.Max(680, SystemParameters.WorkArea.Width - 40)))
    {
        Owner = owner; _service = service; _initialId = initialId;
        foreach (var type in new[] { typeof(System.Windows.Controls.Primitives.ScrollBar), typeof(ContextMenu) })
            if (owner.TryFindResource(type) is Style style) Resources[type] = style;
        var body = new Grid { Margin = new Thickness(22), Height = Math.Min(560, Math.Max(300, SystemParameters.WorkArea.Height - 180)) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var head = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        head.Children.Add(Text(S("title"), 25, false, true));
        _refresh = Neutral(S("refresh")); _refresh.Height = 38; _refresh.MinWidth = 105; _refresh.HorizontalAlignment = HorizontalAlignment.Right;
        _refresh.Click += async (_, _) => { await _service.Refresh(); if (_selectedId is { } id && !_closed) await Open(id); }; head.Children.Add(_refresh); body.Children.Add(head);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); columns.ColumnDefinitions.Add(new() { Width = new GridLength(20) }); columns.ColumnDefinitions.Add(new());
        var left = new Grid(); left.RowDefinitions.Add(new()); left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var listFrame = new G2Border { CornerRadius = new(12), Padding = new(6), Background = B("PanelBg"), Child = _list };
        _list.SetResourceReference(Control.ForegroundProperty, "TextFg");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled); ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 6)));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
            "<ControlTemplate TargetType='ListBoxItem' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:g2='clr-namespace:SMAP_WPF;assembly=SMAP'><g2:G2Border Background='{TemplateBinding Background}' CornerRadius='9'><ContentPresenter HorizontalAlignment='Stretch'/></g2:G2Border></ControlTemplate>")));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, B("RowSel"))); itemStyle.Triggers.Add(selected);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Control.BackgroundProperty, B("MenuHi"))); itemStyle.Triggers.Add(hover);
        _list.ItemContainerStyle = itemStyle; _list.SelectionChanged += async (_, _) => { if (!_rebuilding && _list.SelectedItem is ListBoxItem { Tag: Announcement item }) await Open(item.Id); };
        left.Children.Add(listFrame); _more = Neutral(S("more")); _more.Height = 36; _more.Margin = new(0, 10, 0, 0); Grid.SetRow(_more, 1);
        _more.Click += async (_, _) => await _service.More(); left.Children.Add(_more); columns.Children.Add(left);
        _detailScroll.Content = _detail; Grid.SetColumn(_detailScroll, 2); columns.Children.Add(_detailScroll); Grid.SetRow(columns, 1); body.Children.Add(columns);
        _status.Foreground = B("SubTextFg"); _status.Margin = new(0, 14, 0, 0); Grid.SetRow(_status, 2); body.Children.Add(_status); SetBody(body);
        _detail.Children.Add(Text(S("choose"), 15, true)); _service.Changed += RefreshView;
        Loaded += async (_, _) => { _loaded = true; RefreshView(); await _service.Refresh(); };
        Closed += (_, _) => { _closed = true; _service.Changed -= RefreshView; _lifetime.Cancel(); _detailRequest?.Cancel(); _detailRequest?.Dispose(); _lifetime.Dispose(); };
        RefreshView();
    }
    TextBlock Text(string value, double size, bool muted = false, bool bold = false) => new()
    { Text = value, FontSize = size, Foreground = B(muted ? "SubTextFg" : "TextFg"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    void RefreshView()
    {
        if (_closed) return;
        _rebuilding = true;
        try
        {
            _list.Items.Clear();
            foreach (var item in _service.Items)
            {
                var row = new StackPanel { Margin = new(12) };
                var flags = new List<string>(); if (!_service.IsRead(item)) flags.Add(S("unread")); if (item.Important) flags.Add(S("important"));
                if (flags.Count > 0) { var badge = Text(string.Join(" · ", flags), 11); badge.Foreground = B("Accent"); badge.Margin = new(0, 0, 0, 7); row.Children.Add(badge); }
                var title = Text(item.Title, 15, false, true); title.MaxHeight = 44; title.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(title);
                if (item.Summary.Length > 0) { var summary = Text(item.Summary, 12, true); summary.MaxHeight = 38; summary.TextTrimming = TextTrimming.CharacterEllipsis; summary.Margin = new(0, 7, 0, 0); row.Children.Add(summary); }
                var date = Text(DateTimeOffset.FromUnixTimeSeconds(item.PublishedAt).ToLocalTime().ToString("yyyy-MM-dd"), 11, true); date.Margin = new(0, 8, 0, 0); row.Children.Add(date);
                var entry = new ListBoxItem { Content = row, Tag = item }; _list.Items.Add(entry); if (item.Id == _selectedId) _list.SelectedItem = entry;
            }
            _refresh.IsEnabled = !_service.Busy; _more.IsEnabled = !_service.Busy;
            _more.Visibility = _service.HasMore ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = _service.Busy ? S("loading") : _service.Failed ? S("error") : _service.Items.Count == 0 ? S("empty") : string.Format(S("loaded"), _service.Items.Count, _service.Total);
        }
        finally { _rebuilding = false; }
        if (_loaded && _selectedId == null)
        {
            if (_initialId is { } requested) { _ = Open(requested); }
            else if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }
    }
    public async Task Open(long id)
    {
        if (_closed) return;
        _selectedId = id; int generation = ++_generation; _detailRequest?.Cancel(); _detailRequest?.Dispose();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _detailRequest = request;
        _detail.Children.Clear(); _detail.Children.Add(Text(S("loading"), 14, true));
        try
        {
            var item = await _service.Detail(id, request.Token);
            if (_closed || generation != _generation) return;
            _detail.Children.Clear();
            if (item.Important) { var flag = Text(S("important"), 12, false, true); flag.Foreground = B("Accent"); flag.Margin = new(0, 0, 0, 10); _detail.Children.Add(flag); }
            var title = Text(item.Title, 25, false, true); title.LineHeight = 32; title.MaxHeight = 96; title.TextTrimming = TextTrimming.CharacterEllipsis;
            title.ToolTip = item.Title; title.Margin = new(0, 0, 0, 12); _detail.Children.Add(title);
            var date = Text(DateTimeOffset.FromUnixTimeSeconds(item.PublishedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm"), 12, true); date.Margin = new(0, 0, 0, 22); _detail.Children.Add(date);
            var content = Text(item.Content!, 15); content.LineHeight = 24; _detail.Children.Add(content); _detailScroll.ScrollToTop();
            try { await _service.MarkRead(item); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (!_closed) _status.Text = S("stateSaveError"); }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) when (AnnouncementService.IsReadError(error))
        {
            if (_closed || generation != _generation) return;
            _detail.Children.Clear(); _detail.Children.Add(Text(error is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ? S("unavailable") : S("error"), 14, true));
        }
    }
}

public sealed class AnnouncementNoticeWindow : ChromeWindow
{
    public event Action? ReadRequested;
    public AnnouncementNoticeWindow(Window owner, Announcement item) : base(Lang.Announcement("importantNew"), Math.Min(460, SystemParameters.WorkArea.Width - 40))
    {
        Owner = owner; ShowActivated = false; ShowInTaskbar = false;
        var body = new StackPanel { Margin = new(22) };
        body.Children.Add(new TextBlock { Text = item.Title, ToolTip = item.Title, FontSize = 20, LineHeight = 24, FontWeight = FontWeights.SemiBold, Foreground = B("TextFg"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 72, Margin = new(0, 0, 0, 14) });
        if (item.Summary.Length > 0) body.Children.Add(new TextBlock { Text = item.Summary, FontSize = 13, LineHeight = 20, Foreground = B("SubTextFg"), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 60, Margin = new(0, 0, 0, 20) });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var open = Neutral(Lang.Announcement("open")); open.Height = 38; open.MinWidth = 155; open.Margin = new(0, 0, 10, 0); open.Background = B("PracticeActionBg"); open.Foreground = B("ActionFg");
        open.Click += (_, _) => { Close(); ReadRequested?.Invoke(); };
        var later = Neutral(Lang.Announcement("later")); later.Height = 38; later.MinWidth = 90; later.Click += (_, _) => Close();
        actions.Children.Add(open); actions.Children.Add(later); body.Children.Add(actions); SetBody(body);
    }
}
