using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class EditorWindow : Window
{
    static readonly string[] KeyLabels = { "Y", "U", "I", "O", "P", "H", "J", "K", "L", ";", "N", "M", ",", ".", "/" };
    static readonly Brush MapDefault = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    static readonly Brush MapFlash = new SolidColorBrush(Color.FromRgb(0x4c, 0xaf, 0x50));

    // 物理键盘 → 光遇 15 键 (与底部面板键位一致), 用于键盘写谱
    static readonly Dictionary<Key, int> KeyToLight = new()
    {
        { Key.Y, 0 }, { Key.U, 1 }, { Key.I, 2 }, { Key.O, 3 }, { Key.P, 4 },
        { Key.H, 5 }, { Key.J, 6 }, { Key.K, 7 }, { Key.L, 8 }, { Key.OemSemicolon, 9 },
        { Key.N, 10 }, { Key.M, 11 }, { Key.OemComma, 12 }, { Key.OemPeriod, 13 }, { Key.OemQuestion, 14 },
    };

    readonly Button[] _mapButtons = new Button[15];
    bool _playing;
    readonly Stopwatch _clock = new();
    double _lastMs;
    double _lastPlayBeat;   // 上一帧游标位置, 用于触发新经过的音符发声
    SongDocument _doc = new();   // 当前曲谱文档(含元数据); 新建时为默认空文档

    public EditorWindow()
    {
        InitializeComponent();
        BuildKeyLane();
        BuildMapPanel();
        Roll.NotePlayed = FlashKey;
        Roll.PlayheadChanged = () => { if (!_playing) HighlightActiveKeys(); };   // 手动移游标 → 对应键亮起
        Roll.RequestScrollByX = dx => Scroll.ScrollToHorizontalOffset(Math.Max(0, Scroll.HorizontalOffset - dx));   // 中键平移
        UpdateInfo();

        Scroll.ScrollChanged += (_, __) => UpdateViewport();
        InstrumentCombo.ItemsSource = AudioEngine.Instruments;
        InstrumentCombo.SelectedItem = "Piano";

        GridCombo.ItemsSource = new[] { "1/4 十六分", "1/8 三十二分", "1/3 三连音", "1/6 六连音", "1/12" };
        GridCombo.SelectedIndex = 0;                 // 默认 Subdiv=4, 先设再挂事件避免初始误触
        GridCombo.SelectionChanged += Grid_Changed;
        System.Threading.Tasks.Task.Run(AudioEngine.Init);   // 后台预热音频, 避免首次点击卡顿
    }

    // 自定义标题栏
    void Title_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    void Min_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => WindowState = WindowState.Minimized;
    void Close_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();

    void Instrument_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (InstrumentCombo.SelectedItem is string name)
            System.Threading.Tasks.Task.Run(() => AudioEngine.SetInstrument(name));   // 后台加载音色 wav
    }

    // 网格细分: 下拉索引 → Subdiv。三连音=3(一拍3格), 三十二分=8, 六连音=6
    static readonly int[] GridSubdivs = { 4, 8, 3, 6, 12 };
    void Grid_Changed(object sender, SelectionChangedEventArgs e)
    {
        int idx = GridCombo.SelectedIndex;
        if (idx < 0) return;
        Roll.Subdiv = GridSubdivs[idx];   // 音符按精确 beat 存, 换网格不移动音符, 只改吸附/格线
        Roll.UpdateSize();
    }

    // 把当前可视 x 范围告诉卷帘, 它只画这一段 (超长曲谱才不卡)
    void UpdateViewport()
    {
        Roll.ViewLeft = Scroll.HorizontalOffset;
        Roll.ViewRight = Scroll.HorizontalOffset + Scroll.ViewportWidth;
        Roll.InvalidateVisual();
    }

    /// <summary>从曲库加载一首曲谱 (音符 ms 已在 SongLibrary 换算成拍位置)。</summary>
    public void LoadSong(SongInfo info)
    {
        _doc = SongLibrary.LoadDocument(info);
        Roll.Notes.Clear();
        Roll.Notes.AddRange(_doc.Notes);
        Roll.TotalBeats = (int)_doc.TotalBeats;
        Roll.PlayheadBeat = 0;
        Roll.UpdateSize();
        Roll.ClearHistory();   // 新曲谱清空撤销栈
        BpmBox.Text = _doc.Bpm.ToString("0");   // BPM 只管游标速度, 取整数; 音符对齐用精确 GCD 拍长
        Title = $"钢琴卷帘编辑器 - {_doc.Name}";
        UpdateInfo();
        Dispatcher.BeginInvoke(new Action(UpdateViewport), DispatcherPriority.Loaded);
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_doc.FilePath)) { SaveAs_Click(sender, e); return; }
        DoSave(_doc.FilePath);
    }

    void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Sky 曲谱 (*.txt)|*.txt|JSON (*.json)|*.json",
            FileName = EnsureName(),
            InitialDirectory = System.IO.Directory.Exists(SongLibrary.SongsDir) ? SongLibrary.SongsDir : null
        };
        if (dlg.ShowDialog() == true) DoSave(dlg.FileName);
    }

    // 曲名为空/默认时, 生成不冲突的 -未命名N-
    string EnsureName()
    {
        if (!string.IsNullOrWhiteSpace(_doc.Name) && _doc.Name != "未命名") return _doc.Name;
        for (int i = 1; ; i++)
        {
            string cand = $"-未命名{i}-";
            string f1 = System.IO.Path.Combine(SongLibrary.SongsDir, cand + ".txt");
            string f2 = System.IO.Path.Combine(SongLibrary.SongsDir, cand + ".json");
            if (!System.IO.File.Exists(f1) && !System.IO.File.Exists(f2)) return cand;
        }
    }

    void DoSave(string path)
    {
        _doc.Name = EnsureName();
        _doc.Bpm = (int)ParseBpm();
        try
        {
            SongLibrary.Save(_doc, Roll.Notes, path);
            Title = $"钢琴卷帘编辑器 - {_doc.Name}";
            StatusToast($"已保存: {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex) { MsgBox.Info(this, ex.Message, "保存失败"); }
    }

    void EditInfo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InfoDialog(_doc) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            Title = $"钢琴卷帘编辑器 - {_doc.Name}";
            UpdateInfo();
        }
    }

    void StatusToast(string msg) => Title = $"钢琴卷帘编辑器 - {_doc.Name}  ·  {msg}";

    void BuildKeyLane()
    {
        KeyLane.Children.Add(new Border { Height = Roll.RulerH, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)) });
        for (int row = 0; row < PianoRoll.Keys; row++)
        {
            int key = (PianoRoll.Keys - 1) - row;
            var bg = (key % 5 == 0) ? Color.FromRgb(0x3a, 0x3a, 0x3a) : Color.FromRgb(0x2c, 0x2c, 0x2c);
            var b = new Border
            {
                Height = Roll.RowH,
                Background = new SolidColorBrush(bg),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = $"K{key}  {KeyLabels[key]}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                }
            };
            KeyLane.Children.Add(b);
        }
    }

    // 底部光遇琴键面板 (3×5, K0-4 / K5-9 / K10-14)
    void BuildMapPanel()
    {
        for (int i = 0; i < 15; i++)
        {
            var b = new Button
            {
                Width = 54,
                Height = 38,
                Margin = new Thickness(3),
                Background = MapDefault,
                Foreground = Brushes.White,
                FontSize = 11,
                Content = $"K{i}\n{KeyLabels[i]}",
                Tag = i
            };
            int idx = i;
            b.Click += (_, __) => MapClick(idx);
            _mapButtons[i] = b;
            MapGrid.Children.Add(b);
        }
    }

    void Preview(int key) => AudioEngine.Play(key);

    // 点底部琴键: 试听 + (非播放时)在游标位置 toggle 该键音符
    void MapClick(int key)
    {
        Preview(key);
        if (!_playing)
        {
            double beat = Roll.SnapBeat(Roll.PlayheadBeat);
            var ex = Roll.Notes.Find(n => n.Key == key && Math.Abs(n.Beat - beat) < 1e-6);
            if (ex != null) Roll.Notes.Remove(ex);
            else Roll.Notes.Add(new Note { Key = key, Beat = beat });
            Roll.InvalidateVisual();
            UpdateInfo();
        }
        FlashKey(key);
    }

    // 临时高亮一个键 (放音符/点击时); 播放中由 HighlightActiveKeys 统一管理
    void FlashKey(int key)
    {
        if (key < 0 || key >= 15) return;
        _mapButtons[key].Background = MapFlash;
        if (!_playing)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            t.Tick += (s, e) => { ((DispatcherTimer)s!).Stop(); if (!_playing) _mapButtons[key].Background = MapDefault; };
            t.Start();
        }
    }

    // 播放时: 游标附近(一个细分格内)的音符对应键亮起
    void HighlightActiveKeys()
    {
        double win = 1.0 / Roll.Subdiv;
        var active = new HashSet<int>();
        foreach (var n in Roll.Notes)
            if (Math.Abs(n.Beat - Roll.PlayheadBeat) < win) active.Add(n.Key);
        for (int i = 0; i < 15; i++)
            _mapButtons[i].Background = active.Contains(i) ? MapFlash : MapDefault;
    }

    void UpdateInfo() => InfoText.Text = $"音符 {Roll.Notes.Count}  ·  {Roll.TotalBeats} 拍";

    double ParseBpm()
    {
        if (double.TryParse(BpmBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 1)
            return v;
        return 120;
    }

    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_playing) { StopPlay(); return; }
        if (Roll.PlayheadBeat >= Roll.TotalBeats - 0.001) Roll.PlayheadBeat = 0;
        _lastPlayBeat = Roll.PlayheadBeat - 1e-6;   // 含起点音符(如 beat 0)
        _lastMs = 0;
        _clock.Restart();
        _playing = true;
        CompositionTarget.Rendering += OnFrame;   // 跟随显示器刷新率, 高刷屏可达 120+
        PlayBtn.Content = "⏸";
    }

    void Home_Click(object sender, RoutedEventArgs e)
    {
        StopPlay();
        Roll.PlayheadBeat = 0;
        ScrollToPlayhead();
        HighlightActiveKeys();
        Roll.InvalidateVisual();
    }

    void StepLeft_Click(object sender, RoutedEventArgs e) => Step(-1);
    void StepRight_Click(object sender, RoutedEventArgs e) => Step(1);

    // 游标移动一格(一个细分格 = 1/Subdiv 拍)
    void Step(int dir)
    {
        if (_playing) return;   // 播放中不响应
        double step = 1.0 / Roll.Subdiv;
        Roll.PlayheadBeat = Math.Max(0, Roll.SnapBeat(Roll.PlayheadBeat) + dir * step);
        ScrollToPlayhead();
        HighlightActiveKeys();
        Roll.InvalidateVisual();
    }

    // 游标移出视口时滚动跟随, 保证可见
    void ScrollToPlayhead()
    {
        double phx = Roll.PlayheadBeat * Roll.BaseBeatWidth * Roll.Zoom;
        if (phx < Scroll.HorizontalOffset + 40)
            Scroll.ScrollToHorizontalOffset(Math.Max(0, phx - 40));
        else if (phx > Scroll.HorizontalOffset + Scroll.ViewportWidth - 40)
            Scroll.ScrollToHorizontalOffset(phx - Scroll.ViewportWidth + 40);
    }

    void StopPlay()
    {
        if (_playing) { CompositionTarget.Rendering -= OnFrame; _playing = false; }
        _clock.Stop();
        PlayBtn.Content = "▶";
        for (int i = 0; i < 15; i++) _mapButtons[i].Background = MapDefault;
    }

    // 窗口级快捷键: 不依赖卷帘焦点; TextBox(如 BPM) 有焦点时放行, 让用户正常输入
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (ctrl && e.Key == Key.S) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; return; }   // 保存: 全局生效
        if (Keyboard.FocusedElement is TextBox) { base.OnPreviewKeyDown(e); return; }
        if (shift && e.Key == Key.X) { Roll.DeleteSelected(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { Roll.Copy(); e.Handled = true; }
        else if (ctrl && e.Key == Key.X) { Roll.Cut(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { Roll.Paste(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z) { Roll.Undo(); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { Roll.Redo(); e.Handled = true; }
        else if (e.Key == Key.Delete) { Roll.DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.Space) { Play_Click(this, new RoutedEventArgs()); e.Handled = true; }
        // ←/→: 有选区则移动音符, 否则移动游标(卷帘跟随)
        else if (e.Key == Key.Left) { if (Roll.Selected.Count > 0) Roll.MoveSelected(-1, 0); else Step(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { if (Roll.Selected.Count > 0) Roll.MoveSelected(1, 0); else Step(1); e.Handled = true; }
        // 物理键盘写谱: 按光遇键位对应键 → 在游标位置 toggle 该键音符
        else if (!ctrl && !shift && KeyToLight.TryGetValue(e.Key, out int lk)) { MapClick(lk); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }

    // 关闭窗口: 停播放定时器 + 静音, 否则播放中直接关会一直响
    protected override void OnClosed(EventArgs e)
    {
        StopPlay();
        AudioEngine.StopAll();
        base.OnClosed(e);
    }

    void OnFrame(object? sender, EventArgs e)
    {
        double nowMs = _clock.Elapsed.TotalMilliseconds;
        double dt = nowMs - _lastMs;
        _lastMs = nowMs;

        double msPerBeat = 60000.0 / ParseBpm();
        Roll.PlayheadBeat += dt / msPerBeat;

        // 触发游标新经过的音符发声
        foreach (var n in Roll.Notes)
            if (n.Beat > _lastPlayBeat && n.Beat <= Roll.PlayheadBeat) AudioEngine.Play(n.Key);
        _lastPlayBeat = Roll.PlayheadBeat;

        if (Roll.PlayheadBeat >= Roll.TotalBeats)
        {
            Roll.PlayheadBeat = Roll.TotalBeats;
            StopPlay();
        }

        // 播放时游标固定视口中央, 卷帘向左流动
        double phx = Roll.PlayheadBeat * Roll.BaseBeatWidth * Roll.Zoom;
        Scroll.ScrollToHorizontalOffset(phx - Scroll.ViewportWidth / 2);

        HighlightActiveKeys();
        Roll.InvalidateVisual();
    }
}
