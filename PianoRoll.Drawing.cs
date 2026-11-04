using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class PianoRoll
{
    static readonly Brush Blue = Brush("#72b1ff"), Amber = Brush("#efb24f"), CursorBrush = Brush("#e47968");
    void DrawGrid()
    {
        using var dc = _grid.RenderOpen(); GridLayerBuilds++;
        var bg = Resource("WindowBg", "#121214"); var box = Resource("BoxBg", "#262626");
        var panel = Resource("PanelBg", "#1c1c1c"); var sub = Resource("SubTextFg", "#9a9ab0");
        var line = Resource("BoxBorder", "#41414c");
        dc.DrawRectangle(bg, null, new Rect(RenderSize));
        double bottom = ActualHeight - UnmappedHeight;
        for (int row = 0; row < 15; row++)
        {
            double y = RulerHeight + row * RowHeight - _verticalOffset; if (y + RowHeight < RulerHeight || y > bottom) continue;
            double top = Math.Max(RulerHeight, y); double height = Math.Min(y + RowHeight, bottom) - top;
            if (height <= 0) continue;
            dc.DrawRectangle(row / 5 == 1 ? panel : box, null, new Rect(0, top, ActualWidth, height));
            dc.DrawLine(Stroke(line, .5), new Point(0, y + RowHeight), new Point(ActualWidth, y + RowHeight));
        }
        decimal right = _viewBeat + VisibleBeats;
        decimal step = 1m / _session.Project.GridSubdiv;
        if ((double)step * BeatWidth < 5) step = 1;
        while ((double)step * BeatWidth < 5) step *= 2;
        for (decimal b = decimal.Floor(_viewBeat / step) * step; b <= right; b += step)
        {
            double x = LaneWidth + (double)(b - _viewBeat) * BeatWidth;
            if (x < LaneWidth) continue;
            dc.DrawLine(Stroke(line, b % 1 == 0 ? .8 : .4), new Point(x, RulerHeight), new Point(x, bottom));
        }
        dc.DrawRectangle(panel, null, new Rect(0, 0, ActualWidth, RulerHeight));
        Text(dc, Lang.S("ed.keys"), new Point(8, 12), sub, 10);
        var timeline = new ProjectTimeline(_session.Project.Tempos);
        foreach (var bar in new EditorMeterMap(_session.Project.Meters).Visible(_viewBeat, right))
        {
            double x = LaneWidth + (double)(bar.Beat - _viewBeat) * BeatWidth; if (x < LaneWidth - 1) continue;
            dc.DrawLine(Stroke(sub, 1), new Point(x, RulerHeight), new Point(x, bottom));
            Text(dc, bar.Number + (bar.Partial ? "*" : ""), new Point(x + 4, 1), Resource("TextFg", "#e0e0e0"), 11);
            Text(dc, TimeLabel(timeline.BeatToMicroseconds(bar.Beat) / 1_000_000), new Point(x + 4, 21), sub, 9);
            if (bar.Beat == bar.Meter.Beat && (double)(bar.Meter.Numerator * 4m / bar.Meter.Denominator) * BeatWidth >= 95)
                Text(dc, $"{bar.Meter.Numerator}/{bar.Meter.Denominator}", new Point(x + 28, 1), sub, 10);
        }
        foreach (var tempo in _session.Project.Tempos.Where(t => t.Beat > 0 && t.Beat >= _viewBeat && t.Beat <= right))
        {
            double x = LaneWidth + (double)(tempo.Beat - _viewBeat) * BeatWidth;
            Text(dc, $"{60_000_000m / tempo.MicrosecondsPerBeat:0.##} BPM", new Point(x + 55, 1), Amber, 9);
        }
        dc.PushClip(new RectangleGeometry(new Rect(0, RulerHeight, LaneWidth, Math.Max(0, bottom - RulerHeight))));
        for (int key = 0; key < 15; key++)
        {
            double y = RulerHeight + (14 - key) * RowHeight - _verticalOffset;
            dc.DrawRectangle(panel, null, new Rect(0, y, LaneWidth, RowHeight));
            var state = PerformanceCompatibility.Evaluate(_session.ActiveTrack, new() { Key = key });
            string pitch = state.Key?.MidiPitch is { } p ? MidiImportDialog.Pitch(p) : state.Key?.SoundId ?? "";
            Text(dc, $"K{key} {pitch}", new Point(8, y + (RowHeight - 14) / 2), state.Availability == KeyAvailability.Unavailable ? line : sub, 10);
        }
        dc.Pop(); dc.DrawLine(Stroke(line), new Point(LaneWidth, 0), new Point(LaneWidth, bottom));
        if (MidiUnmappedHeight > 0)
        {
            dc.DrawRectangle(panel, Stroke(line), new Rect(0, bottom, ActualWidth, UnmappedHeight));
            Text(dc, Lang.S("ed.unmapped"), new Point(5, bottom + 6), Amber, 10);
            foreach (var (source, _) in UnmappedNotes())
            {
                decimal beat = _session.Project.MidiConversion!.Source.Beat(source.StartTick);
                double x = LaneWidth + (double)(beat - _viewBeat) * BeatWidth;
                if (x < LaneWidth || x > ActualWidth - 30) continue;
                dc.DrawRoundedRectangle(null, Stroke(Amber), new Rect(x, bottom + 4, 48, 20), 3, 3);
                Text(dc, "! " + MidiImportDialog.Pitch(source.Pitch), new Point(x + 3, bottom + 7), Amber, 9);
            }
        }
        if(PercussionHeight>0)
        {
            double top=bottom+MidiUnmappedHeight;
            dc.DrawRectangle(panel,Stroke(line),new Rect(0,top,ActualWidth,29));Text(dc,Lang.S("map.percussion"),new(5,top+6),sub,10);
            int drawn=0;
            foreach(var track in _session.Project.Tracks.Where(t=>t.Id!=_session.ActiveTrackId && InstrumentCapabilities.ForTrack(t)?.Kind==GameInstrumentKind.UnpitchedPercussion))
            foreach(var note in _indexes[track.Id].Query(_viewBeat,right,tapWidth:1m/_session.Project.GridSubdiv))
            {
                if(++drawn>2000)break;double x=LaneWidth+(double)(note.Beat-_viewBeat)*BeatWidth;if(x<LaneWidth||x>ActualWidth-12)continue;
                var key=InstrumentCapabilities.ForTrack(track)?.Resolve(note.Key,track.KeyDomain);
                dc.DrawRoundedRectangle(null,Stroke(Amber),new Rect(x,top+5,Math.Min(48,ActualWidth-x),18),3,3);
                Text(dc,key?.SoundId??"♪",new(x+3,top+7),sub,9);
            }
        }
    }
    public static string TimeLabel(decimal seconds)
    {
        decimal minutes = decimal.Floor(seconds / 60); return $"{minutes:0}:{seconds % 60:00.00}";
    }
    void DrawNotes()
    {
        using var dc = _notes.RenderOpen(); NoteLayerBuilds++; LastRenderedNotes = 0; ReferenceOutOfRange = 0;
        dc.PushClip(new RectangleGeometry(new Rect(LaneWidth, RulerHeight, Math.Max(0, ActualWidth - LaneWidth), Math.Max(0, ActualHeight - RulerHeight - UnmappedHeight))));
        decimal left = _viewBeat - 1m / _session.Project.GridSubdiv, right = _viewBeat + VisibleBeats;
        if (_references)
        {
            int referenceCount = 0;
            var target = _session.ActiveTrack.GameTarget;
            foreach (var track in _session.Project.Tracks.Where(t => t.Id != _session.ActiveTrackId))
            {
                if (referenceCount >= 4000) break;
                var fill = Resource("SubTextFg", "#9a9ab0").Clone(); fill.Opacity = .45; fill.Freeze();
                foreach (var n in _indexes[track.Id].Query(left, right, tapWidth: 1m / _session.Project.GridSubdiv))
                {
                    int key = PerformanceCompatibility.ReferenceKey(track, n, _session.ActiveTrack);
                    if (key < 0 && InstrumentCapabilities.ForTrack(track) == null && InstrumentCapabilities.ForTrack(_session.ActiveTrack) == null
                        && track.KeyDomain == _session.ActiveTrack.KeyDomain
                        && n.Key >= 0 && n.Key < _session.KeyCount) key = n.Key;
                    if (key < 0) { ReferenceOutOfRange++; continue; }
                    if (++referenceCount > 4000) break;
                    DrawNote(dc, n with { Key = key }, fill, ghost: true);
                }
            }
        }
        var color = new SolidColorBrush(TrackColor?.Invoke(_session.ActiveTrackId) ?? Color.FromRgb(114, 177, 255)); color.Freeze();
        var visible = _indexes[_session.ActiveTrackId].Query(left, right, tapWidth: 1m / _session.Project.GridSubdiv).ToArray();
        DenseView = visible.Length > 1500;
        ToolTip = DenseView ? Lang.S("ed.denseView") : null;
        if (DenseView) { DrawDenseNotes(dc, visible, color); dc.Pop(); return; }
        var ends = new decimal[15]; Array.Fill(ends, -1); var counts = new int[15];
        foreach (var n in visible)
        {
            Rect r = Bounds(n); if (r.Bottom < RulerHeight || r.Top > ActualHeight - UnmappedHeight) continue;
            LastRenderedNotes++; DrawNote(dc, n, color);
            if (ends[n.Key] > n.Beat)
            {
                double x = Math.Max(LaneWidth, r.X); double stop = Math.Min(r.Right, LaneWidth + (double)(ends[n.Key] - _viewBeat) * BeatWidth);
                if (stop > x)
                {
                    for (double dx = x; dx < Math.Min(stop, x + 160); dx += 8)
                        dc.DrawLine(Stroke(Amber, 1), new Point(dx, r.Bottom), new Point(Math.Min(dx + 6, stop), r.Top));
                    if (r.Width > 22) Text(dc, "×" + (++counts[n.Key] + 1), new Point(x + 2, r.Top), Amber, 9);
                }
            }
            else counts[n.Key] = 0;
            ends[n.Key] = Math.Max(ends[n.Key], n.Beat + n.LengthBeats);
        }
        dc.Pop();
    }
    void DrawDenseNotes(DrawingContext dc, ProjectNote[] visible, Brush fill)
    {
        // 密集概览只合并绘图几何，命中、选择、编辑始终使用完整的音符索引。
        foreach (var lane in visible.GroupBy(n => n.Key))
        {
            var laneState=PerformanceCompatibility.Evaluate(_session.ActiveTrack,new(){Key=lane.Key});
            var laneFill=laneState.Availability==KeyAvailability.Unavailable?Resource("SubTextFg","#777781"):fill;
            double y = RulerHeight + (14 - lane.Key) * RowHeight - _verticalOffset + 3;
            if (y + RowHeight < RulerHeight || y > ActualHeight - UnmappedHeight) continue;
            var starts = new HashSet<int>(); var regions = new List<(double left, double right, bool approximate)>();
            foreach (var n in lane)
            {
                var bounds = Bounds(n); double left = Math.Max(LaneWidth, bounds.Left), right = Math.Min(ActualWidth, bounds.Right);
                if (left >= right) continue;
                starts.Add((int)left);
                bool approximate = n.SourceMidiNoteId is { } id && _mapping.GetValueOrDefault(id) == PitchMappingKind.Approximate;
                if (regions.Count > 0 && left <= regions[^1].right + 1)
                {
                    var previous = regions[^1]; regions[^1] = (previous.left, Math.Max(previous.right, right), previous.approximate || approximate);
                }
                else regions.Add((left, right, approximate));
            }
            foreach (var region in regions)
            {
                dc.DrawRoundedRectangle(laneFill, Stroke(region.approximate ? Amber : laneFill), new Rect(region.left, y, region.right - region.left, RowHeight - 6), 3, 3);
                LastRenderedNotes++;
            }
            var onsets = new StreamGeometry();
            using (var lines = onsets.Open()) foreach (int x in starts)
            {
                lines.BeginFigure(new Point(x, y + 2), false, false); lines.LineTo(new Point(x, y + RowHeight - 8), true, false);
            }
            onsets.Freeze(); dc.DrawGeometry(null, Stroke(Resource("PanelBg", "#1c1c1c"), .7), onsets);
            LastRenderedNotes += starts.Count;
            if (regions.Count > 0) Text(dc, "▦ ×" + lane.Count(), new Point(regions[0].left + 3, y), Brush("#163251"), 9);
        }
    }
    void DrawNote(DrawingContext dc, ProjectNote note, Brush fill, bool ghost = false, bool selected = false, bool resizeHandle = false)
    {
        var rect = Bounds(note); if (rect.Width <= 0) return;
        var compatibility = ghost ? null : PerformanceCompatibility.Evaluate(_session.ActiveTrack, note);
        if (compatibility?.Availability == KeyAvailability.Unavailable) fill = Resource("SubTextFg", "#777781");
        double left = Math.Max(LaneWidth, rect.Left), right = Math.Min(ActualWidth + 1, rect.Right);
        if (right <= left || rect.Bottom < RulerHeight || rect.Top > ActualHeight - UnmappedHeight) return;
        // 裁剪几何，超长时值不生成超大绘图对象。
        var clipped = new Rect(left, rect.Y, right - left, rect.Height);
        var pen = Stroke(selected ? Resource("TextFg", "#ffffff") : fill);
        if (ghost) pen = new Pen(fill, 1) { DashStyle = DashStyles.Dash };
        dc.DrawRoundedRectangle(fill, pen, clipped, 3, 3);
        if (compatibility?.Availability == KeyAvailability.Unavailable)
        {
            for (double x = left; x < Math.Min(right, left + 200); x += 8)
                dc.DrawLine(Stroke(Resource("PanelBg", "#222228")), new(x, rect.Bottom), new(Math.Min(x + 7, right), rect.Top));
            if (clipped.Width > 16) Text(dc, "×", new(left + 2, rect.Y), Resource("TextFg", "#ffffff"), 10);
        }
        if (note.Action == NoteAction.Tap && !ghost)
            dc.DrawEllipse(Brush("#163251"), null, new Point(rect.Left + Math.Min(rect.Width / 2, 8), rect.Y + rect.Height / 2), 1.6, 1.6);
        else if (rect.Right <= ActualWidth) dc.DrawLine(Stroke(Brush("#23384d"), 1.5), new Point(rect.Right - 4, rect.Top + 2), new Point(rect.Right - 4, rect.Bottom - 2));
        if (!ghost && (compatibility?.Approximate == true || note.SourceMidiNoteId is { } id && _mapping.GetValueOrDefault(id) == PitchMappingKind.Approximate))
        {
            dc.DrawRoundedRectangle(null, new Pen(Amber, 1) { DashStyle = DashStyles.Dash }, clipped, 3, 3);
            if (clipped.Width >= 16) Text(dc, "≈", new Point(left + 2, rect.Y - 1), Brush("#163251"), 10);
        }
        if (resizeHandle && rect.Right >= LaneWidth && rect.Right <= ActualWidth)
            dc.DrawRoundedRectangle(Resource("TextFg", "#ffffff"), Stroke(Amber), new Rect(rect.Right - 3, rect.Y - 2, 6, rect.Height + 4), 2, 2);
    }
    void DrawOverlay()
    {
        using var dc = _overlay.RenderOpen(); if (_session == null || ActualWidth < LaneWidth || ActualHeight < RulerHeight) return;
        dc.PushClip(new RectangleGeometry(new Rect(LaneWidth, RulerHeight, ActualWidth - LaneWidth, Math.Max(0, ActualHeight - RulerHeight - UnmappedHeight))));
        decimal extra = _gesture == Gesture.Resize ? Math.Max(0, _deltaBeat) : 0;
        var visible = _session.Selected.Count == 0 ? Enumerable.Empty<ProjectNote>()
            : _indexes[_session.ActiveTrackId].Query(_viewBeat - _deltaBeat - extra - 1m / _session.Project.GridSubdiv, _viewBeat + VisibleBeats - _deltaBeat, tapWidth: 1m / _session.Project.GridSubdiv);
        int count = 0;
        foreach (var n in visible.Where(n => _session.Selected.Contains(n.Id)))
        {
            if (++count > 8000) break;
            bool editing = _created == null && (_gesture is Gesture.Move or Gesture.Resize) && _originals.Any(o => o.Id == n.Id);
            if (!_quietSelection || n.Id == _hoverNote || editing)
                DrawNote(dc, editing ? PreviewNote(n) : n, Amber, selected: true, resizeHandle: n.Id == _hoverHandle || editing && _gesture == Gesture.Resize);
        }
        if (_hoverNote is { } hovered && !_session.Selected.Contains(hovered) && _session.Find(hovered) is { } hover && hover.TrackId == _session.ActiveTrackId)
            DrawNote(dc, hover, Amber, selected: true, resizeHandle: hovered == _hoverHandle);
        foreach (var n in _paint.Values.Take(8000)) DrawNote(dc, n, Amber, selected: true);
        if (_created is { } created) DrawNote(dc, PreviewNote(created), Amber, selected: true);
        if (_gesture == Gesture.Select)
        {
            var rectangle = SelectionRect(_last); dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(50, 90, 160, 255)), Stroke(Blue), rectangle);
        }
        foreach (var id in _erase) if (_session.Find(id) is { } n) dc.DrawRectangle(null, Stroke(Amber, 2), Bounds(n));
        dc.Pop();
        if (_gesture is Gesture.Move or Gesture.Resize && _originals.Length > 0)
        {
            var n = PreviewNote(_originals[0]); string value = n.Action == NoteAction.Tap ? Lang.S("ed.tap18")
                : string.Format(CultureInfo.CurrentCulture, Lang.S("ed.lengthValue"), n.LengthBeats);
            double x = Math.Clamp(_last.X + 12, LaneWidth, Math.Max(LaneWidth, ActualWidth - 180)); double y = Math.Clamp(_last.Y - 35, RulerHeight, Math.Max(RulerHeight, ActualHeight - 32));
            dc.DrawRoundedRectangle(Resource("PanelBg", "#1c1c1c"), Stroke(Amber), new Rect(x, y, 174, 28), 4, 4);
            Text(dc, value, new Point(x + 8, y + 6), Resource("TextFg", "#e0e0e0"), 11);
        }
    }
    void DrawCursor()
    {
        using var dc = _cursor.RenderOpen(); double x = LaneWidth + (double)(_playhead - _viewBeat) * BeatWidth;
        if (x < LaneWidth || x > ActualWidth || ActualHeight <= 0) return;
        dc.DrawLine(Stroke(CursorBrush, 1.5), new Point(x, 0), new Point(x, ActualHeight));
        dc.DrawGeometry(CursorBrush, null, Geometry.Parse($"M{(x - 5).ToString(CultureInfo.InvariantCulture)},0 L{(x + 5).ToString(CultureInfo.InvariantCulture)},0 L{x.ToString(CultureInfo.InvariantCulture)},6 Z"));
    }
}
