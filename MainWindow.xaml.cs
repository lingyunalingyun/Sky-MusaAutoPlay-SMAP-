using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class MainWindow : Window
{
    readonly Button[] _pianoButtons = new Button[15];
    readonly SkyPlayer _player = new();
    DispatcherTimer? _countdown;
    List<(int key, double ms)> _notes = new();
    double _speed = 1.0;
    bool _playing, _paused, _previewing;

    // 播放列表 (音乐软件式: 双击曲库加入, 跨会话持久化, 内部切歌/自动续播)
    readonly System.Collections.ObjectModel.ObservableCollection<SongInfo> _playlist = new();
    SongInfo? _playCurrent;   // 当前播放上下文对应的播放列表条目(切歌/续播/自动续播用)
    SongInfo? _nowPlaying;    // 当前正在发声的曲目(不论从曲库还是列表启动)
    bool _previewMode;        // 试听模式: 播放键走扬声器(音频)而非发送游戏按键
    static readonly SolidColorBrush _gold = new(Color.FromRgb(0xE6, 0xB5, 0x2A));   // 收藏星填充色
    enum PlayMode { RepeatAll, RepeatOne, Shuffle }   // 列表循环 / 单曲循环 / 随机播放
    PlayMode _playMode = PlayMode.RepeatAll;
    readonly Random _rng = new();
    static string PlayModeFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "playmode.txt");

    // 收藏夹(歌单)
    readonly System.Collections.ObjectModel.ObservableCollection<Folder> _folders = new();
    Folder? _currentFolder;   // 中栏正在查看的收藏夹; null=整个本地曲库

    // 云端曲库(内联, 无限滚动)
    readonly System.Collections.ObjectModel.ObservableCollection<CloudSheet> _cloud = new();
    bool _cloudMode, _cloudLoading;
    int _cloudPage = 1, _cloudPages = 1;
    static string PlaylistFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "playlist.txt");

    int _remapIndex = -1;   // >=0 时表示正在等待为该光遇键重绑物理键

    static MainWindow()
    {
        // 所有 WPF 动画默认帧率提到 120fps(WPF 默认约 60), 一处生效、全局动画受益
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline),
            new FrameworkPropertyMetadata { DefaultValue = 120 });
        // TextBox 获焦点时才启用输入法(主窗口默认禁用见实例构造), 免物理键弹琴弹出中文候选
        EventManager.RegisterClassHandler(typeof(TextBox), GotFocusEvent,
            new RoutedEventHandler((s, _) => System.Windows.Input.InputMethod.SetIsInputMethodEnabled((TextBox)s, true)));
    }

    public MainWindow()
    {
        Lang.Load();
        Theme.Apply(Theme.LoadDark());   // 资源就位后 InitializeComponent 里的 DynamicResource 才解析得到
        InitializeComponent();
        _player.Vk = KeyConfig.Load();
        BuildPianoGrid();
        BuildPracticeGrid();
        PracticeCard.RenderTransform = new TransformGroup { Children = { _pCardScale, _pCardTrans } };
        _rootShadow = WindowRoot.Effect;   // 练习转场期间临时摘除, 免整窗子树每帧重渲染进阴影 Effect 拖垮帧率
        BuildSettingsGrid();
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);   // 锁定输入法: 物理键弹琴不弹中文候选
        SortCombo.SelectionChanged += (_, __) => { if (!_cloudMode) ApplyFilter(); };
        FilterCombo.SelectionChanged += (_, __) => { if (!_cloudMode) ApplyFilter(); };
        SearchBox.TextChanged += (_, __) => { if (!_cloudMode) ApplyFilter(); };
        SearchBox.KeyDown += (_, e) => { if (_cloudMode && e.Key == System.Windows.Input.Key.Enter) { _cloudPage = 1; _ = LoadCloud(false); } };
        SongList.SelectionChanged += (_, __) => OnSongSelected();
        CloudList.ItemsSource = _cloud;
        CloudSortCombo.ItemsSource = new[] { "最新", "最热", "下载量" };
        CloudSortCombo.SelectedIndex = 0;
        CloudDiffCombo.ItemsSource = new[] { "全部难度", "★", "★★", "★★★", "★★★★", "★★★★★" };
        CloudDiffCombo.SelectedIndex = 0;
        CloudSortCombo.SelectionChanged += (_, __) => { if (_cloudMode) { _cloudPage = 1; _ = LoadCloud(false); } };
        CloudDiffCombo.SelectionChanged += (_, __) => { if (_cloudMode) { _cloudPage = 1; _ = LoadCloud(false); } };
        CloudList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(CloudScroll));

        CloudApi.LoadAuth();
        UpdateLoginButton();
        SetLibTab(true);
        RefreshLibrary();
        PlaylistView.ItemsSource = _playlist;
        LoadPlaylist();
        LoadPlayMode();
        foreach (var f in FolderStore.Load()) _folders.Add(f);
        FolderList.ItemsSource = _folders;
        UpdateFoldersHeader();
        ApplyLanguage();

        Title = Lang.S("app.title") + $"  v{UpdateChecker.AppVersion}";
        _ = CheckUpdateAsync();
    }

    // 应用当前语言到主界面全部静态文字 + 下拉/菜单
    public void ApplyLanguage()
    {
        AppTitleText.Text = Lang.S("app.title");
        CountdownLabel.Text = Lang.S("countdown");
        StartBtn.Content = Lang.S("btn.start");
        PauseBtn.Content = Lang.S("btn.pause");
        PreviewBtn.Content = Lang.S("btn.preview");
        EditBtn.Content = Lang.S("btn.edit");
        CreateBtn.Content = Lang.S("btn.create");
        CloudBtn.Content = Lang.S("btn.cloud");
        ImportBtn.Content = Lang.S("btn.import");
        RefreshBtn.Content = Lang.S("btn.refresh");
        if (!CloudApi.LoggedIn) LoginBtn.Content = Lang.S("btn.login");
        UpdateLibHeader();
        KeysHeader.Text = Lang.S("keys.header");
        KeysHint.Text = Lang.S("keys.hint");
        KeyEditBtn.Content = _editingKeys ? Lang.S("keys.save") : Lang.S("keys.edit");
        CaveBtn.Content = $"{Lang.S("cave")}: {Lang.S(AudioEngine.Cave ? "on" : "off")}";
        InstrumentBtn.Content = $"{Lang.S("instrument")}: {Lang.Instrument(_instrumentName)}";
        InstrumentPill.Content = $"{Lang.S("instrument")}:{Lang.Instrument(_instrumentName)}";
        _instrumentMenu = null;   // 语言变了, 重建带翻译的音色菜单
        RefreshPitchPill();
        ThemeBtn.Content = $"{Lang.S("theme")}: {Lang.S(Theme.Dark ? "theme.dark" : "theme.light")}";
        AboutBtn.Content = Lang.S("about");

        int si = SortCombo.SelectedIndex < 0 ? 0 : SortCombo.SelectedIndex;
        SortCombo.ItemsSource = new[] { Lang.S("sort.az"), Lang.S("sort.za"), Lang.S("sort.fav") };
        SortCombo.SelectedIndex = si;

        RebuildFilterOptions();

        // ── 侧边栏 ──
        LocalLibBtn.Content = Lang.S("nav.local");
        CloudLibBtn.Content = Lang.S("nav.cloud");
        SideImportBtn.Content = Lang.S("side.import");
        SideSettingsBtn.Content = Lang.S("side.settings");
        UpdateFoldersHeader();
        UpdateProfileCard();

        // ── 右栏 / 播放器 ──
        CreateRightBtn.Content = Lang.S("right.create");
        PracticeBtn.Content = Lang.S("right.practice");
        PracticeBackBtn.Content = Lang.S("practice.back");
        SearchBox.ToolTip = Lang.S("search.hint");
        PrevBtn.ToolTip = Lang.S("tip.prev");
        NextBtn.ToolTip = Lang.S("tip.next");
        PlaylistBtn.ToolTip = Lang.S("tip.playlist");
        PreviewIcon.ToolTip = Lang.S("tip.preview");
        CaveIcon.ToolTip = Lang.S("tip.cave");
        InstrumentPill.ToolTip = Lang.S("tip.inst");
        PlayModeBtn.ToolTip = $"{Lang.S("tip.playmode")}: {PlayModeName(_playMode)}";
        ProgBar.ToolTip = Lang.S("tip.seek");
        if (!_playing && !_previewing) PlayerSongName.Text = Lang.S("player.nosong");
        PlayerAuthor.Text = Lang.S("player.artist");
        PlayerTranscriber.Text = Lang.S("player.trans");

        // ── 播放列表面板 ──
        PlaylistTitle.Text = Lang.S("pl.title");
        PlaylistClearBtn.Content = Lang.S("pl.clear");
        PlaylistEmpty.Text = Lang.S("pl.empty");
        UpdatePlaylistHeader();

        // ── 云端筛选/排序下拉 ──
        int cs = CloudSortCombo.SelectedIndex < 0 ? 0 : CloudSortCombo.SelectedIndex;
        CloudSortCombo.ItemsSource = new[] { Lang.S("cloud.newest"), Lang.S("cloud.hot"), Lang.S("cloud.downs") };
        CloudSortCombo.SelectedIndex = cs;
        int cd = CloudDiffCombo.SelectedIndex < 0 ? 0 : CloudDiffCombo.SelectedIndex;
        CloudDiffCombo.ItemsSource = new[] { Lang.S("cloud.diffall"), "★", "★★", "★★★", "★★★★", "★★★★★" };
        CloudDiffCombo.SelectedIndex = cd;

        // ── 设置界面 ──
        SetTitleTx.Text = Lang.S("set.title");
        SetLangLbl.Text = Lang.S("set.lang");
        SetThemeLbl.Text = Lang.S("set.theme");
        SetUpdateLbl.Text = Lang.S("set.update");
        SetUpdateBtn.Content = Lang.S("set.checkupd");
        SetLogLbl.Text = Lang.S("set.log");
        SetLogBtn.Content = Lang.S("set.uplog");
        SetUiLbl.Text = Lang.S("set.ui");
        SetFontLbl.Text = Lang.S("set.font");
        SetWaitLbl.Text = Lang.S("set.wait");
        SetBindLbl.Text = Lang.S("set.bind");
        SetBindBtn.Content = _editingKeys ? Lang.S("set.bindDone") : Lang.S("set.bindEdit");
        SettingsBindHint.Text = Lang.S("set.bindHint");
        SetPitchLbl.Text = Lang.S("set.pitch");
        SetPitchResetBtn.Content = Lang.S("set.pitchReset");
        SetSoftInfoTitle.Text = Lang.S("set.softinfo");
        AboutNameTx.Text = Lang.S("about.name");
        AboutVersion.Text = $"{Lang.S("about.version")}: v{UpdateChecker.AppVersion}-WPF";
        AboutAuthorTx.Text = $"{Lang.S("about.author")}: LingYunALingYun";
        AboutRepoRun.Text = $"{Lang.S("about.repo")}: ";

        // 刷新绑定文本(未知作者/创谱者、收藏夹曲数等随语言变)
        SongList.Items.Refresh();
        PlaylistView.Items.Refresh();
        CloudList.Items.Refresh();
        FolderList.Items.Refresh();
        _instrumentMenu = null;

        OnSongSelected();
    }

    // ---- 自定义标题栏窗口控制 ----
    void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- 选中曲目 = 仅选中(不演奏); 底部播放栏只跟随"正在播放", 不跟随选中 ----
    void OnSongSelected()
    {
        if (Selected is { } s)
        {
            FilePathBox.Text = s.Name;
            SongInfoText.Text = $"BPM:{(int)s.Bpm}  {Lang.S("info.notes")}:{s.NoteCount}";
        }
        else
        {
            FilePathBox.Text = Lang.S("nosong");
            SongInfoText.Text = $"BPM:--  {Lang.S("info.notes")}:--";
        }
    }

    // ---- 底部播放器栏 (beta): 收藏星 / 上一首 / 下一首 ----
    void FavStar_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var s = _nowPlaying ?? Selected;
        if (s == null) return;
        ToggleFav(s);
        FavStar.Fill = s.Fav ? _gold : System.Windows.Media.Brushes.Transparent;
    }

    void PrevSong_Click(object sender, RoutedEventArgs e) => StepSong(-1);
    void NextSong_Click(object sender, RoutedEventArgs e) => StepSong(+1);

    void StepSong(int delta)
    {
        // 有播放列表 → 在列表内切歌; 否则在曲库里移动选择
        if (_playlist.Count > 0)
        {
            SongInfo s;
            if (_playMode == PlayMode.Shuffle) s = RandomItem(_playCurrent);   // 随机: ⏮/⏭ 都随机
            else
            {
                int idx = _playCurrent != null ? _playlist.IndexOf(_playCurrent) : -1;
                idx = idx < 0 ? (delta > 0 ? 0 : _playlist.Count - 1) : (idx + delta);
                if (idx < 0) idx = _playlist.Count - 1;
                if (idx >= _playlist.Count) idx = 0;
                s = _playlist[idx];
            }
            if (_playing || _previewing) PlayPlaylistItem(s);   // 正在放 → 直接切到并播放
            else { _playCurrent = s; UpdateNowPlaying(s); }     // 未在放 → 仅切换当前
            return;
        }
        int n = SongList.Items.Count;
        if (n == 0) return;
        int i = SongList.SelectedIndex < 0 ? (delta > 0 ? -1 : 0) : SongList.SelectedIndex;
        i = Math.Clamp(i + delta, 0, n - 1);
        SongList.SelectedIndex = i;
        SongList.ScrollIntoView(SongList.SelectedItem);
    }

    // ================= 播放列表 =================
    void ClearPlayingMarks() { foreach (var s in _playlist) s.IsPlaying = false; }

    void UpdatePlaylistHeader()
    {
        PlaylistCount.Text = $"{_playlist.Count} {Lang.S("unit.songs")}";
        PlaylistEmpty.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlaylistClearBtn.Visibility = _playlist.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // 底部"正在播放"信息跟随指定曲目
    void UpdateNowPlaying(SongInfo s)
    {
        PlayerSongName.Text = s.Name;
        PlayerAuthor.Text = s.AuthorText;
        PlayerTranscriber.Text = s.TranscriberText;
        FavStar.Fill = s.Fav ? _gold : System.Windows.Media.Brushes.Transparent;
        PlayerAuthor.Visibility = Visibility.Visible;
        PlayerTranscriber.Visibility = Visibility.Visible;
        FavStar.Visibility = Visibility.Visible;
        TotalText.Text = Fmt(s.DurationMs);
        ProgTipText.Text = $"00:00 / {TotalText.Text}";
        ProgFill.Width = 0;
        ProgBar.Visibility = Visibility.Visible;
    }

    // 无曲目播放: 只显封面 + 提示, 隐藏作者/创谱者/星
    void SetIdlePlayer()
    {
        PlayerSongName.Text = "未有正在播放的歌曲";
        PlayerAuthor.Visibility = Visibility.Collapsed;
        PlayerTranscriber.Visibility = Visibility.Collapsed;
        FavStar.Visibility = Visibility.Collapsed;
        TotalText.Text = "00:00";
        ElapsedText.Text = "00:00";
        ProgTipText.Text = "00:00 / 00:00";
        ProgFill.Width = 0;
        ProgBar.Visibility = Visibility.Collapsed;
    }

    void AddToPlaylist(SongInfo s)
    {
        if (_playlist.Any(x => x.File == s.File)) { ShowToast(string.Format(Lang.S("t.inQueue"), s.Name)); return; }
        _playlist.Add(s);
        UpdatePlaylistHeader();
        SavePlaylist();
        ShowToast(string.Format(Lang.S("t.addedQueue"), s.Name));
    }

    void PlayPlaylistItem(SongInfo s)
    {
        _advanceTimer?.Stop();
        if (_playing || _previewing) StopPlaying();
        if (!TryLoad(s)) return;
        _playCurrent = s;
        _nowPlaying = s;
        _paused = false;
        UpdateNowPlaying(s);
        int sec = _previewMode ? 0 : (int.TryParse(CountdownBox.Text, out int x) ? Math.Max(0, x) : 0);
        BeginCountdown(sec);
    }

    // 双击播放列表条目 → 播放
    void PlaylistView_DoubleClick(object sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is SongInfo s) PlayPlaylistItem(s);
    }

    // 点封面叠层 → 播放该条
    void PlaylistItemPlay_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SongInfo s) { PlayPlaylistItem(s); e.Handled = true; }
    }

    // 条目收藏星
    void PlaylistFav_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SongInfo s) return;
        ToggleFav(s);
        if (ReferenceEquals(s, Selected) || ReferenceEquals(s, _nowPlaying)) FavStar.Fill = s.Fav ? _gold : System.Windows.Media.Brushes.Transparent;
        e.Handled = true;
    }

    // 条目 ⋯ 菜单 (点更多按钮)
    void PlaylistMore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SongInfo s) OpenRowMenu(BuildPlaylistMenu(s), sender);
    }

    // 条目右键 → 同一份菜单
    void PlaylistRow_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SongInfo s) return;
        OpenRowMenu(BuildPlaylistMenu(s), sender);
        e.Handled = true;
    }

    ContextMenu BuildPlaylistMenu(SongInfo s)
    {
        var cm = new ContextMenu();

        var play = new MenuItem { Header = Lang.S("m.play") };
        play.Click += (_, __) => PlayPlaylistItem(s);

        var favTo = BuildFavToMenu(s);   // 收藏到收藏夹(歌单)

        var remove = new MenuItem { Header = Lang.S("m.removeQueue") };
        remove.Click += (_, __) => RemoveFromPlaylist(s);
        var open = new MenuItem { Header = Lang.S("m.openLoc") };
        open.Click += (_, __) => { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{s.File}\""); } catch { } };
        var edit = new MenuItem { Header = Lang.S("m.editSong") };
        edit.Click += (_, __) => OpenEditor(s);
        var info = new MenuItem { Header = Lang.S("m.songInfo") };
        info.Click += (_, __) => PlaylistSongInfo(s);

        cm.Items.Add(play);
        cm.Items.Add(favTo);
        cm.Items.Add(remove);
        cm.Items.Add(open);
        cm.Items.Add(new Separator());
        cm.Items.Add(edit);
        cm.Items.Add(info);
        return cm;
    }

    // 歌曲信息: 编辑曲名/作者/创谱者, 保存回文件并刷新显示
    void PlaylistSongInfo(SongInfo s)
    {
        var doc = SongLibrary.LoadDocument(s);
        var dlg = new InfoDialog(doc) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        SongLibrary.Save(doc, doc.Notes, doc.FilePath ?? s.File);
        s.Name = doc.Name; s.Author = doc.Author; s.TranscribedBy = doc.TranscribedBy;
        PlaylistView.Items.Refresh();
        RefreshLibrary();
        if (ReferenceEquals(s, _playCurrent) || (_nowPlaying != null && _nowPlaying.File == s.File)) UpdateNowPlaying(s);
    }

    void RemoveFromPlaylist(SongInfo s)
    {
        bool wasCurrent = ReferenceEquals(s, _playCurrent) || (_nowPlaying != null && _nowPlaying.File == s.File);
        _playlist.Remove(s);
        if (ReferenceEquals(s, _playCurrent)) _playCurrent = null;
        if (wasCurrent)
        {
            if (_playing || _previewing) StopPlaying();   // 移除的正是在放的 → 停止
            _nowPlaying = null;
            OnSongSelected();                             // 底部信息回落到曲库选中
        }
        UpdatePlaylistHeader();
        SavePlaylist();
    }

    void PlaylistClear_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;
        bool wasPlaying = _playing && !_paused;
        if (wasPlaying) SetPaused(true);              // 弹窗前先暂停
        if (!MsgBox.Confirm(this, Lang.S("c.clearQueue"), Lang.S("c.queueTitle")))
        {
            if (wasPlaying) SetPaused(false);         // 取消 → 继续播放
            return;
        }
        if (_playing || _previewing) StopPlaying();   // 确认 → 一并停止
        _playlist.Clear();
        _playCurrent = null;
        _nowPlaying = null;
        UpdatePlaylistHeader();
        SavePlaylist();
    }

    // 右侧滑出面板开合
    void TogglePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistPanel.Visibility != Visibility.Visible) OpenPlaylistPanel();
        else ClosePlaylistPanel();
    }

    void OpenPlaylistPanel()
    {
        PlaylistBackdrop.Visibility = Visibility.Visible;
        PlaylistPanel.Visibility = Visibility.Visible;
        var a = new DoubleAnimation(PlaylistPanel.Width, 0, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        PlaylistSlide.BeginAnimation(TranslateTransform.XProperty, a);
    }

    void ClosePlaylistPanel()
    {
        PlaylistBackdrop.Visibility = Visibility.Collapsed;
        var a = new DoubleAnimation(0, PlaylistPanel.Width, TimeSpan.FromMilliseconds(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        a.Completed += (_, __) => PlaylistPanel.Visibility = Visibility.Collapsed;
        PlaylistSlide.BeginAnimation(TranslateTransform.XProperty, a);
    }

    // 点面板外任意处 → 收回
    void PlaylistBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ClosePlaylistPanel();

    void LoadPlaylist()
    {
        try
        {
            if (!System.IO.File.Exists(PlaylistFile)) { UpdatePlaylistHeader(); return; }
            foreach (var line in System.IO.File.ReadAllLines(PlaylistFile))
            {
                var path = line.Trim();
                if (path.Length == 0) continue;
                var s = _all.FirstOrDefault(x => x.File == path);
                if (s != null && !_playlist.Contains(s)) _playlist.Add(s);
            }
        }
        catch { }
        UpdatePlaylistHeader();
    }

    void SavePlaylist()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlaylistFile)!);
            System.IO.File.WriteAllLines(PlaylistFile, _playlist.Select(s => s.File));
        }
        catch { }
    }

    static string Fmt(double ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    // 曲目列表悬停/选中高亮(主题感知; 文字色由全局 TextBlock 样式跟随主题)
    void StyleSongList()
    {
        var border = new System.Windows.FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new System.Windows.TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        border.AppendChild(new System.Windows.FrameworkElementFactory(typeof(ContentPresenter)));
        var tmpl = new ControlTemplate(typeof(ListViewItem)) { VisualTree = border };

        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.TemplateProperty, tmpl));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        var selTrig = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selTrig.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Theme.ListSel)));
        style.Triggers.Add(selTrig);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Theme.ListHover)));
        style.Triggers.Add(hover);
        SongList.ItemContainerStyle = style;
    }

    async System.Threading.Tasks.Task CheckUpdateAsync()
    {
        var r = await UpdateChecker.CheckAsync();
        if (r is not { } rel) return;
        if (MsgBox.Confirm(this,
            $"当前版本: v{UpdateChecker.AppVersion}\n最新版本: v{rel.Tag}\n\n前往 GitHub 下载最新版本?",
            $"发现新版本 — SMAP {rel.Name}") && rel.Url.Length > 0)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rel.Url) { UseShellExecute = true });
    }

    // ---- 曲库: 搜索 / 筛选 / 排序 / 收藏 / 标签 ----
    const string TagPrefix = "🏷 ";
    string _instrumentName = "Piano";
    List<SongInfo> _all = new();

    void RefreshLibrary()
    {
        _all = SongLibrary.Scan();
        foreach (var s in _all) s.Fav = IsCollected(s);
        RebuildFilterOptions();
        ApplyFilter();
        StatusText.Text = $"状态: 曲库 {_all.Count} 首  ({SongLibrary.SongsDir})";
    }

    void RebuildFilterOptions()
    {
        int idx = FilterCombo.SelectedIndex < 0 ? 0 : FilterCombo.SelectedIndex;   // 按索引保留(翻译后字串会变)
        var items = new List<string> { Lang.S("filter.all"), Lang.S("filter.fav") };
        FilterCombo.ItemsSource = items;
        FilterCombo.SelectedIndex = idx < items.Count ? idx : 0;
    }

    void ApplyFilter()
    {
        if (_all.Count == 0 && SongList.ItemsSource == null) { SongList.ItemsSource = _all; return; }
        string q = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        int fi = FilterCombo.SelectedIndex;   // 0=全部 1=仅收藏

        // 选中收藏夹 → 只看该收藏夹曲目; 否则整个曲库
        IEnumerable<SongInfo> res = _currentFolder != null
            ? _all.Where(s => _currentFolder.Files.Contains(s.File))
            : _all;
        if (q.Length > 0) res = res.Where(s => s.Name.ToLowerInvariant().Contains(q));
        if (fi == 1) res = res.Where(s => s.Fav);

        res = SortCombo.SelectedIndex switch
        {
            1 => res.OrderByDescending(s => s.Name, StringComparer.CurrentCulture),
            2 => res.OrderByDescending(s => s.Fav).ThenBy(s => s.Name, StringComparer.CurrentCulture),
            _ => res.OrderBy(s => s.Name, StringComparer.CurrentCulture)
        };
        SongList.ItemsSource = res.ToList();
    }

    SongInfo? Selected => SongList.SelectedItem as SongInfo;

    // 统一开菜单: 先关掉上一个已开的, 避免右键时叠出多个/干扰二级子菜单
    ContextMenu? _rowMenu;
    void OpenRowMenu(ContextMenu cm, object sender)
    {
        if (_rowMenu != null && _rowMenu.IsOpen) _rowMenu.IsOpen = false;
        _rowMenu = cm;
        cm.PlacementTarget = sender as UIElement;
        cm.IsOpen = true;
    }

    // ===== 中栏曲库富列表行交互 (Stage3) =====
    void LibAdd_Click(object sender, RoutedEventArgs e)
    { if ((sender as FrameworkElement)?.DataContext is SongInfo s) AddToPlaylist(s); }

    void LibFav_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SongInfo s) return;
        ToggleFav(s);
        if (ReferenceEquals(s, _nowPlaying)) FavStar.Fill = s.Fav ? _gold : System.Windows.Media.Brushes.Transparent;
        e.Handled = true;
    }

    void LibMore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SongInfo s) OpenRowMenu(BuildLibraryMenu(s), sender);
    }

    void LibRow_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SongInfo s) return;
        OpenRowMenu(BuildLibraryMenu(s), sender);
        e.Handled = true;
    }

    // 曲库条目菜单: 添加到播放列表 / 收藏到…… / 从曲库中移除 / 打开文件位置 / 编辑曲目 / 歌曲信息
    ContextMenu BuildLibraryMenu(SongInfo s)
    {
        var cm = new ContextMenu();
        var add = new MenuItem { Header = Lang.S("m.addQueue") };
        add.Click += (_, __) => AddToPlaylist(s);
        var remove = new MenuItem { Header = new TextBlock { Text = Lang.S("m.removeLib"), Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)) } };
        remove.Click += (_, __) => DeleteSong(s);
        var open = new MenuItem { Header = Lang.S("m.openLoc") };
        open.Click += (_, __) => { try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{s.File}\""); } catch { } };
        var edit = new MenuItem { Header = Lang.S("m.editSong") };
        edit.Click += (_, __) => OpenEditor(s);
        var info = new MenuItem { Header = Lang.S("m.songInfo") };
        info.Click += (_, __) => PlaylistSongInfo(s);
        var upload = new MenuItem { Header = Lang.S("m.uploadCloud") };
        upload.Click += (_, __) =>
        {
            if (!CloudApi.LoggedIn)
            {
                if (new LoginDialog(this).ShowDialog() != true) return;
                UpdateLoginButton();
            }
            if (new UploadDialog(this, s.File, s.Name).ShowDialog() == true) ShowToast(string.Format(Lang.S("t.uploaded"), s.Name));
        };
        cm.Items.Add(add);
        cm.Items.Add(BuildFavToMenu(s));
        // 正在查看某收藏夹时: 从该收藏夹移除(不删歌)
        if (_currentFolder is { } cf)
        {
            var rmFolder = new MenuItem { Header = string.Format(Lang.S("m.removeFromFolder"), cf.Name) };
            rmFolder.Click += (_, __) => RemoveFromFolder(cf, s);
            cm.Items.Add(rmFolder);
        }
        cm.Items.Add(remove);
        cm.Items.Add(open);
        cm.Items.Add(new Separator());
        cm.Items.Add(edit);
        cm.Items.Add(info);
        cm.Items.Add(upload);
        return cm;
    }

    // 从磁盘删除曲谱(并从收藏夹/播放列表清理)
    void DeleteSong(SongInfo s)
    {
        if (!MsgBox.Confirm(this, string.Format(Lang.S("c.removeLibConfirm"), s.Name), Lang.S("m.removeLib"))) return;
        try { System.IO.File.Delete(s.File); }
        catch (Exception ex) { MsgBox.Info(this, "删除失败: " + ex.Message); return; }
        LibraryMeta.Forget(s.FileName);
        foreach (var f in _folders) f.Files.Remove(s.File);
        SaveFolders();
        var pl = _playlist.FirstOrDefault(x => x.File == s.File);
        if (pl != null) RemoveFromPlaylist(pl);
        RefreshLibrary();
        ShowToast(string.Format(Lang.S("t.removedLib"), s.Name));
    }

    void OpenEditor(SongInfo? song)
    {
        var win = new EditorWindow { Owner = this };
        if (song != null)
        {
            win.LoadSong(song);
            FilePathBox.Text = song.Name;
        }
        win.Show();
    }

    // 3×5 虚拟琴键: 圆角方块 + 菱形轮廓(光遇琴键造型) + 字母; row0=K0-4, row1=K5-9, row2=K10-14
    readonly TextBlock[] _keyLabels = new TextBlock[15];
    readonly RotateTransform[] _keyRot = new RotateTransform[15];   // 触发时翻转一圈
    readonly Border[] _keyDiamond = new Border[15];                 // 翻转时圆角морф(菱形↔圆)
    readonly ScaleTransform[] _keyScale = new ScaleTransform[15];   // 触发时缩小回弹
    readonly TextBlock[] _setKeyLabels = new TextBlock[15];         // 设置界面里的绑定网格
    readonly Button[] _setKeyBtns = new Button[15];
    readonly Button[] _pBtn = new Button[15];                       // 练习界面: 全屏大键盘, 与主键盘同步亮起翻转
    readonly TextBlock[] _pLabels = new TextBlock[15];
    readonly RotateTransform[] _pRot = new RotateTransform[15];
    readonly Border[] _pDiamond = new Border[15];
    readonly ScaleTransform[] _pScale = new ScaleTransform[15];

    // 设置界面的绑定网格(与主网格共享 KeyConfig; 编辑态点键→按物理键重绑)
    void BuildSettingsGrid()
    {
        for (int i = 0; i < 15; i++)
        {
            var lbl = new TextBlock
            {
                Foreground = new SolidColorBrush(Theme.KeyLetter), FontSize = 13, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var diamond = new Border
            {
                Width = 28, Height = 28, BorderBrush = new SolidColorBrush(Theme.KeyDiamond), BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45)
            };
            var cell = new Grid();
            cell.Children.Add(diamond); cell.Children.Add(lbl);
            var btn = new Button
            {
                Width = 62, Height = 62, Margin = new Thickness(4), Content = cell, Tag = i,
                Background = new SolidColorBrush(Theme.KeySquare), BorderBrush = new SolidColorBrush(Theme.KeyBorder), BorderThickness = new Thickness(1)
            };
            int idx = i;
            btn.Click += (_, __) => { if (_editingKeys) BeginRemap(idx); else { AudioEngine.Play(idx); FlashKey(idx); } };
            _setKeyBtns[i] = btn;
            _setKeyLabels[i] = lbl;
            SettingsPianoGrid.Children.Add(btn);
        }
        for (int i = 0; i < 15; i++) RefreshKey(i);
    }

    void BuildPianoGrid()
    {
        for (int i = 0; i < 15; i++)
        {
            var lbl = new TextBlock
            {
                Foreground = new SolidColorBrush(Theme.KeyLetter),
                FontSize = 15, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var rot = new RotateTransform(45);
            var diamond = new Border
            {
                Width = 30, Height = 30,
                BorderBrush = new SolidColorBrush(Theme.KeyDiamond),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = rot
            };
            _keyRot[i] = rot;
            _keyDiamond[i] = diamond;
            var cell = new Grid();
            cell.Children.Add(diamond);
            cell.Children.Add(lbl);

            var scale = new ScaleTransform(1, 1);
            var btn = new Button
            {
                Width = 66, Height = 66, Margin = new Thickness(4), Content = cell, Tag = i,
                Background = new SolidColorBrush(Theme.KeySquare),   // 每键独立画刷, 供颜色动画
                BorderBrush = new SolidColorBrush(Theme.KeyBorder), BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
            };
            _keyScale[i] = scale;
            int idx = i;
            // 平时点键=试听声音; 编辑态点键=开始重绑
            btn.PreviewMouseLeftButtonDown += (_, ev) => { if (_editingKeys) BeginRemap(idx); else { AudioEngine.NoteOn(idx); FlashKey(idx); } ev.Handled = true; };
            btn.PreviewMouseLeftButtonUp += (_, __) => { if (!_editingKeys) AudioEngine.NoteOff(idx); };
            btn.MouseLeave += (_, __) => { if (!_editingKeys) AudioEngine.NoteOff(idx); };   // 按住拖出也松开
            _pianoButtons[i] = btn;
            _keyLabels[i] = lbl;
            PianoGrid.Children.Add(btn);
            RefreshKey(i);
        }
    }

    // 练习界面的全屏大键盘: 与主键盘同一造型(菱形轮廓+字母), 尺寸放大; 点击试听, 播放时同步亮起翻转
    void BuildPracticeGrid()
    {
        for (int i = 0; i < 15; i++)
        {
            var lbl = new TextBlock
            {
                Foreground = new SolidColorBrush(Theme.KeyLetter),
                FontSize = 30, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var rot = new RotateTransform(45);
            var diamond = new Border
            {
                Width = 58, Height = 58,
                BorderBrush = new SolidColorBrush(Theme.KeyDiamond),
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = rot
            };
            _pRot[i] = rot;
            _pDiamond[i] = diamond;
            var cell = new Grid();
            cell.Children.Add(diamond);
            cell.Children.Add(lbl);

            var scale = new ScaleTransform(1, 1);
            var btn = new Button
            {
                Width = 128, Height = 128, Margin = new Thickness(9), Content = cell, Tag = i,
                Background = new SolidColorBrush(Theme.KeySquare),
                BorderBrush = new SolidColorBrush(Theme.KeyBorder), BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale
            };
            _pScale[i] = scale;
            int idx = i;
            btn.PreviewMouseLeftButtonDown += (_, ev) => { AudioEngine.NoteOn(idx); FlashKey(idx); ev.Handled = true; };
            btn.PreviewMouseLeftButtonUp += (_, __) => AudioEngine.NoteOff(idx);
            btn.MouseLeave += (_, __) => AudioEngine.NoteOff(idx);
            _pBtn[i] = btn;
            _pLabels[i] = lbl;
            PracticePianoGrid.Children.Add(btn);
        }
    }

    void RefreshKey(int i)
    {
        var lab = KeyConfig.Label(_player.Vk[i]);
        var col = i == _remapIndex ? Theme.KeyWait : Theme.KeySquare;
        _keyLabels[i].Text = lab;
        ((SolidColorBrush)_pianoButtons[i].Background).Color = col;
        if (_setKeyBtns[i] != null)
        {
            _setKeyLabels[i].Text = lab;
            ((SolidColorBrush)_setKeyBtns[i].Background).Color = col;
        }
        if (_pBtn[i] != null)
        {
            _pLabels[i].Text = lab;
            ((SolidColorBrush)_pBtn[i].Background).Color = Theme.KeySquare;   // 练习键无重映射等待态
        }
    }

    // 切换主题后给琴键重新上色
    void ApplyKeyTheme()
    {
        for (int i = 0; i < 15; i++)
        {
            _keyLabels[i].Foreground = new SolidColorBrush(Theme.KeyLetter);
            _keyDiamond[i].BorderBrush = new SolidColorBrush(Theme.KeyDiamond);
            _pianoButtons[i].BorderBrush = new SolidColorBrush(Theme.KeyBorder);
            if (_setKeyBtns[i] != null)
            {
                _setKeyLabels[i].Foreground = new SolidColorBrush(Theme.KeyLetter);
                _setKeyBtns[i].BorderBrush = new SolidColorBrush(Theme.KeyBorder);
            }
            if (_pBtn[i] != null)
            {
                _pLabels[i].Foreground = new SolidColorBrush(Theme.KeyLetter);
                _pDiamond[i].BorderBrush = new SolidColorBrush(Theme.KeyDiamond);
                _pBtn[i].BorderBrush = new SolidColorBrush(Theme.KeyBorder);
            }
            RefreshKey(i);
        }
    }

    // ---- 播放/试听时琴键同步亮起 + 进度条 ----
    DispatcherTimer? _flashTimer;
    bool _progDragging;

    // 进度条自绘: 填充宽度=进度, 圆点/药丸横移到进度交界点(参考点)
    void UpdateProgUi() => RenderProg(_player.TotalMs > 0 ? _player.PositionMs / _player.TotalMs : 0, _player.PositionMs, _player.TotalMs);

    void RenderProg(double frac, double posMs, double totalMs)
    {
        frac = Math.Clamp(frac, 0, 1);
        double w = ProgBar.ActualWidth;
        if (w <= 0) return;
        double x = frac * w;                        // 交界点(参考点)
        ProgFill.Width = x;
        ProgThumb.Margin = new Thickness(x - ProgThumb.Width / 2, 0, 0, 0);
        ProgTipText.Text = $"{Fmt(posMs)} / {Fmt(totalMs)}";
        ProgTipPill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pw = ProgTipPill.DesiredSize.Width;
        double cx = Math.Clamp(x, pw / 2, Math.Max(pw / 2, w - pw / 2));    // 两端不超出软件
        ProgTip.HorizontalOffset = cx - pw / 2;                            // 居中于参考点(受界限约束)
    }

    void ProgBar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateProgUi();

    void ProgBar_Enter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ProgTrack.Height = ProgFill.Height = 6;     // 向上加粗(VerticalAlignment=Center 视觉居中变粗)
        ProgThumb.Width = ProgThumb.Height = 15;
        ProgTip.IsOpen = true;
        UpdateProgUi();
    }
    void ProgBar_Leave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_progDragging) return;
        ProgTrack.Height = ProgFill.Height = 3;
        ProgThumb.Width = ProgThumb.Height = 0;
        ProgTip.IsOpen = false;
    }
    void ProgBar_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _progDragging = true; ProgBar.CaptureMouse(); SeekToMouse(e.GetPosition(ProgBar).X); e.Handled = true;
    }
    void ProgBar_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_progDragging) SeekToMouse(e.GetPosition(ProgBar).X);
    }
    void ProgBar_Up(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_progDragging) return;
        _progDragging = false; ProgBar.ReleaseMouseCapture();
        if (!ProgBar.IsMouseOver) ProgBar_Leave(sender, e);
    }
    void SeekToMouse(double mouseX)
    {
        if (!_playing && !_previewing) return;      // 暂停时 _playing 仍为 true → 可拖动
        double w = ProgBar.ActualWidth; if (w <= 0) return;
        double frac = Math.Clamp(mouseX / w, 0, 1);
        double total = _player.TotalMs;
        _player.Seek(frac * total);
        RenderProg(frac, frac * total, total);      // 直接按鼠标位置渲染, 不等回写
    }

    void StartFlash()
    {
        _player.NoteFired = k => Dispatcher.BeginInvoke(() => FlashKey(k));
        _flashTimer ??= CreateFlashTimer();
        _flashTimer.Start();
    }

    DispatcherTimer CreateFlashTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        t.Tick += (_, __) =>
        {
            if (_progDragging) return;   // 拖动时不回写
            UpdateProgUi();
        };
        return t;
    }

    void StopFlash()
    {
        _flashTimer?.Stop();
        ProgFill.Width = 0;
        ProgThumb.Margin = new Thickness(0);
    }

    // 触发: 背景色变深回弹(颜色动画自动回基准) + 翻转 + 缩放; 主键盘与练习大键盘同步
    void FlashKey(int k)
    {
        if (k < 0 || k >= 15 || k == _remapIndex) return;
        var brush = (SolidColorBrush)_pianoButtons[k].Background;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Theme.KeyLit, Theme.KeySquare, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
        SpinKey(_keyRot[k], _keyDiamond[k], _keyScale[k]);
        if (_pBtn[k] != null)
        {
            ((SolidColorBrush)_pBtn[k].Background).BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(Theme.KeyLit, Theme.KeySquare, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
            SpinKey(_pRot[k], _pDiamond[k], _pScale[k]);
        }
    }

    // 光遇式翻转: 旋转一整圈(45°→405°) + 圆角морф(菱形→满圆→菱形, 峰值=半宽故任意尺寸都成正圆), 中途成圆再变回
    void SpinKey(RotateTransform rot, Border diamond, ScaleTransform scale)
    {
        const int ms = 360;
        var spin = new DoubleAnimation(45, 405, TimeSpan.FromMilliseconds(ms))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        rot.BeginAnimation(RotateTransform.AngleProperty, spin);

        double peak = diamond.Width / 2;   // 满圆半径 = 半宽
        var morph = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(ms) };
        morph.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(0)));
        morph.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromPercent(0.5), new SineEase { EasingMode = EasingMode.EaseInOut }));
        morph.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
        diamond.BeginAnimation(KeyFx.RoundProperty, morph);

        // 按下缩小回弹(线性): 1 → 0.85 → 1
        var sc = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(ms) };
        sc.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        sc.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.35)));
        sc.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1)));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
    }

    void BeginRemap(int idx)
    {
        int prev = _remapIndex;
        _remapIndex = _remapIndex == idx ? -1 : idx;   // 再点一次取消
        if (prev >= 0) RefreshKey(prev);
        if (_remapIndex >= 0)
        {
            RefreshKey(_remapIndex);
            StatusText.Text = $"状态: 按下要绑给 K{idx} 的物理键 (Esc 取消)";
        }
        else StatusText.Text = "状态: 已取消重映射";
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        // 重绑等待中: 捕获下一次按键
        if (_remapIndex >= 0)
        {
            int idx = _remapIndex;
            _remapIndex = -1;
            if (e.Key != System.Windows.Input.Key.Escape)
            {
                var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                ushort vk = (ushort)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                _player.Vk[idx] = vk;
                StatusText.Text = $"状态: K{idx} → {KeyConfig.Label(vk)} (记得点保存按键映射)";
            }
            else StatusText.Text = "状态: 已取消重映射";
            RefreshKey(idx);
            e.Handled = true;
            return;
        }

        // 练习界面按 Esc 快速返回主界面
        if (_practiceOpen && e.Key == System.Windows.Input.Key.Escape)
        {
            ShowPractice(false);
            e.Handled = true;
            return;
        }

        // 物理键盘触发对应琴键(发声+动画); 焦点在输入框时放行, 忽略长按重复
        if (!e.IsRepeat && System.Windows.Input.Keyboard.FocusedElement is not TextBox)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            int idx = Array.IndexOf(_player.Vk, (ushort)vk);
            if (idx >= 0)
            {
                AudioEngine.NoteOn(idx);
                FlashKey(idx);
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        // 松开物理键 -> 停对应琴键的持续长音
        if (System.Windows.Input.Keyboard.FocusedElement is not TextBox)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            int idx = Array.IndexOf(_player.Vk, (ushort)vk);
            if (idx >= 0) { AudioEngine.NoteOff(idx); e.Handled = true; return; }
        }
        base.OnPreviewKeyUp(e);
    }

    // ---- 洞穴音效 / 音色 / 主题 / 软件信息 ----
    void Cave_Click(object sender, RoutedEventArgs e)
    {
        AudioEngine.Cave = !AudioEngine.Cave;
        CaveBtn.Content = $"{Lang.S("cave")}: {Lang.S(AudioEngine.Cave ? "on" : "off")}";
        CaveIcon.Foreground = AudioEngine.Cave ? Brushes.DeepSkyBlue : (Brush)Application.Current.Resources["SubTextFg"];
        StatusText.Text = $"状态: 洞穴音效已{(AudioEngine.Cave ? "开启" : "关闭")}";
    }

    ContextMenu? _instrumentMenu;
    void Instrument_Click(object sender, RoutedEventArgs e)
    {
        if (_instrumentMenu == null)
        {
            _instrumentMenu = new ContextMenu { MaxHeight = 296 };   // 约8项高, 超出滚轮翻动
            foreach (var name in AudioEngine.Instruments)
            {
                var it = new MenuItem { Header = Lang.Instrument(name) };
                var n = name;
                it.Click += (_, __) =>
                {
                    System.Threading.Tasks.Task.Run(() => AudioEngine.SetInstrument(n));
                    _instrumentName = n;
                    InstrumentBtn.Content = $"{Lang.S("instrument")}: {Lang.Instrument(n)}";
                    InstrumentPill.Content = $"{Lang.S("instrument")}:{Lang.Instrument(n)}";
                    RefreshPitchPill();
                    ShowToast($"{Lang.S("instrument")} → {Lang.Instrument(n)}");
                };
                _instrumentMenu.Items.Add(it);
            }
        }
        _instrumentMenu.PlacementTarget = (sender as UIElement) ?? InstrumentBtn;
        _instrumentMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;   // 向上展开
        _instrumentMenu.IsOpen = true;
    }

    // ---- 音高(每乐器移调, 存 %APPDATA%\SMAP\pitch.json) ----
    static readonly string[] PitchNames = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
    static string NoteName(int semi) => PitchNames[((semi % 12) + 12) % 12];
    static string PitchLabel(int semi) => $"{(semi > 0 ? "+" : "")}{semi} {NoteName(semi)}";

    void RefreshPitchPill()
    {
        int semi = AudioEngine.GetOffset(_instrumentName);
        PitchPill.Content = $"{Lang.S("pitch")}:{PitchLabel(semi)}";
        PitchPill.ToolTip = Lang.S("tip.pitch");
    }

    void Pitch_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { MaxHeight = 296 };
        int cur = AudioEngine.GetOffset(_instrumentName);
        foreach (int semi in new[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, -12, -24 })
        {
            var s = semi;
            var it = new MenuItem { IsChecked = s == cur };
            var hg = new Grid { Width = 64 };
            hg.ColumnDefinitions.Add(new ColumnDefinition());
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var numTb = new TextBlock { Text = (s > 0 ? "+" : "") + s };                       // 数字左对齐
            var noteTb = new TextBlock { Text = NoteName(s), HorizontalAlignment = HorizontalAlignment.Right };  // 音名右对齐
            Grid.SetColumn(noteTb, 1);
            hg.Children.Add(numTb);
            hg.Children.Add(noteTb);
            it.Header = hg;
            it.Click += (_, __) =>
            {
                var inst = _instrumentName;
                AudioEngine.SetOffset(inst, s);   // 内部同步存值 + 异步重载采样
                RefreshPitchPill();               // 立即读回新值, 胶囊即时更新
                ShowToast($"{Lang.S("pitch")} {Lang.Instrument(inst)} → {PitchLabel(s)}");
            };
            menu.Items.Add(it);
        }
        menu.PlacementTarget = (sender as UIElement) ?? PitchPill;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;   // 向上展开
        menu.IsOpen = true;
    }

    void SetPitchReset_Click(object sender, RoutedEventArgs e)
    {
        PitchConfig.ResetAll();
        System.Threading.Tasks.Task.Run(() => AudioEngine.ClearCache());
        RefreshPitchPill();
        ShowToast(Lang.S("t.pitchReset"));
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        Theme.Apply(!Theme.Dark);
        ApplyKeyTheme();
        ThemeBtn.Content = $"{Lang.S("theme")}: {Lang.S(Theme.Dark ? "theme.dark" : "theme.light")}";
        StatusText.Text = $"状态: 已切换到{(Theme.Dark ? "深色" : "浅色")}主题";
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(this).ShowDialog();

    void Create_Click(object sender, RoutedEventArgs e) => OpenEditor(null);
    void Edit_Click(object sender, RoutedEventArgs e) => OpenEditor(Selected);
    // 双击曲库 = 加入播放列表并立即播放 (编辑改由"编辑"按钮)
    void SongList_DoubleClick(object sender, RoutedEventArgs e) { if (Selected is { } s) { AddToPlaylist(s); PlayPlaylistItem(s); } }

    // 载入指定曲谱 → _notes(key, ms); 空谱返回 false 并更新状态栏
    bool TryLoad(SongInfo song)
    {
        var doc = SongLibrary.LoadDocument(song);
        _notes = doc.Notes.Select(n => (n.Key, n.Beat * doc.MsPerBeat)).ToList();
        if (_notes.Count == 0) { StatusText.Text = "状态: 该曲谱无音符"; return false; }
        return true;
    }

    bool TryLoadSelected()
    {
        if (Selected is not { } song) { StatusText.Text = "状态: 请先在中间选一首曲谱"; return false; }
        return TryLoad(song);
    }

    // useCountdown: 按钮启动需倒计时(留时间切到光遇); 热键启动人已在游戏里, 立即开始
    void StartAuto(bool useCountdown)
    {
        if (_playing || _previewing) { StopPlaying(); return; }
        if (!TryLoadSelected()) return;
        _nowPlaying = Selected;
        _paused = false;
        int sec = useCountdown && int.TryParse(CountdownBox.Text, out int s) ? Math.Max(0, s) : 0;
        BeginCountdown(sec);
    }

    // F1: 开始 / 停止 (只演奏播放列表, 选中曲库歌曲不再演奏)
    void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_playing || _previewing) { StopPlaying(); return; }
        PlayCurrentOrFirst();
    }

    // 底部大播放键: 音乐软件式 播放/暂停 (只从播放列表播)
    void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_playing && !_previewing) { PlayCurrentOrFirst(); return; }
        if (_previewing) { StopPlaying(); return; }   // 试听态: 直接停
        SetPaused(!_paused);   // 演奏态 → 暂停/继续 (保留进度)
    }

    // 播放列表当前曲(无则第一首)
    void PlayCurrentOrFirst()
    {
        var target = _playCurrent ?? _playlist.FirstOrDefault();
        if (target == null) { ShowToast(Lang.S("t.emptyQueue")); return; }
        PlayPlaylistItem(target);
    }

    // 暂停/继续: 统一更新播放器 + 暂停按钮 + 大播放键图标 + 状态
    void SetPaused(bool paused)
    {
        _paused = paused;
        _player.Pause(paused);
        PauseBtn.Content = paused ? "▶ 继续 (F2)" : "⏸ 暂停 (F2)";
        SetPlayGlyph(!paused);
        StatusText.Text = paused ? "状态: ⏸ 已暂停" : "状态: 🎵 演奏中... (F1 停止 / F2 暂停)";
    }

    // 试听: 通过扬声器(AudioEngine)放整首, 不发按键/不切游戏窗口, 无需倒计时
    void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_playing || _previewing) { StopPlaying(); return; }
        if (!TryLoadSelected()) return;
        _previewing = true;
        PreviewBtn.Content = "⏹ 停止试听";
        StatusText.Text = $"状态: 🎧 试听中 ({_speed:0.0}x)";
        StartFlash();
        _player.Play(_notes, _speed, () => Dispatcher.BeginInvoke(new Action(OnPlayDone)), AudioEngine.Play);
    }

    void BeginCountdown(int sec)
    {
        _playing = true;
        StartBtn.Content = "⏹ 停止 (F1)";
        SetPlayGlyph(true);
        _countdown?.Stop();
        if (sec <= 0) { StartPlaying(); return; }
        int left = sec;
        StatusText.Text = $"状态: {left} 秒后开始, 快切到光遇窗口...";
        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, __) =>
        {
            left--;
            if (left > 0) StatusText.Text = $"状态: {left} 秒后开始, 快切到光遇窗口...";
            else { _countdown!.Stop(); StartPlaying(); }
        };
        _countdown.Start();
    }

    void StartPlaying()
    {
        // 无论从曲库还是播放列表启动, 都按文件匹配点亮列表里正在播放的那一条
        ClearPlayingMarks();
        var item = _nowPlaying != null ? _playlist.FirstOrDefault(x => x.File == _nowPlaying.File) : null;
        if (item != null) { item.IsPlaying = true; _playCurrent = item; }
        else _playCurrent = null;
        StartFlash();
        if (_previewMode)   // 试听模式: 走扬声器, 不发游戏按键
        {
            StatusText.Text = $"状态: 🎧 试听中 ({_speed:0.0}x)";
            _player.Play(_notes, _speed, () => Dispatcher.BeginInvoke(new Action(OnPlayDone)), AudioEngine.Play);
        }
        else
        {
            StatusText.Text = "状态: 🎵 演奏中... (F1 停止 / F2 暂停)";
            _player.Play(_notes, _speed, () => Dispatcher.BeginInvoke(new Action(OnPlayDone)));
        }
    }

    // 试听模式开关: 高亮=开
    void PreviewMode_Click(object sender, RoutedEventArgs e)
    {
        _previewMode = !_previewMode;
        PreviewIcon.Foreground = _previewMode ? Brushes.MediumTurquoise : (Brush)Application.Current.Resources["SubTextFg"];
        StatusText.Text = $"状态: 试听模式已{(_previewMode ? "开启(走扬声器)" : "关闭(发送按键)")}";
    }

    void ResetPlayUi()
    {
        _playing = false; _previewing = false;
        StopFlash();
        StartBtn.Content = "▶ 开始 (F1)";
        PauseBtn.Content = "⏸ 暂停 (F2)";
        PreviewBtn.Content = "🎧 试听 (扬声器)";
        SetPlayGlyph(false);
        ClearPlayingMarks();
        SetIdlePlayer();
    }

    // 底部大播放键: ▶/⏹ 切换 + 缩放弹跳动画
    void SetPlayGlyph(bool playing)
    {
        PlayIcon.Data = (Geometry)FindResource(playing ? "IconPause" : "IconPlay");
        var st = new ScaleTransform(1, 1);
        PlayBtn.RenderTransformOrigin = new Point(0.5, 0.5);
        PlayBtn.RenderTransform = st;
        var pop = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(280))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    void OnPlayDone()
    {
        bool wasPreview = _previewing;
        var finished = _playCurrent;
        ResetPlayUi();
        AudioEngine.StopAll();
        if (StatusText.Text.Contains("演奏中")) StatusText.Text = "状态: 演奏完成";
        else if (wasPreview && StatusText.Text.Contains("试听")) StatusText.Text = "状态: 试听结束";

        // 自动续播: 按播放方式决定下一首, 间隔 2 秒 (试听按钮 wasPreview 除外; 试听模式仍续播)
        if (!wasPreview && finished != null && _playlist.Count > 0)
        {
            var next = NextByMode(finished);
            if (next != null)
            {
                _advanceTimer?.Stop();
                _advanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _advanceTimer.Tick += (_, __) =>
                {
                    _advanceTimer!.Stop();
                    if (!_playing && !_previewing && _playlist.Contains(next)) PlayPlaylistItem(next);
                };
                _advanceTimer.Start();
            }
        }
    }
    DispatcherTimer? _advanceTimer;   // 曲间 2 秒续播延时

    // 播放方式循环: 列表循环 → 单曲循环 → 随机 → ...
    void PlayMode_Click(object sender, RoutedEventArgs e)
    {
        _playMode = _playMode switch
        {
            PlayMode.RepeatAll => PlayMode.RepeatOne,
            PlayMode.RepeatOne => PlayMode.Shuffle,
            _ => PlayMode.RepeatAll,
        };
        UpdatePlayModeButton();
        SavePlayMode();
        StatusText.Text = $"状态: 播放方式 — {PlayModeName(_playMode)}";
    }

    static string PlayModeName(PlayMode m) => m switch
    {
        PlayMode.RepeatOne => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        _ => "列表循环",
    };

    void UpdatePlayModeButton()
    {
        PlayModeIcon.Data = (Geometry)FindResource(_playMode switch
        { PlayMode.RepeatOne => "IconRepeatOne", PlayMode.Shuffle => "IconShuffle", _ => "IconRepeat" });
        PlayModeBtn.ToolTip = $"播放方式: {PlayModeName(_playMode)}";
    }

    void LoadPlayMode()
    {
        try { if (System.IO.File.Exists(PlayModeFile) && Enum.TryParse(System.IO.File.ReadAllText(PlayModeFile).Trim(), out PlayMode m)) _playMode = m; }
        catch { }
        UpdatePlayModeButton();
    }

    void SavePlayMode()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlayModeFile)!);
            System.IO.File.WriteAllText(PlayModeFile, _playMode.ToString());
        }
        catch { }
    }

    SongInfo? NextByMode(SongInfo cur)
    {
        if (_playlist.Count == 0) return null;
        switch (_playMode)
        {
            case PlayMode.RepeatOne: return cur;                 // 单曲循环
            case PlayMode.Shuffle: return RandomItem(cur);       // 随机
            default:                                             // 列表循环(到底回头)
                int i = _playlist.IndexOf(cur);
                return _playlist[(i < 0 ? 0 : i + 1) % _playlist.Count];
        }
    }

    SongInfo RandomItem(SongInfo? exclude)
    {
        if (_playlist.Count == 1) return _playlist[0];
        SongInfo s;
        do { s = _playlist[_rng.Next(_playlist.Count)]; } while (ReferenceEquals(s, exclude));
        return s;
    }

    void StopPlaying()
    {
        _advanceTimer?.Stop();   // 取消挂起的曲间续播
        _countdown?.Stop();
        _player.Stop();
        AudioEngine.StopAll();
        ResetPlayUi();
        StatusText.Text = "状态: 已停止";
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!_playing) return;
        SetPaused(!_paused);
    }
    void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLibrary();

    void UpdateLibHeader() => LibHeader.Text = _currentFolder?.Name ?? Lang.S("nav.local");

    // 导入 json/txt(直接复制) 与 midi(转换对话框), 支持多选
    void Cloud_Click(object sender, RoutedEventArgs e) => new CloudWindow(this, RefreshLibrary).Show();

    void Login_Click(object sender, RoutedEventArgs e)
    {
        if (CloudApi.LoggedIn)
        {
            var pw = new ProfileWindow(this);
            pw.ShowDialog();
            if (pw.LoggedOut)
            {
                UpdateLoginButton();
                StatusText.Text = "状态: 已退出登录";
            }
            return;
        }
        if (new LoginDialog(this).ShowDialog() == true)
        {
            UpdateLoginButton();
            StatusText.Text = $"状态: 登录成功 — {CloudApi.Username}";
        }
    }

    void UpdateLoginButton()
    {
        LoginBtn.Content = CloudApi.LoggedIn ? $"👤 {CloudApi.Username}" : Lang.S("btn.login");
        LoginBtn.Background = CloudApi.LoggedIn
            ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));
        UpdateProfileCard();
    }

    // ===== 侧边栏 (Stage1) =====
    void UpdateProfileCard()
    {
        if (CloudApi.LoggedIn)
        {
            var name = CloudApi.Username ?? "";
            ProfileName.Text = name;
            AvatarInitial.Text = name.Length > 0 ? name.Substring(0, 1).ToUpperInvariant() : "?";
            ProfileLevel.Text = Lang.S("profile.in");
            ProfileSign.Text = Lang.S("profile.acct");
        }
        else
        {
            ProfileName.Text = Lang.S("profile.guest");
            AvatarInitial.Text = "?";
            ProfileLevel.Text = Lang.S("profile.login");
            ProfileSign.Text = Lang.S("profile.acct");
        }
    }

    // 头像资料卡 → 登录 / 个人主页 (复用登录按钮逻辑)
    void Profile_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Login_Click(sender, new RoutedEventArgs());

    // 本地/云端切换 (Stage3: 云端内联到中栏)
    void ShowLocal_Click(object sender, RoutedEventArgs e)
    {
        CloseSettings();
        SetLibTab(true);
        SetCloudMode(false);
        _currentFolder = null;
        FolderList.SelectedItem = null;
        UpdateLibHeader();
        ApplyFilter();
    }
    void ShowCloud_Click(object sender, RoutedEventArgs e)
    {
        CloseSettings();
        SetLibTab(false);
        SetCloudMode(true);
    }

    void SetLibTab(bool local)
    {
        LocalLibBtn.Background = local ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xD0)) : new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a));
        CloudLibBtn.Background = local ? new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)) : new SolidColorBrush(Color.FromRgb(0x12, 0x79, 0x5A));
    }

    // ===== 云端曲库(内联) Stage3 =====
    void SetCloudMode(bool cloud)
    {
        _cloudMode = cloud;
        SongList.Visibility = cloud ? Visibility.Collapsed : Visibility.Visible;
        CloudList.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
        CloudFilterRow.Visibility = cloud ? Visibility.Visible : Visibility.Collapsed;
        LocalFilterRow.Visibility = cloud ? Visibility.Collapsed : Visibility.Visible;
        LibHeader.Text = cloud ? Lang.S("nav.cloud") : (_currentFolder?.Name ?? Lang.S("nav.local"));
        SearchBox.Text = "";
        if (cloud) { _cloudPage = 1; _ = LoadCloud(false); }
    }

    // append=false: 换页/换筛选, 清空重载; append=true: 滚动到底加载下一页追加
    async System.Threading.Tasks.Task LoadCloud(bool append)
    {
        if (_cloudLoading) return;
        _cloudLoading = true;
        try
        {
            string sort = CloudSortCombo.SelectedIndex switch { 1 => "hot", 2 => "downloads", _ => "newest" };
            int diff = CloudDiffCombo.SelectedIndex;   // 0=全部 1..5=难度
            var r = await CloudApi.ListAsync(SearchBox.Text, sort, diff, _cloudPage, 20);
            if (!r.Ok) { ShowToast(r.Err ?? "云端加载失败"); return; }
            if (!append) _cloud.Clear();
            foreach (var it in r.Items) _cloud.Add(it);
            _cloudPages = r.Pages;
            if (!append && _cloud.Count == 0) ShowToast(Lang.S("t.cloudEmpty"));
        }
        finally { _cloudLoading = false; }
    }

    // 无限滚动: 接近底部 → 加载下一页
    void CloudScroll(object sender, ScrollChangedEventArgs e)
    {
        if (!_cloudMode || _cloudLoading || _cloudPage >= _cloudPages) return;
        if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 240 && e.ExtentHeight > 0)
        {
            _cloudPage++;
            _ = LoadCloud(true);
        }
    }

    async void CloudDownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CloudSheet sheet) return;
        ShowToast(string.Format(Lang.S("t.downloading"), sheet.Title));
        var path = await CloudApi.DownloadAsync(sheet, SongLibrary.SongsDir, msg => Dispatcher.Invoke(() => ShowToast(msg)));
        if (path != null) { RefreshLibrary(); ShowToast(string.Format(Lang.S("t.downloaded"), sheet.Title)); }
    }

    // ===== 设置界面(内联) =====
    bool _settingsOpen;
    DateTime _lastSettingsToggle = DateTime.MinValue;
    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if ((DateTime.Now - _lastSettingsToggle).TotalMilliseconds < 420) return;   // 防抖: 动画(400ms)期间忽略连点, 免状态错乱
        _lastSettingsToggle = DateTime.Now;
        if (_settingsOpen) CloseSettings(); else OpenSettings();
    }

    const double SettingsShift = 90;

    static void Slide(TranslateTransform t, double from, double to) =>
        t.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    static void Fade(UIElement el, double from, double to) =>
        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

    void OpenSettings()
    {
        _settingsOpen = true;
        RefreshSettingsPills();
        // 旧界面: 下移 + 淡出
        Slide(MidSlide, 0, SettingsShift); Fade(MidContent, 1, 0);
        Slide(RightSlide, 0, SettingsShift); Fade(RightContent, 1, 0);
        // 设置: 从上方下移进入 + 淡入
        SettingsPanel.Visibility = Visibility.Visible;
        Slide(SettingsSlide, -SettingsShift, 0); Fade(SettingsPanel, 0, 1);
    }

    void CloseSettings()
    {
        if (!_settingsOpen) return;
        _settingsOpen = false;
        if (_editingKeys) { KeyEdit_Click(this, new RoutedEventArgs()); SetBindBtn.Content = "点击修改"; SettingsBindArea.Visibility = Visibility.Collapsed; }
        // 旧界面: 移回 + 淡入
        Slide(MidSlide, SettingsShift, 0); Fade(MidContent, 0, 1);
        Slide(RightSlide, SettingsShift, 0); Fade(RightContent, 0, 1);
        // 设置: 上移退出 + 淡出
        Slide(SettingsSlide, 0, -SettingsShift);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        fade.Completed += (_, __) => { if (!_settingsOpen) SettingsPanel.Visibility = Visibility.Collapsed; };   // 期间又打开了就别隐藏
        SettingsPanel.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    bool _settingsInit;
    double _uiScale = 1.0, _fontScale = 1.0;
    static readonly int[] ScalePercents = { 80, 85, 90, 95, 100, 105, 110, 115, 120 };
    static int ScaleIndex(double s) { int i = Array.IndexOf(ScalePercents, (int)Math.Round(s * 100)); return i < 0 ? 4 : i; }

    void RefreshSettingsPills()
    {
        if (!_settingsInit)
        {
            SetLangCombo.ItemsSource = Lang.Names;
            SetUiCombo.ItemsSource = Array.ConvertAll(ScalePercents, p => $"{p}%");
            SetFontCombo.ItemsSource = Array.ConvertAll(ScalePercents, p => $"{p}%");
            _settingsInit = true;
        }
        SetLangCombo.SelectedIndex = (int)Lang.Current;
        SetThemeCombo.ItemsSource = new[] { Lang.S("theme.dark"), Lang.S("theme.light") };
        SetThemeCombo.SelectedIndex = Theme.Dark ? 0 : 1;
        SetWaitBox.Text = CountdownBox.Text;
        SetUiCombo.SelectedIndex = ScaleIndex(_uiScale);
        SetFontCombo.SelectedIndex = ScaleIndex(_fontScale);
        AboutVersion.Text = $"软件版本: v{UpdateChecker.AppVersion}-WPF";
    }

    void SetLangCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInit || SetLangCombo.SelectedIndex < 0) return;
        Lang.Set((AppLang)SetLangCombo.SelectedIndex);
        ApplyLanguage();
        SetThemeCombo.ItemsSource = new[] { Lang.S("theme.dark"), Lang.S("theme.light") };
        SetThemeCombo.SelectedIndex = Theme.Dark ? 0 : 1;
    }
    void SetThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInit || SetThemeCombo.SelectedIndex < 0) return;
        bool dark = SetThemeCombo.SelectedIndex == 0;
        if (dark == Theme.Dark) return;
        Theme.Apply(dark);
        ApplyKeyTheme();
        ThemeBtn.Content = $"{Lang.S("theme")}: {Lang.S(Theme.Dark ? "theme.dark" : "theme.light")}";
    }
    void SetWaitBox_Changed(object sender, TextChangedEventArgs e)
    {
        var digits = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(SetWaitBox.Text, char.IsDigit)));
        int sec = int.TryParse(digits, out int v) ? Math.Clamp(v, 0, 30) : 0;
        CountdownBox.Text = sec.ToString();
    }
    void SetUpdate_Click(object sender, RoutedEventArgs e) => _ = CheckUpdateManual();
    void SetLog_Click(object sender, RoutedEventArgs e) => _ = UploadLogAsync();

    // 界面比例: 整体缩放(LayoutTransform) + 同步放大窗口
    void SetUiCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInit || SetUiCombo.SelectedIndex < 0) return;
        _uiScale = ScalePercents[SetUiCombo.SelectedIndex] / 100.0;
        UiScale.ScaleX = UiScale.ScaleY = _uiScale;
        Width = 1200 * _uiScale; Height = 760 * _uiScale;
    }

    // 字体比例: 调整根节点继承字号
    void SetFontCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInit || SetFontCombo.SelectedIndex < 0) return;
        _fontScale = ScalePercents[SetFontCombo.SelectedIndex] / 100.0;
        RootScale.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 13.0 * _fontScale);
    }

    // 绑定: 就地进入/退出重映射 (在设置右栏琴键网格上操作, 不离开设置)
    void SetBind_Click(object sender, RoutedEventArgs e)
    {
        KeyEdit_Click(sender, e);   // 切换 _editingKeys + 保存
        bool on = _editingKeys;
        SetBindBtn.Content = on ? "完成绑定" : "点击修改";
        SettingsBindArea.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ShowToast(on ? Lang.S("t.bindOn") : Lang.S("t.bindOff"));
    }
    void Repo_Nav(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
        e.Handled = true;
    }

    async System.Threading.Tasks.Task CheckUpdateManual()
    {
        var r = await UpdateChecker.CheckAsync();
        if (r is not { } rel) { ShowToast(Lang.S("t.latest")); return; }
        if (MsgBox.Confirm(this, $"发现新版本 v{rel.Tag}\n当前 v{UpdateChecker.AppVersion}\n\n前往 GitHub 下载?", Lang.S("set.checkupd")) && rel.Url.Length > 0)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rel.Url) { UseShellExecute = true });
    }

    async System.Threading.Tasks.Task UploadLogAsync()
    {
        ShowToast(Lang.S("t.uploading"));
        var err = await CloudApi.UploadLogAsync(Logger.Recent(3));
        ShowToast(err == null ? Lang.S("t.logok") : Lang.S("t.logfail") + err);
    }

    // 练习: 进入全屏大键盘界面(跟弹展示后续再接入); 返回退出
    // 转场: ①背景快速盖住(先隐藏主界面) ②练习卡片从右侧小键盘的位置/尺寸带过冲非线性放大展开
    //       动画只给终点不锁起点 → 从当前值续演, 中途点返回/再进可平滑打断反向
    bool _practiceOpen, _pInit;
    System.Windows.Media.Effects.Effect? _rootShadow;
    readonly ScaleTransform _pCardScale = new(1, 1);
    readonly TranslateTransform _pCardTrans = new(0, 0);

    void Practice_Click(object sender, RoutedEventArgs e) => ShowPractice(true);
    void PracticeBack_Click(object sender, RoutedEventArgs e) => ShowPractice(false);

    Rect RectIn(FrameworkElement el) => el.TransformToVisual(RootScale).TransformBounds(new Rect(el.RenderSize));

    void ShowPractice(bool on)
    {
        if (_practiceOpen == on) return;
        _practiceOpen = on;
        if (on) { PracticePanel.Visibility = Visibility.Visible; PracticePanel.UpdateLayout(); }

        var small = RectIn(PianoGrid);
        var bigGrid = RectIn(PracticePianoGrid);   // 用大键盘本体(不含卡片内边距)算缩放, 末帧键盘本体才与小键盘等大
        var card = RectIn(PracticeCard);
        double s = bigGrid.Width > 0 ? small.Width / bigGrid.Width : 0.5;           // 缩到大键盘本体=小键盘等宽
        double dx = (small.Left + small.Width / 2) - (card.Left + card.Width / 2);  // 卡片中心对齐小键盘中心(键盘居中于卡片, 故键盘中心同步对齐)
        double dy = (small.Top + small.Height / 2) - (card.Top + card.Height / 2);

        // 首次: 无动画在持有, 直接把卡片落到"小键盘"起点(之后每次开/关都从当前值续演)
        if (on && !_pInit)
        {
            _pCardScale.ScaleX = _pCardScale.ScaleY = s;
            _pCardTrans.X = dx; _pCardTrans.Y = dy;
            PracticeBg.Opacity = 0; PracticeBackBtn.Opacity = 0;
            _pInit = true;
        }

        _pSmallScale = s;   // 供背景跟随钩子换算展开进度(0=小键盘, 1=全屏)

        // 打断适配: 按当前值到目标的剩余距离缩放时长, 反转一个快完成的动画只花很短时间, 不再整段橡皮筋
        double cur = _pCardScale.ScaleX, target = on ? 1 : s, full = 1 - s;
        double frac = full > 1e-6 ? Math.Clamp(Math.Abs(target - cur) / full, 0.15, 1) : 1;
        var dur = TimeSpan.FromMilliseconds((on ? 420 : 340) * frac);
        IEasingFunction ease = on
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.32 }    // 展开: 明显过冲弹一下
            : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 };    // 收回: 前期猛缩, 落到小键盘上回弹一下

        // 转场期间: ①摘窗口阴影(免整窗每帧重渲染进 Effect 拖垮帧率) ②整卡片位图缓存(缩放走显卡合成, 不每帧重绘15键)
        WindowRoot.Effect = null;
        PracticeCard.CacheMode ??= new BitmapCache();   // 只建一次, 打断不重建; 不吸附像素(免每帧重栅格)
        RenderOptions.SetBitmapScalingMode(PracticeCard, BitmapScalingMode.LowQuality);   // 缓存纹理走快速双线性缩放, 弱GPU也顺
        // 背景每帧跟着卡片当前缩放走 → 无论怎么中途打断都自洽(卡片≈小键盘才露主界面, 接近全屏就全遮)
        if (!_pRenderingHooked) { CompositionTarget.Rendering += PracticeBgFollow; _pRenderingHooked = true; }

        // 缩放动画兼作"收尾驱动": 被后续动画替换时旧动画 Completed 不触发, 只有最新一次会跑 → 天然处理打断
        var sx = new DoubleAnimation(target, dur) { EasingFunction = ease };
        sx.Completed += (_, __) =>
        {
            CompositionTarget.Rendering -= PracticeBgFollow;
            _pRenderingHooked = false;
            PracticeBgFollow(null, EventArgs.Empty);       // 锁到终值
            PracticeCard.CacheMode = null;                 // 交互态清晰 + 下次转场重建
            WindowRoot.Effect = _rootShadow;               // 装回窗口阴影
            if (!_practiceOpen) PracticePanel.Visibility = Visibility.Collapsed;
        };
        _pCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);

        Anim(_pCardScale, ScaleTransform.ScaleYProperty, target, dur, ease);
        Anim(_pCardTrans, TranslateTransform.XProperty, on ? 0 : dx, dur, ease);
        Anim(_pCardTrans, TranslateTransform.YProperty, on ? 0 : dy, dur, ease);
        Anim(PracticeBackBtn, UIElement.OpacityProperty, on ? 1 : 0, dur, ease);
    }

    double _pSmallScale = 0.5;
    bool _pRenderingHooked;
    // 背景不透明度 = 卡片展开进度的函数(走到 35% 即全遮); 纯当前缩放的函数, 故任意打断都一致
    void PracticeBgFollow(object? sender, EventArgs e)
    {
        double span = 1 - _pSmallScale;
        double t = span > 1e-6 ? (_pCardScale.ScaleX - _pSmallScale) / span : 1;
        PracticeBg.Opacity = Math.Clamp(t / 0.35, 0, 1);
    }

    // 只给终点(To), 省略 From → 从属性当前值(含正在播放的动画值)接着演, 天然可打断; 帧率交回显示器 vsync(更稳)
    static void Anim(IAnimatable t, DependencyProperty p, double to, TimeSpan dur, IEasingFunction ease)
        => t.BeginAnimation(p, new DoubleAnimation(to, dur) { EasingFunction = ease });

    // 轻提示: 底部居中淡入淡出 (StatusText 已随旧面板隐藏, 用它做用户反馈)
    DispatcherTimer? _toastTimer;
    void ShowToast(string msg)
    {
        ToastText.Text = msg;
        Toast.Visibility = Visibility.Visible;
        Toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _toastTimer.Tick += (_, __) =>
        {
            _toastTimer!.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, ___) => Toast.Visibility = Visibility.Collapsed;
            Toast.BeginAnimation(OpacityProperty, fade);
        };
        _toastTimer.Start();
    }

    // ===== 收藏夹(歌单) Stage2 =====
    void SaveFolders() => FolderStore.Save(_folders);
    void UpdateFoldersHeader() => FoldersHeader.Text = $"{Lang.S("side.folders")} {_folders.Count}";

    // ⭐ 星标 = 是否在"默认收藏夹"(第一个收藏夹)里
    Folder? DefaultFolder => _folders.FirstOrDefault();

    // 从某收藏夹移除该曲(不删曲谱文件); 若是默认收藏夹则同步熄灭星标
    void RemoveFromFolder(Folder f, SongInfo s)
    {
        if (!f.Files.Remove(s.File)) return;
        f.OnChanged(nameof(Folder.CountText));
        s.Fav = IsCollected(s);
        SaveFolders();
        if (ReferenceEquals(_currentFolder, f)) ApplyFilter();   // 从当前视图消失
        ShowToast(string.Format(Lang.S("t.removedFrom"), f.Name) + $"「{s.Name}」");
    }

    // ⭐ 亮 = 收藏在任意收藏夹里 (音乐软件式)
    bool IsCollected(SongInfo s) => _folders.Any(f => f.Files.Contains(s.File));

    // 点星标 = 加入/移出"默认收藏夹"(快捷收藏); 星标显示则看是否收藏在任意收藏夹
    void ToggleFav(SongInfo s)
    {
        var def = DefaultFolder;
        if (def == null) return;
        bool inDef = def.Files.Contains(s.File);
        if (inDef) def.Files.Remove(s.File); else def.Files.Add(s.File);
        def.OnChanged(nameof(Folder.CountText));
        s.Fav = IsCollected(s);
        SaveFolders();
        if (ReferenceEquals(_currentFolder, def)) ApplyFilter();
        ShowToast(!inDef ? string.Format(Lang.S("t.favTo"), def.Name) : (s.Fav ? string.Format(Lang.S("t.removedFrom"), def.Name) : Lang.S("t.unfav")));
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = InputBox.Ask(this, Lang.S("d.newFolder"), "", Lang.S("d.folderName"));
        if (string.IsNullOrWhiteSpace(name)) return;
        _folders.Add(new Folder { Name = name.Trim() });
        SaveFolders();
        UpdateFoldersHeader();
        ShowToast(string.Format(Lang.S("t.newFolder"), name.Trim()));
    }

    // 收藏夹拖动排序 (拖动只重排, 不进入收藏夹; 干净点击才进入)
    Point _folderDragStart;
    Folder? _folderDown;
    bool _folderDragging;
    void FolderList_DragDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _folderDragStart = e.GetPosition(null);
        _folderDown = (e.OriginalSource as FrameworkElement)?.DataContext as Folder;
        _folderDragging = false;
    }
    void FolderList_DragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _folderDown == null || _folderDragging) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _folderDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _folderDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _folderDragging = true;
        DragDrop.DoDragDrop(FolderList, _folderDown, DragDropEffects.Move);   // 阻塞至放下
        DropLine.Visibility = Visibility.Collapsed;
        _folderDown = null;
    }
    void FolderList_DragUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_folderDragging && _folderDown != null) OpenFolder(_folderDown);   // 干净点击 → 进入收藏夹
        _folderDown = null; _folderDragging = false;
    }
    int _dropIndex = -1;   // 落点插入位置(在该索引之前)
    void FolderList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(Folder))) return;
        var pt = e.GetPosition(FolderListGrid);
        _dropIndex = _folders.Count;
        double lineY = 0;
        for (int i = 0; i < FolderList.Items.Count; i++)
        {
            if (FolderList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement lbi) continue;
            double top = lbi.TranslatePoint(new Point(0, 0), FolderListGrid).Y;
            double mid = top + lbi.ActualHeight / 2;
            if (pt.Y < mid) { _dropIndex = i; lineY = top; break; }
            lineY = top + lbi.ActualHeight;   // 落到该项之后(最后一次生效=末尾)
        }
        DropLine.Margin = new Thickness(10, Math.Max(0, lineY - 1), 10, 0);
        DropLine.Visibility = Visibility.Visible;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }
    void FolderList_DragLeave(object sender, DragEventArgs e) => DropLine.Visibility = Visibility.Collapsed;

    void FolderList_Drop(object sender, DragEventArgs e)
    {
        DropLine.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(typeof(Folder)) is not Folder src) return;
        int from = _folders.IndexOf(src);
        if (from < 0 || _dropIndex < 0) return;
        int to = _dropIndex > from ? _dropIndex - 1 : _dropIndex;   // 移除后目标索引修正
        to = Math.Clamp(to, 0, _folders.Count - 1);
        if (from == to) return;
        _folders.Move(from, to);
        SaveFolders();
        RefreshLibrary();   // 默认收藏夹(第一个)可能变了 → 重算星标
    }

    void OpenFolder(Folder f)
    {
        CloseSettings();
        _currentFolder = f;
        FolderList.SelectedItem = f;
        SetLibTab(true);
        SetCloudMode(false);
        UpdateLibHeader();
        ApplyFilter();
    }

    // 选中变化不再自动导航(避免拖动时进入); 导航改由 FolderList_DragUp 的干净点击触发
    void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    // 收藏夹右键: 重命名 / 删除
    void FolderList_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var f = (e.OriginalSource as FrameworkElement)?.DataContext as Folder;
        if (f == null) return;
        var cm = new ContextMenu();
        var open = new MenuItem { Header = Lang.S("m.view") };
        open.Click += (_, __) => OpenFolder(f);
        var rename = new MenuItem { Header = Lang.S("m.rename") };
        rename.Click += (_, __) =>
        {
            var n = InputBox.Ask(this, Lang.S("d.renameFolder"), f.Name, Lang.S("d.newName"));
            if (!string.IsNullOrWhiteSpace(n)) { f.Name = n.Trim(); SaveFolders(); }
        };
        var del = new MenuItem { Header = new TextBlock { Text = Lang.S("m.delFolder"), Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)) } };
        del.Click += (_, __) =>
        {
            if (!MsgBox.Confirm(this, string.Format(Lang.S("c.delFolder"), f.Name), Lang.S("m.delFolder"))) return;
            _folders.Remove(f);
            if (ReferenceEquals(_currentFolder, f)) { _currentFolder = null; ApplyFilter(); }
            SaveFolders();
            UpdateFoldersHeader();
        };
        cm.Items.Add(open); cm.Items.Add(rename); cm.Items.Add(del);
        cm.IsOpen = true;
        e.Handled = true;
    }

    // 曲目"收藏到……"子菜单: 各收藏夹(勾选=在其中) + 新建收藏夹
    MenuItem BuildFavToMenu(SongInfo s)
    {
        var favTo = new MenuItem { Header = Lang.S("m.favTo") };
        foreach (var f in _folders)
        {
            var mi = new MenuItem { Header = f.Name, IsCheckable = true, IsChecked = f.Files.Contains(s.File) };
            var folder = f;
            mi.Click += (_, __) =>
            {
                if (mi.IsChecked) { if (!folder.Files.Contains(s.File)) folder.Files.Add(s.File); }
                else folder.Files.Remove(s.File);
                folder.OnChanged(nameof(Folder.CountText));
                s.Fav = IsCollected(s);   // 在任意收藏夹 → 星标亮
                SaveFolders();
                if (ReferenceEquals(_currentFolder, folder)) ApplyFilter();
                ShowToast(string.Format(Lang.S(mi.IsChecked ? "t.favTo" : "t.removedFrom"), folder.Name));
            };
            favTo.Items.Add(mi);
        }
        favTo.Items.Add(new Separator());
        var neu = new MenuItem { Header = Lang.S("m.newFolder") };
        neu.Click += (_, __) =>
        {
            var name = InputBox.Ask(this, Lang.S("d.newFolder"), "", Lang.S("d.folderName"));
            if (string.IsNullOrWhiteSpace(name)) return;
            var f = new Folder { Name = name.Trim() };
            f.Files.Add(s.File);
            _folders.Add(f);
            s.Fav = IsCollected(s);
            SaveFolders();
            UpdateFoldersHeader();
            ShowToast(string.Format(Lang.S("t.favTo"), f.Name));
        };
        favTo.Items.Add(neu);
        return favTo;
    }

    void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要导入的曲谱 (可多选)",
            Multiselect = true,
            Filter = "所有支持格式|*.json;*.txt;*.mid;*.midi|曲谱 (json/txt)|*.json;*.txt|MIDI|*.mid;*.midi|所有文件|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        System.IO.Directory.CreateDirectory(SongLibrary.SongsDir);
        int ok = 0, fail = 0;
        foreach (var src in dlg.FileNames)
        {
            var ext = System.IO.Path.GetExtension(src).ToLowerInvariant();
            try
            {
                if (ext is ".mid" or ".midi") { if (ImportMidi(src)) ok++; else fail++; }
                else
                {
                    var dest = System.IO.Path.Combine(SongLibrary.SongsDir, System.IO.Path.GetFileName(src));
                    System.IO.File.Copy(src, dest, overwrite: true);
                    ok++;
                }
            }
            catch { fail++; }
        }
        RefreshLibrary();
        StatusText.Text = fail == 0 ? $"状态: ✅ 已导入 {ok} 个曲谱" : $"状态: 导入 {ok} 个, 失败 {fail} 个";
    }

    // 返回 true=已导入; false=用户取消或无音符
    bool ImportMidi(string path)
    {
        var importer = new MidiImporter(path);
        var tracks = importer.AnalyzeTracks();
        if (tracks.Count == 0) { MsgBox.Info(this, "MIDI 文件中没有音符数据", System.IO.Path.GetFileName(path)); return false; }

        var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
        var win = new MidiImportDialog(importer, tracks, baseName) { Owner = this };
        if (win.ShowDialog() != true || win.ResultNotes == null) return false;

        SongLibrary.WriteImported(win.ResultName, "SMAP MIDI Import", win.ResultBpm, win.ResultNotes);
        return true;
    }
    // 按键映射两段式: 平时"编辑按键映射"(点键试听) → 点此进编辑态"保存按键映射"(点键重绑) → 再点保存退出
    bool _editingKeys;
    void KeyEdit_Click(object sender, RoutedEventArgs e)
    {
        _editingKeys = !_editingKeys;
        if (_editingKeys)
        {
            KeyEditBtn.Content = Lang.S("keys.save");
            StatusText.Text = "状态: 编辑态 — 点琴键格再按物理键重绑";
        }
        else
        {
            if (_remapIndex >= 0) { int p = _remapIndex; _remapIndex = -1; RefreshKey(p); }   // 取消未完成重绑
            KeyConfig.Save(_player.Vk);
            KeyEditBtn.Content = Lang.S("keys.edit");
            StatusText.Text = "状态: ✅ 按键映射已保存";
        }
    }

    // ---- 全局热键 (光遇里也能控制): F1开始停止 F2暂停 F3减速 F4加速 F5后退5s F6前进10s ----
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int HK_START = 1, HK_PAUSE = 2, HK_SLOW = 3, HK_FAST = 4, HK_BACK = 5, HK_FWD = 6;
    IntPtr _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        RegisterHotKey(_hwnd, HK_START, 0, 0x70);   // F1
        RegisterHotKey(_hwnd, HK_PAUSE, 0, 0x71);   // F2
        RegisterHotKey(_hwnd, HK_SLOW, 0, 0x72);    // F3 减速
        RegisterHotKey(_hwnd, HK_FAST, 0, 0x73);    // F4 加速
        RegisterHotKey(_hwnd, HK_BACK, 0, 0x74);    // F5 后退 5s
        RegisterHotKey(_hwnd, HK_FWD, 0, 0x75);     // F6 前进 10s
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            switch (wp.ToInt32())
            {
                case HK_START: StartAuto(false); handled = true; break;   // 热键: 无倒计时
                case HK_PAUSE: Pause_Click(this, new RoutedEventArgs()); handled = true; break;
                case HK_SLOW: AdjustSpeed(-0.1); handled = true; break;
                case HK_FAST: AdjustSpeed(+0.1); handled = true; break;
                case HK_BACK: SeekRelative(-5000); handled = true; break;
                case HK_FWD: SeekRelative(+10000); handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    // 调速: 改滑块值(ValueChanged 会同步 SpeedLabel + SkyPlayer.SpeedFactor)
    void AdjustSpeed(double delta)
    {
        SetSpeed(_speed + delta);
        StatusText.Text = $"状态: 速度 {_speed:0.0}x";
    }

    // 播放速度: 胶囊显示 + 立即变速
    void SetSpeed(double v)
    {
        _speed = Math.Clamp(v, 0.5, 2.0);
        _player.RandomSpeed = false;
        SpeedPill.Content = $"{_speed:0.0}x";
        _player.SpeedFactor = _speed;
    }

    void Speed_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { MaxHeight = 296 };
        var rnd = new MenuItem { Header = Lang.S("speed.random"), IsChecked = _player.RandomSpeed };
        rnd.Click += (_, __) =>
        {
            _player.RandomSpeed = true;                    // 每音符随机变速
            SpeedPill.Content = Lang.S("speed.random");
        };
        menu.Items.Add(rnd);
        foreach (double v in new[] { 2.0, 1.75, 1.5, 1.25, 1.0, 0.75, 0.5 })
        {
            var s = v;
            var it = new MenuItem { Header = $"{s:0.0}x", IsChecked = !_player.RandomSpeed && Math.Abs(s - _speed) < 0.01 };
            it.Click += (_, __) => SetSpeed(s);
            menu.Items.Add(it);
        }
        menu.PlacementTarget = (sender as UIElement) ?? SpeedPill;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    // 相对跳转(仅播放/试听中); 夹到 [0, 总时长]
    void SeekRelative(double deltaMs)
    {
        if (!_playing && !_previewing) return;
        double target = Math.Clamp(_player.PositionMs + deltaMs, 0, _player.TotalMs);
        _player.Seek(target);
        StatusText.Text = $"状态: {(deltaMs < 0 ? "后退" : "前进")} → {Fmt(target)}";
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
            foreach (int id in new[] { HK_START, HK_PAUSE, HK_SLOW, HK_FAST, HK_BACK, HK_FWD })
                UnregisterHotKey(_hwnd, id);
        _player.Stop();
        base.OnClosed(e);
    }
}
