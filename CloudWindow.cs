using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>在线曲库浏览窗口: 搜索/排序/难度筛选/翻页/下载到本地曲库。</summary>
public class CloudWindow : Window
{
    const int PerPage = 20;
    readonly Action _onDownloaded;

    readonly TextBox _search = new() { Height = 28, Width = 200, VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)) };
    readonly ComboBox _sort = new() { Height = 28, Width = 100, ItemsSource = new[] { "最新", "最热", "下载最多" }, SelectedIndex = 0 };
    readonly ComboBox _diff = new() { Height = 28, Width = 110, ItemsSource = new[] { "全部难度", "★", "★★", "★★★", "★★★★", "★★★★★" }, SelectedIndex = 0 };
    readonly ListView _list = new() { Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)), Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)) };
    readonly Button _download = new() { Content = "↓ 下载到本地曲库", Height = 32, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), IsEnabled = false, Padding = new Thickness(10, 0, 10, 0) };
    readonly Button _prev = new() { Content = "← 上一页", Height = 28, Padding = new Thickness(8, 0, 8, 0) };
    readonly Button _next = new() { Content = "下一页 →", Height = 28, Padding = new Thickness(8, 0, 8, 0) };
    readonly TextBlock _pageLbl = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xbb, 0xbb, 0xbb)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), VerticalAlignment = VerticalAlignment.Center };

    int _page = 1, _pages = 1;

    public CloudWindow(Window owner, Action onDownloaded)
    {
        _onDownloaded = onDownloaded;
        Title = "在线曲库 — 缪斯树屋";
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        Width = 860; Height = 600; Owner = owner;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e));

        BuildColumns();
        StyleItems();

        var search = new Button { Content = "🔍 搜索", Height = 28, Padding = new Thickness(8, 0, 8, 0) };
        var refresh = new Button { Content = "🔄 刷新", Height = 28, Padding = new Thickness(8, 0, 8, 0) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in new UIElement[] { _search, search, _sort, _diff, refresh })
        { if (c is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 0); toolbar.Children.Add(c); }

        search.Click += (_, __) => { _page = 1; Load(); };
        refresh.Click += (_, __) => Load();
        _search.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { _page = 1; Load(); } };
        _sort.SelectionChanged += (_, __) => { _page = 1; Load(); };
        _diff.SelectionChanged += (_, __) => { _page = 1; Load(); };
        _list.SelectionChanged += (_, __) => _download.IsEnabled = _list.SelectedItem is CloudSheet;
        _list.MouseDoubleClick += (_, __) => DoDownload();
        _download.Click += (_, __) => DoDownload();
        _prev.Click += (_, __) => { if (_page > 1) { _page--; Load(); } };
        _next.Click += (_, __) => { if (_page < _pages) { _page++; Load(); } };

        var pager = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        pager.Children.Add(_download);
        pager.Children.Add(new FrameworkElement { Width = 20 });
        pager.Children.Add(_prev);
        pager.Children.Add(_pageLbl);
        pager.Children.Add(_next);
        pager.Children.Add(new FrameworkElement { Width = 16 });
        pager.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(pager, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(pager);
        root.Children.Add(_list);
        Content = root;

        Loaded += (_, __) => Load();
    }

    void BuildColumns()
    {
        var gv = new GridView();
        gv.Columns.Add(Col("曲名", "Title", 220));
        gv.Columns.Add(Col("作者", "Artist", 120));
        gv.Columns.Add(Col("创谱", "TranscribedBy", 90));
        gv.Columns.Add(Col("难度", "Stars", 90));
        gv.Columns.Add(Col("BPM", "Bpm", 55));
        gv.Columns.Add(Col("↓", "Downloads", 50));
        gv.Columns.Add(Col("♥", "Likes", 45));
        gv.Columns.Add(Col("上传者", "Uploader", 100));
        _list.View = gv;
    }

    static GridViewColumn Col(string header, string path, double width) =>
        new() { Header = header, DisplayMemberBinding = new System.Windows.Data.Binding(path), Width = width };

    // 自绘 ListViewItem 模板: 选中=深蓝白字 / 悬停=深灰(默认浅蓝高亮会让浅色文字看不见)
    void StyleItems()
    {
        var sel = new SolidColorBrush(Color.FromRgb(0x2d, 0x5a, 0x88));
        var light = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));

        var border = new System.Windows.FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new Thickness(2, 3, 2, 3));
        var presenter = new System.Windows.FrameworkElementFactory(typeof(GridViewRowPresenter));
        presenter.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new System.Windows.TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(presenter);
        var tmpl = new ControlTemplate(typeof(ListViewItem)) { VisualTree = border };

        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, light));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        var selTrig = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selTrig.Setters.Add(new Setter(Control.BackgroundProperty, sel));
        selTrig.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        style.Triggers.Add(selTrig);

        var hoverTrig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrig.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))));
        style.Triggers.Add(hoverTrig);

        _list.ItemContainerStyle = style;
    }

    async void Load()
    {
        _status.Text = "加载中...";
        string sort = _sort.SelectedIndex switch { 1 => "hot", 2 => "downloads", _ => "newest" };
        int diff = _diff.SelectedIndex;   // 0=全部, 1..5=难度
        var r = await CloudApi.ListAsync(_search.Text, sort, diff, _page, PerPage);
        if (!r.Ok) { _status.Text = r.Err ?? "加载失败"; return; }

        _pages = r.Pages;
        _list.ItemsSource = r.Items;
        _pageLbl.Text = $"第 {_page}/{_pages} 页";
        _prev.IsEnabled = _page > 1;
        _next.IsEnabled = _page < _pages;
        _status.Text = $"总数 {r.Total}, 本页 {r.Items.Count} 首";
    }

    async void DoDownload()
    {
        if (_list.SelectedItem is not CloudSheet s) return;
        _download.IsEnabled = false;
        _status.Text = "下载中: " + s.Title;
        var path = await CloudApi.DownloadAsync(s, SongLibrary.SongsDir, err => _status.Text = err);
        _download.IsEnabled = true;
        if (path != null)
        {
            _status.Text = "✓ 已下载: " + System.IO.Path.GetFileName(path);
            s.Downloads++;
            _list.Items.Refresh();
            _onDownloaded();
        }
    }
}
