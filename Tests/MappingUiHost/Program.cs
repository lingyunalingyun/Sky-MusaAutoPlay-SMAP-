using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

namespace SMAP.MappingTests;

static class Program
{
    static readonly string Output=Path.Combine(Path.GetTempPath(),"SMAP-Phase5B-verification");
    static int _checks;
    static void Check(bool value,string message){_checks++;if(!value)throw new InvalidOperationException(message);}
    [STAThread]static int Main(string[] args)
    {
        try
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Directory.CreateDirectory(Output);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            if(args.Contains("metrics"))Metrics();else if(args.Contains("capture"))CaptureSmoke();else if(args.Contains("accessibility"))Accessibility();else if(args.Contains("placement"))Placement();else if(args.Contains("feedback"))Feedback();else if(args.Length>1&&args[0]=="reference")Reference(args[1]);else Verify();
            Console.WriteLine(JsonSerializer.Serialize(new{checks=_checks,screenshots=Output}));app.Shutdown();return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    static void Placement()
    {
        Theme.Apply(AppTheme.Dark,persist:false);var c=Capability(15);var locations=Locations(c);var keys=c.Keys.ToDictionary(k=>k.LogicalKey,k=>k.Id);
        var overlay=new SheetOverlayWindow();var bounds=new PhysicalRect(100,100,1100,700);var hints=new PracticeHintSnapshot(new[]{0},Array.Empty<int>(),Array.Empty<HoldHint>(),null,false,false);
        overlay.Present(bounds,96,c,locations,keys,hints,"Follow");
        overlay.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>SetWindowPos(overlay.Handle,IntPtr.Zero,bounds.Left,bounds.Top,600,400,0x14)));
        Pump();GetWindowRect(overlay.Handle,out var placed);Check(placed==bounds,"Deferred window sizing changed the calibrated physical bounds");
        overlay.Present(new PhysicalRect(120,120,1120,720),96,c,locations,keys,hints,"Follow");overlay.Hide();Pump();Check(!overlay.IsVisible,"Pending placement resurrected a hidden overlay");overlay.Close();
        Console.WriteLine("MAPPING_PLACEMENT physical_bounds=True;hidden=True");
    }
    static void Feedback()
    {
        Theme.Apply(AppTheme.Dark,persist:false);var window=new SheetMappingWindow(Project(),c=>PerformanceBindingProfile.FromLegacy(c,SkyPlayer.DefaultVk));
        window.Show();Pump();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string name,object? value)=>typeof(SheetMappingWindow).GetField(name,flags)!.SetValue(window,value);
        var status=(TextBlock)typeof(SheetMappingWindow).GetField("_status",flags)!.GetValue(window)!;
        var start=typeof(SheetMappingWindow).GetMethod("StartPractice",flags)!;Set("_calibration",null);start.Invoke(window,null);
        Check(status.Text==Lang.S("map.uncalibrated"),"Start without calibration returned without explaining the missing positions");
        var c=Capability(15);c=c with{Keys=c.Keys.Select(k=>k with{Availability=KeyAvailability.Available}).ToList()};
        var track=Project().Tracks[0] with{Capability=c,GameTarget=GamePitchProfiles.Get("Piano").Target()};
        Set("_capability",c);Set("_track",track);Set("_target",null);Set("_binding",PerformanceBindingProfile.FromLegacy(c,SkyPlayer.DefaultVk));
        Set("_calibration",new SheetCalibration{Confirmed=true,Keys=Locations(c),Width=1000,Height=600});Set("_lastCheck",DateTime.UtcNow);start.Invoke(window,null);
        var practice=(SheetPractice)typeof(SheetMappingWindow).GetField("_practice",flags)!.GetValue(window)!;
        Check(practice.Session.Paused&&status.Text==Lang.S("map.armed"),"Start did not report that it is waiting for the game foreground");
        window.PausePractice();Check(practice.Session.Paused&&status.Text==Lang.S("map.paused"),"Pause did not report its actual practice state");
        window.Close();Console.WriteLine("MAPPING_FEEDBACK uncalibrated=True;armed=True;paused=True");
    }
    static void Accessibility()
    {
        Theme.Apply(AppTheme.Dark,persist:false);
        var main=new MainWindow(true);main.Show();Pump();
        foreach(string instrument in new[]{"Piano","Handpan","Custom4"})
        {
            main.ReviewPreviewLayout(instrument);var capability=InstrumentCapabilities.ForInstrument(instrument)!;
            main.ReviewCapabilities(capability with{Keys=capability.Keys.Select((k,i)=>i==1?k with{Availability=KeyAvailability.Unavailable}:k).ToList()});Pump();
            foreach(var button in main.PreviewGrid.Children.OfType<Button>())
            {
                var peer=UIElementAutomationPeer.CreatePeerForElement(button)!;
                Check(peer.GetName().Contains($"K{button.Tag}"),"A virtual key has no accessible logical identity");
                Check(!string.IsNullOrWhiteSpace(peer.GetHelpText()),"A virtual key has no accessible availability");
            }
        }
        main.Close();
        var settings=new InputSettingsWindow(new(),SkyPlayer.DefaultVk,_=>false,_=>{});settings.Show();Pump();
        var rows=(StackPanel)typeof(InputSettingsWindow).GetField("_rows",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(settings)!;
        foreach(var row in rows.Children.OfType<StackPanel>())foreach(var field in row.Children.OfType<Control>())
            Check(!string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(field)?.GetName()),"A shortcut field has no accessible name");
        settings.Close();
        var mapping=new SheetMappingWindow(Project(),c=>PerformanceBindingProfile.FromLegacy(c,SkyPlayer.DefaultVk));mapping.Show();Pump();
        foreach(string name in new[]{"_windows","_tracks","_instruments","_mode"})
        {
            var field=(ComboBox)typeof(SheetMappingWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mapping)!;
            Check(!string.IsNullOrWhiteSpace(UIElementAutomationPeer.CreatePeerForElement(field)?.GetName()),"A mapping selector has no accessible name");
        }
        mapping.Close();Console.WriteLine("MAPPING_ACCESSIBILITY names=True;physical_input=False;screen_reader=False");
    }
    static void Settle(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static void Capture(Window window,string name)
    {
        Pump();window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(Output,name+".png"));encoder.Save(file);
    }
    static InstrumentCapability Capability(int count)=>count==4?InstrumentCapabilities.CustomFour():InstrumentCapabilities.ForInstrument(count==8?"Handpan":"Piano")!;
    static List<KeyLocation> Locations(InstrumentCapability c)=>c.Slots.Select(s=>new KeyLocation(s.KeyId!,.5+(s.Column-(c.Columns-1)/2.0)*.09,.2+s.Row*.18,.055,.09)).ToList();
    static SmapProject Project()
    {
        var track=new ProjectTrack{Name="旋律 / Melody",Capability=Capability(8),KeyDomain=NoteKeyDomain.StableLogical,GameTarget=GamePitchProfiles.Get("Handpan").Target()};
        track=track with{Notes=new(){new(){TrackId=track.Id,Key=4,Beat=1,Action=NoteAction.Hold,LengthBeats=2},
            new(){TrackId=track.Id,Key=7,Beat=0,Action=NoteAction.Hold,LengthBeats=3,MidiOrigin=new(0,1,61,100,0,1440)},
            new(){TrackId=track.Id,Key=14,Beat=3,Action=NoteAction.Tap}}};
        return new(){FormatVersion=2,KeyInterpretationVersion=1,Tracks=new(){track}};
    }
    static void Verify()
    {
        int renders=0;
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var main=new MainWindow(true){Width=1248,Height=756,Left=20,Top=20};main.Show();Pump();
            foreach(string instrument in new[]{"Piano","Handpan","Custom4"})
            {
                main.ReviewPreviewLayout(instrument);Pump();var c=InstrumentCapabilities.ForInstrument(instrument)!;
                Check(main.PreviewGrid.Rows==c.Rows&&main.PreviewGrid.Columns==c.Columns,"Preview layout mismatch");Check(main.PreviewGrid.Children.Count==c.Slots.Count,"Preview slots collapsed");
                var buttons=main.PreviewGrid.Children.OfType<Button>().ToArray();Check(buttons.Select(b=>b.ActualWidth).Distinct().Count()==1,"Unequal key widths");
                Check(buttons.Select(b=>(int)b.Tag).SequenceEqual(c.Slots.Select(s=>c.Keys.Single(k=>k.Id==s.KeyId).LogicalKey)),"Preview uses a prefix instead of explicit logical keys");
                if(language==AppLang.ZhCN)Capture(main,$"main-{instrument}-{theme}");renders++;
                if(instrument!="Piano")
                {
                    var disabled=c with{Keys=c.Keys.Select((k,i)=>i==1?k with{Availability=KeyAvailability.Unavailable}:k).ToList()};
                    var original=buttons.Select(b=>b.TranslatePoint(new Point(0,0),main.PreviewGrid)).ToArray();main.ReviewCapabilities(disabled);Pump();
                    Check(main.PreviewGrid.Children.Count==c.Slots.Count,"Disabled key removed its slot");
                    var now=main.PreviewGrid.Children.OfType<Button>().ToArray();Check(!now[1].IsEnabled,"Unavailable key remains enabled");
                    Check(now.Select(b=>b.TranslatePoint(new Point(0,0),main.PreviewGrid)).SequenceEqual(original),"Disabled key moved neighboring buttons");
                    if(language==AppLang.ZhCN)Capture(main,$"main-{instrument}-disabled-{theme}");
                }
            }
            var preferencesField=typeof(MainWindow).GetField("_inputs",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var preferences=(InputPreferences)preferencesField.GetValue(main)!;
            preferencesField.SetValue(main,preferences with{Functions=preferences.Functions.Select(s=>s.Action==ShortcutAction.PlayStop?s with{Chord=new(0x78,ShortcutModifiers.Ctrl)}:s.Action==ShortcutAction.PauseResume?s with{Chord=null}:s).ToList()});
            main.ApplyLanguage();
            Check(main.StartBtn.Content.ToString()!.Contains("Ctrl+F9")&&!main.StartBtn.Content.ToString()!.Contains("F1"),"Player label retained an obsolete shortcut");
            Check(main.PauseBtn.Content.ToString()!.Contains(Lang.S("map.unbound")),"Unbound player shortcut is still presented as F2");
            main.Close();Pump();
            var settings=new InputSettingsWindow(new(),SkyPlayer.DefaultVk,_=>true,_=>{});settings.Show();Pump();
            Check(settings.ActualWidth>=900&&settings.ActualHeight<1000,"Shortcut settings clipped");
            if(language==AppLang.ZhCN)Capture(settings,$"shortcuts-{theme}");renders++;
            settings.ReviewPerformance("Handpan");Pump();if(language==AppLang.ZhCN)Capture(settings,$"bindings-{theme}");settings.Close();
        }
        var recording=new InputSettingsWindow(new(),SkyPlayer.DefaultVk,_=>true,_=>{});recording.Show();Pump();ShortcutChord? captured=null;
        var recordMethod=typeof(InputSettingsWindow).GetMethod("Capture",BindingFlags.Instance|BindingFlags.NonPublic)!;
        recordMethod.Invoke(recording,new object[]{new Button(),new Action<ShortcutChord?>(c=>captured=c)});
        recording.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(recording),0,System.Windows.Input.Key.F9)
            {RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent});
        Check(captured==new ShortcutChord(0x78),"Shortcut capture did not consume the actual WPF key event");recording.Close();
        Theme.Apply(AppTheme.Dark,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,AppLang.ZhCN);
        var editor=new EditorWindow(Project()){AudioEnabled=false,Width=1232,Height=760};editor.Show();Pump();
        var n=editor.Session.ActiveTrack.Notes[0];var r=editor.Viewport.Bounds(n);Check(editor.Viewport.HitNotes(new(r.X+4,r.Y+4)).Any(x=>x.Id==n.Id),"Grey note cannot be selected");
        editor.Session.Select(new[]{n.Id});editor.Session.MoveSelected(1,0);editor.Session.Undo();Check(editor.Session.Find(n.Id)==n,"Grey note undo failed");Capture(editor,"roll-grey");editor.Session.MarkSaved();editor.Close();
        var guarded=new MainWindow(true);int outputAttempts=0;guarded.GameInputFactory=()=>{outputAttempts++;throw new InvalidOperationException("Mapping attempted automatic game output");};
        var control=new SheetMappingWindow(Project(),c=>PerformanceBindingProfile.FromLegacy(c,SkyPlayer.DefaultVk));control.Show();Pump();
        var trackSelector=(ComboBox)typeof(SheetMappingWindow).GetField("_tracks",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(control)!;
        var targetSelector=(ComboBox)typeof(SheetMappingWindow).GetField("_instruments",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(control)!;
        Check(trackSelector.SelectionBoxItem.ToString()=="旋律 / Melody","Mapping selector exposed the internal project record");
        Check(targetSelector.SelectionBoxItem.ToString()==Lang.Instrument("Handpan"),"Mapping target instrument was not localized");Capture(control,"mapping-control");
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;typeof(MainWindow).GetField("_mapping",flags)!.SetValue(guarded,control);
        typeof(MainWindow).GetMethod("PlaySequence",flags)!.Invoke(guarded,new object[]{false,0d});
        foreach(var action in new[]{ShortcutAction.PlayStop,ShortcutAction.PauseResume,ShortcutAction.SeekBack,ShortcutAction.SeekForward,ShortcutAction.TogglePreview})
            typeof(MainWindow).GetMethod("ExecuteShortcut",flags)!.Invoke(guarded,new object[]{action});
        Check(outputAttempts==0,"Mapping mode reached the automatic input factory");
        typeof(MainWindow).GetField("_mapping",flags)!.SetValue(guarded,null);control.Close();guarded.Close();
        foreach(int count in new[]{15,8,4})
        {
            var c=Capability(count);var locations=Locations(c);var game=Fixture(c,locations);game.Show();game.Activate();Settle(200);
            IntPtr hwnd=new WindowInteropHelper(game).Handle;var target=GameWindows.Read(hwnd)!;
            if(target==null)
            {
                int hr=DwmGetWindowAttribute(hwnd,14,out int cloaked,4);GetWindowRect(hwnd,out var rect);GetWindowThreadProcessId(hwnd,out uint pid);
                Console.WriteLine(JsonSerializer.Serialize(new{hwnd=hwnd.ToInt64(),game.IsVisible,state=game.WindowState,visible=IsWindowVisible(hwnd),cloaked,hr,rect,pid,current=Environment.ProcessId}));
                Check(cloaked!=0,"Window unexpectedly unavailable without cloaking");
                DwmBounds(hwnd,9,out var captureRect,16);
                using var own=Process.GetCurrentProcess();
                target=new(hwnd,Environment.ProcessId,own.StartTime.ToUniversalTime().Ticks,game.Title,captureRect,GetDpiForWindow(hwnd));
            }
            Check(target!=null,"Window identity unavailable");
            var before=GameWindows.GetForegroundWindow();var overlay=new SheetOverlayWindow();
            var keys=c.Keys.ToDictionary(k=>k.LogicalKey,k=>k.Id);
            var hint=new PracticeHintSnapshot(new[]{c.Keys[0].LogicalKey},new[]{c.Keys[^1].LogicalKey},new[]{new HoldHint(c.Keys[1].LogicalKey,800,false,false)},null,false,false);
            overlay.Present(target!.Bounds,target.Dpi,c,locations,keys,hint,Lang.S("map.follow"));Pump();Check(GameWindows.GetForegroundWindow()==before,"Overlay stole focus");
            long style=GetWindowLongPtr(new WindowInteropHelper(overlay).Handle,-20).ToInt64();Check((style&0x8000020)==0x8000020,"Overlay not pass through and non activating");
            Capture(overlay,$"overlay-{count}-transparent");overlay.Hide();Settle(100);
            var watch=Stopwatch.StartNew();var task=Task.Run(()=>WindowCapture.OnceAsync(hwnd));Wait(task);var pixels=task.GetAwaiter().GetResult();watch.Stop();
            Check(pixels.Width>0&&pixels.Height>0,"WGC produced empty frame");Console.WriteLine(JsonSerializer.Serialize(new{scenario="wgc-owned-fixture",keys=count,elapsed_ms=watch.Elapsed.TotalMilliseconds,width=pixels.Width,height=pixels.Height,bytes=pixels.Bgra.Length}));
            var detected=SheetKeyDetector.Detect(pixels,c);Check(detected.Keys.Count==count,"WGC key grid detection failed");
            Capture(game,$"captured-layout-{count}");
            var calibration=new SheetCalibrationWindow(pixels,c);calibration.Show();Pump();Capture(calibration,$"calibration-{count}");calibration.Close();
            overlay.Close();game.Close();Pump();
        }
        Console.WriteLine($"MAPPING_UI palettes=3;languages=4;renders={renders};actual_windows=True;capture=WGC;game_input=False;game_acceptance=False");
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr h,out PhysicalRect rect);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]static extern int DwmBounds(IntPtr h,int attr,out PhysicalRect rect,int size);
    static void Wait(Task task)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,_)=>{if(task.IsCompleted){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);
    }
    static void CaptureSmoke()
    {
        Theme.Apply(AppTheme.Dark,persist:false);var c=Capability(15);var game=Fixture(c,Locations(c));game.Show();Settle(200);
        var hwnd=new WindowInteropHelper(game).Handle;var watch=Stopwatch.StartNew();
        var task=Task.Run(()=>WindowCapture.OnceAsync(hwnd));Wait(task);var pixels=task.GetAwaiter().GetResult();
        Console.WriteLine(JsonSerializer.Serialize(new{scenario="wgc-owned-window",elapsed_ms=watch.Elapsed.TotalMilliseconds,pixels.Width,pixels.Height,bytes=pixels.Bgra.Length}));
        game.Close();
    }
    static void Reference(string path)
    {
        var image=new BitmapImage(new Uri(Path.GetFullPath(path)));var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
        int stride=converted.PixelWidth*4;var bytes=new byte[stride*converted.PixelHeight];converted.CopyPixels(bytes,stride,0);
        var watch=Stopwatch.StartNew();var detected=SheetKeyDetector.Detect(new(bytes,converted.PixelWidth,converted.PixelHeight,stride),Capability(15));
        Console.WriteLine(JsonSerializer.Serialize(new{scenario="provided-screenshot-offline",offline_only=true,detected=detected.Keys.Count,detected.Confidence,detected.Reason,elapsed_ms=watch.Elapsed.TotalMilliseconds,positions=detected.Keys}));
    }
    static Window Fixture(InstrumentCapability c,IReadOnlyList<KeyLocation> locations)
    {
        var canvas=new Canvas{Background=new SolidColorBrush(Color.FromRgb(55,100,140)),Width=1000,Height=600};
        foreach(var k in locations)
        {
            var button=new Border{Width=k.Width*1000,Height=k.Height*600,CornerRadius=new CornerRadius(8),Background=new SolidColorBrush(Color.FromRgb(75,75,75)),Child=new TextBlock{Text=k.KeyId,Foreground=Brushes.Bisque,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            Canvas.SetLeft(button,(k.X-k.Width/2)*1000);Canvas.SetTop(button,(k.Y-k.Height/2)*600);canvas.Children.Add(button);
        }
        return new Window{Title="Sheet mapping window fixture",Content=canvas,Width=1016,Height=639,Left=30,Top=30,ResizeMode=ResizeMode.NoResize};
    }
    static void Metrics()
    {
        Theme.Apply(AppTheme.Dark,persist:false);var c=Capability(15);var locations=Locations(c);var overlay=new SheetOverlayWindow();var keys=c.Keys.ToDictionary(k=>k.LogicalKey,k=>k.Id);
        var bounds=new PhysicalRect(30,30,1030,630);var frameTimes=new List<double>();
        for(int i=0;i<120;i++)
        {
            var hints=new PracticeHintSnapshot(new[]{i%15},new[]{(i+1)%15},new[]{new HoldHint((i+2)%15,1200-i*10,false,false)},null,false,false);
            var w=Stopwatch.StartNew();overlay.Present(bounds,96,c,locations,keys,hints,"Follow");Pump();frameTimes.Add(w.Elapsed.TotalMilliseconds);
        }
        using var process=Process.GetCurrentProcess();process.Refresh();var cpu=process.TotalProcessorTime;int threads=process.Threads.Count;var watch=Stopwatch.StartNew();
        int updates=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};
        timer.Tick+=(_,_)=>{updates++;overlay.Present(bounds,96,c,locations,keys,new(new[]{updates%15},Array.Empty<int>(),Array.Empty<HoldHint>(),null,false,false),"Follow");};timer.Start();Settle(3000);timer.Stop();
        process.Refresh();double elapsed=watch.Elapsed.TotalMilliseconds,cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds;
        var warm=frameTimes.Skip(5).Order().ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new{scenario="overlay-15-keys-wpf-active",first_frame_ms=frameTimes[0],warm_submit_mean_ms=warm.Average(),warm_submit_p95_ms=warm[(int)(warm.Length*.95)],active_updates=updates,elapsed_ms=elapsed,cpu_ms=cpuMs,one_core_cpu_percent=cpuMs/elapsed*100,logical_processors=Environment.ProcessorCount,total_cpu_percent=cpuMs/elapsed/Environment.ProcessorCount*100,threads_before=threads,threads_after=process.Threads.Count,peak_working_set=process.PeakWorkingSet64,capture_count=0}));
        var idle=new PracticeHintSnapshot(new[]{0},Array.Empty<int>(),Array.Empty<HoldHint>(),null,false,false);
        overlay.Present(bounds,96,c,locations,keys,idle,"Follow");Settle(200);process.Refresh();cpu=process.TotalProcessorTime;watch.Restart();int before=process.Threads.Count;
        var idleTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(33)};idleTimer.Tick+=(_,_)=>overlay.Present(bounds,96,c,locations,keys,idle,"Follow");idleTimer.Start();Settle(3000);idleTimer.Stop();
        process.Refresh();Console.WriteLine(JsonSerializer.Serialize(new{scenario="overlay-follow-waiting",elapsed_ms=watch.Elapsed.TotalMilliseconds,cpu_ms=(process.TotalProcessorTime-cpu).TotalMilliseconds,threads_before=before,threads_after=process.Threads.Count,capture_count=0}));overlay.Close();
    }
}
