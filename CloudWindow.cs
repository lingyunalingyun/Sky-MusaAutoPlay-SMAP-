using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>在线曲库浏览窗口: 自定义圆角标题栏 + 胶囊工具栏(搜索/排序/难度/刷新) + 圆角列表 + 页码跳转 + 下载。跟随主题。</summary>
public class CloudWindow : Window
{
    const int PerPage = 20;
    readonly Action _onDownloaded;

    readonly TextBox _search = new();
    readonly TextBox _pageBox = new();
    readonly ComboBox _diff = new();
    readonly ListView _list = new();
    readonly TextBlock _totalLbl = new();
    Button _prev = null!, _next = null!, _download = null!;
    readonly Brush _green = new SolidColorBrush(Color.FromRgb(0x12, 0x79, 0x5a));

    int _sortMode = 1;          // 0=A-Z 1=上传时间 2=点赞 3=下载量
    int _page = 1, _pages = 1;

    Brush B(string k) => (Brush)Application.Current.Resources[k];

    public CloudWindow(Window owner, Action onDownloaded)
    {
        _onDownloaded = onDownloaded;
        Owner = owner;
        Title = "SMAP - 云端曲库";
        Width = 1180; Height = 720;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Border { Background = B("WindowBg"), CornerRadius = new CornerRadius(14), BorderBrush = B("WindowBorder"), BorderThickness = new Thickness(1), Margin = new Thickness(6) };
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 标题栏
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 工具栏
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 列表
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 底部

        grid.Children.Add(TitleBar());
        grid.Children.Add(Toolbar());
        grid.Children.Add(ListArea());
        grid.Children.Add(BottomBar());
        root.Child = grid;
        Content = root;

        BuildComplete();   // 高亮默认排序并首次加载
    }

