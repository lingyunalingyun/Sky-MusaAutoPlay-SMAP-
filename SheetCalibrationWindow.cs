using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class SheetCalibrationWindow:ChromeWindow
{
    readonly Canvas _marks=new(){IsHitTestVisible=false};readonly Image _image;readonly TextBlock _instruction=new(){TextWrapping=TextWrapping.Wrap};
    readonly InstrumentCapability _capability;readonly List<KeyLocation> _keys=new();readonly Button _save;
    readonly bool _manual;
    Point? _start;
    public IReadOnlyList<KeyLocation>? Result {get;private set;}
    public SheetCalibrationWindow(CapturePixels pixels,InstrumentCapability capability,KeyDetection? detection=null)
        :this(pixels,capability,detection,FeatureAvailability.RequireSheetMapping()) { }
    SheetCalibrationWindow(CapturePixels pixels,InstrumentCapability capability,KeyDetection? detection,bool available):base(Lang.S("map.manual"),1040)
    {
        _capability=capability;_manual=detection==null;
        if(detection!=null)_keys.AddRange(detection.Keys);
        var root=new StackPanel{Margin=new Thickness(16)};_instruction.Foreground=B("TextFg");root.Children.Add(_instruction);
        var bitmap=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Bgra32,null,pixels.Bgra,pixels.Stride);bitmap.Freeze();
        _image=new Image{Source=bitmap,Width=976,Height=Math.Min(560,976.0*pixels.Height/pixels.Width),Stretch=Stretch.Fill};
        var imageGrid=new Grid{HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,10)};imageGrid.Children.Add(_image);imageGrid.Children.Add(_marks);root.Children.Add(imageGrid);
        imageGrid.MouseLeftButtonDown+=(_,e)=>{if(_manual&&_keys.Count<_capability.Keys.Count){_start=e.GetPosition(_image);imageGrid.CaptureMouse();e.Handled=true;}};
        imageGrid.MouseLeftButtonUp+=Clicked;
        _save=Neutral(Lang.S("map.confirm"));_save.Click+=(_,_)=>{if(SheetCalibration.ValidLocations(_capability,_keys)){Result=_keys.ToArray();DialogResult=true;}};
        var clear=Neutral(Lang.S("map.clear"));clear.Click+=(_,_)=>{if(_manual){_keys.Clear();Draw();}};root.Children.Add(clear);root.Children.Add(_save);
        SetBody(root);Loaded+=(_,_)=>Draw();
    }
    void Clicked(object sender,MouseButtonEventArgs e)
    {
        if(!_manual||_keys.Count>=_capability.Keys.Count)return;
        ((UIElement)sender).ReleaseMouseCapture();if(_start is not {} start)return;_start=null;
        var p=e.GetPosition(_image);if(p.X<0||p.X>_image.ActualWidth||p.Y<0||p.Y>_image.ActualHeight)return;
        double width=Math.Abs(p.X-start.X),height=Math.Abs(p.Y-start.Y);if(width<8||height<8)return;
        var key=_capability.Keys[_keys.Count];
        _keys.Add(new(key.Id,(p.X+start.X)/2/_image.ActualWidth,(p.Y+start.Y)/2/_image.ActualHeight,width/_image.ActualWidth,height/_image.ActualHeight));Draw();
    }
    void Draw()
    {
        _marks.Children.Clear();
        foreach(var k in _keys)
        {
            var ring=new System.Windows.Shapes.Ellipse{Width=k.Width*_image.Width,Height=k.Height*_image.Height,Stroke=Brushes.DeepSkyBlue,StrokeThickness=2};
            Canvas.SetLeft(ring,(k.X-k.Width/2)*_image.Width);Canvas.SetTop(ring,(k.Y-k.Height/2)*_image.Height);_marks.Children.Add(ring);
        }
        _save.IsEnabled=SheetCalibration.ValidLocations(_capability,_keys);
        _instruction.Text=_manual?Lang.S("map.click")+$" · {_keys.Count}/{_capability.Keys.Count}"+(_keys.Count<_capability.Keys.Count?$" · {_capability.Keys[_keys.Count].Id} / K{_capability.Keys[_keys.Count].LogicalKey}":"")
            :Lang.S("map.review");
    }
}
