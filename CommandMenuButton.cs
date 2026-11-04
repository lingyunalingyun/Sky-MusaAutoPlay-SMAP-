using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SMAP_WPF;

// The actual trigger is temporarily lifted above scroll viewers, within its owner window.
public sealed class CommandMenuButton : Button
{
    Border _chrome=null!, _header=null!;
    DockPanel _body=null!;
    StackPanel _items=null!;
    ScrollViewer _scroll=null!;
    Panel? _parent;
    Canvas? _layer;
    Border? _placeholder;
    Window? _owner;
    int _index, _generation;
    double _width, _height, _closedWidth, _closedHeight;
    Thickness _margin;
    HorizontalAlignment _horizontal;
    VerticalAlignment _vertical;
    bool _up, _left, _closing;
    public bool IsOpen { get; private set; }
    public bool ReduceMotion { get; set; }
    public Border Chrome { get { ApplyTemplate(); return _chrome; } }
    public ScrollViewer Scroll { get { ApplyTemplate(); return _scroll; } }
    public event EventHandler? Closed;

    public CommandMenuButton()
    {
        var resources=new ResourceDictionary { Source=new Uri("/SMAP;component/ChoicePopupStyles.xaml",UriKind.Relative) };
        Resources.MergedDictionaries.Add(resources); Style=(Style)resources["CommandMenuButtonStyle"];
        ContextMenuService.SetIsEnabled(this,false);
        SizeChanged+=(_,_)=> { if(_chrome!=null&&!IsOpen)ResetSize(); };
    }
    public override void OnApplyTemplate()
    {
        Close(true); base.OnApplyTemplate();
        _chrome=(Border)GetTemplateChild("PART_Chrome"); _header=(Border)GetTemplateChild("PART_Header");
        _body=(DockPanel)GetTemplateChild("PART_Body"); _items=(StackPanel)GetTemplateChild("PART_Items");
        _scroll=(ScrollViewer)GetTemplateChild("PART_Scroll");
        _chrome.SizeChanged+=(_,_)=>PositionChrome();
        ResetSize();
    }
    void ResetSize()
    {
        _chrome.Width=ActualWidth; _chrome.Height=ActualHeight;
        _header.Width=Math.Max(0,ActualWidth-2); _header.Height=Math.Max(0,ActualHeight-2);
        _body.Width=Math.Max(0,ActualWidth-2); _body.Height=double.NaN;
        _scroll.Visibility=Visibility.Collapsed;
        Canvas.SetLeft(_chrome,0); Canvas.SetTop(_chrome,0);
    }
    public void Toggle(ContextMenu model,Canvas layer)
    {
        ApplyTemplate();
        if(IsOpen&&!_closing) { Close(); return; }
        if(!IsOpen)
        {
            if(Parent is not Panel parent||Window.GetWindow(this) is not { } owner)return;
            _owner=owner; _parent=parent; _layer=layer; _index=parent.Children.IndexOf(this);
            _width=Width; _height=Height; _margin=Margin; _horizontal=HorizontalAlignment; _vertical=VerticalAlignment;
            _closedWidth=ActualWidth; _closedHeight=ActualHeight;
            _placeholder=new Border { Width=_closedWidth,Height=_closedHeight,Margin=Margin,
                HorizontalAlignment=HorizontalAlignment,VerticalAlignment=VerticalAlignment };
            DockPanel.SetDock(_placeholder,DockPanel.GetDock(this));
            IsOpen=true;
            parent.Children.RemoveAt(_index); parent.Children.Insert(_index,_placeholder);
            Width=_closedWidth; Height=_closedHeight; Margin=new Thickness();
            layer.Children.Add(this); owner.UpdateLayout(); FollowAnchor();
            var origin=_placeholder.TranslatePoint(new Point(),layer);
            _left=origin.X+320>layer.ActualWidth-14;
            _up=layer.ActualHeight-origin.Y-_closedHeight<Math.Min(420,model.Items.Count*34)&&origin.Y>layer.ActualHeight-origin.Y-_closedHeight;
            _owner.PreviewMouseDown+=OutsidePress; _owner.Deactivated+=OwnerClosed; _owner.Closed+=OwnerClosed;
            _owner.LayoutUpdated+=FollowLayout;
        }
        _closing=false; _scroll.IsHitTestVisible=true; ContextMenu=model;
        _items.Children.Clear(); AddItems(model.Items,_items);
        _scroll.Visibility=Visibility.Visible;
        DockPanel.SetDock(_header,_up?Dock.Bottom:Dock.Top);
        _body.HorizontalAlignment=_left?HorizontalAlignment.Right:HorizontalAlignment.Left;
        _body.VerticalAlignment=_up?VerticalAlignment.Bottom:VerticalAlignment.Top;
        _header.HorizontalAlignment=_left?HorizontalAlignment.Right:HorizontalAlignment.Left;
        _header.Width=Math.Max(0,_closedWidth-2); _header.Height=Math.Max(0,_closedHeight-2);
        SmoothScroll.Attach(_scroll); Fit(); Focus();
    }
    void AddItems(ItemCollection models,StackPanel destination)
    {
        foreach(var value in models)
        {
            if(value is Separator)
            {
                var line=new Border { Height=1,Margin=new Thickness(10,4,10,4) };
                line.SetResourceReference(Border.BackgroundProperty,"BoxBorder"); destination.Children.Add(line); continue;
            }
            if(value is not MenuItem item)continue;
            var row=new Button { HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(10,7,10,7),
                Margin=new Thickness(0,1,0,1),IsEnabled=item.IsEnabled,ToolTip=item.Header,
                Style=(Style)FindResource("CommandMenuRow") };
            var text=new TextBlock { Text=(item.IsCheckable?(item.IsChecked?"✓  ":"     "):"")+item.Header+(item.HasItems?"  ▾":""),TextTrimming=TextTrimming.CharacterEllipsis };
            if(item.Icon is Border icon)
            {
                var content=new DockPanel(); var swatch=new Border { Width=15,Height=15,Margin=new Thickness(0,0,9,0),Background=icon.Background };
                content.Children.Add(swatch); content.Children.Add(text); row.Content=content;
            }
            else row.Content=text;
            destination.Children.Add(row);
            StackPanel? children=null;
            if(item.HasItems)
            {
                children=new StackPanel { Margin=new Thickness(12,0,0,0),Visibility=Visibility.Collapsed };
                AddItems(item.Items,children); destination.Children.Add(children);
            }
            row.Click+=(_,e)=>
            {
                e.Handled=true;
                if(children!=null) { children.Visibility=children.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible; Fit(); return; }
                Close(true);
                if(item.IsCheckable)item.IsChecked=!item.IsChecked;
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            };
        }
    }
    void Fit()
    {
        if(_owner==null||_layer==null||_placeholder==null)return;
        var origin=_placeholder.TranslatePoint(new Point(),_layer);
        double available=(_up?origin.Y:_layer.ActualHeight-origin.Y-_closedHeight)-16;
        _scroll.MaxHeight=Math.Max(0,Math.Min(420,available));
        _body.Width=318; _body.Height=double.NaN; _body.Measure(new Size(318,double.PositiveInfinity));
        double full=_body.DesiredSize.Height+2; _body.Height=full-2;
        Animate(320,full,false);
    }
    void PositionChrome()
    {
        _chrome.Clip=new RectangleGeometry(new Rect(_chrome.RenderSize),10,10);
        if(!IsOpen)return;
        Canvas.SetLeft(_chrome,_left?_closedWidth-_chrome.ActualWidth:0);
        Canvas.SetTop(_chrome,_up?_closedHeight-_chrome.ActualHeight:0);
    }
    void FollowLayout(object? sender,EventArgs e)=>FollowAnchor();
    void FollowAnchor()
    {
        if(_layer==null||_placeholder==null)return;
        if(PresentationSource.FromVisual(_placeholder)==null) { Close(true); return; }
        var point=_placeholder.TranslatePoint(new Point(),_layer);
        Canvas.SetLeft(this,point.X); Canvas.SetTop(this,point.Y);
        for(DependencyObject? p=VisualTreeHelper.GetParent(_placeholder);p!=null;p=VisualTreeHelper.GetParent(p))
            if(p is ScrollViewer viewer)
            {
                var visible=_placeholder.TransformToAncestor(viewer).TransformBounds(new Rect(_placeholder.RenderSize));
                if(!visible.IntersectsWith(new Rect(viewer.RenderSize)))Close(true);
                break;
            }
    }
    void OwnerClosed(object? sender,EventArgs e)=>Close(true);
    void OutsidePress(object sender,MouseButtonEventArgs e)
    {
        if(e.OriginalSource is DependencyObject source&&(ReferenceEquals(source,this)||IsAncestorOf(source)))return;
        Close();
    }
    public bool HandleKey(Key key)
    {
        if(!IsOpen)return false;
        if(_closing)return true;
        if(key==Key.Escape) { Close(); return true; }
        var rows=Rows(_items).Where(b=>b.IsVisible&&b.IsEnabled).ToList();
        int index=rows.IndexOf(Keyboard.FocusedElement as Button ?? this);
        if(key is Key.Down or Key.Up or Key.Home or Key.End)
        {
            int next=key==Key.Home?0:key==Key.End?rows.Count-1:index<0?(key==Key.Up?rows.Count-1:0):Math.Clamp(index+(key==Key.Up?-1:1),0,rows.Count-1);
            if(next>=0&&next<rows.Count) { rows[next].Focus(); rows[next].BringIntoView(); } return true;
        }
        if(key is Key.Enter or Key.Space)
        { if(index>=0)rows[index].RaiseEvent(new RoutedEventArgs(ClickEvent)); else Close(); return true; }
        return true;
    }
    static IEnumerable<Button> Rows(StackPanel panel)
    {
        foreach(UIElement child in panel.Children)
            if(child is Button button)yield return button;
            else if(child is StackPanel nested)foreach(var row in Rows(nested))yield return row;
    }
    public void Close(bool immediate=false)
    {
        if(!IsOpen)return;
        SmoothScroll.Stop(_scroll); _closing=true; _scroll.IsHitTestVisible=false;
        if(immediate) { ++_generation; _chrome.BeginAnimation(WidthProperty,null); _chrome.BeginAnimation(HeightProperty,null); FinishClose(); }
        else Animate(_closedWidth,_closedHeight,true);
    }
    void FinishClose()
    {
        if(_owner!=null)
        { _owner.PreviewMouseDown-=OutsidePress; _owner.Deactivated-=OwnerClosed; _owner.Closed-=OwnerClosed; _owner.LayoutUpdated-=FollowLayout; }
        bool focused=IsKeyboardFocusWithin;
        _layer?.Children.Remove(this);
        if(_parent!=null&&_placeholder!=null)
        { _parent.Children.Remove(_placeholder); _parent.Children.Insert(Math.Min(_index,_parent.Children.Count),this); }
        Width=_width; Height=_height; Margin=_margin; HorizontalAlignment=_horizontal; VerticalAlignment=_vertical;
        IsOpen=false; _closing=false; _owner=null; _parent=null; _placeholder=null; _layer=null;
        ResetSize(); if(focused)Focus(); Closed?.Invoke(this,EventArgs.Empty);
    }
    void Animate(double width,double height,bool close)
    {
        int generation=++_generation;
        double startWidth=_chrome.Width,startHeight=_chrome.Height;
        _chrome.BeginAnimation(WidthProperty,null); _chrome.BeginAnimation(HeightProperty,null);
        _chrome.Width=width; _chrome.Height=height;
        if(ReduceMotion||!SystemParameters.ClientAreaAnimation) { if(close)FinishClose(); return; }
        DoubleAnimationUsingKeyFrames Motion(double start,double end)
        {
            var motion=new DoubleAnimationUsingKeyFrames();
            motion.KeyFrames.Add(new LinearDoubleKeyFrame(start,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            if(close)motion.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new CubicEase { EasingMode=EasingMode.EaseInOut }));
            else
            {
                motion.KeyFrames.Add(new EasingDoubleKeyFrame(end+(end-start)*.03,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)),new CubicEase { EasingMode=EasingMode.EaseOut }));
                motion.KeyFrames.Add(new EasingDoubleKeyFrame(end-(end-start)*.01,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)),new SineEase { EasingMode=EasingMode.EaseInOut }));
                motion.KeyFrames.Add(new EasingDoubleKeyFrame(end,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),new SineEase { EasingMode=EasingMode.EaseInOut }));
            }
            return motion;
        }
        var vertical=Motion(startHeight,height);
        vertical.Completed+=(_,_)=> { if(generation!=_generation)return; _chrome.BeginAnimation(WidthProperty,null); _chrome.BeginAnimation(HeightProperty,null); _chrome.Width=width; _chrome.Height=height; if(close)FinishClose(); };
        _chrome.BeginAnimation(WidthProperty,Motion(startWidth,width)); _chrome.BeginAnimation(HeightProperty,vertical);
    }
}
