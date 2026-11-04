using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class PianoRollOverview:FrameworkElement
{
    readonly VisualCollection _visuals;
    readonly DrawingVisual _notes=new(),_view=new();
    bool _dirty=true,_hover;
    decimal _extent=4;
    double _grab,_edgeGrab;
    enum RangeDrag { None, Pan, Left, Right }
    RangeDrag _drag;
    decimal _fixedLeft,_fixedRight,_dragExtent;
    const double HandleWidth=8;
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
        SizeChanged+=(_,_)=>{EndRangeDrag();ReleaseMouseCapture();InvalidateNotes();};
        Unloaded+=(_,_)=>{_hover=false;EndRangeDrag();ReleaseMouseCapture();};
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
        double cursor=(double)(Roll.CursorDisplayBeat/_extent)*ActualWidth;
        if(cursor>=0&&cursor<=ActualWidth)overlay.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(228,121,104)),1.5),new Point(cursor,1),new Point(cursor,ActualHeight-1));
        if((_hover||_drag!=RangeDrag.None)&&size>0&&ActualWidth>=HandleWidth&&ActualHeight>4)
        {
            foreach(double edge in new[]{left,left+size})
            {
                double x=Math.Clamp(edge-HandleWidth/2,0,ActualWidth-HandleWidth);
                overlay.DrawRoundedRectangle(accent,null,new Rect(x,2,HandleWidth,ActualHeight-4),2,2);
                double top=Math.Max(4,ActualHeight/2-6),bottom=Math.Min(ActualHeight-4,ActualHeight/2+6);
                var grip=new Pen(Resource("TextFg"),1);
                overlay.DrawLine(grip,new Point(x+3,top),new Point(x+3,bottom));
                overlay.DrawLine(grip,new Point(x+5,top),new Point(x+5,bottom));
            }
        }
    }
    internal void Navigate(double x,bool begin)
    {
        if(Roll==null||ActualWidth<1)return;
        double left=(double)(Roll.ViewBeat/_extent)*ActualWidth,span=(double)(Roll.VisibleBeats/_extent)*ActualWidth;
        if(begin)_grab=x>=left&&x<=left+span?x-left:span/2;
        Roll.SetView(Math.Clamp((decimal)((x-_grab)/ActualWidth)*_extent,0,Math.Max(0,_extent-Roll.VisibleBeats)),Roll.VerticalOffset);
    }
    RangeDrag EdgeAt(double x)
    {
        if(Roll==null||ActualWidth<HandleWidth)return RangeDrag.None;
        double left=Math.Clamp((double)(Roll.ViewBeat/_extent)*ActualWidth,0,ActualWidth);
        double right=Math.Clamp((double)((Roll.ViewBeat+Roll.VisibleBeats)/_extent)*ActualWidth,0,ActualWidth);
        double dl=Math.Abs(x-left),dr=Math.Abs(x-right);
        if(Math.Min(dl,dr)>HandleWidth)return RangeDrag.None;
        return dl<=dr?RangeDrag.Left:RangeDrag.Right;
    }
    internal void BeginRangeDrag(double x)
    {
        EndRangeDrag();
        if(Roll==null||ActualWidth<1)return;
        _drag=EdgeAt(x);_fixedLeft=Roll.ViewBeat;_fixedRight=Roll.ViewBeat+Roll.VisibleBeats;_dragExtent=_extent;
        _edgeGrab=x-(double)((_drag==RangeDrag.Left?_fixedLeft:_fixedRight)/_extent)*ActualWidth;
        if(_drag==RangeDrag.None){_drag=RangeDrag.Pan;Navigate(x,true);}
        Cursor=_drag==RangeDrag.Pan?Cursors.Hand:Cursors.SizeWE;
        RefreshView();
    }
    internal void DragRange(double x)
    {
        if(Roll==null||ActualWidth<1||_drag==RangeDrag.None)return;
        if(_drag==RangeDrag.Pan){Navigate(x,false);return;}
        decimal width=(decimal)(Math.Max(1,Roll.ActualWidth-PianoRoll.LaneWidth)/64);
        decimal minimum=width/8,maximum=width/.0625m;
        decimal limit=_drag==RangeDrag.Left?_fixedRight:_dragExtent-_fixedLeft;
        if(limit<minimum)return;
        double edge=x<=HandleWidth?0:x>=ActualWidth-HandleWidth?ActualWidth:Math.Clamp(x-_edgeGrab,0,ActualWidth);
        decimal beat=(decimal)(edge/ActualWidth)*_dragExtent;
        decimal span=Math.Clamp(_drag==RangeDrag.Left?_fixedRight-beat:beat-_fixedLeft,minimum,Math.Min(maximum,limit));
        double anchor=_drag==RangeDrag.Left?Roll.ActualWidth:PianoRoll.LaneWidth;
        Roll.SetZoom((double)(width/span),Roll.VerticalZoom,new Point(anchor,PianoRoll.RulerHeight));
    }
    internal void EndRangeDrag(){_drag=RangeDrag.None;Cursor=Cursors.Hand;RefreshView();}
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {BeginRangeDrag(e.GetPosition(this).X);if(!CaptureMouse())EndRangeDrag();e.Handled=true;}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        double x=e.GetPosition(this).X;
        if(IsMouseCaptured)DragRange(x);else Cursor=EdgeAt(x)==RangeDrag.None?Cursors.Hand:Cursors.SizeWE;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {if(IsMouseCaptured)DragRange(e.GetPosition(this).X);EndRangeDrag();ReleaseMouseCapture();e.Handled=true;}
    protected override void OnLostMouseCapture(MouseEventArgs e){EndRangeDrag();base.OnLostMouseCapture(e);}
    protected override void OnMouseEnter(MouseEventArgs e){_hover=true;RefreshView();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(MouseEventArgs e){_hover=false;if(!IsMouseCaptured)Cursor=Cursors.Hand;RefreshView();base.OnMouseLeave(e);}
}