    // ---- 标题栏 ----
    UIElement TitleBar()
    {
        var bar = new Border { Height = 34, CornerRadius = new CornerRadius(9), Background = B("TitleGrad") };
        Grid.SetRow(bar, 0);
        bar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var g = new Grid();
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        left.Children.Add(new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xf5)) });
        left.Children.Add(new TextBlock { Text = "SMAP - 云端曲库", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
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

    // ---- 工具栏 ----
    UIElement Toolbar()
    {
        var dock = new DockPanel { Margin = new Thickness(4, 14, 4, 0), LastChildFill = false };
        Grid.SetRow(dock, 1);

        // 左: 搜索
        StyleBox(_search); _search.Width = 300; _search.Height = 32;
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _page = 1; Load(); } };
        var searchBtn = Btn("🔍 搜索"); searchBtn.Click += (_, __) => { _page = 1; Load(); };
        var lp = new StackPanel { Orientation = Orientation.Horizontal };
        lp.Children.Add(Wrap(_search)); lp.Children.Add(Gap()); lp.Children.Add(searchBtn);
        DockPanel.SetDock(lp, Dock.Left); dock.Children.Add(lp);

        // 右(从右往左): 刷新 / 难度 / 排序胶囊
        var rp = new StackPanel { Orientation = Orientation.Horizontal };
        string[] sorts = { "A-Z", "上传时间", "点赞", "下载量" };
        for (int i = 0; i < 4; i++)
        {
            int idx = i;
            var pill = Btn(sorts[i]);
            pill.Click += (_, __) => SetSort(idx);
            _sortPillsList[i] = pill;
            rp.Children.Add(pill); rp.Children.Add(Gap());
        }
        _diff.ItemsSource = new[] { "全部难度", "★", "★★", "★★★", "★★★★", "★★★★★" };
        _diff.SelectedIndex = 0; _diff.Height = 32; _diff.Width = 96; _diff.Foreground = B("TextFg"); _diff.Background = B("ComboBg");
        StyleCombo(_diff);
        _diff.SelectionChanged += (_, __) => { _page = 1; Load(); };
        rp.Children.Add(_diff); rp.Children.Add(Gap());
        var refresh = Btn("🔄 刷新"); refresh.Click += (_, __) => Load();
        rp.Children.Add(refresh);
        DockPanel.SetDock(rp, Dock.Right); dock.Children.Add(rp);

        return dock;
    }
    readonly Button[] _sortPillsList = new Button[4];

    // ---- 列表 ----
    UIElement ListArea()
    {
        var border = new Border { Background = B("ListBg"), CornerRadius = new CornerRadius(12), BorderBrush = B("ListBorder"), BorderThickness = new Thickness(1), Margin = new Thickness(4, 14, 4, 0) };
        Grid.SetRow(border, 2);

        _list.Background = Brushes.Transparent; _list.Foreground = B("TextFg"); _list.BorderThickness = new Thickness(0); _list.Margin = new Thickness(6);
        var gv = new GridView();
        gv.Columns.Add(Col("曲名", "Title", 200));
        gv.Columns.Add(Col("作者", "Artist", 120));
        gv.Columns.Add(Col("创谱", "TranscribedBy", 90));
        gv.Columns.Add(Col("难度", "Stars", 90));
        gv.Columns.Add(Col("BPM", "Bpm", 55));
        gv.Columns.Add(Col("下载量", "Downloads", 65));
        gv.Columns.Add(Col("点赞数", "Likes", 65));
        gv.Columns.Add(Col("上传者", "Uploader", 100));
        gv.Columns.Add(Col("上传时间", "UploadTime", 150));
        _list.View = gv;
        StyleHeaders();
        StyleItems();
        _list.SelectionChanged += (_, __) =>
        {
            bool sel = _list.SelectedItem is CloudSheet;
            _download.IsEnabled = sel;
            _download.Background = sel ? _green : B("NeutralBtnBg");
            _download.Foreground = sel ? Brushes.White : B("SubTextFg");
        };
        _list.MouseDoubleClick += (_, __) => DoDownload();

        border.Child = _list;
        return border;
    }

    // ---- 底部: 翻页 + 下载 ----
    UIElement BottomBar()
    {
        var grid = new Grid { Margin = new Thickness(4, 14, 4, 0) };
        Grid.SetRow(grid, 3);

        var pager = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _prev = Btn("← 上一页"); _prev.Click += (_, __) => { if (_page > 1) { _page--; Load(); } };
        _next = Btn("下一页 →"); _next.Click += (_, __) => { if (_page < _pages) { _page++; Load(); } };
        StyleBox(_pageBox); _pageBox.Width = 54; _pageBox.Height = 30; _pageBox.HorizontalContentAlignment = HorizontalAlignment.Center; _pageBox.Text = "1";
        _pageBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Jump(); };
        var jump = Btn("跳转"); jump.Click += (_, __) => Jump();
        _totalLbl.Foreground = B("TextFg"); _totalLbl.VerticalAlignment = VerticalAlignment.Center;

        pager.Children.Add(_prev); pager.Children.Add(Gap());
        pager.Children.Add(new TextBlock { Text = "第", Foreground = B("TextFg"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
        pager.Children.Add(Wrap(_pageBox));
        pager.Children.Add(_totalLbl);
        pager.Children.Add(Gap()); pager.Children.Add(jump);
        pager.Children.Add(Gap()); pager.Children.Add(_next);
        grid.Children.Add(pager);

        // 默认灰(不可选), 选中后变绿
        _download = Btn("↓ 下载到本地", B("NeutralBtnBg"), B("SubTextFg"));
        _download.FontWeight = FontWeights.Bold; _download.IsEnabled = false; _download.Height = 38;
        _download.HorizontalAlignment = HorizontalAlignment.Right; _download.VerticalAlignment = VerticalAlignment.Center;
        _download.Click += (_, __) => DoDownload();
        grid.Children.Add(_download);

        return grid;
    }

    void BuildComplete() => SetSort(_sortMode);   // 初始高亮默认排序

    void Jump()
    {
        if (int.TryParse(_pageBox.Text.Trim(), out int p)) { _page = Math.Clamp(p, 1, _pages); Load(); }
    }

    void SetSort(int mode)
    {
        _sortMode = mode;
        for (int i = 0; i < 4; i++)
        {
            bool on = i == mode;
            _sortPillsList[i].Background = on ? new SolidColorBrush(Color.FromRgb(0x2d, 0x5a, 0x88)) : B("NeutralBtnBg");
            _sortPillsList[i].Foreground = on ? Brushes.White : B("TextFg");
        }
        _page = 1;
        Load();
    }

    async void Load()
    {
        _totalLbl.Text = "/… 加载中";
        string sort = _sortMode switch { 2 => "hot", 3 => "downloads", _ => "newest" };  // A-Z/上传时间 都取 newest
        int diff = _diff.SelectedIndex;   // 0=全部, 1..5
        var r = await CloudApi.ListAsync(_search.Text, sort, diff, _page, PerPage);
        if (!r.Ok) { _totalLbl.Text = r.Err ?? "加载失败"; return; }

        var items = r.Items;
        if (_sortMode == 0) items = items.OrderBy(s => s.Title, StringComparer.CurrentCulture).ToList();   // A-Z 客户端排当前页
        _pages = r.Pages;
        _list.ItemsSource = items;
        _pageBox.Text = _page.ToString();
        _totalLbl.Text = $"/{_pages}页";
        _prev.IsEnabled = _page > 1;
        _next.IsEnabled = _page < _pages;
    }

    async void DoDownload()
    {
        if (_list.SelectedItem is not CloudSheet s) return;
        _download.IsEnabled = false;
        var old = _download.Content;
        _download.Content = "下载中...";
        var path = await CloudApi.DownloadAsync(s, SongLibrary.SongsDir, err => MsgBox.Info(this, err, "下载"));
        _download.Content = old;
        _download.IsEnabled = _list.SelectedItem is CloudSheet;
        if (path != null) { s.Downloads++; _list.Items.Refresh(); _onDownloaded(); }
    }

    // ---- 小工具 ----
    Button Btn(string text, Brush? bg = null, Brush? fg = null) => new()
    {
        Content = text, Height = 32, Cursor = Cursors.Hand, FontSize = 13,
        Background = bg ?? B("NeutralBtnBg"), Foreground = fg ?? B("TextFg"),
        BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 0, 12, 0), Template = BtnTpl()
    };
    static FrameworkElement Gap() => new() { Width = 8 };
    void StyleBox(TextBox b) { b.Background = B("BoxBg"); b.Foreground = B("TextFg"); b.CaretBrush = B("TextFg"); b.BorderThickness = new Thickness(0); b.VerticalContentAlignment = VerticalAlignment.Center; b.Padding = new Thickness(10, 0, 10, 0); }
    Border Wrap(TextBox b) => new() { Background = B("BoxBg"), BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = b };

    static GridViewColumn Col(string header, string path, double width) =>
        new() { Header = header, DisplayMemberBinding = new System.Windows.Data.Binding(path), Width = width };

    void StyleHeaders()
    {
        var st = new Style(typeof(GridViewColumnHeader));
        st.Setters.Add(new Setter(FrameworkElement.HeightProperty, 32.0));
        st.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
@"<ControlTemplate TargetType='GridViewColumnHeader' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='bd' Background='{DynamicResource PanelBg}' BorderBrush='{DynamicResource ListBorder}' BorderThickness='0,0,1,0'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' TextElement.Foreground='{DynamicResource TextFg}'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#3a3a46'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>")));
        _list.Resources.Add(typeof(GridViewColumnHeader), st);
    }

    void StyleItems()
    {
        var border = new System.Windows.FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new Thickness(2, 3, 2, 3));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        var presenter = new System.Windows.FrameworkElementFactory(typeof(GridViewRowPresenter));
        presenter.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new System.Windows.TemplateBindingExtension(Control.ForegroundProperty));
        border.AppendChild(presenter);
        var tmpl = new ControlTemplate(typeof(ListViewItem)) { VisualTree = border };

        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, B("TextFg")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var selTrig = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selTrig.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Theme.ListSel)));
        style.Triggers.Add(selTrig);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Theme.ListHover)));
        style.Triggers.Add(hover);
        _list.ItemContainerStyle = style;
    }

    // 圆角深色下拉(跟随主题, DynamicResource 解析到 App 资源)
    void StyleCombo(ComboBox cb)
    {
        cb.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
@"<ControlTemplate TargetType='ComboBox' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Grid>
    <ToggleButton Focusable='False' ClickMode='Press' Background='{DynamicResource ComboBg}' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
      <ToggleButton.Template>
        <ControlTemplate TargetType='ToggleButton'>
          <Border Background='{TemplateBinding Background}' BorderBrush='{DynamicResource BoxBorder}' BorderThickness='1' CornerRadius='8'>
            <Grid TextElement.Foreground='{DynamicResource TextFg}'>
              <ContentPresenter Margin='10,0,24,0' HorizontalAlignment='Left' VerticalAlignment='Center' Content='{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}'/>
              <Path Data='M0,0 L4,4 L8,0 Z' Fill='#999' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,10,0'/>
            </Grid>
          </Border>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <Popup IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False' PopupAnimation='Slide'>
      <Border Background='{DynamicResource ComboBg}' BorderBrush='{DynamicResource BoxBorder}' BorderThickness='1' CornerRadius='8' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}'>
        <StackPanel IsItemsHost='True' Margin='2'/>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>");
        cb.ItemContainerStyle = (Style)System.Windows.Markup.XamlReader.Parse(
@"<Style TargetType='ComboBoxItem' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Setter Property='Foreground' Value='{DynamicResource TextFg}'/>
  <Setter Property='Padding' Value='10,6'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='ComboBoxItem'>
        <Border x:Name='bd' Background='Transparent' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='#2d5a88'/><Setter Property='Foreground' Value='White'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
    }

    const string XmlNs = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";
    static ControlTemplate BtnTpl() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        $@"<ControlTemplate TargetType='Button' {XmlNs}>
            <Border x:Name='bd' Background='{{TemplateBinding Background}}' BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='{{TemplateBinding BorderThickness}}' CornerRadius='8' Padding='{{TemplateBinding Padding}}'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate>");
}
