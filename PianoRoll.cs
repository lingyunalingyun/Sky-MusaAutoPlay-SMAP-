using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public class Note
{
    public int Key;
    public double Beat;
}

public enum RollTool { Draw, Paint, Erase }

public partial class PianoRoll : FrameworkElement
{
    public const int Keys = 15;
    public const double LaneWidth = 76, RulerHeight = 40;
    readonly VisualCollection _visuals;
    readonly DrawingVisual _grid = new(), _notes = new(), _overlay = new(), _cursor = new();
    readonly Dictionary<Guid, NoteViewportIndex> _indexes = new();
    readonly Dictionary<Guid, List<ProjectNote>> _indexedNotes = new();
    MidiConversion? _indexedMapping;
    readonly Dictionary<Guid, PitchMappingKind> _mapping = new();
    readonly Dictionary<Guid, List<(MidiSourceNote source, MappedMidiNote mapping)>> _unmapped = new();
    EditorSession _session = null!;
    int _revision = -1;
    bool _staticDirty = true, _notesDirty = true;
    double _zoom = 1, _verticalZoom = 1, _verticalOffset;
    decimal _viewBeat, _playhead;
    bool _references;
    internal bool CenterCursor { get; set; } = true;
    internal decimal CursorDisplayBeat => _playhead + (CenterCursor && SnapEnabled && _session != null ? .5m / _session.Project.GridSubdiv : 0);
    public RollTool Tool { get; set; }
    public bool SnapEnabled { get; set; } = true;
    public Action? EditStarting, ViewChanged, PlayheadChanged;
    public Action<string>? Status;
    public Func<Guid, Color>? TrackColor;
    public int NoteLayerBuilds { get; private set; }
    public int GridLayerBuilds { get; private set; }
    public int LastRenderedNotes { get; private set; }
    public bool DenseView { get; private set; }
    public int ReferenceOutOfRange { get; private set; }
    public double BeatWidth => 64 * _zoom;
    public double RowHeight => 22 * _verticalZoom;
    public decimal VisibleBeats => (decimal)(Math.Max(1, ActualWidth - LaneWidth) / BeatWidth);
    public decimal ViewBeat => _viewBeat;
    public double VerticalOffset => _verticalOffset;
    public bool References { get => _references; set { _references = value; InvalidateLayers(); } }
    public double Zoom => _zoom;
    public double VerticalZoom => _verticalZoom;
    public decimal PlayheadBeat
    {
        get => _playhead;
        set { _playhead = Math.Max(0, value); DrawCursor(); }
    }
    public EditorSession Session
    {
        get => _session;
        set
        {
            if (_session != null) { _session.Changed -= ContentChanged; _session.SelectionChanged -= SelectionChanged; }
            _session = value; _revision = -1; _session.Changed += ContentChanged; _session.SelectionChanged += SelectionChanged;
            ContentChanged();
        }
    }
    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
    public PianoRoll()
    {
        Focusable = true; ClipToBounds = true; SnapsToDevicePixels = true;
        InputMethod.SetIsInputMethodEnabled(this, false);
        _visuals = new(this) { _grid, _notes, _overlay, _cursor };
        SizeChanged += (_, _) => { SetView(_viewBeat, _verticalOffset); InvalidateLayers(); };
        Unloaded += (_, _) => { CancelGesture(); _autoScroll.Stop(); };
        _autoScroll.Tick += (_, _) => AutoScroll();
    }
    void ContentChanged()
    {
        if (_session == null) return;
        if (_revision != _session.Revision)
        {
            foreach (var id in _indexes.Keys.Where(id => !_session.Project.Tracks.Any(t => t.Id == id)).ToArray()) { _indexes.Remove(id); _indexedNotes.Remove(id); }
            foreach (var t in _session.Project.Tracks) if (!_indexedNotes.TryGetValue(t.Id, out var previous) || !ReferenceEquals(previous, t.Notes))
            { _indexes[t.Id] = new(t.Notes, ordered: true); _indexedNotes[t.Id] = t.Notes; }
            if (_indexedMapping != _session.Project.MidiConversion) { _mapping.Clear(); _unmapped.Clear(); }
            if (_indexedMapping != _session.Project.MidiConversion && _session.Project.MidiConversion is { } conversion)
            {
                var originals = conversion.Source.Notes.ToDictionary(n => n.Id);
                foreach (var n in conversion.Notes)
                {
                    _mapping[n.SourceId] = n.Kind;
                    if (n.Kind != PitchMappingKind.Unplayable) continue;
                    Guid track = MidiSource.StableId($"{conversion.Source.Sha256}:track:{n.VoiceId}");
                    if (!_unmapped.TryGetValue(track, out var list)) _unmapped[track] = list = new();
                    list.Add((originals[n.SourceId], n));
                }
            }
            _indexedMapping = _session.Project.MidiConversion;
            _revision = _session.Revision;
        }
        InvalidateLayers();
    }
    void SelectionChanged() { _quietSelection = false; CancelGesture(); InvalidateLayers(); }
    public void InvalidateLayers() { _staticDirty = _notesDirty = true; InvalidateVisual(); }
    public void SetView(decimal beat, double vertical = 0)
    {
        _viewBeat = Math.Max(0, beat);
        _verticalOffset = Math.Clamp(vertical, 0, Math.Max(0, 15 * RowHeight - Math.Max(1, ActualHeight - RulerHeight - UnmappedHeight)));
        InvalidateLayers(); ViewChanged?.Invoke();
    }
    public void SetZoom(double horizontal, double vertical, Point? anchor = null)
    {
        Point p = anchor ?? new Point(LaneWidth + (ActualWidth - LaneWidth) / 2, RulerHeight + (ActualHeight - RulerHeight) / 2);
        decimal beat = BeatAt(p.X); double row = (p.Y - RulerHeight + _verticalOffset) / RowHeight;
        _zoom = Math.Clamp(horizontal, .0625, 8); _verticalZoom = Math.Clamp(vertical, .8, 3);
        SetView(Math.Max(0, beat - (decimal)((p.X - LaneWidth) / BeatWidth)), row * RowHeight - (p.Y - RulerHeight));
    }
    public void EnsurePlayheadVisible(bool centered = false)
    {
        decimal margin = Math.Min(VisibleBeats / 5, 1);
        if (_playhead < _viewBeat + margin || _playhead > _viewBeat + VisibleBeats - margin)
            SetView(Math.Max(0, _playhead - (centered ? VisibleBeats / 2 : VisibleBeats - margin)), _verticalOffset);
    }
    public decimal SnapBeat(decimal beat, bool free = false) => !SnapEnabled || free ? Math.Max(0, beat)
        : Math.Max(0, decimal.Floor(beat * _session.Project.GridSubdiv) / _session.Project.GridSubdiv);
    public decimal BeatAt(double x) => _viewBeat + (decimal)((x - LaneWidth) / BeatWidth);
    public int KeyAt(double y)
    {
        if (y < RulerHeight || y >= ActualHeight - UnmappedHeight) return -1;
        int key = 14 - (int)Math.Floor((y - RulerHeight + _verticalOffset) / RowHeight);
        return key >= 0 && key < _session.KeyCount ? key : -1;
    }
    public Rect Bounds(ProjectNote note)
    {
        double x = LaneWidth + (double)(note.Beat - _viewBeat) * BeatWidth;
        decimal length = note.Action == NoteAction.Tap ? 1m / _session.Project.GridSubdiv : note.LengthBeats;
        double width = Math.Max(7, (double)length * BeatWidth - 2);
        return new(x + 1, RulerHeight + (14 - note.Key) * RowHeight - _verticalOffset + 3, width, RowHeight - 6);
    }
    internal HashSet<int> KeysAtPlayhead()
    {
        if(_session==null||!_indexes.TryGetValue(_session.ActiveTrackId,out var index))return new();
        decimal tap=1m/_session.Project.GridSubdiv;
        return index.Query(_playhead,_playhead,tapWidth:tap)
            .Where(n=>_playhead<n.Beat+(n.Action==NoteAction.Tap?tap:n.LengthBeats)).Select(n=>n.Key).ToHashSet();
    }
    double MidiUnmappedHeight => _session != null && _unmapped.TryGetValue(_session.ActiveTrackId, out var notes) && notes.Count > 0 ? 29 : 0;
    double PercussionHeight => _references && _session.Project.Tracks.Any(t=>t.Id!=_session.ActiveTrackId && InstrumentCapabilities.ForTrack(t)?.Kind==GameInstrumentKind.UnpitchedPercussion)?29:0;
    double UnmappedHeight => MidiUnmappedHeight+PercussionHeight;
    public IEnumerable<(MidiSourceNote source, MappedMidiNote mapping)> UnmappedNotes()
    {
        if (_session != null && _unmapped.TryGetValue(_session.ActiveTrackId, out var notes)) foreach (var note in notes) yield return note;
    }
    internal ProjectNote[] HitNotes(Point point, bool handle = false)
    {
        int key = KeyAt(point.Y); if (key < 0 || point.X < LaneWidth) return Array.Empty<ProjectNote>();
        decimal beat = BeatAt(point.X), margin = (decimal)(10 / BeatWidth);
        return _indexes[_session.ActiveTrackId].Query(beat - margin, beat + margin, key, key, 1m / _session.Project.GridSubdiv)
            .Where(n =>
            {
                var r = Bounds(n); return point.Y >= r.Top - 2 && point.Y <= r.Bottom + 2
                && (handle ? r.Width < 22 ? point.X >= r.Right + 2 && point.X <= r.Right + 10 : Math.Abs(point.X - r.Right) <= 7
                    : point.X >= r.Left - 2 && point.X <= r.Right + 2);
            })
            .OrderByDescending(n => _session.Selected.Contains(n.Id)).ThenByDescending(n => n.Beat).ThenBy(n => n.Id).ToArray();
    }
    Brush Resource(string name, string fallback) => TryFindResource(name) as Brush ?? Brush(fallback);
    static SolidColorBrush Brush(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
    static Pen Stroke(Brush brush, double width = 1) { var p = new Pen(brush, width); p.Freeze(); return p; }
    void Text(DrawingContext dc, string text, Point position, Brush brush, double size = 11)
        => dc.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), position);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (_session == null || ActualWidth <= LaneWidth || ActualHeight <= RulerHeight) return;
        if (_staticDirty) { DrawGrid(); _staticDirty = false; }
        if (_notesDirty) { DrawNotes(); _notesDirty = false; }
        DrawOverlay(); DrawCursor();
    }
    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters) => new PointHitTestResult(this, hitTestParameters.HitPoint);
}
