using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class PianoRollOverview:FrameworkElement
{
    readonly VisualCollection _visuals;
    readonly DrawingVisual _notes=new(),_view=new();
    bool _dirty=true;
    decimal _extent=4;
    double _grab;
    public PianoRoll? Roll {get;set;}
    public Func<Guid,Color>? TrackColor {get;set;}
    public int NoteLayerBuilds {get;private set;}
    public int LastDrawnCells {get;private set;}
    public decimal ExtentBeats=>_extent;
    protected override int VisualChildrenCount=>_visuals.Count;
    protected override Visual GetVisualChild(int index)=>_visuals[index];
    public PianoRollOverview()
    {
        ClipToBounds=true;Cursor=Cursors.Hand;_visuals=new(this){_notes,_view};
        SizeChanged+=(_,_)=>InvalidateNotes();
        Unloaded+=(_,_)=>ReleaseMouseCapture();
    }
    Brush Resource(string key)=>(Brush)FindResource(key);
    public void InvalidateNotes(){_dirty=true;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);RefreshView();}
    public void RefreshView()
    {
        if(Roll?.Session is not {} session||ActualWidth<1||ActualHeight<1)return;
        if(_dirty)
        {
            _dirty=false;NoteLayerBuilds++;LastDrawnCells=0;
            var notes=session.Project.Tracks.SelectMany(t=>t.Notes).ToArray();
            decimal last=notes.Select(n=>n.Beat+n.LengthBeats).DefaultIfEmpty(0).Max();
            decimal end=new EditorMeterMap(session.Project.Meters).EndOfBar(last,notes.Any(n=>n.Action==NoteAction.Tap&&n.Beat==last));
            _extent=Math.Max(end+4,Roll.VisibleBeats);
            using var drawing=_notes.RenderOpen();drawing.DrawRoundedRectangle(Resource("PanelBg"),new Pen(Resource("BoxBorder"),1),new Rect(RenderSize),4,4);
            var cells=new HashSet<(int x,int key,bool active)>();int width=Math.Max(1,(int)ActualWidth);
            foreach(var track in session.Project.Tracks)
            {
                bool active=track.Id==session.ActiveTrackId;
                var brush=new SolidColorBrush(TrackColor?.Invoke(track.Id)??Color.FromRgb(114,177,255)){Opacity=active?1:.35};brush.Freeze();
                foreach(var note in track.Notes)
                {
                    int x=Math.Clamp((int)(note.Beat/_extent*width),0,width-1);
                    if(!cells.Add((x,note.Key,active)))continue;
                    double y=3+(14-note.Key)*Math.Max(1,(ActualHeight-6)/15);
                    double length=Math.Max(1,Math.Min(ActualWidth-x,Math.Max(2,(double)(note.LengthBeats/_extent)*ActualWidth)));
                    drawing.DrawRectangle(brush,null,new Rect(x,y,length,2));LastDrawnCells++;
                }
            }
        }
        using var overlay=_view.RenderOpen();
        double left=Math.Clamp((double)(Roll.ViewBeat/_extent)*ActualWidth,0,ActualWidth);
        double size=Math.Min(ActualWidth-left,(double)(Roll.VisibleBeats/_extent)*ActualWidth);
        var accent=Resource("Accent");var fill=accent.Clone();fill.Opacity=.12;
        overlay.DrawRectangle(fill,new Pen(accent,1.5),new Rect(left,1,Math.Max(0,size),Math.Max(0,ActualHeight-2)));
        double cursor=(double)(Roll.PlayheadBeat/_extent)*ActualWidth;
        if(cursor>=0&&cursor<=ActualWidth)overlay.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(228,121,104)),1.5),new Point(cursor,1),new Point(cursor,ActualHeight-1));
    }
    internal void Navigate(double x,bool begin)
    {
        if(Roll==null||ActualWidth<1)return;
        double left=(double)(Roll.ViewBeat/_extent)*ActualWidth,span=(double)(Roll.VisibleBeats/_extent)*ActualWidth;
        if(begin)_grab=x>=left&&x<=left+span?x-left:span/2;
        Roll.SetView(Math.Clamp((decimal)((x-_grab)/ActualWidth)*_extent,0,Math.Max(0,_extent-Roll.VisibleBeats)));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e){Navigate(e.GetPosition(this).X,true);CaptureMouse();e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e){if(IsMouseCaptured)Navigate(e.GetPosition(this).X,false);}
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e){ReleaseMouseCapture();e.Handled=true;}
}
