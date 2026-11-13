using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class SheetOverlayWindow : Window
{
    const int GwlExStyle=-20;
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetStyle(IntPtr h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] static extern IntPtr SetStyle(IntPtr h,int index,IntPtr style);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out PhysicalRect bounds);
    readonly OverlayCanvas _canvas=new();
    IntPtr _handle;
    PhysicalRect _lastBounds;
    int _lastSignature;
    IReadOnlyList<KeyLocation>? _lastLocations;
    bool _placementQueued;
    public IntPtr Handle=>_handle;
    public SheetOverlayWindow():this(FeatureAvailability.RequireSheetMapping()) { }
    SheetOverlayWindow(bool available)
    {
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
        ShowInTaskbar=false;ShowActivated=false;Focusable=false;Topmost=true;IsHitTestVisible=false;Content=_canvas;
        SourceInitialized+=(_,_)=>
        {
            _handle=new WindowInteropHelper(this).Handle;
            SetStyle(_handle,GwlExStyle,new IntPtr(GetStyle(_handle,GwlExStyle).ToInt64()|0x80000|0x20|0x8000000|0x80));
            HwndSource.FromHwnd(_handle).AddHook((IntPtr h,int message,IntPtr w,IntPtr l,ref bool handled)=>
            {
                if(message==0x84){handled=true;return new IntPtr(-1);}
                if(message==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;
            });
        };
    }
    public void Present(PhysicalRect bounds,uint dpi,InstrumentCapability capability,IReadOnlyList<KeyLocation> keys,
        IReadOnlyDictionary<int,string> noteKeys,PracticeHintSnapshot hints,string mode)
    {
        FeatureAvailability.RequireSheetMapping();
        var signature=new HashCode();foreach(var k in hints.Current)signature.Add(k);signature.Add(-1);foreach(var k in hints.Next)signature.Add(k);
        foreach(var h in hints.Holds){signature.Add(h.Key);signature.Add(Math.Round(h.RemainingMs/33));signature.Add(h.Release);signature.Add(h.AwaitingPress);}
        signature.Add(hints.Holds.Count);
        signature.Add(hints.Paused);signature.Add(hints.Completed);signature.Add(hints.LastJudgment);signature.Add(mode);signature.Add(dpi);
        int hash=signature.ToHashCode();bool visible=IsVisible;
        if(visible&&bounds==_lastBounds&&hash==_lastSignature&&ReferenceEquals(keys,_lastLocations))return;
        if(!visible)Show();
        bool moved=!visible||bounds!=_lastBounds;
        if(moved)SetWindowPos(_handle,new IntPtr(-1),bounds.Left,bounds.Top,bounds.Width,bounds.Height,0x10|0x40);
        _lastBounds=bounds;_lastSignature=hash;_lastLocations=keys;
        if(moved&&!_placementQueued)
        {
            _placementQueued=true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>
            {
                _placementQueued=false;
                if(!IsVisible||!GetWindowRect(_handle,out var actual)||actual==_lastBounds)return;
                // 跨屏 DPI 布局完成后再核对物理边界，避免框架二次缩放覆盖已校准的位置。
                var b=_lastBounds;SetWindowPos(_handle,new IntPtr(-1),b.Left,b.Top,b.Width,b.Height,0x10|0x40);
            }));
        }
        _canvas.Capability=capability;_canvas.Locations=keys;_canvas.NoteKeys=noteKeys;_canvas.Hints=hints;_canvas.Mode=mode;_canvas.InvalidateVisual();
    }
    public sealed class OverlayCanvas:FrameworkElement
    {
        public InstrumentCapability? Capability;
        public IReadOnlyList<KeyLocation> Locations=Array.Empty<KeyLocation>();
        public IReadOnlyDictionary<int,string> NoteKeys=new Dictionary<int,string>();
        public PracticeHintSnapshot? Hints;
        public string Mode="";
        public int Frames {get;private set;}
        static Brush Color(string hex){var b=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));b.Freeze();return b;}
        static readonly Brush Blue=Color("#72b1ff"),Purple=Color("#b8a0f5"),Green=Color("#74c6b0"),Amber=Color("#efb24f"),Red=Color("#f08d83"),Text=Color("#e0e0e0"),Panel=Color("#D9111B23");
        void Label(DrawingContext dc,string text,Point at,Brush color,double size=12)
        {dc.DrawText(new FormattedText(text,CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip),at);}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);Frames++;if(Hints is not {} h||Capability==null||ActualWidth<=0||ActualHeight<=0)return;
            var keyRects=Locations.ToDictionary(k=>k.KeyId,k=>new Rect((k.X-k.Width/2)*ActualWidth,(k.Y-k.Height/2)*ActualHeight,k.Width*ActualWidth,k.Height*ActualHeight));
            var occupied=keyRects.Values.Select(r=>{r.Inflate(5,5);return r;}).ToList();
            Rect hud=new(16,16,Math.Min(ActualWidth-32,610),36);
            if(hud.Width>100 && !occupied.Any(r=>r.IntersectsWith(hud)))
            {G2Geometry.Draw(dc,Panel,null,hud,7,7);Label(dc,"SMAP · "+Mode+" · "+Lang.S(h.Completed?"map.done":h.Paused?"map.pause":"map.readonly"),new(28,25),Text);occupied.Add(hud);}
            foreach(var pair in NoteKeys)
            {
                if(!keyRects.TryGetValue(pair.Value,out var rect))continue;
                var hold=h.Holds.FirstOrDefault(k=>k.Key==pair.Key);bool current=h.Current.Contains(pair.Key),next=h.Next.Contains(pair.Key);
                bool error=h.LastJudgment is {Kind:PracticeJudgmentKind.EarlyRelease or PracticeJudgmentKind.LateRelease or PracticeJudgmentKind.Missed or PracticeJudgmentKind.UnexpectedKey or PracticeJudgmentKind.RepeatedPress} j && j.Key==pair.Key;
                bool completed=h.LastJudgment is {Kind:PracticeJudgmentKind.Completed} done&&done.Key==pair.Key;
                if(!current&&!next&&hold==null&&!error&&!completed)continue;
                var color=error?Red:hold!=null?hold.Release?Amber:Green:completed?Green:current?Blue:Purple;
                var outline=rect;outline.Inflate(4,4);var pen=new Pen(color,2){DashStyle=!current&&hold==null&&!error?DashStyles.Dash:DashStyles.Solid};G2Geometry.Draw(dc,null,pen,outline,10,10);
                if(hold is {TotalMs:>0,RemainingMs:>0})
                {
                    double fraction=Math.Clamp(hold.RemainingMs/hold.TotalMs,0,1),radius=Math.Max(rect.Width,rect.Height)/2+9;
                    var center=new Point(rect.X+rect.Width/2,rect.Y+rect.Height/2);
                    if(fraction>.999)dc.DrawEllipse(null,new Pen(Green,2),center,radius,radius);
                    else
                    {
                        double angle=-Math.PI/2+2*Math.PI*fraction;var arc=new StreamGeometry();
                        using(var drawing=arc.Open()){drawing.BeginFigure(new(center.X,center.Y-radius),false,false);
                            drawing.ArcTo(new(center.X+Math.Cos(angle)*radius,center.Y+Math.Sin(angle)*radius),new(radius,radius),0,fraction>.5,SweepDirection.Clockwise,true,false);}
                        arc.Freeze();dc.DrawGeometry(null,new Pen(Green,2),arc);
                    }
                }
                if(current&&next){var outer=outline;outer.Inflate(5,5);G2Geometry.Draw(dc,null,new Pen(Purple,1.5){DashStyle=DashStyles.Dash},outer,12,12);}
                string text=error?Lang.S("map.error"):hold!=null?hold.AwaitingPress?Lang.S("map.now"):hold.Release?Lang.S("map.release"):$"{Lang.S("map.hold")} {hold.RemainingMs/1000:0.0}s":completed?"✓":Lang.S(current?"map.now":"map.next");
                double labelWidth=Math.Min(160,Math.Max(70,text.Length*12)),height=23;
                var candidates=new[]{new Rect(rect.X+rect.Width/2-labelWidth/2,rect.Bottom+12,labelWidth,height),new Rect(rect.X+rect.Width/2-labelWidth/2,rect.Top-35,labelWidth,height)};
                foreach(var label in candidates)
                    if(label.Left>=2&&label.Right<ActualWidth-2&&label.Top>=2&&label.Bottom<ActualHeight-2&&!occupied.Any(r=>r.IntersectsWith(label)))
                    {G2Geometry.Draw(dc,Panel,new Pen(color,1),label,4,4);Label(dc,text,new(label.X+6,label.Y+3),color,11);occupied.Add(label);break;}
            }
        }
    }
}
