using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class MainWindow
{
    InputPreferences _inputs=InputPreferences.Load();
    readonly ShortcutRouter _shortcuts=new();
    ShortcutRegistrations? _registrations;
    IKeyboardObserver? _observer;
    SheetMappingWindow? _mapping;
    readonly SemaphoreSlim _previewLoad=new(1,1);
    int _previewVersion;
    bool _inputSettingsOpen;
    long _captureFence;
    bool _textInputSuspended,_previewLoading;
    readonly HashSet<ushort> _localPerformanceDown=new();
    internal Action<int,bool> PracticeAudioInput {get;set;}=(key,down)=>{if(down)AudioEngine.NoteOn(key);else AudioEngine.NoteOff(key);};
    bool TextInputFocused=>Keyboard.FocusedElement is TextBoxBase or PasswordBox || Keyboard.FocusedElement is ComboBox{IsEditable:true};
    string ShortcutHint(string text)=>System.Text.RegularExpressions.Regex.Replace(text,@"\bF[1-6]\b|←|→",match=>
    {
        var action=match.Value switch{"F1"=>ShortcutAction.PlayStop,"F2"=>ShortcutAction.PauseResume,"F3"=>ShortcutAction.Slower,
            "F4"=>ShortcutAction.Faster,"F5"=>ShortcutAction.Previous,"F6"=>ShortcutAction.Next,"←"=>ShortcutAction.SeekBack,_=>ShortcutAction.SeekForward};
        return InputSettingsWindow.Chord(_inputs.Functions.Single(s=>s.Action==action).Chord);
    });
    void RefreshShortcutHints()
    {
        StartBtn.Content=ShortcutHint(Lang.S(_playing?"map.stopKey":"btn.start"));
        PauseBtn.Content=ShortcutHint(Lang.S(_paused?"map.resumeKey":"btn.pause"));
        PrevBtn.ToolTip=ShortcutHint(Lang.S("tip.prev"));NextBtn.ToolTip=ShortcutHint(Lang.S("tip.next"));ProgBar.ToolTip=ShortcutHint(Lang.S("tip.seek"));
        if(KeyEditBtn.ContextMenu is {} menu&&menu.Items.Count==2)
        {((MenuItem)menu.Items[0]).Header=Lang.S("map.shortcuts");((MenuItem)menu.Items[1]).Header=Lang.S(FeatureAvailability.SheetMappingEnabled?"map.title":"map.deferredTitle");}
        if(PracticeBtn.ContextMenu?.Items[0] is MenuItem mapping)mapping.Header=Lang.S(FeatureAvailability.SheetMappingEnabled?"map.title":"map.deferredTitle");
    }
    static string PreviewInstrumentText(string name)=>name=="Custom4"?Lang.S("map.custom4"):Lang.Instrument(name);
    void UpdateInputFocus(object? sender,ProcessInputEventArgs e)
    {
        bool focused=AppForeground(GameWindows.GetForegroundWindow())&&TextInputFocused;
        if(focused==_textInputSuspended)return;_textInputSuspended=focused;
        try{_registrations?.Apply(_inputs.Functions,_mapping!=null||_inputSettingsOpen||focused);}
        catch(Exception error){Logger.Exception("快捷键焦点路由更新失败",error);ShowToast(Lang.S("map.applyFailed"));}
    }
    void RestorePerformanceBindings()
    {
        var c=InstrumentCapabilities.ForInstrument("Piano")!;var p=_inputs.For(c,_player.Vk);
        if(p.Valid(c))_player.Vk=c.Keys.OrderBy(k=>k.LegacyKey).Select(k=>p.Keys[k.Id]).ToArray();
    }
    [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
    internal sealed class Registrar(IntPtr hwnd):IShortcutRegistrar
    {
        public bool Register(int id,ShortcutChord chord)
        {
            if(RegisterHotKey(hwnd,id,0x4000|(uint)chord.Modifiers,chord.Key))return true;
            int error=Marshal.GetLastWin32Error();
            throw new System.ComponentModel.Win32Exception(error,$"ShortcutRegistrationFailed: {chord}; error={error}; pid={Environment.ProcessId}; hwnd={hwnd.ToInt64()}; thread={GetCurrentThreadId()}");
        }
        public bool Unregister(int id)=>UnregisterHotKey(hwnd,id);
    }
    internal void InitializeInputRouting(IShortcutRegistrar? registrar=null,IKeyboardObserver? observer=null)
    {
        _shortcuts.Settings=_inputs.Functions.ToList();_shortcuts.PerformanceKeys=_inputs.AllPerformanceKeys(_player.Vk).ToHashSet();_registrations=new(registrar??new Registrar(_hwnd));
        try{_registrations.Apply(_shortcuts.Settings);}
        catch(Exception error){Logger.Exception("快捷键注册失败",error);ShowToast(Lang.S("map.applyFailed"));}
        try{_observer=observer??new GlobalKeyboardObserver();_observer.Key+=ObservedInput;_observer.Overflow+=InputOverflow;}
        catch(Exception error){Logger.Exception("键盘监听启动失败",error);ShowToast(Lang.S("map.applyFailed"));}
        var menu=new ContextMenu();
        var settings=new MenuItem{Header=Lang.S("map.shortcuts")};settings.Click+=(_,_)=>OpenInputSettings();menu.Items.Add(settings);
        var mapping=MappingMenuItem();menu.Items.Add(mapping);
        KeyEditBtn.ContextMenu=menu;
        var button=new Button{Content=Lang.S("map.shortcuts"),Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Left,MinHeight=34};
        button.Click+=(_,_)=>OpenInputSettings();
        if(SettingsBindArea.Parent is Panel panel)panel.Children.Insert(panel.Children.IndexOf(SettingsBindArea),button);
        var mapMenu=new ContextMenu();mapMenu.Items.Add(MappingMenuItem());PracticeBtn.ContextMenu=mapMenu;
        RefreshPreviewLayout();
        InputManager.Current.PostProcessInput+=UpdateInputFocus;
    }
    MenuItem MappingMenuItem()
    {
        var item=new MenuItem{Header=Lang.S(FeatureAvailability.SheetMappingEnabled?"map.title":"map.deferredTitle"),IsEnabled=FeatureAvailability.SheetMappingEnabled};
        item.Click+=(_,_)=>OpenMapping();return item;
    }
    void InputOverflow(){_mapping?.PausePractice();_shortcuts.Clear();_gamePracticeDown.Clear();AudioEngine.StopManual();}
    void CloseInputRouting()
    {
        InputManager.Current.PostProcessInput-=UpdateInputFocus;
        var observer=_observer;_observer=null;
        if(observer!=null){observer.Key-=ObservedInput;observer.Overflow-=InputOverflow;}
        try{_mapping?.Close();}catch(Exception error){Logger.Exception("谱面映射关闭失败",error);}finally{_mapping=null;}
        try{observer?.Dispose();}catch(Exception error){Logger.Exception("键盘监听关闭失败",error);}
        try{_registrations?.Dispose();}catch(Exception error){Logger.Exception("快捷键注销失败",error);}finally{_registrations=null;_shortcuts.Clear();}
    }
    bool AppForeground(IntPtr hwnd)=>Application.Current.Windows.OfType<Window>().Any(w=>new WindowInteropHelper(w).Handle==hwnd);
    ShortcutContext Context(IntPtr hwnd)=>new(_mapping?.GameForeground==true || _mapping==null&&GameWindows.IsKnownGame(hwnd),AppForeground(hwnd),AppForeground(hwnd)
        && (_inputSettingsOpen || Keyboard.FocusedElement is TextBoxBase or PasswordBox || Keyboard.FocusedElement is ComboBox {IsEditable:true}),_mapping!=null);
    void ObservedInput(ObservedKey e)
    {
        if(e.Timestamp<=_captureFence)return;
        if(e.Foreground!=GameWindows.GetForegroundWindow()){_shortcuts.Clear();return;}
        var context=Context(e.Foreground);
        var action=_shortcuts.KeyEvent(e.Key,e.Modifiers,e.Down,context);
        if(action is {} command){ExecuteShortcut(command);return;}
        if(_shortcuts.Capturing)return;
        if(_mapping!=null){_mapping.Input(e);return;}
        if(_gamePracticeOn && _practiceOpen)
        {
            int idx=Array.IndexOf(_player.Vk,e.Key);if(idx<0)return;
            if(e.Down){if(_gamePracticeDown.Add(e.Key)){AudioEngine.NoteOn(idx);FlashKey(idx);_practiceHeld.Add(idx);PracticePress(idx);}}
            else{_gamePracticeDown.Remove(e.Key);AudioEngine.NoteOff(idx);PracticeRelease(idx);}
        }
    }
    bool RegisteredShortcut(int id)
    {
        if(_mapping!=null || _registrations==null)return false;
        var found=_registrations.Registered.FirstOrDefault(p=>p.Value==id);
        if(found.Value!=id)return false;
        if(_shortcuts.KeyEvent(found.Key.Key,found.Key.Modifiers,true,Context(GameWindows.GetForegroundWindow()),true) is {} action)ExecuteShortcut(action);
        return true;
    }
    void ExecuteShortcut(ShortcutAction action)
    {
        if(!FeatureAvailability.ShortcutAvailable(action))return;
        if(_mapping!=null)
        {
            switch(action)
            {
                case ShortcutAction.PlayStop:_mapping.RestartPractice();break;
                case ShortcutAction.PauseResume:case ShortcutAction.PauseMapping:_mapping.TogglePause();break;
                case ShortcutAction.Slower:AdjustSpeed(-.1);_mapping.Speed=_speed;break;
                case ShortcutAction.Faster:AdjustSpeed(.1);_mapping.Speed=_speed;break;
                case ShortcutAction.SeekBack:_mapping.Seek(-2000);break;
                case ShortcutAction.SeekForward:_mapping.Seek(2000);break;
                case ShortcutAction.ToggleMapping:_mapping.ToggleHints();break;
                case ShortcutAction.TogglePractice:_mapping.Close();break;
                case ShortcutAction.ToggleWindow:ToggleMainVisibility();break;
            }
            return;
        }
        switch(action)
        {
            case ShortcutAction.PlayStop:if(_practiceOpen)RestartPractice();else HotkeyStartStop();break;
            case ShortcutAction.PauseResume:if(_practiceOpen&&!_playing&&!_previewing)TogglePracticePaused();else Pause_Click(this,new());break;
            case ShortcutAction.Previous:StepSong(-1);break;case ShortcutAction.Next:StepSong(1);break;
            case ShortcutAction.Slower:AdjustSpeed(-.1);break;case ShortcutAction.Faster:AdjustSpeed(.1);break;
            case ShortcutAction.SeekBack:if(_practiceOpen)PracticeSeek(-1);else SeekRelative(-2000);break;
            case ShortcutAction.SeekForward:if(_practiceOpen)PracticeSeek(1);else SeekRelative(2000);break;
            case ShortcutAction.TogglePreview:PreviewMode_Click(this,new());break;
            case ShortcutAction.TogglePractice:ShowPractice(!_practiceOpen);break;
            case ShortcutAction.ToggleMapping:OpenMapping();break;
            case ShortcutAction.ToggleWindow:ToggleMainVisibility();break;
        }
    }
    void ToggleMainVisibility(){if(IsVisible)Hide();else{Show();Activate();}}
    void OpenInputSettings()
    {
        try
        {
            _inputSettingsOpen=true;
            _registrations?.Apply(_inputs.Functions,true);
            new InputSettingsWindow(_inputs,_player.Vk,ApplyInputPreferences,active=>
            {
                _shortcuts.Capturing=active;_captureFence=System.Diagnostics.Stopwatch.GetTimestamp();_shortcuts.Clear();AudioEngine.StopManual();
                if(active)try{_registrations?.Apply(_inputs.Functions,true);}catch(Exception ex){Logger.Exception("快捷键录入准备失败",ex);}
            }){Owner=this}.ShowDialog();
        }
        finally{_inputSettingsOpen=false;_shortcuts.Capturing=false;try{_registrations?.Apply(_inputs.Functions,_mapping!=null);}catch(Exception ex){Logger.Exception("快捷键恢复失败",ex);ShowToast(Lang.S("map.applyFailed"));}}
    }
    bool ApplyInputPreferences(InputPreferences next)
    {
        var error=ShortcutRules.Validate(next.Functions,next.AllPerformanceKeys(_player.Vk));
        if(error!=null)return false;
        try
        {
            _registrations?.Apply(next.Functions,_mapping!=null);
            try{next.Save();}catch{_registrations?.Apply(_inputs.Functions,_mapping!=null);throw;}
            _inputs=next;_shortcuts.Settings=next.Functions.ToList();RestorePerformanceBindings();
            _shortcuts.PerformanceKeys=next.AllPerformanceKeys(_player.Vk).ToHashSet();_shortcuts.Clear();
            for(int i=0;i<15;i++)RefreshKey(i);
            _mapping?.RefreshInputConfiguration();RefreshPreviewLayout();RefreshShortcutHints();return true;
        }
        catch(Exception ex)
        {
            bool restored=true;try{_registrations?.Apply(_inputs.Functions,_mapping!=null);}catch(Exception rollback){restored=false;ex=new AggregateException(ex,rollback);}
            Logger.Exception("快捷键配置应用失败",ex);MsgBox.Info(this,Lang.S(restored?"map.applyFailed":"map.rollbackFailed")+"\n"+ex.Message,Lang.S("map.shortcuts"));return false;
        }
    }
    void OpenMapping()
    {
        if(!FeatureAvailability.SheetMappingEnabled)return;
        if(_mapping!=null){_mapping.Show();_mapping.Activate();return;}
        if(_notes.Count==0){ShowToast(Lang.S("nosong"));return;}
        if(_observer==null){ShowToast(Lang.S("map.applyFailed"));return;}
        StopPlaying();if(_gamePracticeOn)GamePracticeSwitch.IsChecked=false;
        if(_practiceOpen)ShowPractice(false);
        try
        {
            _registrations?.Apply(_inputs.Functions,true);_shortcuts.Clear();
            var project=_midiProject;
            if(project==null)
            {
                var t=new ProjectTrack{Name=Lang.S("ed.track")};
                t=t with{Notes=_sequence.Notes.Select(n=>new ProjectNote{TrackId=t.Id,Key=n.Key,Beat=n.StartMicroseconds/500000,
                    Action=n.Action,LengthBeats=n.LengthMicroseconds/500000,RetriggerOnOverlap=n.RetriggerOnOverlap}).ToList()};
                project=new(){Tracks=new(){t}};
            }
            _mapping=new(project,c=>_inputs.For(c,_player.Vk),name=>_inputs.Capability(name));
            _mapping.Closed+=(_,_)=>{_mapping=null;_shortcuts.Clear();try{_registrations?.Apply(_inputs.Functions);}catch(Exception ex){Logger.Exception("快捷键恢复失败",ex);ShowToast(Lang.S("map.applyFailed"));}};
            _mapping.Show();
        }
        catch(Exception ex){_mapping=null;try{_registrations?.Apply(_inputs.Functions);}catch(Exception rollback){ex=new AggregateException(ex,rollback);}
            Logger.Exception("谱面映射启动失败",ex);ShowToast(Lang.S("map.applyFailed"));}
    }
    InstrumentCapability? PreviewCapability=>_inputs.Capability(_instrumentName);
    internal void ReviewPreviewLayout(string instrument)
    { _instrumentName=instrument;InstrumentPill.Content=Lang.S("instrument")+":"+PreviewInstrumentText(instrument);RefreshPreviewLayout(); }
    internal System.Windows.Controls.Primitives.UniformGrid PreviewGrid=>PianoGrid;
    internal void ReviewCapabilities(InstrumentCapability capability)
    { _inputs=_inputs with{Capabilities=new(){capability}};RefreshPreviewLayout(); }
    void RefreshPreviewLayout()
    {
        if(_pianoButtons.Any(b=>b==null))return;
        var c=PreviewCapability;PianoGrid.Children.Clear();PianoGrid.Rows=c?.Rows??3;PianoGrid.Columns=c?.Columns??5;
        if(c==null){foreach(var b in _pianoButtons){b.IsEnabled=true;b.Opacity=1;System.Windows.Automation.AutomationProperties.SetName(b,$"K{b.Tag} · {_keyLabels[(int)b.Tag].Text}");System.Windows.Automation.AutomationProperties.SetHelpText(b,Lang.S("map.unknown"));PianoGrid.Children.Add(b);}return;}
        var bindings=_inputs.For(c,_player.Vk);
        foreach(var slot in c.Slots.OrderBy(s=>s.Row).ThenBy(s=>s.Column))
        {
            var k=c.Keys.FirstOrDefault(k=>k.Id==slot.KeyId);
            if(k==null){PianoGrid.Children.Add(new Border{Width=66,Height=66,Margin=new Thickness(4),Opacity=.35});continue;}
            var b=_pianoButtons[k.LogicalKey];b.IsEnabled=k.Availability!=KeyAvailability.Unavailable;b.Opacity=b.IsEnabled?1:.35;
            b.ToolTip=$"K{k.LogicalKey} · {k.Id} · "+Lang.S("map."+(k.Availability==KeyAvailability.Unknown?"unknown":b.IsEnabled?"available":"unavailable"));
            _keyLabels[k.LogicalKey].Text=KeyConfig.Label(bindings.Keys[k.Id]);PianoGrid.Children.Add(b);
            System.Windows.Automation.AutomationProperties.SetName(b,$"K{k.LogicalKey} · {_keyLabels[k.LogicalKey].Text}");
            System.Windows.Automation.AutomationProperties.SetHelpText(b,Lang.S("map."+(k.Availability==KeyAvailability.Unknown?"unknown":b.IsEnabled?"available":"unavailable")));
        }
    }
    void PreviewManual(int logical,bool down)
    {
        if(_shortcuts.Capturing||_previewLoading)return;
        var c=PreviewCapability;var k=c?.Resolve(logical,NoteKeyDomain.StableLogical);
        if(c!=null&&(k==null||k.Availability==KeyAvailability.Unavailable))return;
        int sample=k?.PreviewSample??logical;
        if(down){AudioEngine.NoteOn(sample);FlashKey(logical);}else AudioEngine.NoteOff(sample);
    }
    int PreviewLogical(ushort vk)
    {
        var c=PreviewCapability;if(c==null)return Array.IndexOf(_player.Vk,vk);
        var bindings=_inputs.For(c,_player.Vk);
        return c.Keys.FirstOrDefault(k=>bindings.Keys[k.Id]==vk && k.Availability!=KeyAvailability.Unavailable)?.LogicalKey??-1;
    }
    async Task SelectPreviewInstrument(string name)
    {
        int version=++_previewVersion;_previewLoading=true;await _previewLoad.WaitAsync();
        try{if(version!=_previewVersion)return;AudioEngine.StopManual();await Task.Run(()=>AudioEngine.SetInstrument(name=="Custom4"?"Piano":name));
            if(version==_previewVersion){_instrumentName=name;RefreshPreviewLayout();}}
        catch(Exception error){Logger.Exception("试听音色切换失败",error);ShowToast(Lang.S("map.applyFailed"));}
        finally{if(version==_previewVersion)_previewLoading=false;_previewLoad.Release();}
    }
}
