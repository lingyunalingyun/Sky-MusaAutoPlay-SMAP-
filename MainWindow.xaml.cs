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
    bool _previewMode = true;   // 默认试听(走扬声器); 关闭=演奏模式(发送游戏按键)。右下开关点亮=演奏
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
    static string NowPlayingFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "nowplaying.txt");
    static string PrefsFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "prefs.txt");
    bool _prefsLoading;   // 载入偏好期间抑制回存
    double _pendingResumeMs;    // 上次退出保留的进度: 恢复曲首次播放时 seek 到此处
    SongInfo? _resumeSong;      // 与 _pendingResumeMs 配对, 只对这首生效
    DateTime _lastNpSave;       // "当前曲+进度"节流保存时间戳

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
        _keyboardProc = KeyboardHook;   // 保持低级键盘回调强引用，防止被 GC 回收
        _player.Vk = KeyConfig.Load();
        BuildPianoGrid();
        BuildPracticeGrid();
        PracticeCard.RenderTransform = new TransformGroup { Children = { _pCardScale, _pCardTrans } };
        SizeChanged += (_, __) => { if (_practiceOpen && !_transitioning) ApplyReadLayout(animate: false); };   // 练习键盘布局随窗口尺寸即时重算(读谱/普通都要, 小窗口自动缩不遮控件; 转场中不插手免清动画)
        StateChanged += (_, __) => MaxBtn.Content = WindowState == WindowState.Maximized ? "❐" : "☐";   // 最大化钮字形随状态切换
        Deactivated += (_, __) => CloseLibraryChoice(immediate: true);   // Popup 是独立顶层窗，切到别的软件时必须同步关闭
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
        RestoreNowPlaying();
        LoadPlayMode();
        foreach (var f in FolderStore.Load()) _folders.Add(f);
        FolderList.ItemsSource = _folders;
        UpdateFoldersHeader();
        ApplyLanguage();
        LoadPrefs();   // 恢复音色/洞穴/演奏/倍速

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
        InstrumentList.Items.Clear();   // 语言变了, 下次展开时重建翻译后的音色列表
        RefreshPitchPill();
        ThemeBtn.Content = $"{Lang.S("theme")}: {Lang.S(Theme.Dark ? "theme.dark" : "theme.light")}";
        AboutBtn.Content = Lang.S("about");

        int si = SortCombo.SelectedIndex < 0 ? 0 : SortCombo.SelectedIndex;
        SortCombo.ItemsSource = new[] { Lang.S("sort.az"), Lang.S("sort.za"), Lang.S("sort.fav") };
        SortCombo.SelectedIndex = si;
        SortChoiceBtn.Content = SortCombo.SelectedItem;

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
        ReadModeLbl.Text = Lang.S("practice.readmode");
        MetroModeLbl.Text = Lang.S("practice.metro");
        MetroSpeedLbl.Text = Lang.S("practice.metrospeed");
        GamePracticeLbl.Text = Lang.S("practice.game");
        SearchBox.ToolTip = Lang.S("search.hint");
        PrevBtn.ToolTip = Lang.S("tip.prev");
        NextBtn.ToolTip = Lang.S("tip.next");
        PlaylistBtn.ToolTip = Lang.S("tip.playlist");
        PreviewIcon.ToolTip = Lang.S("tip.perform");
        RefreshPerformIcon();
        CaveIcon.ToolTip = Lang.S("tip.cave");
        InstrumentPill.ToolTip = Lang.S("tip.inst");
        PlayModeBtn.ToolTip = $"{Lang.S("tip.playmode")}: {PlayModeName(_playMode)}";
        ProgBar.ToolTip = Lang.S("tip.seek");
        if (!_playing && !_previewing && !(_pendingResumeMs > 0 && _resumeSong != null)) PlayerSongName.Text = Lang.S("player.nosong");
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
        InstrumentList.Items.Clear();

        OnSongSelected();
    }

    // ---- 自定义标题栏窗口控制 ----
    void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        if (e.ClickCount == 2) { ToggleMax(); return; }               // 双击标题栏: 最大化/还原
        if (WindowState == WindowState.Normal) DragMove();            // 最大化态不拖动
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
        // 练习: 用播放列表切换练习曲(重建高亮/步/进度), 不自动播放
        if (_practiceOpen)
        {
            if (_playlist.Count == 0) return;
            int pi = _practiceSong != null ? _playlist.IndexOf(_practiceSong) : -1;
            pi = pi < 0 ? (delta > 0 ? 0 : _playlist.Count - 1) : (pi + delta);
            if (pi < 0) pi = _playlist.Count - 1;
            if (pi >= _playlist.Count) pi = 0;
            if (_playing || _previewing) StopPlaying();   // 停掉正在展示
            _nowPlaying = _playlist[pi];                  // 让 StartPractice 选到它
            StartPractice();
            return;
        }
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
        SetPlayerCover(s.File);
    }

    // 播放条封面: 曲谱内嵌封面则显示, 否则回退 logo
    void SetPlayerCover(string? file)
    {
        var img = file != null ? CoverUtil.FromBytes(CoverUtil.ReadEmbedded(file)) : null;
        if (img != null) { PlayerCover.Source = img; PlayerCover.Visibility = Visibility.Visible; PlayerCoverLogo.Visibility = Visibility.Collapsed; }
        else { PlayerCover.Source = null; PlayerCover.Visibility = Visibility.Collapsed; PlayerCoverLogo.Visibility = Visibility.Visible; }
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
        SetPlayerCover(null);
    }

    void AddToPlaylist(SongInfo s)
    {
        if (_playlist.Any(x => x.File == s.File)) { ShowToast(string.Format(Lang.S("t.inQueue"), s.Name)); return; }
        _playlist.Add(s);
        UpdatePlaylistHeader();
        SavePlaylist();
        ShowToast(string.Format(Lang.S("t.addedQueue"), s.Name));
    }

    void PlayPlaylistItem(SongInfo s, bool skipCountdown = false)
    {
        _advanceTimer?.Stop();
        if (_playing || _previewing) StopPlaying();
        if (!TryLoad(s)) return;
        _playCurrent = s;
        _nowPlaying = s;
        _paused = false;
        UpdateNowPlaying(s);
        // 续播: UpdateNowPlaying 已把进度条清零, 立刻渲染回保留位置(含倒计时期间), 免"先闪回0"
        if (_pendingResumeMs > 0 && ReferenceEquals(s, _resumeSong))
        {
            double total = _notes.Count > 0 ? _notes[^1].ms : 0;
            RenderProg(total > 0 ? _pendingResumeMs / total : 0, _pendingResumeMs, total);
        }
        int sec = skipCountdown || _previewMode || _practiceOpen ? 0 : (int.TryParse(CountdownBox.Text, out int x) ? Math.Max(0, x) : 0);
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

    // 退出时保存"上次播放的曲 + 进度"; 下次启动恢复到底部条(不自动播放), 按播放从该进度续
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveNowPlaying();
        SavePrefs();
        base.OnClosing(e);
    }

    void SaveNowPlaying()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NowPlayingFile)!);
            if (_nowPlaying == null) { if (System.IO.File.Exists(NowPlayingFile)) System.IO.File.Delete(NowPlayingFile); return; }
            double pos = _player.PositionMs;
            if (pos <= 0 && _pendingResumeMs > 0) pos = _pendingResumeMs;   // 恢复后未播放过 → 保留原进度
            System.IO.File.WriteAllText(NowPlayingFile, $"{_nowPlaying.File}|{(long)pos}");
        }
        catch { }
    }

    void RestoreNowPlaying()
    {
        try
        {
            if (!System.IO.File.Exists(NowPlayingFile)) return;
            var raw = System.IO.File.ReadAllText(NowPlayingFile).Trim();
            int bar = raw.LastIndexOf('|');
            if (bar < 0) return;
            var path = raw[..bar];
            double.TryParse(raw[(bar + 1)..], out double pos);
            var song = _all.FirstOrDefault(x => x.File == path);
            if (song == null) return;
            _nowPlaying = song; _playCurrent = _playlist.FirstOrDefault(x => x.File == song.File) ?? song;
            if (!TryLoad(song)) return;                     // 载入音符(总时长/播放就绪)
            _resumeSong = song; _pendingResumeMs = pos;
            UpdateNowPlaying(song);                          // 底部条显示歌曲信息 + 进度条
            double total = _notes.Count > 0 ? _notes[^1].ms : 0;
            RenderProg(total > 0 ? pos / total : 0, pos, total);   // 进度条落到保留位置
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
        style.Setters.Add(new EventSetter(FrameworkElement.LoadedEvent, new RoutedEventHandler(SongItemLoaded)));   // 项进入可见 → 懒加载封面
        SongList.ItemContainerStyle = style;
    }

    // 列表项容器实例化(仅可见项, 虚拟化) → 后台读该曲内嵌封面
    void SongItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewItem { Content: SongInfo s }) s.EnsureCover();
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
        FilterChoiceBtn.Content = FilterCombo.SelectedItem;
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
            btn.PreviewMouseLeftButtonDown += (_, ev) => { AudioEngine.NoteOn(idx); FlashKey(idx); _practiceHeld.Add(idx); PracticePress(idx); ev.Handled = true; };
            btn.PreviewMouseLeftButtonUp += (_, __) => { AudioEngine.NoteOff(idx); _practiceHeld.Remove(idx); };
            btn.MouseLeave += (_, __) => { AudioEngine.NoteOff(idx); _practiceHeld.Remove(idx); };
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
    void UpdateProgUi()
    {
        if (_practiceOpen && !_playing && !_previewing && _practiceSteps.Count > 0)   // 纯练习(未展示): 用当前步位置
        {
            double pos = _practiceStep < _practiceStepMs.Count ? _practiceStepMs[_practiceStep] : _practiceTotalMs;
            RenderProg(_practiceTotalMs > 0 ? pos / _practiceTotalMs : 0, pos, _practiceTotalMs);
        }
        else if (!_playing && !_previewing && _pendingResumeMs > 0 && _resumeSong != null)   // 恢复上次进度: 未播放前先把进度条落到保留位置
        {
            double total = _notes.Count > 0 ? _notes[^1].ms : 0;
            RenderProg(total > 0 ? _pendingResumeMs / total : 0, _pendingResumeMs, total);
        }
        else RenderProg(_player.TotalMs > 0 ? _player.PositionMs / _player.TotalMs : 0, _player.PositionMs, _player.TotalMs);
    }

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
        double w = ProgBar.ActualWidth; if (w <= 0) return;
        double frac = Math.Clamp(mouseX / w, 0, 1);
        if (_practiceOpen)                          // 练习: 拖动/点击底部条
        {
            if (_practiceSteps.Count == 0) return;
            if (_playing || _previewing)            // 展示中 → 跳播放位置(高亮随后自动跟上)
            {
                double t = _player.TotalMs;
                _player.Seek(frac * t);
                RenderProg(frac, frac * t, t);
            }
            else                                     // 未展示 → 按时间跳到最近的步(落点与高亮对齐)
            {
                _practiceStep = NearestStep(frac * _practiceTotalMs);
                _practiceHeld.Clear();
                RenderPracticeHints();
                UpdateProgUi();
            }
            return;
        }
        if (!_playing && !_previewing)              // 未播放: 若有已载入的曲(如启动恢复的), 拖动=设定续播起点
        {
            if (_nowPlaying == null || _notes.Count == 0) return;
            double t = _notes[^1].ms;
            _pendingResumeMs = frac * t; _resumeSong = _nowPlaying;
            RenderProg(frac, _pendingResumeMs, t);
            return;
        }
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
            if (_practiceOpen) SyncPracticeHighlightToPlayback();   // 练习展示: 高亮跟播放位置走
            UpdateProgUi();
            // 播放时每 2 秒节流保存"当前曲+进度"(非练习), 强杀/崩溃也能恢复
            if (!_practiceOpen && (DateTime.Now - _lastNpSave).TotalSeconds >= 2) { _lastNpSave = DateTime.Now; SaveNowPlaying(); }
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
        if (!_gamePracticeOn && !e.IsRepeat && System.Windows.Input.Keyboard.FocusedElement is not TextBox)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            int idx = Array.IndexOf(_player.Vk, (ushort)vk);
            if (idx >= 0)
            {
                AudioEngine.NoteOn(idx);
                FlashKey(idx);
                if (_practiceOpen) { _practiceHeld.Add(idx); PracticePress(idx); }
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        // 松开物理键 -> 停对应琴键的持续长音
        if (!_gamePracticeOn && System.Windows.Input.Keyboard.FocusedElement is not TextBox)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            int idx = Array.IndexOf(_player.Vk, (ushort)vk);
            if (idx >= 0) { AudioEngine.NoteOff(idx); _practiceHeld.Remove(idx); e.Handled = true; return; }
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
        SavePrefs();
    }

    void Instrument_Click(object sender, RoutedEventArgs e)
    {
        if (InstrumentList.Items.Count == 0)
        {
            foreach (var name in AudioEngine.Instruments)
                InstrumentList.Items.Add(new ListBoxItem { Content = Lang.Instrument(name), Tag = name });
        }
        InstrumentPopup.PlacementTarget = (sender as UIElement) ?? InstrumentBtn;
        var selected = InstrumentList.Items.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, _instrumentName));
        InstrumentList.SelectedItem = selected;
        InstrumentPopup.IsOpen = true;
        if (selected != null)
            Dispatcher.BeginInvoke(() => InstrumentList.ScrollIntoView(selected), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void InstrumentList_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(InstrumentList, source) is not ListBoxItem item ||
            item.Tag is not string name) return;
        System.Threading.Tasks.Task.Run(() => AudioEngine.SetInstrument(name));
        _instrumentName = name;
        InstrumentBtn.Content = $"{Lang.S("instrument")}: {Lang.Instrument(name)}";
        InstrumentPill.Content = $"{Lang.S("instrument")}:{Lang.Instrument(name)}";
        RefreshPitchPill();
        SavePrefs();
        ShowToast($"{Lang.S("instrument")} → {Lang.Instrument(name)}");
        InstrumentPopup.IsOpen = false;
        e.Handled = true;
    }

    ComboBox? _libraryChoiceModel;
    int _libraryChoiceGeneration;

    void FilterChoice_Click(object sender, RoutedEventArgs e) => ToggleLibraryChoice(FilterCombo, FilterChoiceBtn);
    void SortChoice_Click(object sender, RoutedEventArgs e) => ToggleLibraryChoice(SortCombo, SortChoiceBtn);

    void ToggleLibraryChoice(ComboBox model, Button target)
    {
        if (LibraryChoicePopup.IsOpen && ReferenceEquals(_libraryChoiceModel, model)) { CloseLibraryChoice(); return; }
        _libraryChoiceModel = model;
        LibraryChoiceList.Items.Clear();
        for (int i = 0; i < model.Items.Count; i++)
            LibraryChoiceList.Items.Add(new ListBoxItem { Content = model.Items[i], Tag = i });
        LibraryChoiceList.SelectedIndex = model.SelectedIndex;
        LibraryChoicePopup.PlacementTarget = target;
        LibraryChoiceChrome.BeginAnimation(OpacityProperty, null);
        LibraryChoiceMove.BeginAnimation(TranslateTransform.YProperty, null);
        LibraryChoiceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        LibraryChoiceChrome.Opacity = 0;
        LibraryChoiceMove.Y = -10;
        LibraryChoiceScale.ScaleY = 0.92;
        LibraryChoiceChrome.IsHitTestVisible = true;
        LibraryChoicePopup.IsOpen = true;
        var generation = ++_libraryChoiceGeneration;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _libraryChoiceGeneration || !LibraryChoicePopup.IsOpen) return;
            LibraryChoiceChrome.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            LibraryChoiceMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } });
            LibraryChoiceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.92, 1, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } });
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    void CloseLibraryChoice(bool immediate = false)
    {
        if (!LibraryChoicePopup.IsOpen) return;
        var generation = ++_libraryChoiceGeneration;
        if (immediate)
        {
            LibraryChoiceChrome.BeginAnimation(OpacityProperty, null);
            LibraryChoiceMove.BeginAnimation(TranslateTransform.YProperty, null);
            LibraryChoiceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            LibraryChoicePopup.IsOpen = false;
            LibraryChoiceChrome.Opacity = 1;
            LibraryChoiceMove.Y = 0;
            LibraryChoiceScale.ScaleY = 1;
            LibraryChoiceChrome.IsHitTestVisible = true;
            return;
        }
        LibraryChoiceChrome.IsHitTestVisible = false;
        var fade = new DoubleAnimation(LibraryChoiceChrome.Opacity, 0, TimeSpan.FromMilliseconds(170))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, __) =>
        {
            if (generation != _libraryChoiceGeneration) return;
            LibraryChoicePopup.IsOpen = false;
            LibraryChoiceChrome.Opacity = 1;
            LibraryChoiceMove.Y = 0;
            LibraryChoiceScale.ScaleY = 1;
            LibraryChoiceChrome.IsHitTestVisible = true;
        };
        LibraryChoiceChrome.BeginAnimation(OpacityProperty, fade);
        LibraryChoiceMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(LibraryChoiceMove.Y, -8, TimeSpan.FromMilliseconds(170))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        LibraryChoiceScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(LibraryChoiceScale.ScaleY, 0.94, TimeSpan.FromMilliseconds(170))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
    }

    void LibraryChoiceList_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_libraryChoiceModel == null || ClickedOption(LibraryChoiceList, e) is not { Tag: int index }) return;
        _libraryChoiceModel.SelectedIndex = index;
        if (ReferenceEquals(_libraryChoiceModel, FilterCombo)) FilterChoiceBtn.Content = FilterCombo.SelectedItem;
        else SortChoiceBtn.Content = SortCombo.SelectedItem;
        CloseLibraryChoice();
        e.Handled = true;
    }

    void RootScale_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!LibraryChoicePopup.IsOpen || e.OriginalSource is not DependencyObject source) return;
        if (FilterChoiceBtn.IsAncestorOf(source) || SortChoiceBtn.IsAncestorOf(source)) return;
        CloseLibraryChoice();
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
        int cur = AudioEngine.GetOffset(_instrumentName);
        if (PitchList.Items.Count == 0)
        {
            foreach (int semi in new[] { 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, -12, -24 })
                PitchList.Items.Add(new ListBoxItem { Content = PitchLabel(semi), Tag = semi });
        }
        OpenOptionPopup(PitchPopup, PitchList, (sender as UIElement) ?? PitchPill, cur);
    }

    void PitchList_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ClickedOption(PitchList, e) is not { Tag: int semi }) return;
        var inst = _instrumentName;
        AudioEngine.SetOffset(inst, semi);
        RefreshPitchPill();
        ShowToast($"{Lang.S("pitch")} {Lang.Instrument(inst)} → {PitchLabel(semi)}");
        PitchPopup.IsOpen = false;
        e.Handled = true;
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
        _curMsPerBeat = doc.MsPerBeat > 1 ? doc.MsPerBeat : 500;   // 供读谱按拍建网格(含空拍)
        if (_notes.Count == 0) { StatusText.Text = "状态: 该曲谱无音符"; return false; }
        return true;
    }
    double _curMsPerBeat = 500;

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
        if (_practiceOpen)   // 练习: 播放键=展示练习这首(走扬声器, 从当前步开始), 再点停止
        {
            if (_playing || _previewing) { StopPlaying(); return; }
            if (_practiceSteps.Count == 0) return;
            _playing = true; _paused = false;
            SetPlayGlyph(true);
            StatusText.Text = $"状态: 🎧 练习展示中 ({_speed:0.0}x)";
            StartFlash();
            double from = _practiceStep > 0 && _practiceStep < _practiceStepMs.Count ? _practiceStepMs[_practiceStep] : 0;
            _player.Play(_notes, _speed, () => Dispatcher.BeginInvoke(new Action(OnPlayDone)), AudioEngine.Play, from);
            return;
        }
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
        _pendingResumeMs = 0; _resumeSong = null;   // 试听别的曲 → 放弃待恢复进度
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
        // 恢复上次进度: 仅对保留的那首, 从保留位置起播(直接传给 Play, 避免 Play 后再 Seek 的竞态)
        double startMs = (_pendingResumeMs > 0 && ReferenceEquals(_nowPlaying, _resumeSong)) ? _pendingResumeMs : 0;
        _pendingResumeMs = 0; _resumeSong = null;
        StartFlash();
        if (_previewMode || _practiceOpen)   // 试听模式 或 练习展示: 走扬声器, 绝不发游戏按键(不调用模拟输入)
        {
            StatusText.Text = $"状态: 🎧 {(_practiceOpen ? "练习展示" : "试听")}中 ({_speed:0.0}x)";
            _player.Play(_notes, _speed, () => Dispatcher.BeginInvoke(new Action(OnPlayDone)), AudioEngine.Play, startMs);
        }
        else
        {
            StatusText.Text = "状态: 🎵 演奏中... (F1 停止 / F2 暂停)";
            _player.Play(_notes, _speed, () => Dispatcher.BeginInvoke(new Action(OnPlayDone)), null, startMs);
        }
        // 续播: 立刻把进度条渲染到起始位置, 消掉 UpdateNowPlaying 清零导致的"先闪回0"
        if (startMs > 0)
        {
            double total = _notes.Count > 0 ? _notes[^1].ms : 0;
            RenderProg(total > 0 ? startMs / total : 0, startMs, total);
        }
    }

    // 试听模式开关: 高亮=开
    // 右下开关: 切换 演奏模式(发送游戏按键) ↔ 试听(走扬声器, 默认); 点亮=演奏
    void PreviewMode_Click(object sender, RoutedEventArgs e)
    {
        _previewMode = !_previewMode;
        RefreshPerformIcon();
        StatusText.Text = $"状态: 已切到{(_previewMode ? "试听(走扬声器)" : "演奏(发送游戏按键)")}模式";
        SavePrefs();
    }

    // 演奏模式点亮橙色(提示发送真实按键), 试听为暗色
    void RefreshPerformIcon() =>
        PreviewIcon.Foreground = _previewMode ? (Brush)Application.Current.Resources["SubTextFg"] : new SolidColorBrush(Color.FromRgb(0xE6, 0x82, 0x2C));

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
        if (_practiceOpen && _practiceSteps.Count > 0)   // 练习: 展示停止后仍保留歌曲信息+进度条+高亮
        {
            if (_practiceSong != null) UpdateNowPlaying(_practiceSong); else ProgBar.Visibility = Visibility.Visible;
            RenderPracticeHints();
            UpdateProgUi();
        }
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
        if (_practiceOpen) _practiceStep = 0;   // 练习: 一首展示结束 → 回到开头(0 位置)
        ResetPlayUi();
        AudioEngine.StopAll();
        if (StatusText.Text.Contains("演奏中")) StatusText.Text = "状态: 演奏完成";
        else if (wasPreview && StatusText.Text.Contains("试听")) StatusText.Text = "状态: 试听结束";

        // 自动续播: 按播放方式决定下一首, 间隔 2 秒 (试听按钮 wasPreview 除外; 试听模式仍续播; 练习展示不续播)
        if (!wasPreview && !_practiceOpen && finished != null && _playlist.Count > 0)
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

    // 偏好: 音色 / 洞穴 / 演奏(试听) / 倍速; 改动即存, 启动载入
    void LoadPrefs()
    {
        _prefsLoading = true;
        try
        {
            if (System.IO.File.Exists(PrefsFile))
                foreach (var line in System.IO.File.ReadAllLines(PrefsFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    var k = line[..eq].Trim(); var v = line[(eq + 1)..].Trim();
                    switch (k)
                    {
                        case "instrument": if (Array.IndexOf(AudioEngine.Instruments, v) >= 0) _instrumentName = v; break;
                        case "cave": AudioEngine.Cave = v == "1"; break;
                        case "preview": _previewMode = v == "1"; break;
                        case "speed": if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sp)) _speed = Math.Clamp(sp, 0.5, 2.0); break;
                    }
                }
        }
        catch { }
        // 应用到引擎 + 底部栏 UI
        if (_instrumentName != AudioEngine.CurrentInstrument)
            System.Threading.Tasks.Task.Run(() => AudioEngine.SetInstrument(_instrumentName));
        InstrumentBtn.Content = $"{Lang.S("instrument")}: {Lang.Instrument(_instrumentName)}";
        InstrumentPill.Content = $"{Lang.S("instrument")}:{Lang.Instrument(_instrumentName)}";
        RefreshPitchPill();
        CaveBtn.Content = $"{Lang.S("cave")}: {Lang.S(AudioEngine.Cave ? "on" : "off")}";
        CaveIcon.Foreground = AudioEngine.Cave ? Brushes.DeepSkyBlue : (Brush)Application.Current.Resources["SubTextFg"];
        RefreshPerformIcon();
        SetSpeed(_speed);   // 更新倍速药丸 + 播放器
        _prefsLoading = false;
    }

    void SavePrefs()
    {
        if (_prefsLoading) return;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefsFile)!);
            System.IO.File.WriteAllLines(PrefsFile, new[]
            {
                $"instrument={_instrumentName}",
                $"cave={(AudioEngine.Cave ? 1 : 0)}",
                $"preview={(_previewMode ? 1 : 0)}",
                $"speed={_speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}",
            });
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
        if (!_playing && !_previewing) return;
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
    bool _practiceOpen, _pInit, _practicePaused;
    System.Windows.Media.Effects.Effect? _rootShadow;
    readonly ScaleTransform _pCardScale = new(1, 1);
    readonly TranslateTransform _pCardTrans = new(0, 0);

    // 跟弹: 当前曲按时间戳分组成"步"(和弦=同时刻多键); 高亮当前步(主题色)+下一步(淡色), 按对整步才前进
    List<int[]> _practiceSteps = new();
    List<double> _practiceStepMs = new();   // 每步时间戳(供进度条按整曲位置显示)
    double _practiceTotalMs;
    int _practiceStep;
    SongInfo? _practiceSong;                 // 当前练习的曲目(底部条显示 + 展示停止后恢复)
    readonly HashSet<int> _practiceHeld = new();   // 当前物理按住的键(判和弦是否同时按住)

    // 读谱模式: 大键盘上方谱面缩略图墙, 每格=一步和弦的迷你 5×3 键位图, 左右翻页, 当前步高亮
    const int SheetPerPage = 32;             // 8 列 × 4 行
    bool _readMode;
    bool _metroOn;                           // 打点模式(节拍器)开关
    int _metroBpm = 120;                     // 节拍器速度 BPM
    int _sheetPage;
    readonly Border[] _sheetCells = new Border[SheetPerPage];       // 每格外框(当前步描边高亮)
    readonly Border[][] _sheetDots = new Border[SheetPerPage][];    // 每格内 15 键位

    void Practice_Click(object sender, RoutedEventArgs e) => ShowPractice(true);
    void PracticeBack_Click(object sender, RoutedEventArgs e) => ShowPractice(false);

    Rect RectIn(FrameworkElement el) => el.TransformToVisual(RootScale).TransformBounds(new Rect(el.RenderSize));

    void ShowPractice(bool on)
    {
        if (_practiceOpen == on) return;
        _practiceOpen = on;
        if (on) { PracticePanel.Visibility = Visibility.Visible; PracticePanel.UpdateLayout(); StartPractice(); if (_metroOn) AudioEngine.MetronomeOn(_metroBpm); }
        else
        {
            AudioEngine.MetronomeOff();
            if (GamePracticeSwitch.IsChecked == true) GamePracticeSwitch.IsChecked = false;
            else StopGamePractice();
            if (!_playing && !_previewing) { SetIdlePlayer(); UpdateProgUi(); }
        }   // 退出练习: 停节拍器/游戏练习; 无播放则底部条回空闲

        var small = RectIn(PianoGrid);
        var bigGrid = RectIn(PracticePianoGrid);   // 用大键盘本体(不含卡片内边距)算缩放, 末帧键盘本体才与小键盘等大
        var card = RectIn(PracticeCard);
        // s/dx/dy 取绝对目标(除去当前缩放/位移), 这样即便从读谱静止态(已缩小下移)收回也能精确落到小键盘
        double trueBigW = _pCardScale.ScaleX > 1e-3 ? bigGrid.Width / _pCardScale.ScaleX : bigGrid.Width;
        double s = trueBigW > 0 ? small.Width / trueBigW : 0.5;                     // 缩到大键盘本体=小键盘等宽
        double dx = (small.Left + small.Width / 2) - (card.Left + card.Width / 2) + _pCardTrans.X;  // 卡片中心对齐小键盘中心
        double dy = (small.Top + small.Height / 2) - (card.Top + card.Height / 2) + _pCardTrans.Y;

        // 首次: 无动画在持有, 直接把卡片落到"小键盘"起点(之后每次开/关都从当前值续演)
        if (on && !_pInit)
        {
            _pCardScale.ScaleX = _pCardScale.ScaleY = s;
            _pCardTrans.X = dx; _pCardTrans.Y = dy;
            PracticeBg.Opacity = 0; PracticeBackBtn.Opacity = 0;
            _pInit = true;
        }

        // 展开静止态: 读谱模式缩小并落到墙下方(ReadRest), 否则居中全尺寸
        var (restScale, restTransY) = ReadRest();
        _pSmallScale = s;                          // 供背景跟随钩子换算展开进度(0=小键盘, 1=展开态)
        _pOpenScale = _readMode ? restScale : 1;   // 展开态缩放: 读谱下键盘只到 restScale, 背景进度按它算才能遮满

        // 打断适配: 按当前值到目标的剩余距离缩放时长, 反转一个快完成的动画只花很短时间, 不再整段橡皮筋
        double cur = _pCardScale.ScaleX, target = on ? restScale : s;
        double full = Math.Max(Math.Abs(target - s), 1e-3);
        double frac = Math.Clamp(Math.Abs(target - cur) / full, 0.15, 1);
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

        // 收尾用"代次令牌"守护: 每次转场 +1, 只有最新一次的 Completed/兜底定时器能收尾 → 反复开关不串味
        // 关键: 不只靠动画 Completed(被替换/被 resize 清空动画时不触发), 再挂一个 dur+120ms 的兜底定时器, 保证面板一定收起, 不会卡在半开态
        int gen = ++_transitionGen;
        _transitioning = true;
        var sx = new DoubleAnimation(target, dur) { EasingFunction = ease };
        sx.Completed += (_, __) => FinishTransition(gen);
        _pCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);

        Anim(_pCardScale, ScaleTransform.ScaleYProperty, target, dur, ease);
        Anim(_pCardTrans, TranslateTransform.XProperty, on ? 0 : dx, dur, ease);
        Anim(_pCardTrans, TranslateTransform.YProperty, on ? restTransY : dy, dur, ease);
        Anim(PracticeBackBtn, UIElement.OpacityProperty, on ? 1 : 0, dur, ease);
        Anim(ReadModeBar, UIElement.OpacityProperty, on ? 1 : 0, dur, ease);

        _pFinishTimer?.Stop();
        _pFinishTimer = new DispatcherTimer { Interval = dur + TimeSpan.FromMilliseconds(120) };
        _pFinishTimer.Tick += (_, __) => { _pFinishTimer!.Stop(); FinishTransition(gen); };
        _pFinishTimer.Start();
    }

    int _transitionGen;
    bool _transitioning;
    DispatcherTimer? _pFinishTimer;

    // 转场收尾(幂等 + 代次守护): 撤渲染钩子/锁背景到终值/清缓存/装回阴影; 关闭态则真正收起面板
    void FinishTransition(int gen)
    {
        if (gen != _transitionGen) return;   // 已被更晚的转场取代 → 由那次负责收尾
        if (_pRenderingHooked) { CompositionTarget.Rendering -= PracticeBgFollow; _pRenderingHooked = false; }
        PracticeBgFollow(null, EventArgs.Empty);
        PracticeCard.CacheMode = null;
        WindowRoot.Effect = _rootShadow;
        _transitioning = false;
        if (!_practiceOpen) PracticePanel.Visibility = Visibility.Collapsed;
    }

    double _pSmallScale = 0.5;
    double _pOpenScale = 1;   // 键盘"完全展开"时的缩放: 普通=1, 读谱=restScale(明显<1) → 背景进度必须按这个算, 否则读谱下背景永远遮不满
    bool _pRenderingHooked;
    // 背景不透明度 = 卡片展开进度(小键盘→展开态)的函数(走到 35% 即全遮); 纯当前缩放的函数, 故任意打断都一致
    void PracticeBgFollow(object? sender, EventArgs e)
    {
        double span = _pOpenScale - _pSmallScale;
        double t = span > 1e-6 ? (_pCardScale.ScaleX - _pSmallScale) / span : 1;
        PracticeBg.Opacity = Math.Clamp(t / 0.35, 0, 1);
    }

    // 只给终点(To), 省略 From → 从属性当前值(含正在播放的动画值)接着演, 天然可打断; 帧率交回显示器 vsync(更稳)
    static void Anim(IAnimatable t, DependencyProperty p, double to, TimeSpan dur, IEasingFunction ease)
        => t.BeginAnimation(p, new DoubleAnimation(to, dur) { EasingFunction = ease });

    // ---- 跟弹交互: 高亮下一个/下下个要按的键, 按对整步才前进 ----
    void StartPractice()
    {
        _practiceStep = 0; _practiceHeld.Clear(); _practicePaused = false;
        var song = _nowPlaying ?? Selected;         // 优先播放条上那首(与播放键一致), 其次库里选中
        _practiceSong = song;
        if (song != null) TryLoad(song);
        if (_notes.Count > 0) BuildPracticeSteps();
        else { _practiceSteps = new(); _practiceStepMs = new(); _practiceTotalMs = 0; }
        _practiceStep = NextNoteStep(0);            // 光标落到第一个有音符的格(跳过开头休止)
        RenderPracticeHints();
        if (song != null && _practiceSteps.Count > 0) UpdateNowPlaying(song);   // 底部条显示练习曲信息 + 进度条
        else ProgBar.Visibility = Visibility.Collapsed;
        UpdateProgUi();
    }

    // 把 _notes 铺成"按拍网格": 每格一个细分单位(空拍也占一格), 有音符填键否则空; 忠实反映节奏含休止
    void BuildPracticeSteps()
    {
        _practiceSteps = new(); _practiceStepMs = new();
        var ns = _notes.Where(n => n.key is >= 0 and < 15).OrderBy(n => n.ms).ToList();
        _practiceTotalMs = ns.Count > 0 ? ns[^1].ms : 0;
        if (ns.Count == 0) return;
        double mpb = _curMsPerBeat > 1 ? _curMsPerBeat : 500;

        // ① 同刻音符(±20ms)合成一格(拍位 + 键)
        var cells = new List<(double beat, List<int> keys)>();
        int i = 0;
        while (i < ns.Count)
        {
            double t0 = ns[i].ms;
            var keys = new List<int>();
            while (i < ns.Count && ns[i].ms - t0 <= 20) { if (!keys.Contains(ns[i].key)) keys.Add(ns[i].key); i++; }
            cells.Add((t0 / mpb, keys));
        }

        // ② 检测细分: 让所有音符拍位近似落在 k/subdiv 的最小可行细分(1拍/2/三连/16分/6/8)
        int subdiv = 4;
        foreach (var sd in new[] { 1, 2, 3, 4, 6, 8 })
        {
            bool ok = true;
            foreach (var (beat, _) in cells) { double r = beat * sd; if (Math.Abs(r - Math.Round(r)) > 0.18) { ok = false; break; } }
            if (ok) { subdiv = sd; break; }
        }
        double unitMs = mpb / subdiv;

        // ③ 时间网格: 首音符为 slot0, 每格一单位; 空格=休止(空键数组)
        double beat0 = cells[0].beat;
        int maxSlot = Math.Clamp((int)Math.Round((cells[^1].beat - beat0) * subdiv), 0, 20000);
        var slot = new List<int>?[maxSlot + 1];
        foreach (var (beat, keys) in cells)
        {
            int s = Math.Clamp((int)Math.Round((beat - beat0) * subdiv), 0, maxSlot);
            var list = slot[s] ??= new();
            foreach (var k in keys) if (!list.Contains(k)) list.Add(k);
        }
        for (int s = 0; s <= maxSlot; s++)
        {
            _practiceSteps.Add(slot[s]?.ToArray() ?? Array.Empty<int>());
            _practiceStepMs.Add(cells[0].beat * mpb + s * unitMs);
        }
    }

    // 下一个/上一个"有音符"的格(跳过休止); 越界返回 Count / -1 交调用方处理
    int NextNoteStep(int from) { for (int i = Math.Max(0, from); i < _practiceSteps.Count; i++) if (_practiceSteps[i].Length > 0) return i; return _practiceSteps.Count; }
    int PrevNoteStep(int from) { for (int i = Math.Min(from, _practiceSteps.Count - 1); i >= 0; i--) if (_practiceSteps[i].Length > 0) return i; return 0; }

    // 当前步→主题色, 下一步→主题色淡化版, 其余→常态
    void RenderPracticeHints()
    {
        var accent = ((SolidColorBrush)Application.Current.Resources["Accent"]).Color;
        var faded = Lerp(Theme.KeySquare, accent, 0.45);
        // 键盘高亮的步: 练习展示中领先一个音符(显示"下一个要弹的", 好提前准备); 跟弹时=当前步。网格高亮仍是 _practiceStep(当前格, 不提前)
        bool playing = _playing || _previewing;
        int kbStep = playing ? NextNoteStep(_practiceStep + 1) : _practiceStep;
        var cur = kbStep < _practiceSteps.Count ? _practiceSteps[kbStep] : Array.Empty<int>();
        int nStep = NextNoteStep(kbStep + 1);
        var nxt = nStep < _practiceSteps.Count ? _practiceSteps[nStep] : Array.Empty<int>();   // 键盘下一格(再下一个音符)
        for (int i = 0; i < 15; i++)
        {
            var c = Theme.KeySquare;
            if (Array.IndexOf(nxt, i) >= 0) c = faded;
            bool isCur = Array.IndexOf(cur, i) >= 0;
            if (isCur) c = accent;   // 当前步优先于下一步
            var b = (SolidColorBrush)_pBtn[i].Background;
            b.BeginAnimation(SolidColorBrush.ColorProperty, null);   // 清掉残留的按键闪动, 直接落基色
            b.Color = c;
            // 当前步(Accent 蓝底): 字母+菱形转白, 否则回主题色(浅色深字在蓝底上看不清)
            ((SolidColorBrush)_pLabels[i].Foreground).Color = isCur ? Colors.White : Theme.KeyLetter;
            ((SolidColorBrush)_pDiamond[i].BorderBrush).Color = isCur ? Colors.White : Theme.KeyDiamond;
        }
        if (_readMode) SyncSheetToStep();   // 读谱: 当前步高亮/自动翻页跟着走
    }

    // 跟弹时按下某键: 当前步全部键"同时按住"才算过(和弦不能一个个先后按); 按错不动
    void PracticePress(int key)
    {
        if (_practicePaused) return;
        if (_practiceStep >= _practiceSteps.Count) return;
        var cur = _practiceSteps[_practiceStep];
        if (Array.IndexOf(cur, key) < 0) return;          // 不是当前步该按的键
        foreach (var k in cur) if (!_practiceHeld.Contains(k)) return;   // 整步的键要此刻都在按住
        _practiceStep = NextNoteStep(_practiceStep + 1);   // 跳过休止格, 落到下一个有音符的格
        if (_practiceStep >= _practiceSteps.Count)         // 全曲弹完 → 回到开头(首个音符格)
        {
            _practiceStep = NextNoteStep(0);
            ShowToast(Lang.S("t.practiceDone"));
        }
        RenderPracticeHints();
        UpdateProgUi();   // 底部进度条跟着步进
    }

    void PracticeSeek(int deltaSteps)
    {
        if (_practiceSteps.Count == 0) return;
        // ←→ 按"有音符的格"前后跳(跳过休止, 保证能跟弹)
        _practiceStep = deltaSteps > 0 ? NextNoteStep(_practiceStep + 1) : PrevNoteStep(_practiceStep - 1);
        _practiceStep = Math.Clamp(_practiceStep, 0, _practiceSteps.Count - 1);
        _practiceHeld.Clear();
        if ((_playing || _previewing) && _practiceStep < _practiceStepMs.Count)
            _player.Seek(_practiceStepMs[_practiceStep]);
        RenderPracticeHints();
        UpdateProgUi();
    }

    void PracticeSeekMs(double deltaMs)
    {
        if (_practiceSteps.Count == 0) return;
        double current = _practiceStep < _practiceStepMs.Count ? _practiceStepMs[_practiceStep] : 0;
        int step = NearestStep(Math.Clamp(current + deltaMs, 0, _practiceTotalMs));
        if (_practiceSteps[step].Length == 0)
            step = deltaMs >= 0 ? NextNoteStep(step) : PrevNoteStep(step);
        _practiceStep = Math.Clamp(step, 0, _practiceSteps.Count - 1);
        _practiceHeld.Clear();
        RenderPracticeHints();
        UpdateProgUi();
    }

    void TogglePracticePaused()
    {
        _practicePaused = !_practicePaused;
        _practiceHeld.Clear();
        StatusText.Text = _practicePaused ? "状态: ⏸ 练习已暂停" : "状态: 练习已继续";
        ShowToast(_practicePaused ? "练习已暂停" : "练习已继续");
    }

    // 按整曲时间找最近的步(供进度条按时间比例跳转, 使落点与高亮对齐)
    int NearestStep(double ms)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < _practiceStepMs.Count; i++)
        {
            double d = Math.Abs(_practiceStepMs[i] - ms);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    // 练习展示中: 高亮=当前正在响的那一格(按拍网格下, 取最后一个时间戳已到的格; 空拍则停在空拍上)
    void SyncPracticeHighlightToPlayback()
    {
        if (_practiceSteps.Count == 0) return;
        double pos = _player.PositionMs;
        int s = 0;
        while (s + 1 < _practiceStepMs.Count && _practiceStepMs[s + 1] <= pos + 1e-6) s++;   // 下一格时间已到就前进 → s=当前格
        if (s != _practiceStep) { _practiceStep = s; RenderPracticeHints(); }
    }

    // ---- 读谱模式: 谱面缩略图墙 ----
    // 预建 32 个空格(每格 5×3 键位), 翻页/推进只改颜色与尺寸, 不重建控件
    void BuildSheetWall()
    {
        for (int c = 0; c < SheetPerPage; c++)
        {
            var mini = new System.Windows.Controls.Primitives.UniformGrid { Rows = 3, Columns = 5 };
            var dots = new Border[15];
            for (int k = 0; k < 15; k++)
            {
                var dot = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush()
                };
                dots[k] = dot;
                mini.Children.Add(dot);
            }
            _sheetDots[c] = dots;
            var cell = new Border
            {
                Width = 128, Height = 86, Margin = new Thickness(5), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8), BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand, Child = mini
            };
            cell.SetResourceReference(Border.BackgroundProperty, "PanelBg");   // 动态资源: 跟随主题深/浅
            int slot = c;
            cell.MouseLeftButtonUp += (_, __) => SheetCellClick(slot);
            _sheetCells[c] = cell;
            SheetWall.Children.Add(cell);
        }
    }

    void ReadMode_Toggle(object sender, RoutedEventArgs e)
    {
        _readMode = ReadModeSwitch.IsChecked == true;
        if (_readMode && _sheetCells[0] == null) BuildSheetWall();
        SheetArea.Visibility = _readMode ? Visibility.Visible : Visibility.Collapsed;
        if (_readMode) { _sheetPage = _practiceStep / SheetPerPage; RenderSheet(); }
        ApplyReadLayout(animate: true);
    }

    // 大键盘静止态: 读谱→缩小并落到墙与底部之间(上下留白); 关→回居中全尺寸
    // animate: 开关时平滑过渡; resize 时即时跟手(停动画直接落值)
    void ApplyReadLayout(bool animate)
    {
        PracticePanel.UpdateLayout();   // 保证 ReadModeBar/PracticeCard 尺寸已算好, ReadRest 才拿得到正确宽高
        var (scale, transY) = ReadRest();
        _pOpenScale = scale;                       // 展开态缩放基准更新, 供之后关练习的背景淡出换算
        if (_practiceOpen) PracticeBg.Opacity = 1;   // 读谱切换/resize 只是键盘缩放, 面板始终展开 → 背景保持全遮(不淡出)
        if (animate)
        {
            var dur = TimeSpan.FromMilliseconds(300);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Anim(_pCardScale, ScaleTransform.ScaleXProperty, scale, dur, ease);
            Anim(_pCardScale, ScaleTransform.ScaleYProperty, scale, dur, ease);
            Anim(_pCardTrans, TranslateTransform.YProperty, transY, dur, ease);
        }
        else
        {
            _pCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _pCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _pCardTrans.BeginAnimation(TranslateTransform.YProperty, null);
            _pCardScale.ScaleX = _pCardScale.ScaleY = scale;
            _pCardTrans.Y = transY;
        }
    }

    // 读谱模式下大键盘的静止缩放/纵移: 谱面墙限高 42% 面板后, 键盘缩到墙下方可用高, 居中于 [墙底+gap, 面板底-gap]
    (double scale, double transY) ReadRest()
    {
        double panelH = PracticePanel.ActualHeight, panelW = PracticePanel.ActualWidth;
        double cardH = PracticeCard.ActualHeight, cardW = PracticeCard.ActualWidth;
        if (panelH <= 0 || cardH <= 0 || cardW <= 0) return (1, 0);
        const double gap = 40;

        double topReserve = 0;   // 顶部留给谱面墙(读谱)的高度; 非读谱为 0
        if (_readMode)
        {
            SheetWallBox.MaxHeight = panelH * 0.42;   // 谱面墙最多占面板 42% 高, Viewbox 随之等比缩
            SheetArea.UpdateLayout();
            topReserve = SheetArea.Margin.Top + SheetArea.ActualHeight;   // 读谱区(墙与翻页列取高者)底边
        }
        // 键盘居中于顶部保留区下方; 缩放同时受"下方可用高"和"两侧要避开右上控件栏"约束, 小窗口自动缩小不遮挡
        double availH = panelH - topReserve - 2 * gap;
        double ctrlW = ReadModeBar.ActualWidth + 24;                 // 右上控件栏总宽 + 右边距
        double availW = panelW - 2 * (ctrlW + gap);                  // 键盘居中 → 两侧对称留出控件宽
        double s = Math.Clamp(Math.Min(Math.Min(availH / cardH, availW / cardW), 1), 0.4, 1);
        return (s, topReserve / 2);   // 纵移把卡片中心从面板正中移到保留区下方区域正中(read-off topReserve=0 → 居中)
    }

    // 打点模式(节拍器): 独立咔哒声, 只在练习界面内响
    void Metro_Toggle(object sender, RoutedEventArgs e)
    {
        _metroOn = MetroSwitch.IsChecked == true;
        if (_metroOn && _practiceOpen) AudioEngine.MetronomeOn(_metroBpm);
        else AudioEngine.MetronomeOff();
    }

    // 游戏练习: 窗口置顶 + 低级键盘钩子观察全局按键；不吞键，游戏仍会正常收到输入
    bool _gamePracticeOn;
    IntPtr _keyboardHook;
    readonly LowLevelKeyboardProc _keyboardProc;
    readonly HashSet<uint> _gamePracticeDown = new();

    void GamePractice_Toggle(object sender, RoutedEventArgs e)
    {
        bool on = GamePracticeSwitch.IsChecked == true;
        if (on)
        {
            if (!StartGamePractice())
            {
                ShowToast("无法启动全局按键监听");
                GamePracticeSwitch.IsChecked = false;
            }
        }
        else StopGamePractice();
    }

    bool StartGamePractice()
    {
        if (_gamePracticeOn) return true;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        IntPtr moduleHandle = module == null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
        if (_keyboardHook == IntPtr.Zero) return false;
        _gamePracticeOn = true;
        Topmost = true;
        return true;
    }

    void StopGamePractice()
    {
        _gamePracticeOn = false;
        Topmost = false;
        _gamePracticeDown.Clear();
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        AudioEngine.StopAll();
    }

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _gamePracticeOn && _practiceOpen)
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((data.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) == 0)
            {
                int idx = Array.IndexOf(_player.Vk, (ushort)data.vkCode);
                if (idx >= 0)
                {
                    int message = wParam.ToInt32();
                    if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (_gamePracticeDown.Add(data.vkCode))
                            Dispatcher.BeginInvoke(() =>
                            {
                                if (!_gamePracticeOn || !_practiceOpen) return;
                                AudioEngine.NoteOn(idx);
                                FlashKey(idx);
                                _practiceHeld.Add(idx);
                                PracticePress(idx);
                            });
                    }
                    else if (message is WM_KEYUP or WM_SYSKEYUP)
                    {
                        _gamePracticeDown.Remove(data.vkCode);
                        Dispatcher.BeginInvoke(() =>
                        {
                            AudioEngine.NoteOff(idx);
                            _practiceHeld.Remove(idx);
                        });
                    }
                }
            }
        }
        return CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLKHF_LOWER_IL_INJECTED = 0x02, LLKHF_INJECTED = 0x10;
    delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern IntPtr GetModuleHandle(string? moduleName);

    // 点击胶囊输入 BPM(30–300)
    void MetroBpm_Click(object sender, RoutedEventArgs e)
    {
        var s = InputBox.Ask(this, Lang.S("metro.title"), Lang.S("practice.metro"), Lang.S("metro.prompt"));
        if (s == null || !int.TryParse(s.Trim(), out int bpm)) return;
        _metroBpm = Math.Clamp(bpm, 30, 300);
        MetroBpmPill.Content = _metroBpm.ToString();
        AudioEngine.MetronomeBpm(_metroBpm);   // 在响则立即变速
    }

    void SheetPrev_Click(object sender, RoutedEventArgs e) { if (_sheetPage > 0) { _sheetPage--; RenderSheet(); } }
    void SheetNext_Click(object sender, RoutedEventArgs e) { if ((_sheetPage + 1) * SheetPerPage < _practiceSteps.Count) { _sheetPage++; RenderSheet(); } }

    // 点某格 → 跳到那一步(空拍格吸附到其后第一个有音符的格, 保证能跟弹)
    void SheetCellClick(int slot)
    {
        int step = _sheetPage * SheetPerPage + slot;
        if (step >= _practiceSteps.Count) return;
        if (_practiceSteps[step].Length == 0) step = NextNoteStep(step);
        if (step >= _practiceSteps.Count) return;
        _practiceStep = step; _practiceHeld.Clear();
        RenderPracticeHints();   // 内部会回刷谱面高亮
        UpdateProgUi();
    }

    // 当前步变了 → 若不在本页则翻到它所在页, 再刷新
    void SyncSheetToStep()
    {
        int page = _practiceSteps.Count == 0 ? 0 : Math.Min(_practiceStep, _practiceSteps.Count - 1) / SheetPerPage;
        if (page != _sheetPage) _sheetPage = page;
        RenderSheet();
    }

    // 按当前页填色: 该步键=蓝方块(当前步→白+外框描边), 其余=灰点; 越界格空置
    void RenderSheet()
    {
        if (!_readMode || _sheetCells[0] == null) return;
        var accent = ((SolidColorBrush)Application.Current.Resources["Accent"]).Color;
        var cellBd = (Brush)Application.Current.Resources["ListBorder"];
        bool dark = Theme.Dark;
        var dotOff = dark ? Color.FromArgb(110, 128, 128, 128) : Color.FromArgb(140, 120, 120, 130);   // 未按键位: 深浅主题各取可见灰
        var curColor = dark ? Colors.White : Color.FromRgb(0x1c, 0x1c, 0x28);                          // 当前步高亮: 深色→白, 浅色→近黑(浅底上才看得见)
        int start = _sheetPage * SheetPerPage;
        int total = _practiceSteps.Count;
        for (int c = 0; c < SheetPerPage; c++)
        {
            int step = start + c;
            var cell = _sheetCells[c];
            if (step >= total)                               // 越界: 空框
            {
                cell.Opacity = 0.35; cell.BorderBrush = cellBd; cell.BorderThickness = new Thickness(1);
                foreach (var d in _sheetDots[c]) SetDot(d, dotOff, false);
                continue;
            }
            var keys = _practiceSteps[step];
            bool isCur = step == _practiceStep;
            cell.Opacity = keys.Length == 0 && !isCur ? 0.5 : 1;   // 空拍(休止)格更淡, 让有音符的格更突出; 当前格保持清晰
            cell.BorderBrush = isCur ? new SolidColorBrush(accent) : cellBd;
            cell.BorderThickness = new Thickness(isCur ? 2 : 1);
            var onColor = isCur ? curColor : accent;    // 当前步高亮(主题相关) / 其余步=Accent 蓝
            for (int k = 0; k < 15; k++)
            {
                bool on = Array.IndexOf(keys, k) >= 0;
                SetDot(_sheetDots[c][k], on ? onColor : dotOff, on);
            }
        }
        int totalPages = Math.Max(1, (total + SheetPerPage - 1) / SheetPerPage);
        SheetRangeL.Text = (_sheetPage + 1).ToString();   // 当前页
        SheetRangeR.Text = totalPages.ToString();          // 总页数
        SheetPrevBtn.IsEnabled = _sheetPage > 0;
        SheetNextBtn.IsEnabled = (_sheetPage + 1) * SheetPerPage < total;
    }

    // 键位: 按下=大圆角方块, 未按=小圆点
    static void SetDot(Border d, Color color, bool on)
    {
        d.Width = d.Height = on ? 16 : 5;
        d.CornerRadius = new CornerRadius(on ? 4 : 2.5);
        ((SolidColorBrush)d.Background).Color = color;
    }

    static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

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
        OpenRowMenu(cm, e.OriginalSource);   // 走统一开菜单(设 PlacementTarget → 解析到窗口的圆角深色菜单样式)
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

    // ---- 全局热键 (光遇里也能控制): F1从头播放/停止 F2暂停/继续 F3/F4变速 F5/F6切歌 ←/→跳转2s ----
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int HK_START = 1, HK_PAUSE = 2, HK_SLOW = 3, HK_FAST = 4,
              HK_PREV = 5, HK_NEXT = 6, HK_BACK = 7, HK_FWD = 8;
    IntPtr _hwnd;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        CenterOnScreen();   // 分层窗口(AllowsTransparency)+高DPI 下 CenterScreen 会算偏, 手动按工作区居中
        _hwnd = new WindowInteropHelper(this).Handle;
        RegisterHotKey(_hwnd, HK_START, 0, 0x70);   // F1
        RegisterHotKey(_hwnd, HK_PAUSE, 0, 0x71);   // F2
        RegisterHotKey(_hwnd, HK_SLOW, 0, 0x72);    // F3 减速
        RegisterHotKey(_hwnd, HK_FAST, 0, 0x73);    // F4 加速
        RegisterHotKey(_hwnd, HK_PREV, 0, 0x74);    // F5 上一首
        RegisterHotKey(_hwnd, HK_NEXT, 0, 0x75);    // F6 下一首
        RegisterHotKey(_hwnd, HK_BACK, 0, 0x25);    // ← 回退 2s
        RegisterHotKey(_hwnd, HK_FWD, 0, 0x27);     // → 前进 2s
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    // 按主屏工作区把窗口摆正中(WorkArea 与 Left/Top/Width/Height 同为 DIP, DPI 无关)
    void CenterOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Top + (wa.Height - Height) / 2;
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO) { FixMaxSize(h, lp); return IntPtr.Zero; }   // 无边框窗口最大化: 限到工作区(不遮任务栏/不溢出)
        if (msg == WM_HOTKEY)
        {
            switch (wp.ToInt32())
            {
                case HK_START: if (_practiceOpen) RestartPractice(); else HotkeyStartStop(); handled = true; break;
                case HK_PAUSE:
                    if (_practiceOpen && !_playing && !_previewing) TogglePracticePaused();
                    else Pause_Click(this, new RoutedEventArgs());
                    handled = true;
                    break;
                case HK_SLOW: AdjustSpeed(-0.1); handled = true; break;
                case HK_FAST: AdjustSpeed(+0.1); handled = true; break;
                case HK_PREV: StepSong(-1); handled = true; break;
                case HK_NEXT: StepSong(+1); handled = true; break;
                case HK_BACK:
                    if (_practiceOpen) PracticeSeek(-1); else SeekRelative(-2000);
                    handled = true;
                    break;
                case HK_FWD:
                    if (_practiceOpen) PracticeSeek(+1); else SeekRelative(+2000);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    // 无边框窗口最大化会盖住任务栏并溢出屏幕: 把最大尺寸/位置钳到所在显示器的工作区
    static void FixMaxSize(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
        const int MONITOR_DEFAULTTONEAREST = 2;
        IntPtr mon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero)
        {
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(mon, ref mi))
            {
                mmi.ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left;
                mmi.ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
                mmi.ptMaxSize.x = mi.rcWork.right - mi.rcWork.left;
                mmi.ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top;
                System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
#pragma warning disable CS0649   // 互操作结构: 部分字段仅为布局占位, 不直接赋值
    struct POINTL { public int x, y; }
    struct RECTL { public int left, top, right, bottom; }
    struct MINMAXINFO { public POINTL ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    struct MONITORINFO { public int cbSize; public RECTL rcMonitor, rcWork; public int dwFlags; }
#pragma warning restore CS0649

    void Max_Click(object sender, RoutedEventArgs e) => ToggleMax();
    void ToggleMax() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // 调速: 改滑块值(ValueChanged 会同步 SpeedLabel + SkyPlayer.SpeedFactor)
    void AdjustSpeed(double delta)
    {
        SetSpeed(_speed + delta);
        StatusText.Text = $"状态: 速度 {_speed:0.0}x";
    }

    void HotkeyStartStop()
    {
        if (_playing || _previewing) { StopPlaying(); return; }
        var target = _playCurrent ?? _nowPlaying ?? _playlist.FirstOrDefault();
        if (target == null) { ShowToast(Lang.S("t.emptyQueue")); return; }
        _pendingResumeMs = 0;
        _resumeSong = null;
        PlayPlaylistItem(target, skipCountdown: true);
    }

    void RestartPractice()
    {
        if (_practiceSteps.Count == 0) return;
        _practicePaused = false;
        _practiceStep = NextNoteStep(0);
        _practiceHeld.Clear();
        if (_playing || _previewing) _player.Seek(0);
        RenderPracticeHints();
        RenderProg(0, 0, _practiceTotalMs > 0 ? _practiceTotalMs : _player.TotalMs);
        StatusText.Text = "状态: 练习已回到开头";
    }

    // 播放速度: 胶囊显示 + 立即变速
    void SetSpeed(double v)
    {
        _speed = Math.Clamp(v, 0.5, 2.0);
        _player.RandomSpeed = false;
        SpeedPill.Content = $"{_speed:0.0}x";
        _player.SpeedFactor = _speed;
        SavePrefs();
    }

    void Speed_Click(object sender, RoutedEventArgs e)
    {
        SpeedList.Items.Clear();
        SpeedList.Items.Add(new ListBoxItem { Content = Lang.S("speed.random"), Tag = "random" });
        foreach (double speed in new[] { 2.0, 1.75, 1.5, 1.25, 1.0, 0.75, 0.5 })
            SpeedList.Items.Add(new ListBoxItem { Content = $"{speed:0.0}x", Tag = speed });
        object selected = _player.RandomSpeed ? "random" : _speed;
        OpenOptionPopup(SpeedPopup, SpeedList, (sender as UIElement) ?? SpeedPill, selected);
    }

    void SpeedList_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ClickedOption(SpeedList, e) is not { } item) return;
        if (Equals(item.Tag, "random"))
        {
            _player.RandomSpeed = true;
            SpeedPill.Content = Lang.S("speed.random");
        }
        else if (item.Tag is double speed) SetSpeed(speed);
        else return;
        SpeedPopup.IsOpen = false;
        e.Handled = true;
    }

    static ListBoxItem? ClickedOption(ListBox list, System.Windows.Input.MouseButtonEventArgs e) =>
        e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(list, source) as ListBoxItem
            : null;

    void OpenOptionPopup(System.Windows.Controls.Primitives.Popup popup, ListBox list, UIElement target, object selectedTag)
    {
        var selected = list.Items.OfType<ListBoxItem>().FirstOrDefault(item => Equals(item.Tag, selectedTag));
        list.SelectedItem = selected;
        popup.PlacementTarget = target;
        popup.IsOpen = true;
        if (selected != null)
            Dispatcher.BeginInvoke(() => list.ScrollIntoView(selected), System.Windows.Threading.DispatcherPriority.Loaded);
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
        StopGamePractice();
        if (_hwnd != IntPtr.Zero)
            foreach (int id in new[] { HK_START, HK_PAUSE, HK_SLOW, HK_FAST, HK_PREV, HK_NEXT, HK_BACK, HK_FWD })
                UnregisterHotKey(_hwnd, id);
        _player.Stop();
        base.OnClosed(e);
    }
}
