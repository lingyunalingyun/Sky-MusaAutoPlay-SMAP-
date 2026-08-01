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

    int _remapIndex = -1;   // >=0 时表示正在等待为该光遇键重绑物理键

    public MainWindow()
    {
        Lang.Load();
        Theme.Apply(Theme.LoadDark());   // 资源就位后 InitializeComponent 里的 DynamicResource 才解析得到
        InitializeComponent();
        _player.Vk = KeyConfig.Load();
        BuildPianoGrid();
        SpeedSlider.ValueChanged += (_, e) =>
        {
            SpeedLabel.Text = $"{e.NewValue:0.0}x";
            _player.SpeedFactor = e.NewValue;   // 播放中拖动立即变速
        };
        ProgressSlider.ValueChanged += (_, e) =>
        {
            if (_settingProgress) return;                    // 定时器回写, 非用户操作
            if (!_playing && !_previewing) { SetProgress(0); return; }   // 没在放, 无处可跳
            _player.Seek(e.NewValue * _player.TotalMs);
        };

        SortCombo.SelectionChanged += (_, __) => ApplyFilter();
        FilterCombo.SelectionChanged += (_, __) => ApplyFilter();
        SearchBox.TextChanged += (_, __) => ApplyFilter();
        SongList.SelectionChanged += (_, __) => OnSongSelected();
        StyleSongList();

        CloudApi.LoadAuth();
        UpdateLoginButton();
        RefreshLibrary();
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
        LibHeader.Text = Lang.S("lib.header");
        KeysHeader.Text = Lang.S("keys.header");
        KeysHint.Text = Lang.S("keys.hint");
        KeyEditBtn.Content = _editingKeys ? Lang.S("keys.save") : Lang.S("keys.edit");
        CaveBtn.Content = $"{Lang.S("cave")}: {Lang.S(AudioEngine.Cave ? "on" : "off")}";
        InstrumentBtn.Content = $"{Lang.S("instrument")}: {_instrumentName}";
        ThemeBtn.Content = $"{Lang.S("theme")}: {Lang.S(Theme.Dark ? "theme.dark" : "theme.light")}";
        AboutBtn.Content = Lang.S("about");

        int si = SortCombo.SelectedIndex < 0 ? 0 : SortCombo.SelectedIndex;
        SortCombo.ItemsSource = new[] { Lang.S("sort.az"), Lang.S("sort.za"), Lang.S("sort.fav") };
        SortCombo.SelectedIndex = si;

        RebuildFilterOptions();
        SongList.ContextMenu = BuildSongContextMenu();
        OnSongSelected();   // 刷新曲名/BPM/音符数标签
    }

    // ---- 自定义标题栏窗口控制 ----
    void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
    void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- 选中曲目 → 曲名/BPM/音符数/总时长 ----
    void OnSongSelected()
    {
        if (Selected is { } s)
        {
            FilePathBox.Text = s.Name;
            SongInfoText.Text = $"BPM:{(int)s.Bpm}  {Lang.S("info.notes")}:{s.NoteCount}";
            TotalText.Text = Fmt(s.DurationMs);
        }
        else
        {
            FilePathBox.Text = Lang.S("nosong");
            SongInfoText.Text = $"BPM:--  {Lang.S("info.notes")}:--";
            TotalText.Text = "00:00";
        }
        ElapsedText.Text = "00:00";
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
        foreach (var s in _all) s.Fav = LibraryMeta.IsFav(s.FileName);
        RebuildFilterOptions();
        ApplyFilter();
        StatusText.Text = $"状态: 曲库 {_all.Count} 首  ({SongLibrary.SongsDir})";
    }

    void RebuildFilterOptions()
    {
        int idx = FilterCombo.SelectedIndex < 0 ? 0 : FilterCombo.SelectedIndex;   // 按索引保留(翻译后字串会变)
        var items = new List<string> { Lang.S("filter.all"), Lang.S("filter.fav") };
        foreach (var t in LibraryMeta.AllTags()) items.Add(TagPrefix + t);
        FilterCombo.ItemsSource = items;
        FilterCombo.SelectedIndex = idx < items.Count ? idx : 0;
    }

    void ApplyFilter()
    {
        if (_all.Count == 0 && SongList.ItemsSource == null) { SongList.ItemsSource = _all; return; }
        string q = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
        int fi = FilterCombo.SelectedIndex;   // 0=全部 1=仅收藏 ≥2=标签

        IEnumerable<SongInfo> res = _all;
        if (q.Length > 0) res = res.Where(s => s.Name.ToLowerInvariant().Contains(q));
        if (fi == 1) res = res.Where(s => s.Fav);
        else if (fi >= 2 && FilterCombo.SelectedItem is string f && f.StartsWith(TagPrefix))
        {
            var tag = f[TagPrefix.Length..];
            res = res.Where(s => LibraryMeta.TagsOf(s.FileName).Contains(tag));
        }

        res = SortCombo.SelectedIndex switch
        {
            1 => res.OrderByDescending(s => s.Name, StringComparer.CurrentCulture),
            2 => res.OrderByDescending(s => s.Fav).ThenBy(s => s.Name, StringComparer.CurrentCulture),
            _ => res.OrderBy(s => s.Name, StringComparer.CurrentCulture)
        };
        SongList.ItemsSource = res.ToList();
    }

    SongInfo? Selected => SongList.SelectedItem as SongInfo;

    ContextMenu BuildSongContextMenu()
    {
        var cm = new ContextMenu();

        var fav = new MenuItem { Header = Lang.S("menu.fav") };
        fav.Click += (_, __) =>
        {
            if (Selected is not { } s) return;
            LibraryMeta.ToggleFav(s.FileName);
            s.Fav = LibraryMeta.IsFav(s.FileName);
            ApplyFilter();
            StatusText.Text = $"状态: {(s.Fav ? "已收藏" : "已取消收藏")}「{s.Name}」";
        };

        var addTag = new MenuItem { Header = Lang.S("menu.addtag") };
        addTag.Click += (_, __) =>
        {
            if (Selected is not { } s) return;
            var tag = InputBox.Ask(this, "添加标签", s.Name, "标签名:");
            if (string.IsNullOrWhiteSpace(tag)) return;
            LibraryMeta.AddTag(s.FileName, tag.Trim());
            RebuildFilterOptions();
            StatusText.Text = $"状态: 已为「{s.Name}」加标签 {tag.Trim()}";
        };

        var removeTag = new MenuItem { Header = Lang.S("menu.rmtag") };
        removeTag.Click += (_, __) =>
        {
            if (Selected is not { } s) return;
            var tags = LibraryMeta.TagsOf(s.FileName).ToList();
            if (tags.Count == 0) { MsgBox.Info(this, "此曲目暂无标签"); return; }
            var tag = InputBox.Choose(this, "移除标签", s.Name, "选择要移除的标签:", tags);
            if (tag == null) return;
            LibraryMeta.RemoveTag(s.FileName, tag);
            RebuildFilterOptions();
            ApplyFilter();
            StatusText.Text = $"状态: 已移除标签 {tag}";
        };

        var upload = new MenuItem { Header = Lang.S("menu.upload") };
        upload.Click += (_, __) =>
        {
            if (Selected is not { } s) return;
            if (!CloudApi.LoggedIn)
            {
                if (new LoginDialog(this).ShowDialog() != true) return;
                UpdateLoginButton();
            }
            if (new UploadDialog(this, s.File, s.Name).ShowDialog() == true)
                StatusText.Text = $"状态: ✅ 已上传「{s.Name}」";
        };

        // 红字: 用显式 TextBlock 头(本地前景色压过全局 TextBlock 样式)
        var delete = new MenuItem { Header = new TextBlock { Text = Lang.S("menu.delete"), Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)) } };
        delete.Click += (_, __) =>
        {
            if (Selected is not { } s) return;
            if (!MsgBox.Confirm(this, $"确定删除曲谱「{s.Name}」?\n将从磁盘永久删除, 无法撤销。", "删除曲谱")) return;
            try { System.IO.File.Delete(s.File); }
            catch (Exception ex) { MsgBox.Info(this, "删除失败: " + ex.Message); return; }
            LibraryMeta.Forget(s.FileName);
            RefreshLibrary();
            StatusText.Text = $"状态: 已删除「{s.Name}」";
        };

        cm.Items.Add(fav);
        cm.Items.Add(addTag);
        cm.Items.Add(removeTag);
        cm.Items.Add(upload);
        cm.Items.Add(delete);
        return cm;
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
            btn.Click += (_, __) => { if (_editingKeys) BeginRemap(idx); else { AudioEngine.Play(idx); FlashKey(idx); } };
            _pianoButtons[i] = btn;
            _keyLabels[i] = lbl;
            PianoGrid.Children.Add(btn);
            RefreshKey(i);
        }
    }

    void RefreshKey(int i)
    {
        _keyLabels[i].Text = KeyConfig.Label(_player.Vk[i]);
        ((SolidColorBrush)_pianoButtons[i].Background).Color = i == _remapIndex ? Theme.KeyWait : Theme.KeySquare;
    }

    // 切换主题后给琴键重新上色
    void ApplyKeyTheme()
    {
        for (int i = 0; i < 15; i++)
        {
            _keyLabels[i].Foreground = new SolidColorBrush(Theme.KeyLetter);
            _keyDiamond[i].BorderBrush = new SolidColorBrush(Theme.KeyDiamond);
            _pianoButtons[i].BorderBrush = new SolidColorBrush(Theme.KeyBorder);
            RefreshKey(i);
        }
    }

    // ---- 播放/试听时琴键同步亮起 + 进度条 ----
    DispatcherTimer? _flashTimer;
    bool _settingProgress;   // true 时进度条变化来自定时器回写, 不当作用户拖动

    void SetProgress(double frac)
    {
        _settingProgress = true;
        ProgressSlider.Value = frac;
        _settingProgress = false;
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
            SetProgress(_player.TotalMs > 0 ? _player.PositionMs / _player.TotalMs : 0);
            ElapsedText.Text = Fmt(_player.PositionMs);
            TotalText.Text = Fmt(_player.TotalMs);
        };
        return t;
    }

    void StopFlash()
    {
        _flashTimer?.Stop();
        SetProgress(0);
        ElapsedText.Text = "00:00";
        if (Selected is { } s) TotalText.Text = Fmt(s.DurationMs);
    }

    // 触发: 背景色变深回弹(颜色动画自动回基准) + 翻转 + 缩放
    void FlashKey(int k)
    {
        if (k < 0 || k >= 15 || k == _remapIndex) return;
        var brush = (SolidColorBrush)_pianoButtons[k].Background;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(Theme.KeyLit, Theme.KeySquare, TimeSpan.FromMilliseconds(240)) { FillBehavior = FillBehavior.Stop });
        SpinKey(k);
    }

    // 光遇式翻转: 旋转一整圈(45°→405°) + 圆角морф(菱形3→圆15→菱形3), 中途成圆再变回
    void SpinKey(int k)
    {
        if (k < 0 || k >= 15) return;
        const int ms = 360;
        var spin = new DoubleAnimation(45, 405, TimeSpan.FromMilliseconds(ms))
        {
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        _keyRot[k].BeginAnimation(RotateTransform.AngleProperty, spin);

        var morph = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(ms) };
        morph.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(0)));
        morph.KeyFrames.Add(new EasingDoubleKeyFrame(15, KeyTime.FromPercent(0.5), new SineEase { EasingMode = EasingMode.EaseInOut }));
        morph.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromPercent(1), new SineEase { EasingMode = EasingMode.EaseInOut }));
        _keyDiamond[k].BeginAnimation(KeyFx.RoundProperty, morph);

        // 按下缩小回弹(线性): 1 → 0.85 → 1
        var scale = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = TimeSpan.FromMilliseconds(ms) };
        scale.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
        scale.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.35)));
        scale.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1)));
        _keyScale[k].BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        _keyScale[k].BeginAnimation(ScaleTransform.ScaleYProperty, scale);
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

        // 物理键盘触发对应琴键(发声+动画); 焦点在输入框时放行, 忽略长按重复
        if (!e.IsRepeat && System.Windows.Input.Keyboard.FocusedElement is not TextBox)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            int idx = Array.IndexOf(_player.Vk, (ushort)vk);
            if (idx >= 0)
            {
                AudioEngine.Play(idx);
                FlashKey(idx);
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    // ---- 洞穴音效 / 音色 / 主题 / 软件信息 ----
    void Cave_Click(object sender, RoutedEventArgs e)
    {
        AudioEngine.Cave = !AudioEngine.Cave;
        CaveBtn.Content = $"{Lang.S("cave")}: {Lang.S(AudioEngine.Cave ? "on" : "off")}";
        StatusText.Text = $"状态: 洞穴音效已{(AudioEngine.Cave ? "开启" : "关闭")}";
    }

    ContextMenu? _instrumentMenu;
    void Instrument_Click(object sender, RoutedEventArgs e)
    {
        if (_instrumentMenu == null)
        {
            _instrumentMenu = new ContextMenu();
            foreach (var name in AudioEngine.Instruments)
            {
                var it = new MenuItem { Header = name };
                var n = name;
                it.Click += (_, __) =>
                {
                    System.Threading.Tasks.Task.Run(() => AudioEngine.SetInstrument(n));
                    _instrumentName = n;
                    InstrumentBtn.Content = $"{Lang.S("instrument")}: {n}";
                    StatusText.Text = $"状态: 音色 → {n}";
                };
                _instrumentMenu.Items.Add(it);
            }
        }
        _instrumentMenu.PlacementTarget = InstrumentBtn;
        _instrumentMenu.IsOpen = true;
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        Theme.Apply(!Theme.Dark);
        ApplyKeyTheme();
        StyleSongList();   // 悬停/选中色随主题重建
        ThemeBtn.Content = $"{Lang.S("theme")}: {Lang.S(Theme.Dark ? "theme.dark" : "theme.light")}";
        StatusText.Text = $"状态: 已切换到{(Theme.Dark ? "深色" : "浅色")}主题";
    }

    void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(this).ShowDialog();

    void Create_Click(object sender, RoutedEventArgs e) => OpenEditor(null);
    void Edit_Click(object sender, RoutedEventArgs e) => OpenEditor(Selected);
    void SongList_DoubleClick(object sender, RoutedEventArgs e) => OpenEditor(Selected);

    // 选中曲谱 → _notes(key, ms); 无选中/空谱返回 false 并更新状态栏
    bool TryLoadSelected()
    {
        if (Selected is not { } song) { StatusText.Text = "状态: 请先在中间选一首曲谱"; return false; }
        var doc = SongLibrary.LoadDocument(song);
        _notes = doc.Notes.Select(n => (n.Key, n.Beat * doc.MsPerBeat)).ToList();
        if (_notes.Count == 0) { StatusText.Text = "状态: 该曲谱无音符"; return false; }
        return true;
    }

    // useCountdown: 按钮启动需倒计时(留时间切到光遇); 热键启动人已在游戏里, 立即开始
    void StartAuto(bool useCountdown)
    {
        if (_playing || _previewing) { StopPlaying(); return; }
        if (!TryLoadSelected()) return;
        _speed = SpeedSlider.Value;
        _paused = false;
        int sec = useCountdown && int.TryParse(CountdownBox.Text, out int s) ? Math.Max(0, s) : 0;
        BeginCountdown(sec);
    }

    void Start_Click(object sender, RoutedEventArgs e) => StartAuto(true);

    // 试听: 通过扬声器(AudioEngine)放整首, 不发按键/不切游戏窗口, 无需倒计时
    void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_playing || _previewing) { StopPlaying(); return; }
        if (!TryLoadSelected()) return;
        _previewing = true;
        PreviewBtn.Content = "⏹ 停止试听";
        StatusText.Text = $"状态: 🎧 试听中 ({SpeedSlider.Value:0.0}x)";
        StartFlash();
        _player.Play(_notes, SpeedSlider.Value, () => Dispatcher.Invoke(OnPlayDone), AudioEngine.Play);
    }

    void BeginCountdown(int sec)
    {
        _playing = true;
        StartBtn.Content = "⏹ 停止 (F1)";
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
        StatusText.Text = "状态: 🎵 演奏中... (F1 停止 / F2 暂停)";
        StartFlash();
        _player.Play(_notes, _speed, () => Dispatcher.Invoke(OnPlayDone));
    }

    void ResetPlayUi()
    {
        _playing = false; _previewing = false;
        StopFlash();
        StartBtn.Content = "▶ 开始 (F1)";
        PauseBtn.Content = "⏸ 暂停 (F2)";
        PreviewBtn.Content = "🎧 试听 (扬声器)";
    }

    void OnPlayDone()
    {
        bool wasPreview = _previewing;
        ResetPlayUi();
        AudioEngine.StopAll();
        if (StatusText.Text.Contains("演奏中")) StatusText.Text = "状态: 演奏完成";
        else if (wasPreview && StatusText.Text.Contains("试听")) StatusText.Text = "状态: 试听结束";
    }

    void StopPlaying()
    {
        _countdown?.Stop();
        _player.Stop();
        AudioEngine.StopAll();
        ResetPlayUi();
        StatusText.Text = "状态: 已停止";
    }

    void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!_playing) return;
        _paused = !_paused;
        _player.Pause(_paused);
        PauseBtn.Content = _paused ? "▶ 继续 (F2)" : "⏸ 暂停 (F2)";
        StatusText.Text = _paused ? "状态: ⏸ 已暂停" : "状态: 🎵 演奏中... (F1 停止 / F2 暂停)";
    }
    void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLibrary();

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
        SpeedSlider.Value = Math.Clamp(SpeedSlider.Value + delta, SpeedSlider.Minimum, SpeedSlider.Maximum);
        StatusText.Text = $"状态: 速度 {SpeedSlider.Value:0.0}x";
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
