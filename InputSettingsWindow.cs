using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class InputSettingsWindow : ChromeWindow
{
    InputPreferences _draft;
    readonly Func<InputPreferences,bool> _apply;
    readonly Action<bool> _captureMode;
    readonly ushort[] _legacy;
    readonly StackPanel _rows=new(); readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};
    readonly ComboBox _instrument=new(); bool _performance;
    Action<ShortcutChord?>? _capture;
    internal void ReviewPerformance(string instrument){_performance=true;_instrument.SelectedValue=instrument;Refresh();}
    static string S(string k)=>Lang.S("map."+k);
    public InputSettingsWindow(InputPreferences initial,ushort[] legacy,Func<InputPreferences,bool> apply,Action<bool> captureMode)
        :base(S("shortcuts"),960)
    {
        InputMethod.SetIsInputMethodEnabled(this,false);
        _draft=initial with{Functions=initial.Functions.ToList(),Performance=initial.Performance.ToList()};_legacy=(ushort[])legacy.Clone();_apply=apply;_captureMode=captureMode;
        var root=new StackPanel{Margin=new Thickness(18)};
        var tabs=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var (title,performance) in new[]{(S("shortcuts"),false),(S("bindings"),true)})
        {var b=Neutral(title);b.Margin=new Thickness(0,0,10,8);b.Click+=(_,_)=>{CancelCapture();_performance=performance;Refresh();};tabs.Children.Add(b);}root.Children.Add(tabs);
        _instrument.ItemsSource=GamePitchProfiles.Instruments.Concat(new[]{"Custom4"}).Select(n=>new ComboBoxItem{Content=n=="Custom4"?S("custom4"):Lang.Instrument(n),Tag=n}).ToArray();_instrument.SelectedValuePath="Tag";_instrument.SelectedValue="Piano";_instrument.Height=34;_instrument.Template=ComboTpl();
        System.Windows.Automation.AutomationProperties.SetName(_instrument,Lang.S("instrument"));
        _instrument.SelectionChanged+=(_,_)=>{if(_performance)Refresh();};root.Children.Add(_instrument);
        root.Children.Add(new ScrollViewer{Content=_rows,Height=500,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        _status.Foreground=B("SubTextFg");root.Children.Add(_status);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var defaults=Neutral(S("defaults"));defaults.Click+=(_,_)=>{CancelCapture();var retained=_draft.Functions.Where(s=>!FeatureAvailability.ShortcutAvailable(s.Action)).ToArray();_draft=new();_draft=_draft with{Functions=_draft.Functions.Select(s=>retained.FirstOrDefault(r=>r.Action==s.Action)??s).ToList()};Refresh();};buttons.Children.Add(defaults);
        var cancel=Neutral(Lang.S("d.cancel"));cancel.Margin=new Thickness(8,0,8,0);cancel.Click+=(_,_)=>Close();buttons.Children.Add(cancel);
        var save=Neutral(S("save"));save.Click+=(_,_)=>Save();buttons.Children.Add(save);root.Children.Add(buttons);
        SetBody(root);Closed+=(_,_)=>CancelCapture();Refresh();
    }
    internal static string Chord(ShortcutChord? c)=>c is not {} k?S("unbound"):(k.Modifiers.HasFlag(ShortcutModifiers.Ctrl)?"Ctrl+":"")
        +(k.Modifiers.HasFlag(ShortcutModifiers.Alt)?"Alt+":"")+(k.Modifiers.HasFlag(ShortcutModifiers.Shift)?"Shift+":"")+KeyConfig.Label(k.Key);
    void Refresh()
    {
        _rows.Children.Clear();_instrument.Visibility=_performance?Visibility.Visible:Visibility.Collapsed;
        if(!_performance)
        foreach(var setting in _draft.Functions.ToArray())
        {
            var row=Row(S("action."+(int)setting.Action));
            if(!FeatureAvailability.ShortcutAvailable(setting.Action)){row.IsEnabled=false;row.Opacity=.45;row.ToolTip=S("deferred");}
            var bind=Neutral(Chord(setting.Chord));bind.MinWidth=190;bind.Click+=(_,_)=>Capture(bind,chord=>
            {_draft=_draft with{Functions=_draft.Functions.Select(s=>s.Action==setting.Action?s with{Chord=chord}:s).ToList()};Refresh();});row.Children.Add(bind);
            System.Windows.Automation.AutomationProperties.SetName(bind,S("action."+(int)setting.Action)+" · "+Chord(setting.Chord));
            var scope=new ComboBox{ItemsSource=new[]{S("global"),S("game"),S("app")},SelectedIndex=(int)setting.Scope,Width=180,Margin=new Thickness(8,0,8,0),Template=ComboTpl()};
            System.Windows.Automation.AutomationProperties.SetName(scope,S("action."+(int)setting.Action)+" · "+S("scope"));
            scope.SelectionChanged+=(_,_)=>_draft=_draft with{Functions=_draft.Functions.Select(s=>s.Action==setting.Action?s with{Scope=(ShortcutScope)scope.SelectedIndex}:s).ToList()};row.Children.Add(scope);
            var clear=Neutral(S("clear"));clear.Click+=(_,_)=>{_draft=_draft with{Functions=_draft.Functions.Select(s=>s.Action==setting.Action?s with{Chord=null}:s).ToList()};Refresh();};System.Windows.Automation.AutomationProperties.SetName(clear,S("action."+(int)setting.Action)+" · "+S("clear"));row.Children.Add(clear);_rows.Children.Add(row);
        }
        else if(_instrument.SelectedValue is string name&&_draft.Capability(name) is {} c)
        {
            var profile=_draft.For(c,_legacy);
            foreach(var slot in c.Slots.Where(s=>s.KeyId!=null))
            {
                var k=c.Keys.Single(k=>k.Id==slot.KeyId);var row=Row($"{slot.Row+1}:{slot.Column+1} · K{k.LogicalKey} · {k.Id}");
                var bind=Neutral(KeyConfig.Label(profile.Keys[k.Id]));bind.MinWidth=190;
                System.Windows.Automation.AutomationProperties.SetName(bind,$"K{k.LogicalKey} · {KeyConfig.Label(profile.Keys[k.Id])}");
                bind.Click+=(_,_)=>Capture(bind,chord=>{if(chord is not {} value || value.Modifiers!=0){_status.Text=S("PerformanceKeyConflict");return;}
                    var keys=profile.Keys.ToDictionary(p=>p.Key,p=>p.Value);keys[k.Id]=value.Key;var updated=profile with{Keys=keys};
                    if(!updated.Valid(c)){_status.Text=S("DuplicateShortcut");return;}
                    _draft=_draft with{Performance=_draft.Performance.Where(p=>p.CapabilityId!=c.Id).Append(updated).ToList()};Refresh();});row.Children.Add(bind);
                row.Children.Add(new TextBlock{Text=S(k.Availability==KeyAvailability.Available?"available":k.Availability==KeyAvailability.Unavailable?"unavailable":"unknown"),Foreground=B("SubTextFg"),Margin=new Thickness(12,8,0,0)});_rows.Children.Add(row);
            }
        }
        _status.Text=_performance?S("unknownConfirm"):S("shortcutHint")+(FeatureAvailability.SheetMappingEnabled?"":"\n"+S("deferred"));
    }
    StackPanel Row(string label)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,5,0,5)};
        row.Children.Add(new TextBlock{Text=label,Width=270,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,Foreground=B("TextFg")});return row;
    }
    void Capture(Button button,Action<ShortcutChord?> apply)
    {CancelCapture();_capture=apply;button.Content=S("capture");System.Windows.Automation.AutomationProperties.SetHelpText(button,S("capture"));_captureMode(true);Focus();}
    void CancelCapture(){_capture=null;_captureMode(false);}
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if(_capture==null){base.OnPreviewKeyDown(e);return;}e.Handled=true;if(e.IsRepeat)return;
        var key=e.Key==Key.System?e.SystemKey:e.Key;
        if(key==Key.Escape){CancelCapture();Refresh();return;}
        if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return;
        if((Keyboard.Modifiers&ModifierKeys.Windows)!=0){_status.Text=S("ReservedShortcut");return;}
        var mods=ShortcutModifiers.None;if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control))mods|=ShortcutModifiers.Ctrl;
        if(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))mods|=ShortcutModifiers.Alt;if(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))mods|=ShortcutModifiers.Shift;
        var apply=_capture;CancelCapture();apply(new((ushort)KeyInterop.VirtualKeyFromKey(key),mods));
    }
    void Save()
    {
        CancelCapture();var error=ShortcutRules.Validate(_draft.Functions,_draft.AllPerformanceKeys(_legacy));
        if(error!=null){_status.Text=S(error);return;}
        if(_apply(_draft))Close();else _status.Text=S("applyFailed");
    }
}
