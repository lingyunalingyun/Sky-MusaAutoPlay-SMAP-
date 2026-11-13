using Border = SMAP_WPF.G2Border;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SMAP_WPF;

public sealed class AnimatedChoicePopup : FrameworkElement
{
    JoinedChoiceButton? _target;
    bool _reduceMotion;
    public bool IsOpen=>_target?.IsOpen==true;
    public bool ReduceMotion {get=>_reduceMotion;set{_reduceMotion=value;if(_target!=null)_target.ReduceMotion=value;}}
    public Border Chrome=>_target!.Chrome;
    public ListBox Options=>_target!.Options;
    public AnimatedChoicePopup()=>Visibility=Visibility.Collapsed;
    public void Toggle(ComboBox model,Button target)
    {
        if(target is not JoinedChoiceButton joined)throw new ArgumentException("JoinedChoiceButtonRequired",nameof(target));
        if(_target!=null&&!ReferenceEquals(_target,joined))_target.Close(true);
        _target=joined;joined.ReduceMotion=ReduceMotion;joined.Toggle(model);
    }
    public void Close(bool immediate=false)=>_target?.Close(immediate);
    public void CloseOnOutsidePress(MouseButtonEventArgs e)=>_target?.CloseOnOutsidePress(e);
}