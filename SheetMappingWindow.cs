using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class SheetMappingWindow:ChromeWindow
{
    readonly SmapProject _project;readonly Func<InstrumentCapability,PerformanceBindingProfile> _bindings;
    readonly Func<string,InstrumentCapability?> _capabilities;
    readonly ComboBox _windows=new(),_tracks=new(),_instruments=new(),_mode=new();
    readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,12)};
    readonly DispatcherTimer _timer=new(){Interval=TimeSpan.FromMilliseconds(33)};
    readonly CancellationTokenSource _lifetime=new();
    readonly SheetOverlayWindow _overlay=new();
    readonly HashSet<ushort> _pressed=new(),_releaseFirst=new();
    SheetPractice? _practice;InstrumentCapability? _capability;ProjectTrack? _track;
    GameWindow? _target;SheetCalibration? _calibration;PerformanceBindingProfile? _binding;
    bool _busy,_closed,_hidden,_armed,_wasForeground;long _acceptAfter;DateTime _lastCheck;
    int _contextVersion;
    readonly Dictionary<int,string> _noteKeys=new();double _speed=1;
    public bool GameForeground=>_target is {} target&&GameWindows.IsForeground(target);
    public double Speed {get=>_speed;set{_speed=value;if(_practice!=null)_practice.Session.Speed=value;}}
    static string S(string k)=>Lang.S("map."+k);
    static string CalibrationPath(InstrumentCapability c)=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SMAP","calibration",SheetCalibration.Fingerprint(c)+".json");
    public SheetMappingWindow(SmapProject project,Func<InstrumentCapability,PerformanceBindingProfile> bindings,Func<string,InstrumentCapability?>? capabilities=null)
        :this(project,bindings,capabilities,FeatureAvailability.RequireSheetMapping()) { }
    SheetMappingWindow(SmapProject project,Func<InstrumentCapability,PerformanceBindingProfile> bindings,Func<string,InstrumentCapability?>? capabilities,bool available):base(S("title"),780)
    {
        _project=project;_bindings=bindings;_capabilities=capabilities??InstrumentCapabilities.ForInstrument;
        var root=new StackPanel{Margin=new Thickness(18)};
        void Add(string label,ComboBox combo){root.Children.Add(new TextBlock{Text=label,Foreground=B("TextFg"),Margin=new Thickness(0,7,0,4)});combo.Height=34;combo.Template=ComboTpl();System.Windows.Automation.AutomationProperties.SetName(combo,label);root.Children.Add(combo);}
        Add(S("window"),_windows);
        var refresh=Neutral("↻");refresh.Click+=(_,_)=>FindGame();root.Children.Add(refresh);
        Add(Lang.S("ed.track"),_tracks);_tracks.ItemsSource=project.Tracks.Select(t=>new ComboBoxItem{Content=string.IsNullOrWhiteSpace(t.Name)?Lang.S("ed.track"):t.Name,Tag=t}).ToArray();_tracks.SelectedIndex=0;
        Add(Lang.S("ed.target"),_instruments);_instruments.ItemsSource=GamePitchProfiles.Instruments.Concat(new[]{"Custom4"}).Select(n=>new ComboBoxItem{Content=n=="Custom4"?S("custom4"):Lang.Instrument(n),Tag=n}).ToArray();
        _instruments.SelectedValuePath="Tag";_instruments.SelectedValue=project.Tracks[0].Capability?.InstrumentId??project.Tracks[0].GameTarget?.InstrumentId??"Piano";
        Add(S("follow")+" / "+S("rhythm"),_mode);_mode.ItemsSource=new[]{S("follow"),S("rhythm")};_mode.SelectedIndex=0;
        var buttons=new WrapPanel{Margin=new Thickness(0,14,0,0)};
        foreach(var (label,action) in new (string,Action)[]{(S("detect"),()=>_=Calibrate(false)),(S("manual"),()=>_=Calibrate(true)),
            (S("start"),StartPractice),(S("pause"),PausePractice),(S("exit"),Close)})
        {var button=Neutral(label);button.Margin=new Thickness(0,4,8,4);button.Click+=(_,_)=>action();buttons.Children.Add(button);}root.Children.Add(buttons);
        _status.Foreground=B("SubTextFg");root.Children.Add(_status);root.Children.Add(new TextBlock{Text=S("readonly"),Foreground=B("SubTextFg"),TextWrapping=TextWrapping.Wrap});SetBody(root);
        _windows.SelectionChanged+=(_,_)=>Reset();_tracks.SelectionChanged+=(_,_)=>Reset();_instruments.SelectionChanged+=(_,_)=>Reset();_mode.SelectionChanged+=(_,_)=>Reset();
        _timer.Tick+=(_,_)=>Tick();_timer.Start();FindGame();
        Closed+=(_,_)=>{_closed=true;_lifetime.Cancel();_timer.Stop();PausePractice();_overlay.Close();_lifetime.Dispose();};
    }
    void FindGame(){_windows.ItemsSource=GameWindows.Find().Select(w=>new ComboBoxItem{Content=w.Title,Tag=w}).ToArray();if(_windows.Items.Count==1)_windows.SelectedIndex=0;else if(_windows.Items.Count==0)_status.Text=S("noGame");}
    void Reset()
    {
        _contextVersion++;
        PausePractice();_calibration=null;_practice=null;_capability=null;_target=null;
        if(_tracks.SelectedItem is not ComboBoxItem{Tag:ProjectTrack track}||_instruments.SelectedValue is not string name)return;
        _capability=track.Capability?.InstrumentId==name?track.Capability:_capabilities(name);
        if(_capability==null)return;
        bool keepLocal=track.Capability!=null?track.KeyDomain==NoteKeyDomain.LegacyOrdinal:
            track.GameTarget is {} t&&(t.KeyMidiPitches.Count is 4 or 8 || t.KeyMidiPitches.Count==0&&InstrumentCapabilities.ForTrack(track)?.PhysicalKeyCount is 4 or 8);
        _track=track with{Capability=_capability,KeyDomain=keepLocal?NoteKeyDomain.LegacyOrdinal:NoteKeyDomain.StableLogical};
        _binding=_bindings(_capability);if(!_binding.Valid(_capability)){_status.Text=S("unsupported");return;}
        _noteKeys.Clear();foreach(var k in _capability.Keys)_noteKeys[_capability.NoteKey(k,_track.KeyDomain)]=k.Id;
        _target=(_windows.SelectedItem as ComboBoxItem)?.Tag as GameWindow;TryRestore();
    }
    public void RefreshInputConfiguration()=>Reset();
    void TryRestore()
    {
        if(_capability==null||_binding==null||_target==null)return;
        try
        {
            string file=CalibrationPath(_capability);if(!File.Exists(file))return;
            var saved=JsonSerializer.Deserialize<SheetCalibration>(File.ReadAllText(file));
            if(saved?.Matches(_capability,SheetCalibration.Bindings(_binding.Keys),_target.Bounds.Width,_target.Bounds.Height,_target.Dpi)==true)
            {_calibration=saved;_status.Text=S("ready");}
        }
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){Logger.Exception("校准配置读取失败",ex);}
    }
    async Task Calibrate(bool manual)
    {
        FeatureAvailability.RequireSheetMapping();
        if(_busy||_capability==null||_binding==null||_target==null)return;
        _busy=true;PausePractice();var target=_target;var capability=_capability;var binding=_binding;int version=_contextVersion;_calibration=null;
        try
        {
            var pixels=await WindowCapture.OnceAsync(target.Handle,_lifetime.Token);KeyDetection? detection=null;
            if(!pixels.HasContent())throw new InvalidOperationException("CaptureContentUnavailable");
            if(!manual)
            {
                var first=await Task.Run(()=>SheetKeyDetector.Detect(pixels,capability),_lifetime.Token);
                await Task.Delay(120,_lifetime.Token);
                var secondPixels=await WindowCapture.OnceAsync(target.Handle,_lifetime.Token);
                var second=await Task.Run(()=>SheetKeyDetector.Detect(secondPixels,capability),_lifetime.Token);
                pixels=secondPixels;
                if(!SheetKeyDetector.Stable(first,second)){_status.Text=S("failed");manual=true;}
                else detection=second;
            }
            if(_closed||version!=_contextVersion)return;
            var current=GameWindows.Read(target.Handle);
            if(current==null||!GameWindows.SameIdentity(target,current)||current.Bounds!=target.Bounds||current.Dpi!=target.Dpi
                ||Math.Abs(pixels.Width-current.Bounds.Width)>2||Math.Abs(pixels.Height-current.Bounds.Height)>2){_status.Text=S("changed");return;}
            var dialog=new SheetCalibrationWindow(pixels,capability,detection){Owner=this};
            if(dialog.ShowDialog()!=true||dialog.Result is not {} positions)return;
            _calibration=new(){CapabilityFingerprint=SheetCalibration.Fingerprint(capability),BindingFingerprint=SheetCalibration.Bindings(binding.Keys),
                Width=current.Bounds.Width,Height=current.Bounds.Height,Dpi=current.Dpi,Keys=positions.ToList(),Manual=manual,Confirmed=true,
                VisualSignatures=SheetKeyDetector.Signatures(pixels,positions)};
            string file=CalibrationPath(capability);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(tmp,JsonSerializer.Serialize(_calibration));File.Move(tmp,file,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}
            _status.Text=S("ready");
            _lastCheck=DateTime.UtcNow;
        }
        catch(OperationCanceledException){}
        catch(Exception error){Logger.Exception("琴键定位失败",error);if(!_closed)_status.Text=S("failed")+"\n"+error.Message;}
        finally
        {
            _busy=false;
            if(!_closed&&version==_contextVersion&&_calibration==null)
            {TryRestore();if(_calibration!=null)_status.Text=S("retained");}
        }
    }
    void StartPractice()
    {
        FeatureAvailability.RequireSheetMapping();
        if(_calibration==null){_status.Text=S("uncalibrated");return;}
        if(_track==null||_capability==null||_binding==null)return;
        if(DateTime.UtcNow-_lastCheck>TimeSpan.FromSeconds(5)){_=VerifyBeforeStart();return;}
        try
        {
            var project=_project with{FormatVersion=2,KeyInterpretationVersion=1,Tracks=new(){_track with{Muted=false,Solo=false}}};
            PerformanceCompatibility.RequirePlayable(project,allowUnknown:true);
            if(_capability.Keys.Any(k=>k.Availability==KeyAvailability.Unknown) && _practice==null
                && !MsgBox.Choice(this,S("unknownConfirm"),S("title"),Lang.S("d.ok"),Lang.S("d.cancel")))return;
            _practice??=new(PlaybackSequence.FromProject(project),new StopwatchClock(),_mode.SelectedIndex==1?PracticeMode.Rhythm:PracticeMode.Follow);
            _practice.Session.Speed=_speed;_armed=true;_hidden=false;_acceptAfter=Stopwatch.GetTimestamp();
            _releaseFirst.Clear();foreach(var vk in _binding.Keys.Values)if(GetAsyncKeyState(vk)<0)_releaseFirst.Add(vk);
            if(GameForeground){_practice.Context(true);_practice.Resume();_armed=false;}
            _status.Text=S(_armed?"armed":"running");
        }
        catch(Exception error){Logger.Exception("练习兼容检查失败",error);_status.Text=S("unsupported")+"\n"+error.Message;}
    }
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    async Task VerifyBeforeStart()
    {
        if(_busy||_target==null)return;
        if(await Recheck(_target)){_lastCheck=DateTime.UtcNow;StartPractice();}
    }
    public void PausePractice(){_armed=false;_practice?.Pause();_pressed.Clear();_acceptAfter=Stopwatch.GetTimestamp();_lastCheck=DateTime.MinValue;_overlay.Hide();if(_practice!=null)_status.Text=S("paused");}
    public void TogglePause(){if(_practice?.Session.Paused==false)PausePractice();else StartPractice();}
    public void ToggleHints(){_hidden=!_hidden;if(_hidden)PausePractice();else StartPractice();}
    public void RestartPractice(){_practice?.Session.Seek(0);PausePractice();StartPractice();}
    public void Seek(double delta){if(_practice==null)return;_practice.Session.Seek(Math.Max(0,_practice.Session.PositionMs+delta));PausePractice();}
    public void Input(ObservedKey e)
    {
        if(_practice==null||_target==null||_binding==null||_track==null||_capability==null||_hidden||e.Foreground!=_target.Handle
            ||!GameForeground||e.Timestamp<_acceptAfter)return;
        var pair=_binding.Keys.FirstOrDefault(p=>p.Value==e.Key);if(pair.Key==null)return;
        if(!e.Down){_releaseFirst.Remove(e.Key);_pressed.Remove(e.Key);}else if(_releaseFirst.Contains(e.Key)||!_pressed.Add(e.Key))return;
        var k=_capability.Keys.Single(k=>k.Id==pair.Key);_practice.Input(_capability.NoteKey(k,_track.KeyDomain),e.Down,_practice.Generation);
    }
    void Tick()
    {
        if(_closed||_target==null||_capability==null||_binding==null)return;
        try
        {
            var current=GameWindows.Read(_target.Handle);bool foreground=current!=null&&GameWindows.SameIdentity(_target,current)&&GameWindows.IsForeground(current);
            bool obscured=foreground&&GameWindows.Occluded(current!,_overlay.Handle);
            MappingWindowSnapshot Snapshot(GameWindow w,bool active,bool covered=false)=>new(w.Handle.ToInt64(),w.ProcessId,w.ProcessStarted,w.Bounds.Width,w.Bounds.Height,w.Dpi,active,covered);
            var decision=MappingWindowPolicy.Evaluate(Snapshot(_target,false),current==null?null:Snapshot(current,foreground,obscured),_calibration,_capability,SheetCalibration.Bindings(_binding.Keys));
            bool ready=decision.ShowHints;
            if(!ready)
            {
                if(_wasForeground){_practice?.Context(false);PausePractice();}
                _wasForeground=false;_overlay.Hide();_status.Text=S(_armed&&decision.Reason=="focus"?"armed":decision.Reason);return;
            }
            if(!_wasForeground)
            {
                _practice?.Context(true);_acceptAfter=Stopwatch.GetTimestamp();_pressed.Clear();
                if(_binding!=null){_releaseFirst.Clear();foreach(var vk in _binding.Keys.Values)if(GetAsyncKeyState(vk)<0)_releaseFirst.Add(vk);}
                if(_armed)
                {
                    if(DateTime.UtcNow-_lastCheck>TimeSpan.FromSeconds(5)){_armed=false;_=VerifyBeforeStart();return;}
                    _practice?.Resume();_armed=false;
                }
                _wasForeground=true;
                if(_status.Text==S("inactive")||_status.Text==S("focus")||_status.Text==S("covered"))_status.Text=S("ready");
            }
            if(_practice==null||_hidden)return;
            var hints=_practice.Tick();
            _status.Text=S(hints.Completed?"done":hints.Paused?"paused":"running");
            if(!hints.Paused)_overlay.Present(current!.Bounds,current.Dpi,_capability,_calibration!.Keys,_noteKeys,hints,_mode.SelectedIndex==1?S("rhythm"):S("follow"));
            else _overlay.Hide();
            if(!hints.Paused&&DateTime.UtcNow-_lastCheck>TimeSpan.FromSeconds(5))
            {_lastCheck=DateTime.UtcNow;_=Recheck(current!);}
        }
        catch(Exception error){PausePractice();Logger.Exception("谱面映射已暂停",error);_status.Text=S("failed");}
    }
    async Task<bool> Recheck(GameWindow target)
    {
        if(_busy||_closed||_capability==null)return false;_busy=true;
        var capability=_capability;var calibration=_calibration;int version=_contextVersion;
        try
        {
            var frame=await WindowCapture.OnceAsync(target.Handle,_lifetime.Token);
            if(_closed||version!=_contextVersion||calibration==null)return false;
            bool valid;
            if(calibration.Manual)valid=SheetKeyDetector.SameVisualContext(calibration.VisualSignatures,SheetKeyDetector.Signatures(frame,calibration.Keys));
            else
            {
                bool verified=await Task.Run(()=>SheetKeyDetector.VerifyLocations(frame,capability,calibration.Keys),_lifetime.Token);
                if(_closed||version!=_contextVersion)return false;
                valid=verified;
            }
            if(!valid){_calibration=null;PausePractice();_status.Text=S("failed");}
            return valid;
        }
        catch(OperationCanceledException){return false;}
        catch(Exception error){PausePractice();Logger.Exception("琴键区域复核失败",error);return false;}
        finally{_busy=false;}
    }
}
