using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SMAP_WPF;

public sealed class JoinedChoiceButton : Button
{
    ComboBox? _model;
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
    public JoinedChoiceButton()
    {
        var resources=new ResourceDictionary{Source=new Uri("/SMAP;component/ChoicePopupStyles.xaml",UriKind.Relative)};
        Style=(Style)resources["JoinedChoiceButtonStyle"];
        SizeChanged+=(_,_)=>{if(_chrome!=null){_header.Height=Math.Max(0,ActualHeight-2);if(!IsOpen)_chrome.Height=ActualHeight;}};
        Unloaded+=(_,_)=>Close(true);
    }
    public override void OnApplyTemplate()
    {
        if(IsOpen)Close(true);base.OnApplyTemplate();
        _chrome=(Border)GetTemplateChild("PART_Chrome");_header=(Border)GetTemplateChild("PART_Header");
        _separator=(Border)GetTemplateChild("PART_Separator");_content=(StackPanel)GetTemplateChild("PART_Content");_options=(ListBox)GetTemplateChild("PART_Options");
        _chrome.SizeChanged+=(_,_)=>_chrome.Clip=new RectangleGeometry(new Rect(_chrome.RenderSize),10,10);
        _options.PreviewMouseLeftButtonUp+=Commit;_header.Height=Math.Max(0,ActualHeight-2);_chrome.Height=ActualHeight;
    }
    public void Toggle(ComboBox model)
    {
        ApplyTemplate();
        if(IsOpen&&!_closing&&ReferenceEquals(_model,model)){Close();return;}
        double start=IsOpen?_chrome.ActualHeight:ActualHeight;
        if(SmoothScroll.GetEnabled(_options))SmoothScroll.Stop(_options);
        _closing=false;_model=model;_options.Items.Clear();
        for(int i=0;i<model.Items.Count;i++)_options.Items.Add(new ListBoxItem{Content=model.Items[i],Tag=i});
        _options.SelectedIndex=model.SelectedIndex;_options.Visibility=_separator.Visibility=Visibility.Visible;
        if(SmoothScroll.GetEnabled(_options))SmoothScroll.Attach(_options);
        _content.Height=double.NaN;_content.Measure(new Size(Math.Max(1,ActualWidth-2),double.PositiveInfinity));
        double full=_content.DesiredSize.Height+2;_content.Height=full-2;_chrome.IsHitTestVisible=true;
        if(!IsOpen)
        {
            IsOpen=true;_owner=Window.GetWindow(this);
            if(_owner!=null){_owner.Deactivated+=OwnerClosed;_owner.Closed+=OwnerClosed;_owner.PreviewMouseLeftButtonDown+=OutsidePress;}
        }
        AnimateHeight(start,full,false);
    }
    void OwnerClosed(object? sender,EventArgs e)=>Close(true);
    void OutsidePress(object sender,MouseButtonEventArgs e)=>CloseOnOutsidePress(e);
    public void CloseOnOutsidePress(MouseButtonEventArgs e)
    {
        if(!IsOpen||e.OriginalSource is not DependencyObject source)return;
        if(ReferenceEquals(source,this)||IsAncestorOf(source))return;Close();
    }
    void FinishClose()
    {
        IsOpen=false;_closing=false;_options.Visibility=_separator.Visibility=Visibility.Collapsed;
        _content.Height=double.NaN;_chrome.Height=ActualHeight;_chrome.IsHitTestVisible=true;
        if(_owner!=null){_owner.Deactivated-=OwnerClosed;_owner.Closed-=OwnerClosed;_owner.PreviewMouseLeftButtonDown-=OutsidePress;_owner=null;}
    }
    public void Close(bool immediate=false)
    {
        if(!IsOpen)return;_closing=true;
        if(SmoothScroll.GetEnabled(_options))SmoothScroll.Stop(_options);
        if(immediate){++_generation;_chrome.BeginAnimation(FrameworkElement.HeightProperty,null);FinishClose();return;}
        _chrome.IsHitTestVisible=false;AnimateHeight(_chrome.ActualHeight,ActualHeight,true);
    }
    void AnimateHeight(double start,double end,bool close)
    {
        int generation=++_generation;_chrome.BeginAnimation(FrameworkElement.HeightProperty,null);_chrome.Height=end;
        if(ReduceMotion||!SystemParameters.ClientAreaAnimation){if(close)FinishClose();return;}
        var animation=new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(Math.Max(0,start),KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if(close)animation.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new CubicEase{EasingMode=EasingMode.EaseInOut}));
        else
        {
            double body=Math.Max(0,end-ActualHeight);
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(end+body*.03,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new CubicEase{EasingMode=EasingMode.EaseOut}));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(end-body*.01,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),new SineEase{EasingMode=EasingMode.EaseInOut}));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),new SineEase{EasingMode=EasingMode.EaseInOut}));
        }
        animation.Completed+=(_,_)=>{if(generation!=_generation)return;_chrome.BeginAnimation(FrameworkElement.HeightProperty,null);_chrome.Height=end;if(close)FinishClose();};
        _chrome.BeginAnimation(FrameworkElement.HeightProperty,animation);
    }
    void Commit(object sender,MouseButtonEventArgs e)
    {
        if(_closing||_model==null||e.OriginalSource is not DependencyObject source||ItemsControl.ContainerFromElement(_options,source) is not ListBoxItem{Tag:int index})return;
        _model.SelectedIndex=index;SetCurrentValue(ContentProperty,_model.SelectedItem);Close();e.Handled=true;
    }
}
