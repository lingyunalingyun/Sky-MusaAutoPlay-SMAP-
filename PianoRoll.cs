using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SMAP_WPF;

/// <summary>一个音符: 光遇键位 0~14 + 拍位置(beat)。存拍位置而非绝对时间, 网格才能脱离 BPM。</summary>
public class Note
{
    public int Key;      // 0=K0(低) .. 14=K14(高)
    public double Beat;  // 拍位置
}

/// <summary>
/// 钢琴卷帘编辑器 (SkyStudio 式)。
/// 网格大小固定(像素/拍), 只随 Zoom 整体缩放, 与 BPM 无关。
/// BPM 只在播放时决定游标前进速度 (见 MainWindow)。
/// </summary>
public class PianoRoll : FrameworkElement
{
    public const int Keys = 15;
    public double RowH = 22;          // 每键行高 (DIP, WPF 自动按显示器 DPI 缩放)
    public double RulerH = 24;        // 顶部小节标尺
    public double BaseBeatWidth = 64; // 1.0x 时每拍像素宽 (固定网格核心)
    public int Subdiv = 4;            // 每拍细分 (4 = 16 分音符网格)
    public int BeatsPerBar = 4;       // 4/4
    public int TotalBeats = 64;       // 时间轴长度(拍)
    public double Zoom = 1.0;         // 缩放, 只放大缩小网格, 不改音符/BPM

    public readonly List<Note> Notes = new();
    public double PlayheadBeat = 0;
    public double ViewLeft = 0, ViewRight = 0;   // 可视 x 范围(ScrollViewer 更新); OnRender 只画此范围, 避免超长卷帘卡死

    public readonly HashSet<Note> Selected = new();
    static readonly List<(int key, double beat)> _clip = new();   // 剪贴板(相对起点), 跨窗口共享
    public Action<double>? RequestScrollByX;                      // 中键平移: 请求视图水平滚动 dx
    bool _selecting, _moving, _panning, _hasMarquee, _dragPushed;   // _dragPushed: 本次拖动已记撤销点
    Point _selA, _selB, _dragStart, _panStart;
    Dictionary<Note, (double beat, int key)>? _orig;             // 移动前各选中音符原位置
    readonly Stack<List<Note>> _undo = new();
    readonly Stack<List<Note>> _redo = new();

    double BeatWidth => BaseBeatWidth * Zoom;

    public PianoRoll()
    {
        Focusable = true;
        InputMethod.SetIsInputMethodEnabled(this, false);   // 禁输入法, 免得 Shift+X 等按键触发 IME
        UpdateSize();
    }

    public void UpdateSize()
    {
        Width = TotalBeats * BeatWidth;
        Height = RulerH + Keys * RowH;
        InvalidateVisual();
    }

    public double SnapBeat(double beat)
    {
        double step = 1.0 / Subdiv;
        // 用 Floor 而非 Round: 点格子任意位置都落到该格起点, 不会舍入到下一格
        return Math.Max(0, Math.Floor(beat / step) * step);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);

