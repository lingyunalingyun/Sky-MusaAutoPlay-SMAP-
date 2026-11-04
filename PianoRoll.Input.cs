using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class PianoRoll
{
    enum Gesture { None, Select, Move, Resize, Erase, Paint, Pan }
    Gesture _gesture;
    Point _start, _last;
    decimal _startView, _deltaBeat;
    double _startVertical;
    int _deltaKey;
    bool _moved, _copy, _free, _addSelection, _cycle;
    decimal _minimumBeat, _minimumLength;
    int _minimumKey, _maximumKey;
    ProjectNote[] _originals = Array.Empty<ProjectNote>();
    ProjectNote? _created;
    bool _quietSelection;
    Guid? _hoverNote, _hoverHandle;
    readonly HashSet<Guid> _erase = new();
    readonly Dictionary<(decimal beat, int key), ProjectNote> _paint = new();
    readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(33) };

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus(); PointerDown(e.GetPosition(this), e.ChangedButton, Keyboard.Modifiers);
        if (_gesture != Gesture.None) CaptureMouse(); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) => PointerMove(e.GetPosition(this), Keyboard.Modifiers);
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverNote = _hoverHandle = null; DrawOverlay(); base.OnMouseLeave(e);
    }
    void UpdateHover(Point p)
    {
        var handle = HitNotes(p, true).FirstOrDefault();
        Guid? note = (handle ?? HitNotes(p).FirstOrDefault())?.Id;
        if (note == _hoverNote && handle?.Id == _hoverHandle) return;
        _hoverNote = note; _hoverHandle = handle?.Id; DrawOverlay();
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        try { PointerUp(e.GetPosition(this), Keyboard.Modifiers); }
        catch (Exception error)
        {
            Logger.Exception("音符编辑失败", error);
            string key = "ed.error." + error.Message, message = Lang.S(key);
            Status?.Invoke(message == key ? Lang.S("ed.error") : message);
        }
        finally { ReleaseMouseCapture(); }
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { CancelGesture(); base.OnLostMouseCapture(e); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var m = Keyboard.Modifiers;
        if ((m & ModifierKeys.Control) != 0)
        {
            double factor = Math.Pow(1.15, e.Delta / 120.0);
            SetZoom((m & ModifierKeys.Shift) == 0 ? _zoom * factor : _zoom,
                (m & ModifierKeys.Shift) != 0 ? _verticalZoom * factor : _verticalZoom, e.GetPosition(this));
        }
        else if ((m & ModifierKeys.Shift) != 0) SetView(Math.Max(0, _viewBeat - (decimal)(e.Delta / BeatWidth)), _verticalOffset);
        else SetView(_viewBeat, _verticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }
    internal void PointerDown(Point p, MouseButton button, ModifierKeys modifiers)
    {
        CancelGesture(); _start = _last = p; _startView = _viewBeat; _startVertical = _verticalOffset;
        UpdateHover(p);
        _free = (modifiers & ModifierKeys.Alt) != 0 || !SnapEnabled;
        if (button == MouseButton.Middle) { _gesture = Gesture.Pan; return; }
        if (p.Y < RulerHeight && p.X >= LaneWidth && button == MouseButton.Left)
        { PlayheadBeat = SnapBeat(BeatAt(p.X), _free); PlayheadChanged?.Invoke(); return; }
        if (p.X < LaneWidth || KeyAt(p.Y) < 0) return;
        if (button == MouseButton.Left && (modifiers & ModifierKeys.Control) != 0)
        { _gesture = Gesture.Select; _addSelection = (modifiers & ModifierKeys.Shift) != 0; return; }
        if (button == MouseButton.Right || Tool == RollTool.Erase)
        {
            _gesture = Gesture.Erase; foreach (var n in HitNotes(p)) _erase.Add(n.Id);
            if (_erase.Count > 0) EditStarting?.Invoke(); DrawOverlay(); return;
        }
        if (button != MouseButton.Left) return;
        if (Tool == RollTool.Paint)
        { EditStarting?.Invoke(); _gesture = Gesture.Paint; AddPaint(p); DrawOverlay(); return; }
        var handle = HitNotes(p, handle: true).FirstOrDefault(); var hit = handle ?? HitNotes(p).FirstOrDefault();
        if (hit != null)
        {
            if (!_session.Selected.Contains(hit.Id)) { _session.Select(new[] { hit.Id }); _quietSelection = true; }
            _originals = _session.SelectedNotes(); _copy = handle == null && (modifiers & ModifierKeys.Shift) != 0;
            _cycle = handle == null && (modifiers & ModifierKeys.Alt) != 0;
            _gesture = handle != null ? Gesture.Resize : Gesture.Move;
            if (_gesture == Gesture.Resize && _originals.Select(n => n.Action).Distinct().Count() > 1)
            { _gesture = Gesture.None; Status?.Invoke(Lang.S("ed.error.MixedNoteActions")); return; }
        }
        else
        {
            _session.Select(Array.Empty<Guid>());
            _created = new ProjectNote { TrackId = _session.ActiveTrackId, Key = KeyAt(p.Y), Beat = SnapBeat(BeatAt(p.X), _free), Action = NoteAction.Tap };
            _originals = new[] { _created }; _gesture = Gesture.Move;
        }
        _minimumBeat = _originals.Min(n => n.Beat); _minimumKey = _originals.Min(n => n.Key); _maximumKey = _originals.Max(n => n.Key);
        _minimumLength = _originals.Min(n => n.Action == NoteAction.Tap ? 1m / _session.Project.GridSubdiv : n.LengthBeats);
        DrawOverlay();
    }
    internal void PointerMove(Point p, ModifierKeys modifiers)
    {
        Point previous = _last; _last = p; _free = (modifiers & ModifierKeys.Alt) != 0 || !SnapEnabled;
        if (_gesture == Gesture.None)
        {
            UpdateHover(p);
            Cursor = HitNotes(p, true).Length > 0 ? Cursors.SizeWE : HitNotes(p).Length > 0 ? Cursors.SizeAll : Cursors.Pen; return;
        }
        if (_gesture == Gesture.Pan)
        { SetView(Math.Max(0, _startView - (decimal)((p.X - _start.X) / BeatWidth)), _startVertical - p.Y + _start.Y); return; }
        if (!_moved && (p - _start).Length >= 4)
        {
            _moved = true; if (_gesture is Gesture.Move or Gesture.Resize) EditStarting?.Invoke();
        }
        if (_gesture is Gesture.Paint or Gesture.Erase)
        {
            bool started = _erase.Count > 0;
            double spacing = Math.Max(1, Math.Min(BeatWidth / _session.Project.GridSubdiv / 2, RowHeight / 2));
            int steps = Math.Clamp((int)Math.Ceiling((p - previous).Length / spacing), 1, 2048);
            for (int i = 1; i <= steps; i++)
            {
                Point sample = previous + (p - previous) * (i / (double)steps);
                if (_gesture == Gesture.Paint) AddPaint(sample); else foreach (var n in HitNotes(sample)) _erase.Add(n.Id);
            }
            if (_gesture == Gesture.Erase && !started && _erase.Count > 0) EditStarting?.Invoke();
        }
        else if (_gesture is Gesture.Move or Gesture.Resize && _moved)
        {
            decimal delta = (decimal)((p.X - _start.X) / BeatWidth) + _viewBeat - _startView;
            if (!_free) delta = decimal.Round(delta * _session.Project.GridSubdiv, 0, MidpointRounding.AwayFromZero) / _session.Project.GridSubdiv;
            if (_gesture == Gesture.Move)
            {
                _deltaBeat = Math.Max(-_minimumBeat, delta);
                _deltaKey = Math.Clamp(-(int)Math.Round((p.Y - _start.Y + _verticalOffset - _startVertical) / RowHeight),
                    -_minimumKey, _session.KeyCount - 1 - _maximumKey);
            }
            else
            {
                _deltaBeat = Math.Max(-_minimumLength + .000000000001m, delta);
            }
        }
        if (_moved && _gesture is not (Gesture.None or Gesture.Pan)) _autoScroll.Start();
        DrawOverlay();
    }
    ProjectNote PreviewNote(ProjectNote n)
    {
        if (_gesture != Gesture.Resize) return n with { Beat = n.Beat + _deltaBeat, Key = n.Key + _deltaKey };
        if (_originals.Length == 1) return _session.ResizePreview(new[] { n }, _deltaBeat, _free)[0];
        decimal cell = 1m / _session.Project.GridSubdiv, length = (n.Action == NoteAction.Tap ? cell : n.LengthBeats) + _deltaBeat;
        bool tap = n.Action == NoteAction.Tap && !_free && length <= cell;
        return n with { Action = tap ? NoteAction.Tap : NoteAction.Hold, LengthBeats = tap ? 0 : length, RetriggerOnOverlap = !tap && n.RetriggerOnOverlap };
    }
    void AddPaint(Point p)
    {
        int key = KeyAt(p.Y); if (key < 0 || p.X < LaneWidth) return;
        decimal beat = SnapBeat(BeatAt(p.X), _free);
        if (_indexes[_session.ActiveTrackId].Query(beat, beat, key, key).Any(n => n.Beat == beat)) return;
        _paint.TryAdd((beat, key), new ProjectNote { TrackId = _session.ActiveTrackId, Key = key, Beat = beat, Action = NoteAction.Tap });
    }
    internal void PointerUp(Point p, ModifierKeys modifiers)
    {
        try
        {
            if (_gesture == Gesture.Select)
            {
                if (!_moved)
                {
                    var hit = HitNotes(p).FirstOrDefault();
                    if (hit != null) { if (_addSelection) _session.Select(new[] { hit.Id }, true); else _session.ToggleSelection(hit.Id); }
                    else if (!_addSelection) _session.Select(Array.Empty<Guid>());
                }
                else
                {
                    var rect = SelectionRect(p); decimal left = BeatAt(rect.Left), right = BeatAt(rect.Right);
                    var ids = _indexes[_session.ActiveTrackId].Query(left - 1m / _session.Project.GridSubdiv, right, tapWidth: 1m / _session.Project.GridSubdiv)
                        .Where(n => rect.IntersectsWith(Bounds(n))).Select(n => n.Id).ToArray();
                    _session.Select(ids, _addSelection);
                }
            }
            else if (_gesture == Gesture.Erase)
            {
                if (_erase.Count > 0) _session.PutNotes(Array.Empty<ProjectNote>(), _erase.ToArray()); else _session.Select(Array.Empty<Guid>());
            }
            else if (_gesture == Gesture.Paint)
            { if (_paint.Count > 0) { var notes = _paint.Values.ToArray(); _session.PutNotes(notes); _session.Select(notes.Select(n => n.Id)); _quietSelection = true; } }
            else if (_gesture is Gesture.Move or Gesture.Resize)
            {
                if (_cycle && !_moved)
                {
                    var hits = HitNotes(p).OrderBy(n => n.Id).ToArray(); int current = Array.FindIndex(hits, n => _session.Selected.Contains(n.Id));
                    if (hits.Length > 0) _session.Select(new[] { hits[(current + 1) % hits.Length].Id });
                }
                else if (_created != null || _moved)
                {
                    if (_created != null && !_moved) EditStarting?.Invoke();
                    bool quiet = _created != null || _quietSelection;
                    var notes = _originals.Select(PreviewNote).Select(n => _copy ? n with { Id = Guid.NewGuid(), SourceMidiNoteId = null } : n).ToArray();
                    _session.PutNotes(notes); _session.Select(notes.Select(n => n.Id));
                    _quietSelection = quiet;
                }
            }
        }
        finally { CancelGesture(); UpdateHover(p); }
    }
    internal void CancelGesture()
    {
        _autoScroll.Stop(); _gesture = Gesture.None; _originals = Array.Empty<ProjectNote>(); _created = null; _paint.Clear(); _erase.Clear();
        _deltaBeat = 0; _deltaKey = 0; _moved = _copy = _cycle = false;
        if (_session != null) { using var dc = _overlay.RenderOpen(); InvalidateVisual(); }
    }
    Rect SelectionRect(Point p) => new(new Point(_start.X + (double)(_startView - _viewBeat) * BeatWidth, _start.Y + _startVertical - _verticalOffset), p);
    void AutoScroll()
    {
        if (!_moved || _gesture is Gesture.None or Gesture.Pan) { _autoScroll.Stop(); return; }
        double horizontal = _last.X > ActualWidth - 24 ? Math.Min(24, _last.X - ActualWidth + 24) : _last.X < LaneWidth + 24 ? Math.Max(-24, _last.X - LaneWidth - 24) : 0;
        double vertical = _last.Y > ActualHeight - UnmappedHeight - 24 ? 12 : _last.Y < RulerHeight + 24 ? -12 : 0;
        if (horizontal == 0 && vertical == 0) return;
        SetView(Math.Max(0, _viewBeat + (decimal)(horizontal / BeatWidth)), _verticalOffset + vertical);
        PointerMove(_last, Keyboard.Modifiers);
    }
}
