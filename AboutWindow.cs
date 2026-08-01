using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>软件信息窗口: 自定义圆角标题栏 + 名称/版本/作者/仓库 + 检查更新/语言/上传日志。跟随主题与语言。</summary>
public class AboutWindow : Window
{
    const string Repo = "https://github.com/lingyunalingyun/Sky-MusaAutoPlay-SMAP-";
    static readonly string LastCheckFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "lastcheck.txt");

    readonly TextBlock _titleText = new() { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock _nameLine, _verLine, _authorLine;
    readonly Run _repoPrefix = new();
    readonly Run _checkMain = new(), _checkSub = new() { FontSize = 11 };
    readonly Button _langBtn, _logBtn;

    public AboutWindow(Window owner)
    {
        Owner = owner;
        Width = 640; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var logo = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        Icon = logo;

        var root = new Border
        {
            Background = B("WindowBg"), CornerRadius = new CornerRadius(14),
            BorderBrush = B("WindowBorder"), BorderThickness = new Thickness(1), Margin = new Thickness(6)
        };
        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 标题栏
        var title = new Border { Height = 36, CornerRadius = new CornerRadius(9), Background = B("TitleGrad") };
        title.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var titleGrid = new Grid();
        var titleBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        titleBar.Children.Add(new System.Windows.Shapes.Ellipse { Width = 13, Height = 13, Fill = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xf5)) });
        titleBar.Children.Add(_titleText);
        var close = new Button { Content = "✕", Width = 34, Height = 26, Foreground = Brushes.White, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0), Template = CaptionTemplate() };
        close.Click += (_, __) => Close();
        titleGrid.Children.Add(titleBar);
        titleGrid.Children.Add(close);
        title.Child = titleGrid;
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        // 内容
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };
        body.Children.Add(new Image { Source = logo, Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });
        _nameLine = Line(15, true);
        _nameLine.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(_nameLine);
        body.Children.Add(new TextBlock { Text = "Sky-MusaAutoPlay (SMAP)", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = B("TextFg"), HorizontalAlignment = HorizontalAlignment.Center });
        body.Children.Add(Gap(16));
        _verLine = Line(14, false); body.Children.Add(_verLine);
        body.Children.Add(Gap(14));
        _authorLine = Line(14, false); body.Children.Add(_authorLine);

        var repoTb = new TextBlock { Foreground = B("TextFg"), FontSize = 14, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
        repoTb.Inlines.Add(_repoPrefix);
        var link = new Hyperlink(new Run(Repo)) { NavigateUri = new Uri(Repo), Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0x8e, 0xff)) };
        link.RequestNavigate += (_, e) => { Open(e.Uri.ToString()); e.Handled = true; };
        repoTb.Inlines.Add(link);
        body.Children.Add(repoTb);

        body.Children.Add(Gap(22));

        var checkContent = new TextBlock();
        checkContent.Inlines.Add(_checkMain);
        checkContent.Inlines.Add(_checkSub);
        var checkBtn = Neutral(); checkBtn.Content = checkContent;
        checkBtn.Click += (_, __) => CheckUpdate(checkBtn);
        body.Children.Add(checkBtn);

        _langBtn = Neutral(); _langBtn.Margin = new Thickness(0, 10, 0, 0);
        _langBtn.Click += (_, __) => ShowLangMenu();
        body.Children.Add(_langBtn);

        _logBtn = Neutral(); _logBtn.Margin = new Thickness(0, 10, 0, 0);
        _logBtn.Click += (_, __) => MessageBox.Show(this, "日志上传功能开发中。", "Log");
        body.Children.Add(_logBtn);

        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        root.Child = grid;
        Content = root;

        ApplyLang();
    }

    void ApplyLang()
    {
        Title = Lang.S("about");
        _titleText.Text = Lang.S("about");
        _nameLine.Text = Lang.S("about.name");
        _verLine.Text = $"{Lang.S("about.version")}: v{UpdateChecker.AppVersion}-WPF";
        _authorLine.Text = $"{Lang.S("about.author")}: LingYunALingYun";
        _repoPrefix.Text = $"{Lang.S("about.repo")}: ";
        _checkMain.Text = Lang.S("about.check");
        _checkSub.Text = $"  ({Lang.S("about.lastcheck")}: {LoadLastCheck()})";
        _langBtn.Content = $"{Lang.S("about.lang")}: {Lang.Names[(int)Lang.Current]}";
        _logBtn.Content = Lang.S("about.log");
    }

    void ShowLangMenu()
    {
        var cm = new ContextMenu { PlacementTarget = _langBtn };
        for (int i = 0; i < Lang.Names.Length; i++)
        {
            var mi = new MenuItem { Header = Lang.Names[i] };
            var lang = (AppLang)i;
            mi.Click += (_, __) =>
            {
                Lang.Set(lang);
                ApplyLang();
                (Owner as MainWindow)?.ApplyLanguage();
            };
            cm.Items.Add(mi);
        }
        cm.IsOpen = true;
    }

    async void CheckUpdate(Button btn)
    {
        btn.IsEnabled = false;
        var r = await UpdateChecker.CheckAsync();
        var now = DateTime.Now.ToString("yyyy-MM-dd");
        SaveLastCheck(now);
        _checkSub.Text = $"  ({Lang.S("about.lastcheck")}: {now})";
        btn.IsEnabled = true;
        if (r is { } rel)
        {
            if (MessageBox.Show(this, $"v{rel.Tag}\n{Lang.S("about.version")}: v{UpdateChecker.AppVersion}\n\nGitHub?", Lang.S("about.check"), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                Open(rel.Url.Length > 0 ? rel.Url : Repo);
        }
        else MessageBox.Show(this, Lang.S("about.latest"), Lang.S("about.check"));
    }

    string LoadLastCheck()
    {
        try { if (File.Exists(LastCheckFile)) return File.ReadAllText(LastCheckFile).Trim(); } catch { }
        return Lang.S("about.never");
    }
    static void SaveLastCheck(string d)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(LastCheckFile)!); File.WriteAllText(LastCheckFile, d); } catch { }
    }
    static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    Brush B(string key) => (Brush)Application.Current.Resources[key];
    TextBlock Line(double size, bool bold) => new() { FontSize = size, Foreground = B("TextFg"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    static FrameworkElement Gap(double h) => new FrameworkElement { Height = h };

    Button Neutral() => new()
    {
        Height = 44, FontSize = 14, Cursor = Cursors.Hand,
        Foreground = B("NeutralBtnFg"), Background = B("NeutralBtnBg"),
        BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1),
        Template = NeutralTemplate()
    };

    const string XmlNs = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

    static ControlTemplate NeutralTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        $@"<ControlTemplate TargetType='Button' {XmlNs}>
            <Border x:Name='bd' Background='{{TemplateBinding Background}}' BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='{{TemplateBinding BorderThickness}}' CornerRadius='8'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Opacity' Value='0.85'/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate>");

    static ControlTemplate CaptionTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        $@"<ControlTemplate TargetType='Button' {XmlNs}>
            <Border x:Name='bd' Background='{{TemplateBinding Background}}' CornerRadius='6'>
              <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='#40000000'/></Trigger></ControlTemplate.Triggers>
          </ControlTemplate>");
}
