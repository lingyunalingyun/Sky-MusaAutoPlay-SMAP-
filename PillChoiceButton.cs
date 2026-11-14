using Border = SMAP_WPF.G2Border;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SMAP_WPF;

public sealed class PillChoiceButton : Button
{
    Border _chrome=null!,_header=null!,_separator=null!;
    StackPanel _content=null!;
    ListBox _options=null!;
    Window? _owner;
    int _generation;
    bool _closing;
    public bool IsOpen {get;private set;}
    public bool ReduceMotion {get;set;}
    public Border Chrome {get{ApplyTemplate();return _chrome;}}
    public ListBox Options {get{ApplyTemplate();return _options;}}
    public PillChoiceButton()
    {
        var resources=new ResourceDictionary{Source=new Uri("/SMAP;component/ChoicePopupStyles.xaml",UriKind.Relative)};
        Style=(Style)resources["PillChoiceButtonStyle"];
        SizeChanged+=(_,_)=>{if(_chrome!=null&&!IsOpen)ResetSize();};
        Unloaded+=(_,_)=>Close(true);
    }
    public override void OnApplyTemplate()
    {
        if(IsOpen)Close(true);base.OnApplyTemplate();
        _chrome=(Border)GetTemplateChild("PART_Chrome");_header=(Border)GetTemplateChild("PART_Header");
        _separator=(Border)GetTemplateChild("PART_Separator");_content=(StackPanel)GetTemplateChild("PART_Content");_options=(ListBox)GetTemplateChild("PART_Options");
        _chrome.SizeChanged+=(_,_)=>_chrome.Clip=G2Geometry.Create(new Rect(_chrome.RenderSize),new CornerRadius(13));
        ResetSize();
    }
    void ResetSize()
    {
        _header.Height=Math.Max(0,ActualHeight-2);
        _chrome.Width=ActualWidth;_chrome.Height=ActualHeight;
    }
    public void Toggle()
    {
        ApplyTemplate();
        if(IsOpen&&!_closing){Close();return;}
        _closing=false;_options.IsHitTestVisible=true;
        if(!IsOpen)
        {
            _owner=Window.GetWindow(this);
            if(_owner!=null){_owner.Deactivated+=OwnerClosed;_owner.Closed+=OwnerClosed;_owner.PreviewMouseLeftButtonDown+=OutsidePress;_owner.PreviewKeyDown+=OwnerKeyDown;}
            IsOpen=true;
        }
        double available=250;
        if(_owner!=null)
        {
            var transform=TransformToAncestor(_owner);var origin=transform.Transform(new Point());
            double scale=transform.Transform(new Point(0,1)).Y-origin.Y;
            available=Math.Clamp((origin.Y-12)/Math.Max(.01,scale)-ActualHeight-11,0,250);
        }
        _options.MaxHeight=available;_options.Visibility=_separator.Visibility=Visibility.Visible;
        SmoothScroll.Attach(_options);
        _header.Height=Math.Max(0,ActualHeight-2);
        _content.Height=double.NaN;_content.Measure(new Size(198,double.PositiveInfinity));
        double full=_content.DesiredSize.Height+2;_content.Height=full-2;
        Animate(200,full,false);
    }
    void OwnerClosed(object? sender,EventArgs e)=>Close(true);
    void OutsidePress(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source&&(ReferenceEquals(source,this)||IsAncestorOf(source)))return;
        Close();
    }
    void OwnerKeyDown(object sender,KeyEventArgs e)
    {if(e.Key==Key.Escape){Close();e.Handled=true;}}
    public void Close(bool immediate=false)
    {
        if(!IsOpen)return;
        SmoothScroll.Stop(_options);
        _closing=true;_options.IsHitTestVisible=false;
        if(immediate)
        {
            ++_generation;_chrome.BeginAnimation(WidthProperty,null);_chrome.BeginAnimation(HeightProperty,null);FinishClose();
        }
        else Animate(ActualWidth,ActualHeight,true);
    }
    void FinishClose()
    {
        IsOpen=false;_closing=false;_options.Visibility=_separator.Visibility=Visibility.Collapsed;_content.Height=double.NaN;ResetSize();
        if(_owner!=null){_owner.Deactivated-=OwnerClosed;_owner.Closed-=OwnerClosed;_owner.PreviewMouseLeftButtonDown-=OutsidePress;_owner.PreviewKeyDown-=OwnerKeyDown;_owner=null;}
    }
    void Animate(double width,double height,bool close)
    {
        int generation=++_generation;
        double startWidth=_chrome.Width,startHeight=_chrome.Height;
        _chrome.BeginAnimation(WidthProperty,null);_chrome.BeginAnimation(HeightProperty,null);
        _chrome.Width=width;_chrome.Height=height;
        if(ReduceMotion||!SystemParameters.ClientAreaAnimation){if(close)FinishClose();return;}
        DoubleAnimationUsingKeyFrames Motion(double start,double end)
        {
            var motion=new DoubleAnimationUsingKeyFrames();
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(start,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if(close)motion.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new CubicEase{EasingMode=EasingMode.EaseInOut}));
            else
            {
                motion.KeyFrames.Add(new EasingDoubleKeyFrame(end+(end-start)*.03,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new CubicEase{EasingMode=EasingMode.EaseOut}));
                motion.KeyFrames.Add(new EasingDoubleKeyFrame(end-(end-start)*.01,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),new SineEase{EasingMode=EasingMode.EaseInOut}));
                motion.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),new SineEase{EasingMode=EasingMode.EaseInOut}));
            }
            return motion;
        }
        var horizontal=Motion(startWidth,width);var vertical=Motion(startHeight,height);
        vertical.Completed+=(_,_)=>{if(generation!=_generation)return;_chrome.BeginAnimation(WidthProperty,null);_chrome.BeginAnimation(HeightProperty,null);_chrome.Width=width;_chrome.Height=height;if(close)FinishClose();};
        _chrome.BeginAnimation(WidthProperty,horizontal);_chrome.BeginAnimation(HeightProperty,vertical);
    }
}