        // 中键: 平移视图
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true; _panStart = e.GetPosition(null); CaptureMouse(); e.Handled = true; return;
        }

        // 标尺: 设游标
        if (p.Y < RulerH && e.ChangedButton == MouseButton.Left)
        {
            PlayheadBeat = SnapBeat(p.X / BeatWidth);
            PlayheadChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (e.ChangedButton == MouseButton.Left)
        {
            if (ctrl)   // Ctrl+左键: 框选
            {
                _selecting = true; _selA = _selB = p; CaptureMouse(); return;
            }

            // 框选后: 任意位置左键都拖动整个选区(不必点在音符上)
            if (_hasMarquee && Selected.Count > 0)
            {
                _moving = true; _dragStart = p; _dragPushed = false;
                _orig = Selected.ToDictionary(x => x, x => (x.Beat, x.Key));
                CaptureMouse();
                return;
            }

            var hit = HitNote(p);
            if (hit != null)
            {
                if (!Selected.Contains(hit)) { Selected.Clear(); Selected.Add(hit); }   // 点未选中音符→只选它
                _dragPushed = false;   // 拖动首次移动时才记撤销
            }
            else   // 空白格: 放新音符并选中
            {
                int key = YToKey(p.Y);
                if (key < 0) return;
                PushUndo();
                var n = new Note { Key = key, Beat = SnapBeat(p.X / BeatWidth) };
                Notes.Add(n);
                Selected.Clear(); Selected.Add(n);
                NotePlayed?.Invoke(key);
                _dragPushed = true;   // 放音符已记撤销, 拖动不再记
            }
            SetMarquee(false);   // 放音符/单选不属于框选拖动模式
            _moving = true; _dragStart = p;
            _orig = Selected.ToDictionary(x => x, x => (x.Beat, x.Key));   // 记录原位置供拖动
            CaptureMouse();
            InvalidateVisual();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            if (Selected.Count > 0) { Selected.Clear(); SetMarquee(false); InvalidateVisual(); return; }   // 取消框选
            var hit = HitNote(p);
            if (hit != null) { PushUndo(); Notes.Remove(hit); InvalidateVisual(); }       // 删除音符
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_panning)
        {
            var pn = e.GetPosition(null);
            RequestScrollByX?.Invoke(pn.X - _panStart.X);
            _panStart = pn;
            return;
        }
        if (!_selecting && !_moving) return;
        var p = e.GetPosition(this);

        if (_selecting) { _selB = p; InvalidateVisual(); return; }

        if (_moving && _orig != null)   // 拖动选中音符
        {
            double step = 1.0 / Subdiv;
            double dBeat = Math.Round((p.X - _dragStart.X) / BeatWidth / step) * step;
            int dRow = (int)Math.Round((p.Y - _dragStart.Y) / RowH);
            if (!_dragPushed && (Math.Abs(dBeat) > 1e-9 || dRow != 0)) { PushUndo(); _dragPushed = true; }   // 首次实际移动记撤销
            foreach (var kv in _orig)
            {
                kv.Key.Beat = Math.Max(0, kv.Value.beat + dBeat);
                kv.Key.Key = Math.Clamp(kv.Value.key - dRow, 0, Keys - 1);   // 屏幕下移=音更低
            }
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_selecting) { _selecting = false; FinishSelect(); SetMarquee(Selected.Count > 0); ReleaseMouseCapture(); InvalidateVisual(); }
        else if (_moving) { _moving = false; _orig = null; ReleaseMouseCapture(); InvalidateVisual(); }
        else if (_panning && e.ChangedButton == MouseButton.Middle) { _panning = false; ReleaseMouseCapture(); }
    }

    // 进出框选模式: 框选后光标变四向移动箭头, 提示可随处拖动
    void SetMarquee(bool v)
    {
        _hasMarquee = v;
        Cursor = v ? Cursors.SizeAll : null;
    }

    int YToKey(double y)
    {
        int row = (int)((y - RulerH) / RowH);
        return (row < 0 || row >= Keys) ? -1 : (Keys - 1) - row;
    }

    Note? HitNote(Point p)
    {
        if (p.Y < RulerH) return null;
        int key = YToKey(p.Y); if (key < 0) return null;
        double beat = p.X / BeatWidth, cell = 1.0 / Subdiv;
        foreach (var n in Notes)
            if (n.Key == key && beat >= n.Beat && beat < n.Beat + cell) return n;
        return null;
    }

    void FinishSelect()
    {
        double x0 = Math.Min(_selA.X, _selB.X), x1 = Math.Max(_selA.X, _selB.X);
        double y0 = Math.Min(_selA.Y, _selB.Y), y1 = Math.Max(_selA.Y, _selB.Y);
        double cellW = BeatWidth / Subdiv;
        Selected.Clear();
        foreach (var n in Notes)
        {
            int row = (Keys - 1) - n.Key;
            double nx = n.Beat * BeatWidth, ny = RulerH + row * RowH;
            if (nx + cellW >= x0 && nx <= x1 && ny + RowH >= y0 && ny <= y1) Selected.Add(n);
        }
    }

    public void DeleteSelected()
    {
        if (Selected.Count == 0) return;
        PushUndo();
        Notes.RemoveAll(Selected.Contains);
        Selected.Clear();
        InvalidateVisual();
    }

    public void Copy()
    {
        if (Selected.Count == 0) return;
        double min = Selected.Min(n => n.Beat);
        _clip.Clear();
        foreach (var n in Selected) _clip.Add((n.Key, n.Beat - min));
    }

    public void Paste()
    {
        if (_clip.Count == 0) return;
        PushUndo();
        double bas = SnapBeat(PlayheadBeat);
        Selected.Clear();
        foreach (var (key, beat) in _clip)
        {
            var n = new Note { Key = key, Beat = bas + beat };
            Notes.Add(n); Selected.Add(n);
        }
        InvalidateVisual();
    }

    public void Cut() { Copy(); DeleteSelected(); }

    // 方向键移动选中音符: dCells=左右格数, dKeys=上下键数
    public void MoveSelected(int dCells, int dKeys)
    {
        if (Selected.Count == 0) return;
        PushUndo();
        double step = 1.0 / Subdiv;
        foreach (var n in Selected)
        {
            n.Beat = Math.Max(0, n.Beat + dCells * step);
            n.Key = Math.Clamp(n.Key + dKeys, 0, Keys - 1);
        }
        InvalidateVisual();
    }

    // 撤销/重做: 改动前 PushUndo 存快照
    List<Note> Snapshot() => Notes.Select(n => new Note { Key = n.Key, Beat = n.Beat }).ToList();
    void PushUndo() { _undo.Push(Snapshot()); _redo.Clear(); }
    public void ClearHistory() { _undo.Clear(); _redo.Clear(); }
    public void Undo() { if (_undo.Count == 0) return; _redo.Push(Snapshot()); Restore(_undo.Pop()); }
    public void Redo() { if (_redo.Count == 0) return; _undo.Push(Snapshot()); Restore(_redo.Pop()); }
    void Restore(List<Note> snap)
    {
        Notes.Clear(); Notes.AddRange(snap);
        Selected.Clear(); SetMarquee(false);
        InvalidateVisual();
    }

    /// <summary>放置音符时回调 (试听用)。</summary>
    public Action<int>? NotePlayed;
    /// <summary>游标位置改变时回调 (用于同步底部琴键高亮)。</summary>
    public Action? PlayheadChanged;

    protected override void OnRender(DrawingContext dc)
    {
        double w = Width, h = Height, bw = BeatWidth;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // 只绘制可见范围(带余量): 卷帘可长达数万像素, 全画会卡死
        double vl = Math.Max(0, ViewLeft - 200);
        double vr = (ViewRight > ViewLeft + 10) ? Math.Min(w, ViewRight + 200) : Math.Min(w, vl + 3000);
        double vw = vr - vl;

        double subW = bw / Subdiv;
        bool drawSub = subW >= 4;
        int subPerBar = Subdiv * BeatsPerBar;   // 16 格 = 1 小节
        int totalSub = TotalBeats * Subdiv;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x1e)), null, new Rect(vl, 0, vw, h));

        // 行背景: 每 5 键一组交替深浅
        for (int row = 0; row < Keys; row++)
        {
            int key = (Keys - 1) - row;
            var c = (key / 5 == 1) ? Color.FromRgb(0x26, 0x26, 0x26) : Color.FromRgb(0x2e, 0x2e, 0x2e);
            dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(vl, RulerH + row * RowH, vw, RowH));
        }

        // 斑马底色 (仅可见小节)
        var barShade = new SolidColorBrush(Color.FromArgb(0x16, 0xff, 0xff, 0xff));
        for (int bar = Math.Max(0, (int)(vl / (subPerBar * subW))); bar * BeatsPerBar < TotalBeats; bar++)
        {
            double bx = bar * subPerBar * subW;
            if (bx > vr) break;
            if (bar % 2 == 1)
                dc.DrawRectangle(barShade, null, new Rect(bx, RulerH, subPerBar * subW, h - RulerH));
        }

        // 竖线 (仅可见): 小节最粗 / 拍明显 / 细分很淡
        var subPen = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), 1);
        var beatPen = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), 1.2);
        var barPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9a, 0x9a, 0x9a)), 1.8);
        for (int s = Math.Max(0, (int)(vl / subW)); s <= totalSub; s++)
        {
            double x = s * subW;
            if (x > vr) break;
            Pen pen;
            if (s % subPerBar == 0) pen = barPen;
            else if (s % Subdiv == 0) pen = beatPen;
            else if (drawSub) pen = subPen;
            else continue;
            dc.DrawLine(pen, new Point(x, RulerH), new Point(x, h));
        }

        // 横向行线 + 5 键分组强线
        var rowPen = new Pen(new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a)), 1);
        var groupPen = new Pen(Brushes.Black, 1);
        for (int row = 0; row <= Keys; row++)
        {
            int key = (Keys - 1) - row;
            bool grp = row == 0 || row == Keys || key == 4 || key == 9;
            dc.DrawLine(grp ? groupPen : rowPen, new Point(vl, RulerH + row * RowH), new Point(vr, RulerH + row * RowH));
        }

        // 音符方块 (仅可见, 选中的用橙色)
        var noteFill = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xe8));
        var selFill = new SolidColorBrush(Color.FromRgb(0xff, 0xa5, 0x2a));
        var noteStroke = new Pen(new SolidColorBrush(Color.FromRgb(0x3a, 0x8a, 0xd0)), 1);
        double noteW = Math.Max(6, subW - 2);
        foreach (var n in Notes)
        {
            double x = n.Beat * bw;
            if (x + noteW < vl || x > vr) continue;
            int row = (Keys - 1) - n.Key;
            double y = RulerH + row * RowH;
            dc.DrawRoundedRectangle(Selected.Contains(n) ? selFill : noteFill, noteStroke, new Rect(x + 1, y + 3, noteW, RowH - 6), 3, 3);
        }

        // 标尺 + 小节号 (仅可见)
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)), null, new Rect(vl, 0, vw, RulerH));
        var gray = new SolidColorBrush(Color.FromRgb(0xaa, 0xaa, 0xaa));
        var tf = new Typeface("Segoe UI");
        for (int bar = Math.Max(0, (int)(vl / (BeatsPerBar * bw))); bar * BeatsPerBar < TotalBeats; bar++)
        {
            double x = bar * BeatsPerBar * bw;
            if (x > vr) break;
            var ft = new FormattedText((bar + 1).ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, tf, 11, gray, dpi);
            dc.DrawText(ft, new Point(x + 3, 5));
        }
        dc.DrawLine(new Pen(Brushes.Black, 1), new Point(vl, RulerH), new Point(vr, RulerH));

        // 播放游标
        double phx = PlayheadBeat * bw;
        if (phx >= vl - 2 && phx <= vr + 2)
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0xff, 0x44, 0x44)), 2), new Point(phx, 0), new Point(phx, h));

        // 框选矩形
        if (_selecting)
        {
            var r = new Rect(Math.Min(_selA.X, _selB.X), Math.Min(_selA.Y, _selB.Y),
                Math.Abs(_selB.X - _selA.X), Math.Abs(_selB.Y - _selA.Y));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x33, 0x4a, 0x9e, 0xff)),
                new Pen(new SolidColorBrush(Color.FromRgb(0x4a, 0x9e, 0xff)), 1), r);
        }
    }
}
