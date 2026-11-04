using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

namespace SMAP.VersionTests;

static class Program
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int _checks;
    static void Check(bool condition,string message){_checks++;if(!condition)throw new InvalidOperationException(message);}
    static object? Field(object value,string name)=>value.GetType().GetField(name,Private)!.GetValue(value);
    static object? Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Private)!.Invoke(value,args);
    static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    sealed class Registrar:IShortcutRegistrar
    {
        internal readonly Dictionary<int,ShortcutChord> Active=new();
        public bool Register(int id,ShortcutChord chord)=>Active.TryAdd(id,chord);
        public bool Unregister(int id)=>Active.Remove(id);
    }
    sealed class Observer(bool fail=false):IKeyboardObserver
    {
        public event Action<ObservedKey>? Key;public event Action? Overflow;
        internal bool Disposed;
        internal bool Subscribed=>Key!=null||Overflow!=null;
        internal void Send(ObservedKey e)=>Key?.Invoke(e);
        internal void Full()=>Overflow?.Invoke();
        public void Dispose(){Disposed=true;if(fail)throw new InvalidOperationException("ObserverTestFailure");}
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Check(!FeatureAvailability.SheetMappingEnabled,"Production host loaded a mapping development assembly");
            if(args.Contains("manual")){Manual(app,args);return 0;}
            Theme.Apply(AppTheme.Dark,persist:false);
            if(args.Contains("windows"))Windows(args.Length>1?args[1]:null);
            else if(args.Contains("lifecycle"))Lifecycle();
            else if(args.Contains("observer"))NativeObserver();
            else if(args.Contains("native-shortcuts"))NativeShortcuts();
            else if(args.Contains("localization"))TrackLocalization();
            else if(args.Contains("editor-fixes"))EditorFixes(args.Length>1?args[1]:null);
            else if(args.Contains("library-practice"))LibraryPractice();
            else if(args.Contains("registration-rollback"))RegistrationRollback();
            else if(args.Contains("choice-reuse"))ChoiceReuse();
            else if(args.Contains("choice-layout"))ChoiceLayout(args.Length>1?args[1]:null);
            else if(args.Contains("joined-choice"))JoinedChoice(args.Length>1?args[1]:null);
            else if(args.Contains("cursor-keys"))CursorKeys(args.Length>1?args[1]:null);
            else if(args.Contains("choice-lifecycle"))ChoiceLifecycle(args.Length>1?args[1]:null);
            else if(args.Contains("cloud-choice"))CloudChoice(args.Length>1?args[1]:null);
            else if(args.Contains("settings-choice"))SettingsChoice(args.Length>1?args[1]:null);
            else if(args.Contains("choice-motion"))ChoiceMotion();
            else if(args.Contains("instrument-pill"))InstrumentPill(args.Length>1?args[1]:null);
            else if(args.Contains("instrument-scroll"))InstrumentScroll();
            else if(args.Contains("pitch-pill"))PitchPill(args.Length>1?args[1]:null);
            else if(args.Contains("speed-pill"))SpeedPill(args.Length>1?args[1]:null);
            else if(args.Contains("editor-choices"))EditorChoices(args.Length>1?args[1]:null);
            else if(args.Contains("editor-track-choice"))EditorTrackChoice(args.Length>1?args[1]:null);
            else if(args.Contains("editor-command-menu"))EditorCommandMenu(args.Length>1?args[1]:null);
            else if(args.Contains("editor-window-mode"))EditorWindowMode();
            else if(args.Contains("roll-note-preview"))RollNotePreview(args.Length>1?args[1]:null);
            else if(args.Contains("overview-range"))OverviewRange(args.Length>1?args[1]:null);
            else if(args.Contains("editor-reference-keys"))EditorReferenceKeys(args.Length>1?args[1]:null);
            else if(args.Contains("editor-open"))EditorOpen();
            else if(args.Contains("track-toggles"))TrackToggles(args.Length>1?args[1]:null);
            else if(args.Contains("cursor-center"))CursorCenter(args.Length>1?args[1]:null);
            else if(args.Any(a=>a is "practice-cover" or "practice-timing" or "practice-return" or "practice-rebound"))PracticeReturn(args[0],args.Length>1?args[1]:null);
            else Boundaries();
            Console.WriteLine(JsonSerializer.Serialize(new{checks=_checks,mapping=false,game_capture=false}));app.Shutdown();return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Blocked(Action action)
    {
        int before=Application.Current.Windows.Count;
        try{action();throw new InvalidOperationException("Mapping entry was not blocked");}
        catch(NotSupportedException e){Check(e.Message=="SheetMappingDeferredToV31","Wrong mapping rejection");}
        Check(Application.Current.Windows.Count==before,"A blocked entry created a WPF window");
    }
    static void Boundaries()
    {
        var project=new SmapProject();var c=InstrumentCapabilities.ForInstrument("Piano")!;
        Blocked(()=>_ =new SheetMappingWindow(project,x=>PerformanceBindingProfile.FromLegacy(x,SkyPlayer.DefaultVk)));
        Blocked(()=>_ =new SheetOverlayWindow());
        Blocked(()=>_ =new SheetCalibrationWindow(new(Array.Empty<byte>(),0,0,0),c));
        Blocked(()=>_ =WindowCapture.OnceAsync(IntPtr.Zero));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Blocked(()=>_ =WindowCapture.OnceAsync(new IntPtr(1),cancelled.Token));
        Console.WriteLine("VERSION_BOUNDARIES windows=0;capture_entry=blocked;old_state=blocked");
    }
    static MainWindow CreateMain(Registrar registrar,Observer observer)
    {
        var main=new MainWindow(true);main.Show();Pump();main.InitializeInputRouting(registrar,observer);return main;
    }
    static void Lifecycle()
    {
        var unshown=new MainWindow(true);unshown.Close();Check(Field(unshown,"_observer")==null,"Closing an unshown window started input observation");
        foreach(bool fail in new[]{false,true})
        {
            var registrar=new Registrar();var observer=new Observer(fail);var main=CreateMain(registrar,observer);int windows=Application.Current.Windows.Count;
            Check(observer.Subscribed&&registrar.Active.Count==8,"Ordinary input services failed to start");
            Check(registrar.Active.Values.All(c=>!SkyPlayer.DefaultVk.Contains(c.Key)),"A performance key was registered globally");
            double speed=(double)Field(main,"_speed")!;
            observer.Send(new(0x72,0,true,GameWindows.GetForegroundWindow(),Stopwatch.GetTimestamp()));
            Check((double)Field(main,"_speed")! ==speed,"The keyboard observer duplicated a registered shortcut");
            Check((bool)Call(main,"RegisteredShortcut",registrar.Active.Single(p=>p.Value.Key==0x72).Key)!,"F3 registration was not recognized");
            Check(Math.Abs((double)Field(main,"_speed")!-(speed-.1))<.000001,"F3 did not change the actual main-window speed");
            Call(main,"RegisteredShortcut",registrar.Active.Single(p=>p.Value.Key==0x73).Key);
            Check(Math.Abs((double)Field(main,"_speed")!-speed)<.000001,"F4 did not restore the actual main-window speed");
            int sends=0;main.GameInputFactory=()=>{sends++;throw new InvalidOperationException("UnexpectedGameInput");};
            for(int i=0;i<3;i++)
            {
                Call(main,"OpenMapping");Call(main,"ExecuteShortcut",ShortcutAction.ToggleMapping);Call(main,"ExecuteShortcut",ShortcutAction.PauseMapping);
                Call(main,"ShowPractice",true);Check((bool)Call(main,"StartGamePractice")!,"Ordinary practice could not start");
                observer.Full();Call(main,"StopGamePractice");Call(main,"ShowPractice",false);Pump();
                Check(Field(main,"_mapping")==null&&Application.Current.Windows.Count==windows,"Mode switching created a mapping window");
                Check(observer.Subscribed&&registrar.Active.Count==8,"Mapping isolation broke ordinary shortcuts");
            }
            Check(sends==0,"Mapping commands sent game input");main.Close();Pump();
            Check(observer.Disposed&&!observer.Subscribed,"Closing retained an observer subscription");
            Check(registrar.Active.Count==0,"Closing retained registered shortcuts");
            Check(Field(main,"_observer")==null&&Field(main,"_registrations")==null,"Closed input services remained reachable");
            Check(!(bool)Field(main,"_pRenderingHooked")!,"Closing retained the practice rendering subscription");
            observer.Send(new(0x59,0,true,IntPtr.Zero,Stopwatch.GetTimestamp()));observer.Full();
        }
        Console.WriteLine("VERSION_LIFECYCLE practice=True;game_input_factory_calls=0;subscriptions=0;hotkeys=0;dispose_failure=cleaned");
    }
    static void NativeObserver()
    {
        for(int i=0;i<3;i++)
        {
            var observer=new GlobalKeyboardObserver();var worker=(Dispatcher)Field(observer,"_hookDispatcher")!;var thread=(Thread)Field(observer,"_thread")!;
            Check(thread.ManagedThreadId!=Environment.CurrentManagedThreadId,"Keyboard hook still runs on the UI thread");
            var operation=worker.InvokeAsync(()=>Environment.CurrentManagedThreadId);
            Check(operation.Task.Wait(TimeSpan.FromSeconds(2))&&operation.Result==thread.ManagedThreadId,"Keyboard worker stopped while the UI thread waited");
            Check((IntPtr)Field(observer,"_hook")! !=IntPtr.Zero,"Keyboard hook was not installed");
            observer.Dispose();observer.Dispose();
            Check(!thread.IsAlive&&(IntPtr)Field(observer,"_hook")! ==IntPtr.Zero,"Keyboard worker or hook survived disposal");
        }
        Console.WriteLine("VERSION_OBSERVER dedicated_thread=True;cycles=3;remaining_hooks=0;physical_input=False");
    }
    static void NativeShortcuts()
    {
        var observer=new Observer();var main=new MainWindow(true);main.Show();Pump();main.InitializeInputRouting(observer:observer);
        var registrations=(ShortcutRegistrations)Field(main,"_registrations")!;
        Console.WriteLine($"VERSION_NATIVE_SHORTCUTS registered={registrations.Registered.Count};hwnd={new WindowInteropHelper(main).Handle};thread={Environment.CurrentManagedThreadId}");
        main.Close();Pump();Check(registrations.Registered.Count==0,"Native shortcut registrations survived window closure");
    }
    static void TrackLocalization()
    {
        int dialogs=0;
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);Theme.Apply(theme,persist:false);
            var track=new ProjectTrack{PreviewInstrument="Violin"};track=track with{Notes=new(){new(){TrackId=track.Id,Key=0}}};
            var window=new EditorTrackDialog(track);Exception? failure=null;
            window.Loaded+=(_,_)=>
            {
                try
                {
                    var combos=Descendants<ComboBox>(window).ToArray();Check(combos.Length==3,"Track settings lost a selector");
                    Check(Equals(combos[0].SelectedValue,"Violin"),"Translation changed the selected instrument identity");
                    foreach(var combo in combos.Take(2))foreach(var item in combo.Items.OfType<ComboBoxItem>())
                    {string id=(string)item.Tag;Check(Equals(item.Content,Lang.Instrument(id)),"An instrument option bypassed localization");}
                    combos[0].SelectedValue="Flute";combos[1].SelectedValue="Handpan";
                    Descendants<Button>(window).Single(b=>Equals(b.Content,Lang.S("ed.apply"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch(Exception error){failure=error;window.Close();}
            };
            window.ShowDialog();if(failure!=null)throw failure;
            Check(window.Result is {PreviewInstrument:"Flute",GameTarget.InstrumentId:"Handpan"},"A localized selector saved display text as an instrument identity");
            Check(window.Result!.Notes.SequenceEqual(track.Notes),"Changing an instrument silently changed the notes");dialogs++;
        }
        Console.WriteLine($"VERSION_TRACK_LOCALIZATION dialogs={dialogs};languages=4;identities=preserved");
    }
    sealed class Notes:INoteOutput
    {
        public readonly HashSet<Guid> Active=new();public int Ons,Stops;
        public void Apply(IReadOnlyList<NoteOutputEvent> changes)
        {foreach(var e in changes)if(e.IsOn){Active.Add(e.Note.NoteId);Ons++;}else Active.Remove(e.Note.NoteId);}
        public void StopAll(){Active.Clear();Stops++;}
    }
    static void Until(Func<bool> condition)
    {
        var watch=Stopwatch.StartNew();
        while(!condition()&&watch.Elapsed<TimeSpan.FromSeconds(5))
        {var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        Check(condition(),"WPF operation did not complete");
    }
    static void EditorFixes(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);int layouts=0;
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);Theme.Apply(theme,persist:false);
            var track=new ProjectTrack{PreviewInstrument="Violin"};track=track with{Notes=Enumerable.Range(0,100).Select(i=>new ProjectNote{TrackId=track.Id,Key=i%15,Beat=i,Action=NoteAction.Hold,LengthBeats=1}).ToList()};
            var project=new SmapProject{Tracks=new(){track}};var window=new EditorWindow(project){AudioEnabled=false};window.Show();Pump();
            var overview=(PianoRollOverview)window.FindName("Overview");var roll=window.Viewport;int builds=overview.NoteLayerBuilds;string identity=PerformanceIdentity.ForProject(window.Session.Project);
            Check(System.Windows.Automation.AutomationProperties.GetName(overview)==Lang.S("ed.overview"),"Overview lost its localized accessible name");
            overview.Navigate(overview.ActualWidth*.8,true);Pump();Check(roll.ViewBeat>0,"Overview click did not move the viewport");
            overview.Navigate(overview.ActualWidth*.6,false);Pump();Check(overview.NoteLayerBuilds==builds,"Overview drag rebuilt the note cache");
            Check(PerformanceIdentity.ForProject(window.Session.Project)==identity,"Overview navigation modified notes");
            var keys=(Button[])Field(window,"_keys")!;Check(keys.Length==15&&keys.All(k=>k.Width==48&&k.Height==36),"Compact key sizes changed");
            Check(keys.All(k=>((Grid)k.Content).Children.OfType<Border>().Any(b=>b.RenderTransform is RotateTransform{Angle:45})),"Compact keys lost their diamond style");
            var trigger=(CommandMenuButton)window.FindName("MoreButton"); ((EditorViewPreferences)Field(window,"_view")!).ReduceMotion=true;
            Call(window,"ShowMore");Pump();var menu=trigger.Chrome;
            Check(trigger.IsOpen&&!trigger.ContextMenu!.IsOpen&&menu.Width==320,"More menu bypassed integrated expansion");
            var owner=window.PointToScreen(new Point());var top=menu.PointToScreen(new Point());var dpi=VisualTreeHelper.GetDpi(window);
            Check(top.X>=owner.X-2&&top.Y>=owner.Y-2&&top.X+menu.ActualWidth*dpi.DpiScaleX<=owner.X+window.ActualWidth*dpi.DpiScaleX+2
                &&top.Y+menu.ActualHeight*dpi.DpiScaleY<=owner.Y+window.ActualHeight*dpi.DpiScaleY+2,"More menu escaped the editor bounds");
            Check(((SolidColorBrush)menu.Background).Color==((SolidColorBrush)window.FindResource("PanelBg")).Color,"Menu used platform colors instead of theme colors");
            if(output!=null){Capture(window,Path.Combine(output,$"editor-{theme}-{language}.png"));Capture(menu,Path.Combine(output,$"editor-menu-{theme}-{language}.png"));}
            trigger.Close(true);window.Width=940;window.Height=540;Pump();Call(window,"ShowMore");Pump();menu=trigger.Chrome;
            Check(menu.ActualHeight<=window.ActualHeight-70,"Compact menu exceeded the available height");trigger.Close(true);
            window.Session.MarkSaved();window.Close();layouts++;
        }
        Theme.Apply(AppTheme.Dark,persist:false);
        var input=new EditorWindow(EditorSession.NewProject()){AudioEnabled=false};input.Show();Pump();var notes=new Notes();input.KeyOutputFactory=_=>notes;input.AudioEnabled=true;
        input.HandleKey(Key.Y,0);input.HandleKey(Key.U,0);Until(()=>notes.Active.Count==2);
        int ons=notes.Ons;input.HandleKey(Key.Y,0,repeat:true);input.HandleKey(Key.Y,0);Check(notes.Ons==ons,"A held editor key retriggered");
        Call(input,"EndKeyPreview",0);Check(notes.Active.Count==1,"Releasing one editor key stopped the chord");Call(input,"EndKeyPreview",1);Check(notes.Active.Count==0,"Editor key release was lost");
        input.Session.MarkSaved();input.Close();Check(notes.Stops>0,"Closing the editor retained manual voices");
        Console.WriteLine($"EDITOR_FIXES layouts={layouts};overview=cache;keys=polyphonic;menus=bounded");
    }
    static void LibraryPractice()
    {
        Theme.Apply(AppTheme.Dark,persist:false);var main=new MainWindow(true);main.Show();Pump();
        var songs=(List<SongInfo>)Field(main,"_all")!;songs.AddRange(new[]{new SongInfo{Name="Alpha",File="a"},new SongInfo{Name="Zulu",File="z"}});Call(main,"ApplyFilter");
        var sort=(ComboBox)main.FindName("SortCombo");var button=(Button)main.FindName("SortChoiceBtn");Call(main,"ToggleLibraryChoice",sort,button);Pump();
        var list=((AnimatedChoicePopup)main.FindName("LibraryChoicePopup")).Options;var choice=(ListBoxItem)list.Items[1];
        var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent,Source=choice};
        Call(main,"RootScale_PreviewMouseLeftButtonDown",main,down);
        Check(((AnimatedChoicePopup)main.FindName("LibraryChoicePopup")).Chrome.IsHitTestVisible,"Clicking a sort item closed its popup before mouse-up");
        var up=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=choice};Call(main,"LibraryChoiceList_Click",list,up);Pump();
        Check(sort.SelectedIndex==1&&((ListBox)main.FindName("SongList")).Items.OfType<SongInfo>().First().Name=="Zulu","Sort selection did not reorder songs");
        sort.SelectedIndex=0;Check(((ListBox)main.FindName("SongList")).Items.OfType<SongInfo>().First().Name=="Alpha","Ascending sort did not reverse the order");
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".smap");var track=new ProjectTrack();track=track with{Notes=new(){new(){TrackId=track.Id,Key=0,Beat=0},new(){TrackId=track.Id,Key=1,Beat=1},new(){TrackId=track.Id,Key=2,Beat=2}}};
        Call(main,"CloseLibraryChoice",true);main.Activate();main.Focus();Pump();
        SmapFile.Save(new(){Tracks=new(){track}},file);typeof(MainWindow).GetField("_nowPlaying",Private)!.SetValue(main,new SongInfo{File=file,Name="Practice"});
        typeof(MainWindow).GetField("_playing",Private)!.SetValue(main,true);typeof(MainWindow).GetField("_paused",Private)!.SetValue(main,true);
        var audio=new List<(int key,bool down)>();main.PracticeAudioInput=(key,on)=>audio.Add((key,on));Call(main,"ShowPractice",true);
        var session=(PracticeSession)Field(main,"_practiceSession")!;
        Console.WriteLine(JsonSerializer.Serialize(new{stage="practice-entry",session.Paused,playing=Field(main,"_playing"),practicePaused=Field(main,"_practicePaused"),previewing=Field(main,"_previewing"),main.IsActive,step=session.CurrentStepIndex,held=session.HoldingKeys.ToArray()}));
        Check(!session.Paused&&!(bool)Field(main,"_playing")!,"Practice inherited the old playback pause state");
        var keys=(Button[])Field(main,"_pBtn")!;
        void MouseKey(int key,bool on)=>keys[key].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=on?UIElement.PreviewMouseLeftButtonDownEvent:UIElement.PreviewMouseLeftButtonUpEvent});
        MouseKey(0,true);MouseKey(0,false);Check(session.CurrentStepIndex==1,"First practice key did not advance without seeking");
        MouseKey(2,true);MouseKey(2,false);Check(session.CurrentStepIndex==1,"Wrong practice key incorrectly advanced");
        var source=PresentationSource.FromVisual(main)!;
        var kd=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.U){RoutedEvent=Keyboard.PreviewKeyDownEvent};main.RaiseEvent(kd);
        int count=audio.Count(p=>p.down);main.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.U){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Check(audio.Count(p=>p.down)==count,"Duplicate physical key-down retriggered practice audio");
        main.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.U){RoutedEvent=Keyboard.PreviewKeyUpEvent});
        Check(session.CurrentStepIndex==2,"Physical practice key did not advance");main.Close();File.Delete(file);
        Console.WriteLine("LIBRARY_PRACTICE sort=mouse-up;initial_follow=True;repeat=ignored;wrong_key=ignored");
    }
    static void CursorCenter(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(int grid in new[]{4,8,3})
        {
            var track=new ProjectTrack();track=track with{Notes=new(){new(){TrackId=track.Id,Key=4,Beat=0},new(){TrackId=track.Id,Key=7,Beat=2}}};
            var window=new EditorWindow(new(){GridSubdiv=grid,Tracks=new(){track}}){AudioEnabled=false};window.Show();Pump();var roll=window.Viewport;var session=window.Session;
            IEnumerable<GeometryDrawing> Geometry(Drawing d){if(d is DrawingGroup g)foreach(var c in g.Children)foreach(var x in Geometry(c))yield return x;else if(d is GeometryDrawing geometry)yield return geometry;}
            double CursorX()=>Geometry(((DrawingVisual)Field(roll,"_cursor")!).Drawing).Select(g=>g.Geometry).OfType<LineGeometry>().Single().StartPoint.X;
            void Centered(string stage){Pump();double expected=PianoRoll.LaneWidth+(double)(roll.PlayheadBeat-roll.ViewBeat+.5m/grid)*roll.BeatWidth;Check(Math.Abs(CursorX()-expected)<.000001,stage+" cursor is not centered on snap cell");}
            string identity=PerformanceIdentity.ForProject(session.Project);int history=session.UndoCount;
            Centered("Initial");Check(roll.PlayheadBeat==0&&roll.KeysAtPlayhead().SetEquals(new[]{4})&&EditorWindow.PreviewSequence(session.Project).Notes.Min(n=>n.StartMicroseconds)==0,"Centering changed beat zero, key highlight or first note start");
            session.Select(new[]{track.Notes[0].Id});window.HandleKey(Key.Right,0);Centered("Right");Check(roll.PlayheadBeat==1m/grid,"Right changed semantic grid step");window.HandleKey(Key.Left,0);Centered("Left");window.HandleKey(Key.Left,0);Check(roll.PlayheadBeat==0,"Left crossed zero");
            roll.SetZoom(2,1);roll.SetView(0);Centered("Zoom");
            var overview=(PianoRollOverview)window.FindName("Overview");overview.RefreshView();var red=Color.FromRgb(228,121,104);
            var line=Geometry(((DrawingVisual)Field(overview,"_view")!).Drawing).Single(g=>g.Geometry is LineGeometry&&g.Pen?.Brush is SolidColorBrush b&&b.Color==red);
            Check(Math.Abs(((LineGeometry)line.Geometry).StartPoint.X-(double)(roll.CursorDisplayBeat/overview.ExtentBeats)*overview.ActualWidth)<.000001,"Overview cursor disagrees with centered roll cursor");
            roll.CenterCursor=false;roll.PlayheadBeat=.37m;Pump();Check(Math.Abs(CursorX()-(PianoRoll.LaneWidth+(double)(.37m-roll.ViewBeat)*roll.BeatWidth))<.000001,"Continuous playback cursor gained a half-cell time offset");
            window.HandleKey(Key.Home,0);Centered("Home");roll.SnapEnabled=false;roll.PlayheadBeat=.37m;Pump();Check(roll.CursorDisplayBeat==.37m,"Free positioning was snapped or offset");roll.SnapEnabled=true;
            roll.PointerDown(new Point(PianoRoll.LaneWidth+roll.BeatWidth*1.2,10),MouseButton.Left,0);Centered("Ruler");
            Check(session.UndoCount==history&&PerformanceIdentity.ForProject(session.Project)==identity,"Cursor centering changed notes or undo history");
            if(output!=null&&grid==4)Capture(window,Path.Combine(output,"cursor-cell-center.png"));
            session.MarkSaved();window.Close();
        }
        Console.WriteLine("CURSOR_CENTER initial=True;arrows=True;home=True;overview=True;timing=True;free=True");
    }
    static void TrackToggles(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var tracks=Enumerable.Range(0,3).Select(i=>new ProjectTrack { Name="Track "+(i+1) }).Select(t=>t with { Notes=new(){new(){TrackId=t.Id,Key=4,Beat=0}} }).ToList();
        var window=new EditorWindow(new(){Tracks=tracks}) {AudioEnabled=false};window.Show();window.ToggleAdvanced();Pump();
        var scroll=(ScrollViewer)Field(window,"_trackScroll")!;var session=window.Session;
        var notes=JsonSerializer.Serialize(session.Project.Tracks.SelectMany(t=>t.Notes));
        System.Windows.Controls.Primitives.ToggleButton Button(Guid id,string key)=>Descendants<System.Windows.Controls.Primitives.ToggleButton>(scroll).Single(b=>Equals(b.Tag,$"{id:N}:{key}"));
        void Toggle(Guid id,string key)
        {
            var b=Button(id,key);var peer=new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(b);
            ((System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();Pump();
        }
        foreach(var theme in Enum.GetValues<AppTheme>())
        {
            Theme.Apply(theme,persist:false);Pump();
            var mute=Button(tracks[1].Id,"mute");var solo=Button(tracks[1].Id,"solo");
            Check(Equals(mute.Content,"M")&&Equals(solo.Content,"S")&&mute.Width==36&&mute.Height==28&&mute.Margin.Right==6,"Track buttons lost approved compact layout");
            Toggle(tracks[1].Id,"mute");mute=Button(tracks[1].Id,"mute");
            Check(mute.IsChecked==true&&session.Project.Tracks[1].Muted&&!session.Project.Tracks[0].Muted,"Mute toggled wrong track");
            Check(((SolidColorBrush)mute.Background).Color==Color.FromRgb(239,178,79)&&mute.ToolTip.ToString()!.Contains(Lang.S("ed.stateOn")),"Mute active appearance or tooltip is incorrect");
            Toggle(tracks[1].Id,"solo");Check(Button(tracks[1].Id,"solo").IsChecked==true&&session.Project.Tracks[1].Solo&&!session.Project.Tracks[0].Solo,"Solo toggled wrong track");
            Check(((SolidColorBrush)Button(tracks[1].Id,"solo").Background).Color==Color.FromRgb(114,177,255),"Solo active color is incorrect");
            Toggle(tracks[1].Id,"mute");Check(session.Project.AudibleTracks().Select(t=>t.Id).SequenceEqual(new[]{tracks[1].Id}),"Unmuted solo did not isolate target track");
            if(output!=null)Capture(window,Path.Combine(output,$"track-solo-{theme}.png"));
            Toggle(tracks[1].Id,"solo");Check(session.Project.AudibleTracks().Count()==3,"Disabling solo did not restore all tracks");
        }
        var focused=Button(tracks[0].Id,"mute");focused.Focus();Pump();
        Check(((Border)focused.Template.FindName("FocusRing",focused)).Visibility==Visibility.Visible,"Keyboard focus lacks visible outline");
        var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,Environment.TickCount,Key.Space){RoutedEvent=Keyboard.PreviewKeyDownEvent};window.RaiseEvent(key);
        Check(!key.Handled&&!(bool)Field(window,"_playing")!,"Toggle Space was intercepted by editor playback");
        Toggle(tracks[0].Id,"mute");Check(Button(tracks[0].Id,"mute").IsKeyboardFocused,"Refreshing state lost toggle keyboard focus");
        Toggle(tracks[0].Id,"mute");
        int history=session.UndoCount;Call(window,"SetBusy",true);Toggle(tracks[0].Id,"mute");Check(!session.Project.Tracks[0].Muted&&Button(tracks[0].Id,"mute").IsChecked==false&&session.UndoCount==history,"Busy toggle displayed a state not applied to project");Call(window,"SetBusy",false);
        foreach(var language in Enum.GetValues<AppLang>())
        {
            typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);Call(window,"RefreshTracks");Pump();var b=Button(tracks[0].Id,"mute");
            Check(b.ToolTip.ToString()!.Contains(Lang.S("ed.mute"))&&b.ToolTip.ToString()!.Contains(Lang.S("ed.stateOff")),"Toggle tooltip is not localized");
            Check(System.Windows.Automation.AutomationProperties.GetName(b).Contains(Lang.S("ed.mute")),"Toggle accessible name lost full action");
        }
        Check(JsonSerializer.Serialize(session.Project.Tracks.SelectMany(t=>t.Notes))==notes&&session.ActiveTrackId==tracks[0].Id,"Track toggles modified notes or active track");
        session.MarkSaved();window.Close();Console.WriteLine("TRACK_TOGGLES state=True;scope=True;theme=True;focus=True;localized=True");
    }
    static void EditorOpen()
    {
        string folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(folder);
        var window=new EditorWindow(EditorSession.NewProject()){AudioEnabled=false};window.Show();Pump();
        try
        {
            string a=Path.Combine(folder,"A.smap"),b=Path.Combine(folder,"B.smap"),bad=Path.Combine(folder,"invalid.smap"),legacy=Path.Combine(folder,"old.json");
            var track=new ProjectTrack();track=track with {Notes=new(){new(){TrackId=track.Id,Key=4,Beat=1,Action=NoteAction.Hold,LengthBeats=2}}};
            SmapFile.Save(new(){Metadata=new(){Name="A"},Tracks=new(){track,new(){Name="Second"}}},a);SmapFile.Save(EditorSession.NewProject() with {Metadata=new(){Name="B"}},b);File.WriteAllText(bad,"broken");
            bool Open(string file,string? answer=null)
            {
                bool prompted=false;var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
                timer.Tick+=(_,_)=> {timer.Stop();var prompt=Application.Current.Windows.OfType<ChromeWindow>().Single(w=>ReferenceEquals(w.Owner,window));prompted=true;Descendants<Button>(prompt).Single(x=>Equals(x.Content,answer)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};
                if(answer!=null)timer.Start();try{bool result=window.OpenProjectFile(file);if(answer!=null)Check(prompted,"Unsaved open bypassed confirmation");return result;}finally{timer.Stop();}
            }
            Check(Open(a)&&window.Session.Project.Metadata.Name=="A"&&window.Session.Project.Tracks.Count==2,"Native open lost metadata or tracks");
            Check(window.Session.ActiveTrack.Notes[0].Action==NoteAction.Hold&&!window.Session.IsDirty&&Equals(Field(window,"_path"),a),"Native open lost holds, saved state or file path");
            window.Session.Create(5,4);var current=window.Session;string identity=PerformanceIdentity.ForProject(current.Project);
            Check(!Open(b,Lang.S("d.cancel"))&&ReferenceEquals(current,window.Session)&&window.Session.IsDirty&&PerformanceIdentity.ForProject(window.Session.Project)==identity,"Cancel open lost current edits");
            Check(Open(a,Lang.S("ed.save"))&&window.Session.ActiveTrack.Notes.Count==2&&SmapFile.Load(a).Tracks[0].Notes.Count==2,"Saving then opening same file loaded stale contents");
            window.Session.Create(6,5);Check(Open(b,Lang.S("ed.save"))&&window.Session.Project.Metadata.Name=="B"&&SmapFile.Load(a).Tracks[0].Notes.Count==3,"Save-before-open failed to preserve old project");
            window.Session.Create(7,6);Check(Open(a,Lang.S("ed.discard"))&&window.Session.Project.Metadata.Name=="A"&&SmapFile.Load(b).Tracks[0].Notes.Count==0,"Discard-before-open wrote discarded changes");
            current=window.Session;identity=PerformanceIdentity.ForProject(current.Project);bool failed=false;
            try{Open(bad);}catch{failed=true;}Check(failed&&ReferenceEquals(current,window.Session)&&PerformanceIdentity.ForProject(current.Project)==identity&&Equals(Field(window,"_path"),a),"Invalid open replaced current session or path");
            window.Viewport.PlayheadBeat=8;typeof(EditorWindow).GetField("_regionEndBeat",Private)!.SetValue(window,12m);
            File.WriteAllText(legacy,JsonSerializer.Serialize(new{name="Legacy",bpm=120,songNotes=new[]{new{time=0,key="1Key2"},new{time=500,key="1Key3"}}}));string text=File.ReadAllText(legacy);
            Check(Open(legacy)&&window.Session.Project.Metadata.Name=="Legacy"&&window.Session.ActiveTrack.Notes.Count==2&&Field(window,"_path")==null,"Legacy open failed or would overwrite JSON on normal save");
            Check(window.Viewport.PlayheadBeat==0&&window.Viewport.ViewBeat==0&&Field(window,"_regionEndBeat")==null&&window.Session.UndoCount==0&&File.ReadAllText(legacy)==text,"Opening retained old cursor/region/history or modified source");
            Call(window,"SetBusy",true);current=window.Session;Check(!Open(a)&&ReferenceEquals(current,window.Session),"Busy editor replaced current project");Call(window,"SetBusy",false);
            foreach(var language in Enum.GetValues<AppLang>())
            {
                typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);Call(window,"ShowMore");Pump();var more=(CommandMenuButton)window.FindName("MoreButton");
                Check(Equals(((MenuItem)more.ContextMenu!.Items[0]).Header,Lang.S("ed.openProject"))&&Lang.S("ed.openProject")!="ed.openProject","Open entry missing or untranslated");more.Close(true);
            }
            window.Session.MarkSaved();window.Close();Console.WriteLine("EDITOR_OPEN native=True;legacy=True;cancel=True;save=True;discard=True;invalid=True;localized=True");
        }
        finally { if(window.IsVisible){window.Session.MarkSaved();window.Close();}Directory.Delete(folder,true); }
    }
    static void EditorReferenceKeys(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var first=new ProjectTrack {Name="Editing"};first=first with {Notes=new(){new(){TrackId=first.Id,Key=4,Beat=2}}};
        var other=new ProjectTrack {Name="Reference"};other=other with {Notes=new(){new(){TrackId=other.Id,Key=9,Beat=4,Action=NoteAction.Hold,LengthBeats=2}}};
        var harp=new ProjectTrack {Name="Harp reference",PreviewInstrument="Harp"};harp=harp with {Notes=new(){new(){TrackId=harp.Id,Key=12,Beat=6}}};
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".smap");SmapFile.Save(new(){Tracks=new(){first,other,harp}},path);
        var registrar=new Registrar();var observer=new Observer();var main=CreateMain(registrar,observer);
        Call(main,"OpenEditor",new SongInfo{File=path,Name="Reference and routing"});Pump();var editor=((HashSet<EditorWindow>)Field(main,"_editors")!).Single();
        editor.Activate();Until(()=>editor.IsActive);Pump();var roll=editor.Viewport;editor.ToggleAdvanced();Pump();
        IntPtr foreground=new WindowInteropHelper(editor).Handle;main.ShortcutForeground=()=>foreground;Call(main,"RefreshShortcutFocus");
        IEnumerable<Drawing> Drawings(Drawing d){if(d is DrawingGroup g)foreach(var c in g.Children)foreach(var x in Drawings(c))yield return x;else yield return d;}
        int Ghosts()=>Drawings(((DrawingVisual)Field(roll,"_notes")!).Drawing).OfType<GeometryDrawing>().Count(d=>d.Geometry is RectangleGeometry&&d.Brush is SolidColorBrush b&&Math.Abs(b.Opacity-.45)<.001);
        Check(Ghosts()==2,"Ordinary piano/harp tracks did not render grey key-position references");
        var gray=Drawings(((DrawingVisual)Field(roll,"_notes")!).Drawing).OfType<GeometryDrawing>().Where(d=>d.Geometry is RectangleGeometry&&d.Brush is SolidColorBrush b&&Math.Abs(b.Opacity-.45)<.001);
        Check(gray.All(d=>((SolidColorBrush)d.Brush).Color==((SolidColorBrush)editor.FindResource("SubTextFg")).Color),"Reference retained colored track fill instead of grey");
        var bounds=roll.Bounds(other.Notes[0]);Check(roll.HitNotes(new Point(bounds.Left+5,bounds.Top+5)).Length==0,"Reference note became editable");
        roll.References=false;Pump();Check(Ghosts()==0,"Reference visibility switch did not hide grey notes");roll.References=true;Pump();
        string identity=PerformanceIdentity.ForProject(editor.Session.Project);int history=editor.Session.UndoCount;editor.Session.Select(new[]{first.Notes[0].Id});roll.PlayheadBeat=2;
        editor.HandleKey(Key.Right,0);Check(roll.PlayheadBeat==2.25m,"Right arrow moved selected note instead of cursor");editor.HandleKey(Key.Left,0);Check(roll.PlayheadBeat==2,"Left arrow did not move cursor by grid step");
        roll.PlayheadBeat=0;editor.HandleKey(Key.Left,0);Check(roll.PlayheadBeat==0,"Left arrow crossed beat zero");editor.HandleKey(Key.Right,0,true);Check(roll.PlayheadBeat==.25m,"Held arrow did not advance cursor");
        Check(PerformanceIdentity.ForProject(editor.Session.Project)==identity&&editor.Session.UndoCount==history,"Arrow cursor navigation mutated notes or history");
        var snap=(JoinedChoiceButton)Field(editor,"_snapChoice")!;snap.ReduceMotion=true;snap.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();decimal cursor=roll.PlayheadBeat;
        editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(editor)!,Environment.TickCount,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});Check(roll.PlayheadBeat==cursor,"Open selector routed arrow into roll");snap.Close(true);
        Check(registrar.Active.Count==0,"Main global hotkeys still intercept editor keys");double speed=(double)Field(main,"_speed")!;
        Call(main,"ExecuteShortcut",ShortcutAction.Faster);Call(main,"RegisteredShortcut",1201);Check((double)Field(main,"_speed")! ==speed,"Main command ran while editor foreground");
        observer.Send(new(0x72,0,true,foreground,Stopwatch.GetTimestamp()));Check((double)Field(main,"_speed")! ==speed,"Observed key triggered main while editor foreground");
        var child=new Window{Owner=editor,Width=200,Height=120,ShowActivated=false};child.Show();Pump();foreground=new WindowInteropHelper(child).Handle;Call(main,"RefreshShortcutFocus");Call(main,"ExecuteShortcut",ShortcutAction.Faster);Check(registrar.Active.Count==0&&(double)Field(main,"_speed")! ==speed,"Editor-owned native window did not retain shortcut priority");child.Close();foreground=new WindowInteropHelper(editor).Handle;
        if(output!=null)Capture(editor,Path.Combine(output,"grey-reference.png"));
        foreground=new WindowInteropHelper(main).Handle;main.Activate();Until(()=>main.IsActive);Pump();Call(main,"RefreshShortcutFocus");Check(registrar.Active.Count==8,"Main shortcuts did not restore after leaving editor");Call(main,"RegisteredShortcut",registrar.Active.Single(p=>p.Value.Key==0x72).Key);Check(Math.Abs((double)Field(main,"_speed")!-(speed-.1))<.000001,"Main shortcut stopped working after editor focus");
        foreground=new WindowInteropHelper(editor).Handle;editor.Activate();Until(()=>editor.IsActive);Pump();Call(main,"RefreshShortcutFocus");Check(registrar.Active.Count==0,"Returning to editor did not suspend hotkeys again");
        foreground=new WindowInteropHelper(main).Handle;editor.Close();Pump();main.Activate();Until(()=>main.IsActive);Pump();Call(main,"RefreshShortcutFocus");Check(registrar.Active.Count==8,"Closing editor did not restore registrations");main.Close();File.Delete(path);
        Console.WriteLine("EDITOR_REFERENCE_KEYS grey=True;readonly=True;cursor=True;focus=True;restore=True");
    }
    static void OverviewRange(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var track=new ProjectTrack();track=track with { Notes=new(){new(){TrackId=track.Id,Key=5,Beat=4},new(){TrackId=track.Id,Key=9,Beat=64,Action=NoteAction.Hold,LengthBeats=2}} };
        var window=new EditorWindow(new(){Tracks=new(){track}}){AudioEnabled=false};window.Show();Pump();
        var roll=window.Viewport;var overview=(PianoRollOverview)window.FindName("Overview");roll.SetView(8,15);Pump();overview.RefreshView();
        decimal extent=overview.ExtentBeats,playhead=roll.PlayheadBeat;int cache=overview.NoteLayerBuilds,history=window.Session.UndoCount;
        string identity=PerformanceIdentity.ForProject(window.Session.Project);double vertical=roll.VerticalOffset;
        IEnumerable<Drawing> Drawings(Drawing drawing)
        { if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var value in Drawings(child))yield return value;else yield return drawing; }
        int Handles()=>Drawings(((DrawingVisual)Field(overview,"_view")!).Drawing).OfType<GeometryDrawing>().Count(d=>d.Brush!=null&&d.Geometry is RectangleGeometry g&&Math.Abs(g.Rect.Width-8)<.001&&Math.Abs(g.Rect.Height-overview.ActualHeight+4)<.001);
        void Hover(bool enter)=>overview.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=enter?UIElement.MouseEnterEvent:UIElement.MouseLeaveEvent});
        Hover(false);Check(Handles()==0,"Overview grips remained visible away from pointer");Hover(true);Check(Handles()==2,"Hover did not show both grips");Hover(false);Check(Handles()==0,"Leaving overview did not hide grips");
        double X(decimal beat)=>(double)(beat/extent)*overview.ActualWidth;
        void CloseEnough(decimal actual,decimal expected,string label)=>Check(Math.Abs(actual-expected)<.000001m,label);
        decimal right=roll.ViewBeat+roll.VisibleBeats,span=roll.VisibleBeats;
        overview.BeginRangeDrag(X(roll.ViewBeat)+3);Hover(false);Check(Handles()==2,"Leaving during drag hid grips");overview.DragRange(X(roll.ViewBeat)+3);CloseEnough(roll.VisibleBeats,span,"Off-center handle grab jumped range");
        overview.DragRange(X(right-span*.6m)+3);Pump();CloseEnough(roll.ViewBeat+roll.VisibleBeats,right,"Left handle moved right boundary");Check(roll.VisibleBeats<span&&roll.Zoom>1,"Left handle did not zoom main roll");overview.EndRangeDrag();
        if(output!=null)Capture(window,Path.Combine(output,"left-handle.png"));
        decimal left=roll.ViewBeat;span=roll.VisibleBeats;
        overview.BeginRangeDrag(X(left+span));overview.DragRange(X(left+span*1.5m));Pump();CloseEnough(roll.ViewBeat,left,"Right handle moved left boundary");Check(roll.VisibleBeats>span,"Right handle did not widen visible range");overview.EndRangeDrag();
        if(output!=null)Capture(window,Path.Combine(output,"right-handle.png"));
        double zoom=roll.Zoom;left=roll.ViewBeat;span=roll.VisibleBeats;double center=X(left+span/2);
        overview.BeginRangeDrag(center);overview.DragRange(center+X(3));overview.EndRangeDrag();Pump();CloseEnough(roll.ViewBeat,left+3,"Interior drag did not pan");Check(Math.Abs(roll.Zoom-zoom)<.000001,"Panning changed zoom");
        overview.BeginRangeDrag(X(roll.ViewBeat+roll.VisibleBeats));overview.DragRange(-10000);overview.EndRangeDrag();Pump();Check(Math.Abs(roll.Zoom-8)<.000001&&roll.VisibleBeats>0,"Crossing edge did not clamp at maximum zoom");
        left=roll.ViewBeat;overview.BeginRangeDrag(X(left+roll.VisibleBeats)+3);overview.DragRange(overview.ActualWidth-1);overview.EndRangeDrag();Pump();CloseEnough(roll.ViewBeat,left,"Expanding to end moved fixed boundary");CloseEnough(roll.ViewBeat+roll.VisibleBeats,extent,"Off-center right grip could not reach overview end from inside control");Check(Handles()==0,"Ending drag away from pointer kept grips visible");
        right=roll.ViewBeat+roll.VisibleBeats;overview.BeginRangeDrag(X(roll.ViewBeat));overview.DragRange(-10000);overview.EndRangeDrag();Pump();CloseEnough(roll.ViewBeat,0,"Left handle escaped beat zero");CloseEnough(roll.ViewBeat+roll.VisibleBeats,right,"Left edge clamping moved right boundary");
        roll.SetZoom(1,roll.VerticalZoom);roll.SetView(8,vertical);Pump();
        overview.BeginRangeDrag(X(roll.ViewBeat));overview.CaptureMouse();overview.ReleaseMouseCapture();left=roll.ViewBeat;zoom=roll.Zoom;overview.DragRange(X(16));Check(roll.ViewBeat==left&&roll.Zoom==zoom,"Lost capture kept resizing");
        overview.BeginRangeDrag(X(roll.ViewBeat+roll.VisibleBeats));overview.EndRangeDrag();overview.DragRange(0);Check(roll.ViewBeat==left&&roll.Zoom==zoom,"Released handle kept resizing");
        overview.BeginRangeDrag(X(55));overview.EndRangeDrag();Pump();Check(roll.ViewBeat>left&&roll.Zoom==zoom,"Click outside range lost jump navigation");
        Check(overview.NoteLayerBuilds==cache&&overview.ExtentBeats==extent,"Range gestures rebuilt note cache or changed overview scale");
        Check(Math.Abs(roll.VerticalOffset-vertical)<.000001&&roll.PlayheadBeat==playhead&&window.Session.UndoCount==history&&PerformanceIdentity.ForProject(window.Session.Project)==identity,$"Range edit changed vertical view, cursor, notes or history: vertical={roll.VerticalOffset}/{vertical}, cursor={roll.PlayheadBeat}/{playhead}, history={window.Session.UndoCount}/{history}");
        roll.SetZoom(.0625,roll.VerticalZoom);overview.InvalidateNotes();Pump();overview.RefreshView();extent=overview.ExtentBeats;
        overview.BeginRangeDrag(overview.ActualWidth);overview.DragRange(overview.ActualWidth+1000);overview.EndRangeDrag();Check(Math.Abs(roll.Zoom-.0625)<.000001,"Minimum zoom clamp failed");
        overview.BeginRangeDrag(overview.ActualWidth/2);window.Close();overview.DragRange(0);Check(Field(overview,"_drag")!.ToString()=="None","Closing editor retained range gesture");
        Console.WriteLine("OVERVIEW_RANGE left=True;right=True;pan=True;bounds=True;cache=True;cleanup=True");
    }
    static void RollNotePreview(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        Theme.Apply(AppTheme.Dark,persist:false);var window=new EditorWindow(EditorSession.NewProject()){AudioEnabled=false};window.Show();Pump();
        var roll=window.Viewport;var session=window.Session;
        Point At(decimal beat,int key)=>new(PianoRoll.LaneWidth+(double)beat*roll.BeatWidth+4,PianoRoll.RulerHeight+(14-key+.5)*roll.RowHeight);
        IEnumerable<Drawing> Drawings(Drawing drawing)
        {
            if(drawing is DrawingGroup group)foreach(var child in group.Children)foreach(var value in Drawings(child))yield return value;
            else yield return drawing;
        }
        GeometryDrawing[] OverlayNotes()=>Drawings(((DrawingVisual)Field(roll,"_overlay")!).Drawing).OfType<GeometryDrawing>().Where(d=>d.Brush is SolidColorBrush b&&b.Color==Color.FromRgb(239,178,79)&&d.Geometry is RectangleGeometry).ToArray();
        int Handles()=>Drawings(((DrawingVisual)Field(roll,"_overlay")!).Drawing).OfType<GeometryDrawing>().Count(d=>d.Geometry is RectangleGeometry g&&Math.Abs(g.Rect.Width-6)<.01&&Math.Abs(g.Rect.Height-roll.RowHeight+2)<.01);
        void Click(Point p){roll.PointerDown(p,MouseButton.Left,0);roll.PointerUp(p,0);Pump();}
        Click(At(0,13));var first=session.ActiveTrack.Notes.Single();var original=first;
        roll.PointerMove(At(5,1),0);Pump();Check(OverlayNotes().Length==0&&Handles()==0,"Created note retained yellow highlight or resize bar away from pointer");
        int history=session.UndoCount;var start=At(2,9);var end=At(4,6);
        roll.PointerDown(start,MouseButton.Left,0);roll.PointerMove(end,0);Pump();
        var preview=OverlayNotes();Check(preview.Length==1,"Creating another note also moved the selected first-note ghost");
        Check(preview[0].Geometry.Bounds.Left>PianoRoll.LaneWidth+3*roll.BeatWidth&&session.Find(first.Id)==original&&session.ActiveTrack.Notes.Count==1&&session.UndoCount==history,"Creation preview mutated existing note or history");
        Check(Handles()==0,"Creation preview showed a resize bar away from its right edge");
        if(output!=null)Capture(window,Path.Combine(output,"new-note-drag.png"));
        roll.PointerUp(end,0);roll.PointerMove(At(6,1),0);Pump();Check(session.ActiveTrack.Notes.Count==2&&session.Find(first.Id)==original&&session.UndoCount==history+1&&OverlayNotes().Length==0,"Creation commit changed first note or left a ghost");
        var rect=roll.Bounds(first);roll.PointerMove(new Point(rect.Left+3,rect.Top+rect.Height/2),0);Pump();Check(OverlayNotes().Length==1&&Handles()==0,"Body hover failed yellow highlight or exposed resize bar");
        var edge=new Point(rect.Right+4,rect.Top+rect.Height/2);roll.PointerMove(edge,0);Pump();Check(OverlayNotes().Length==1&&Handles()==1,"Right-edge proximity did not reveal resize handle");
        if(output!=null)Capture(window,Path.Combine(output,"hover-right-edge.png"));
        roll.PointerDown(edge,MouseButton.Left,0);roll.PointerMove(new Point(edge.X+roll.BeatWidth,edge.Y),0);roll.PointerUp(new Point(edge.X+roll.BeatWidth,edge.Y),0);Pump();
        Check(session.Find(first.Id)!.Action==NoteAction.Hold,"Right-edge resize no longer extends note");session.Undo();Pump();Check(session.Find(first.Id)==original,"Resize undo changed original tap");
        var a=At(0,14);var b=At(5,5);roll.PointerDown(a,MouseButton.Left,ModifierKeys.Control);roll.PointerMove(b,ModifierKeys.Control);roll.PointerUp(b,ModifierKeys.Control);roll.PointerMove(At(7,1),0);Pump();
        Check(session.Selected.Count==2&&OverlayNotes().Length==2&&Handles()==0,"Marquee selection lost persistent yellow or showed all resize bars");
        roll.PointerDown(At(6,3),MouseButton.Left,0);roll.PointerMove(At(7,2),0);roll.CancelGesture();roll.PointerMove(At(8,1),0);Pump();Check(session.ActiveTrack.Notes.Count==2&&session.Find(first.Id)==original&&OverlayNotes().Length==0,"Cancelling a new creation left a ghost");
        session.MarkSaved();window.Close();Console.WriteLine("ROLL_NOTE_PREVIEW ghost=False;hover=True;edge_handle=True;marquee=True;history=True");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowEnabled(IntPtr window);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetWindow(IntPtr window,uint command);
    static void EditorWindowMode()
    {
        var main=new MainWindow(true);main.Show();Pump();
        var editors=(HashSet<EditorWindow>)Field(main,"_editors")!;
        bool blocked=false;var guard=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        guard.Tick+=(_,_)=> { blocked=true;guard.Stop();foreach(var editor in Application.Current.Windows.OfType<EditorWindow>().ToArray()){editor.Session.MarkSaved();editor.Close();} };
        guard.Start();Call(main,"OpenEditor",new object[]{null!});guard.Stop();Pump();
        Check(!blocked&&editors.Count==1,"Production editor entry blocked in a modal loop");
        var editor=editors.Single();var mainHandle=new WindowInteropHelper(main).Handle;var editorHandle=new WindowInteropHelper(editor).Handle;
        Check(editor.Owner==null&&GetWindow(editorHandle,4)==IntPtr.Zero&&!editor.Topmost,"Editor is still owned or topmost");
        Check(IsWindowEnabled(mainHandle),"Editor disabled the main window");
        main.Activate();Until(()=>main.IsActive);Check(!editor.IsActive,"Main cannot be activated above editor");
        editor.Activate();Until(()=>editor.IsActive);Check(!main.IsActive,"Editor cannot be activated independently");
        editor.WindowState=WindowState.Minimized;Pump();Check(main.WindowState!=WindowState.Minimized&&main.IsVisible,"Minimizing editor affected main");
        editor.WindowState=WindowState.Normal;main.WindowState=WindowState.Minimized;Pump();Check(editor.IsVisible&&editor.WindowState==WindowState.Normal,"Minimizing main minimized editor");main.WindowState=WindowState.Normal;
        main.Hide();Pump();Check(editor.IsVisible,"Hiding main hid editor");main.Show();Pump();
        var dialog=new Window { Owner=editor,Content="Modal editor dialog",Width=260,Height=140,ShowInTaskbar=false };
        bool mainStayedOpen=false,dialogModal=false;var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        timer.Tick+=(_,_)=>
        {
            timer.Stop();dialogModal=!IsWindowEnabled(mainHandle)&&!IsWindowEnabled(editorHandle);
            main.Close();mainStayedOpen=main.IsVisible&&editor.IsVisible&&editors.Count==1;dialog.Close();
        };
        timer.Start();dialog.ShowDialog();Pump();
        Check(dialogModal&&mainStayedOpen,"Editor dialog lost modality or main close bypassed dialog");
        bool cancel=true;System.ComponentModel.CancelEventHandler veto=(_,e)=>e.Cancel=cancel;
        editor.Closing+=veto;main.Close();Pump();Check(main.IsVisible&&editor.IsVisible&&editors.Count==1,"Cancelled editor close still closed main");
        cancel=false;editor.Closing-=veto;
        editor.Session.Create(0,0);bool savePrompt=false;var confirm=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        confirm.Tick+=(_,_)=>
        {
            confirm.Stop();var prompt=Application.Current.Windows.OfType<ChromeWindow>().Single(w=>ReferenceEquals(w.Owner,editor));
            savePrompt=true;Descendants<Button>(prompt).Single(b=>Equals(b.Content,Lang.S("d.cancel"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        confirm.Start();main.Close();Pump();Check(savePrompt&&main.IsVisible&&editor.IsVisible&&editor.Session.IsDirty,"Unsaved cancel did not preserve editor and main");editor.Session.MarkSaved();
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".smap");
        try
        {
            SmapFile.Save(EditorSession.NewProject(),path);Call(main,"OpenEditor",new SongInfo { File=path,Name="Window lifecycle" });Pump();
            Check(editors.Count==2&&editors.All(w=>w.Owner==null),"Opening another editor lost existing window");
            var second=editors.Single(w=>!ReferenceEquals(w,editor));second.Session.Create(2,1);
            Check((bool)Call(second,"Save",false)!&&!second.Session.IsDirty&&SmapFile.Load(path).Tracks[0].Notes.Count==1,"Nonmodal editor lost save operation or event callback");
            second.Close();Pump();Check(editors.Count==1&&editor.IsVisible,"Closing one editor removed another");
        }
        finally { File.Delete(path); }
        main.Close();Pump();Check(!main.IsVisible&&!editor.IsVisible&&editors.Count==0,"Main close left editor or tracking subscription behind");
        Console.WriteLine("EDITOR_WINDOW_MODE nonmodal=True;unowned=True;activation=True;minimize=True;close=True;dialog=True");
    }
    static void EditorCommandMenu(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var tracks=Enumerable.Range(0,9).Select(i=>new ProjectTrack{Name="Track "+(i+1)}).ToList();
        var window=new EditorWindow(new(){Tracks=tracks}){AudioEnabled=false};window.Show();Pump();
        var view=(EditorViewPreferences)Field(window,"_view")!;view.ReduceMotion=true;
        var more=(CommandMenuButton)window.FindName("MoreButton");var originalParent=more.Parent;
        string identity=PerformanceIdentity.ForProject(window.Session.Project);int history=window.Session.UndoCount;
        void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        void KeyPress(Key key)=>window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        Button Row(CommandMenuButton button,string label)=>Descendants<Button>(button.Scroll).Single(b=>b.ToolTip?.ToString()==label);
        void Bounds(CommandMenuButton button)
        {
            var rect=button.Chrome.TransformToAncestor(window).TransformBounds(new Rect(button.Chrome.RenderSize));
            Check(rect.Left>=6&&rect.Top>=6&&rect.Right<=window.ActualWidth-6&&rect.Bottom<=window.ActualHeight-6,"Command shell escaped window");
            Check(button.Chrome.Clip is RectangleGeometry{RadiusX:10,RadiusY:10},"Shell lost rounded clipping");
            Check(ReferenceEquals(PresentationSource.FromVisual(button.Chrome),PresentationSource.FromVisual(window)),"Command created a detached popup");
            var row=Descendants<Button>(button.Scroll).First(b=> { double y=b.TranslatePoint(new Point(0,b.ActualHeight/2),button.Scroll).Y; return b.IsVisible&&y>8&&y<button.Scroll.ActualHeight-8; });var p=row.TranslatePoint(new Point(20,row.ActualHeight/2),window);
            Check(window.InputHitTest(p) is DependencyObject hit&&row.IsAncestorOf(hit),"Command rows are covered by editor");
        }
        Click(more);Bounds(more);
        Check(more.IsOpen&&!more.ContextMenu!.IsOpen&&more.Parent is Canvas&&Application.Current.Windows.Count==1,"More did not expand the actual trigger");
        var commands=more.ContextMenu.Items.OfType<MenuItem>().ToArray();
        foreach(string key in new[]{"saveAs","metadata","exportTrack","exportLegacy","exportMidi","selectAll","delete","quantize","split","properties","trackSettings","region","home","zoomIn","zoomOut","reduceMotion","help"})
            Check(commands.Any(m=>Equals(m.Header,Lang.S("ed."+key))),"Original command missing: "+key);
        Check(SmoothScroll.GetEnabled(more.Scroll)&&!more.Scroll.CanContentScroll&&more.Scroll.ScrollableHeight>0,"Long menu lost pixel scrolling");
        var offsets=new List<double>();more.Scroll.ScrollChanged+=(_,e)=> { if(e.VerticalChange!=0)offsets.Add(more.Scroll.VerticalOffset); };
        more.Scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});
        var wheel=System.Diagnostics.Stopwatch.StartNew();Until(()=>wheel.ElapsedMilliseconds>=260);
        Check(offsets.Distinct().Count()>=3&&Math.Abs(more.Scroll.VerticalOffset-120)<1,"Command wheel scrolling jumped instead of moving through pixels");
        double zoom=window.Viewport.Zoom;Click(Row(more,Lang.S("ed.zoomIn")));Check(!more.IsOpen&&ReferenceEquals(more.Parent,originalParent)&&window.Viewport.Zoom>zoom,"Command did not restore button before dispatch");
        Click(more);var current=more.ContextMenu!.Items.OfType<MenuItem>().First(m=>m.IsCheckable);Click(Row(more,current.Header.ToString()!));
        Check(current.IsChecked&&(bool)Field(window,"_currentOnly")!,"Check command did not toggle original state");
        Click(more);var motion=more.ContextMenu!.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,Lang.S("ed.reduceMotion")));Click(Row(more,motion.Header.ToString()!));
        Check(!motion.IsChecked&&!view.ReduceMotion,"Reduced motion check did not use original preference");view.ReduceMotion=true;
        Click(more);zoom=window.Viewport.Zoom;Row(more,Lang.S("ed.zoomOut")).Focus();KeyPress(Key.Enter);Pump();Check(!more.IsOpen&&window.Viewport.Zoom<zoom,"Keyboard command did not dispatch");
        Click(more);Click((JoinedChoiceButton)Field(window,"_snapChoice")!);Check(!more.IsOpen,"Selector did not dismiss command menu");Click(more);Check(!((JoinedChoiceButton)Field(window,"_snapChoice")!).IsOpen,"Command did not dismiss selector");more.Close(true);
        Click(more);KeyPress(Key.Y);Check(window.Session.ActiveTrack.Notes.Count==0,"Menu created a musical note");KeyPress(Key.Home);KeyPress(Key.Down);KeyPress(Key.Escape);Until(()=>!more.IsOpen);Pump();
        Check(ReferenceEquals(more.Parent,originalParent),"Escape left trigger detached");
        window.ToggleAdvanced();Pump();var sidebar=(ScrollViewer)Field(window,"_trackScroll")!;sidebar.ScrollToVerticalOffset(250);Pump();
        var target=Descendants<CommandMenuButton>(sidebar).First(b=>b.TranslatePoint(new Point(),sidebar).Y>40);
        int trackIndex=Descendants<CommandMenuButton>(sidebar).ToList().IndexOf(target);Guid targetId=tracks[trackIndex].Id;
        var anchor=target.TranslatePoint(new Point(),window);var trackParent=target.Parent;
        Click(target);Bounds(target);Check((target.TranslatePoint(new Point(),window)-anchor).Length<.1,"Track trigger moved away from clicked anchor");
        Check(target.Chrome.ActualWidth==320&&target.ContextMenu!.Items.OfType<MenuItem>().Count()==5,"Track command inventory changed");
        Click(Row(target,Lang.S("ed.color")));Bounds(target);
        Check(Descendants<Button>(target.Scroll).Count(b=>EditorViewPreferences.Palette.Contains(b.ToolTip?.ToString()))==EditorViewPreferences.Palette.Length,"Inline palette lost original colors");
        if(output!=null)Capture(window,Path.Combine(output,"track-command-palette.png"));
        Click(Row(target,EditorViewPreferences.Palette[4]));Check(!target.IsOpen&&view.Colors[targetId]==EditorViewPreferences.Palette[4]&&window.Session.ActiveTrackId==tracks[0].Id,"Palette modified the wrong track or retained overlay");
        Check(ReferenceEquals(target.Parent,trackParent),"Color dispatch did not restore trigger before refreshing tracks");
        target=Descendants<CommandMenuButton>(sidebar).First();sidebar.ScrollToTop();Pump();Click(target);window.Left+=35;window.Top+=20;Pump();Bounds(target);
        var before=target.TranslatePoint(new Point(),window);sidebar.ScrollToVerticalOffset(20);Pump();Check(target.IsOpen&&(target.TranslatePoint(new Point(),window)-before).Y<0,"Track scroll separated menu from trigger");
        sidebar.ScrollToBottom();Pump();Check(!target.IsOpen,"Offscreen track retained floating menu");
        Click(more);Call(window,"SetBusy",true);Pump();Check(!more.IsOpen&&!more.IsEnabled&&ReferenceEquals(more.Parent,originalParent),"Busy operation retained promoted menu");Call(window,"SetBusy",false);
        view.ReduceMotion=false;Click(more);var clock=System.Diagnostics.Stopwatch.StartNew();Until(()=>clock.ElapsedMilliseconds>90);
        double intermediate=more.Chrome.ActualHeight;Check(intermediate>more.ActualHeight&&intermediate<500,"Reveal had no intermediate frames");
        var text=Descendants<TextBlock>(more.Scroll).First();var p1=text.TranslatePoint(new Point(),more.Scroll);
        Until(()=>clock.ElapsedMilliseconds>210);var p2=text.TranslatePoint(new Point(),more.Scroll);Check((p1-p2).Length<.1&&text.LayoutTransform is MatrixTransform{Matrix.IsIdentity:true},"Menu text scaled or bounced with shell");
        Row(more,Lang.S("ed.zoomIn")).Focus();zoom=window.Viewport.Zoom;more.Close();KeyPress(Key.Enter);Check(window.Viewport.Zoom==zoom,"Closing menu dispatched a pending keyboard command");
        Click(more);Until(()=>clock.ElapsedMilliseconds>650);Check(more.IsOpen&&more.Chrome.ActualWidth==320,"Old close animation hid rapid reopen");
        if(output!=null)Capture(window,Path.Combine(output,"editor-more-integrated.png"));
        more.Close(true);view.ReduceMotion=true;
        for(int i=0;i<3;i++){Click(more);Click(more);Check(!more.IsOpen&&ReferenceEquals(more.Parent,originalParent),"Repeated close failed to restore actual button");}
        Click(more);window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseDownEvent,Source=window.Viewport});Pump();Check(!more.IsOpen,"Outside click did not dismiss");
        Click(more);window.Width=940;Pump();Check(!more.IsOpen,"Resize retained menu");Click(more);Bounds(more);
        Check(PerformanceIdentity.ForProject(window.Session.Project)==identity&&window.Session.UndoCount==history,"Command UI changed notes or history");
        window.Session.MarkSaved();window.Close();Check(!more.IsOpen&&Field(window,"_commandMenu")==null,"Editor close retained menu hooks");
        var root=new Grid();var parent=new StackPanel { VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(20) };
        var bottomButton=new CommandMenuButton { Content="...",ReduceMotion=true };parent.Children.Add(bottomButton);root.Children.Add(parent);
        var overlay=new Canvas();root.Children.Add(overlay);var owner=new Window { Width=600,Height=400,Content=root };owner.Show();Pump();
        var model=new ContextMenu();for(int i=0;i<5;i++)model.Items.Add(new MenuItem { Header="Command "+i });
        var anchorBottom=bottomButton.TranslatePoint(new Point(),root);bottomButton.Toggle(model,overlay);Pump();
        var shell=bottomButton.Chrome.TransformToAncestor(root).TransformBounds(new Rect(bottomButton.Chrome.RenderSize));
        Check(shell.Top<anchorBottom.Y&&shell.Top>=6&&Math.Abs(shell.Bottom-anchorBottom.Y-bottomButton.ActualHeight)<.1,"Bottom anchor failed upward bounded expansion");
        owner.Close();Check(!bottomButton.IsOpen&&ReferenceEquals(bottomButton.Parent,parent),"Bottom anchor lifecycle did not restore trigger");
        Console.WriteLine("EDITOR_COMMAND_MENU joined=True;track=True;commands=True;palette=True;lifecycle=True;motion=True");
    }
    static void EditorTrackChoice(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            ProjectTrack Track(string name,string instrument,int key){var t=new ProjectTrack{Name=name,PreviewInstrument=instrument};return t with{Notes=new(){new(){TrackId=t.Id,Key=key,Beat=1,LengthBeats=1,Action=NoteAction.Hold}}};}
            var tracks=new[]{Track("Melody","Piano",2),Track("","Harp",7),Track("A long track name that must retain its identity and full tooltip","Piano",9)};
            var window=new EditorWindow(new(){Tracks=tracks.ToList()}){AudioEnabled=false,ShowActivated=false};window.Show();Pump();
            var model=(ComboBox)window.FindName("TrackSelect");var button=(JoinedChoiceButton)Field(window,"_trackChoice")!;
            var snap=(JoinedChoiceButton)Field(window,"_snapChoice")!;var tools=(JoinedChoiceButton)Field(window,"_toolChoice")!;button.ReduceMotion=snap.ReduceMotion=tools.ReduceMotion=true;
            string identity=PerformanceIdentity.ForProject(window.Session.Project);int history=window.Session.UndoCount;
            void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void KeyPress(Key key)=>window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Click(button);Pump();Check(button.IsOpen&&model.Visibility==Visibility.Collapsed&&button.IsAncestorOf(button.Options),"Track selector still uses separate dropdown");
            var ids=model.Items.Cast<object>().Select(o=>(Guid)o.GetType().GetProperty("Id")!.GetValue(o)!).ToArray();Check(ids.SequenceEqual(tracks.Select(t=>t.Id)),"Track IDs or order changed");
            var item=(ListBoxItem)button.Options.Items[1];var point=item.TranslatePoint(new Point(item.ActualWidth/2,item.ActualHeight/2),window);
            Console.WriteLine(JsonSerializer.Serialize(new{track_menu_point=point.ToString(),item_width=item.ActualWidth,item_height=item.ActualHeight,chrome_height=button.Chrome.ActualHeight,hit=window.InputHitTest(point)?.GetType().Name}));
            Check(window.InputHitTest(point) is DependencyObject hit&&ItemsControl.ContainerFromElement(button.Options,hit) is ListBoxItem,"Overview or grid covers track choices");
            if(output!=null)Capture(window,Path.Combine(output,$"editor-track-{theme}-{language}.png"));
            button.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=item});Pump();
            Check(window.Session.ActiveTrackId==tracks[1].Id&&Equals(model.SelectedValue,tracks[1].Id)&&!button.IsOpen,"Mouse track selection did not switch active editing track");
            Check(((TextBlock)window.FindName("TrackCaption")).Text.Contains(Lang.Instrument("Harp")),"Track caption did not follow selected instrument");
            Click(button);Pump();int count=window.Session.ActiveTrack.Notes.Count;KeyPress(Key.Y);Check(window.Session.ActiveTrack.Notes.Count==count,"Track dropdown routed a musical key into note creation");
            KeyPress(Key.End);KeyPress(Key.Enter);Pump();Check(window.Session.ActiveTrackId==tracks[2].Id&&!button.IsOpen,"Keyboard track selection failed");
            Check(button.ToolTip.ToString()!.Contains(tracks[2].Name),"Long track tooltip lost full name");
            Click(button);Pump();KeyPress(Key.Up);KeyPress(Key.Escape);Pump();Check(window.Session.ActiveTrackId==tracks[2].Id&&!button.IsOpen,"Escape committed a pending track choice");
            Click(tools);Pump();Click(button);Pump();Check(button.IsOpen&&!tools.IsOpen,"Track and tool dropdowns overlapped");Click(snap);Pump();Check(snap.IsOpen&&!button.IsOpen,"Snap did not dismiss track dropdown");KeyPress(Key.Escape);Pump();
            Click(button);Pump();Call(window,"SetBusy",true);Pump();Check(!button.IsEnabled&&!button.IsOpen,"Visible track selector ignored busy state");Call(window,"SetBusy",false);Pump();Check(button.IsEnabled,"Track selector did not recover after busy state");
            Check(PerformanceIdentity.ForProject(window.Session.Project)==identity&&window.Session.UndoCount==history,"Track switching rewrote notes or history");
            window.Session.Create(4,2);Check(window.Session.ActiveTrack.Notes.Count==2&&window.Session.Project.Tracks[0].Notes.Count==1,"Editing after switch modified wrong track");window.HandleKey(Key.Z,ModifierKeys.Control);Check(PerformanceIdentity.ForProject(window.Session.Project)==identity,"Undo failed after switching tracks");window.HandleKey(Key.Y,ModifierKeys.Control);Check(window.Session.ActiveTrack.Notes.Count==2,"Redo failed after switching tracks");
            window.Width=940;Pump();for(int i=0;i<3;i++){Click(button);Pump();Check(button.IsOpen,"Compact track selector failed to reopen");Click(button);Pump();Check(!button.IsOpen,"Track selector failed to close");}
            Click(button);Pump();window.Session.MarkSaved();window.Close();Check(!button.IsOpen,"Editor close retained track choices");
        }
        Console.WriteLine("EDITOR_TRACK_CHOICE identity=True;switch=True;keyboard=True;busy=True;history=True");
    }
    static void EditorChoices(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var track=new ProjectTrack();track=track with{Notes=new(){new(){TrackId=track.Id,Key=3,Beat=1,Action=NoteAction.Hold,LengthBeats=1}}};
            var window=new EditorWindow(new(){GridSubdiv=5,Tracks=new(){track}}){AudioEnabled=false,ShowActivated=false};window.Show();Pump();
            var snap=(ComboBox)Field(window,"_snap")!;var tools=(ComboBox)Field(window,"_tools")!;
            var snapButton=(JoinedChoiceButton)Field(window,"_snapChoice")!;var toolButton=(JoinedChoiceButton)Field(window,"_toolChoice")!;snapButton.ReduceMotion=toolButton.ReduceMotion=true;
            string identity=PerformanceIdentity.ForProject(window.Session.Project);int history=window.Session.UndoCount;
            void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Choose(JoinedChoiceButton b,int index)=>b.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=b.Options.Items[index]});
            void KeyPress(Key key)=>window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window)!,Environment.TickCount,key){RoutedEvent=Keyboard.PreviewKeyDownEvent});
            Click(snapButton);Pump();
            var values=snap.Items.Cast<object>().Select(o=>(int)o.GetType().GetProperty("Subdiv")!.GetValue(o)!).ToArray();
            Check(values.SequenceEqual(new[]{0,4,8,3,6,12,5}),"Snap choices or custom grid value changed");
            Check(snap.Visibility==Visibility.Collapsed&&snapButton.IsAncestorOf(snapButton.Options)&&ReferenceEquals(PresentationSource.FromVisual(snapButton.Options),PresentationSource.FromVisual(snapButton)),"Snap still uses a separate popup");
            Check(SmoothScroll.GetEnabled(snapButton.Options)&&VirtualizingPanel.GetScrollUnit(snapButton.Options)==ScrollUnit.Pixel,"Editor list did not reuse smooth pixel scrolling");
            var item=(ListBoxItem)snapButton.Options.Items[2];var point=item.TranslatePoint(new Point(item.ActualWidth/2,item.ActualHeight/2),window);
            Check(window.InputHitTest(point) is DependencyObject hit&&ItemsControl.ContainerFromElement(snapButton.Options,hit) is ListBoxItem,"Editor choices were covered by track row or overview");
            int notes=window.Session.ActiveTrack.Notes.Count;KeyPress(Key.Y);Check(window.Session.ActiveTrack.Notes.Count==notes,"Open selector routed musical key into note creation");
            if(output!=null)Capture(window,Path.Combine(output,$"editor-snap-{theme}-{language}.png"));
            Choose(snapButton,2);Pump();Check(window.Session.Project.GridSubdiv==8&&window.Viewport.SnapEnabled&&!snapButton.IsOpen,"Snap choice did not update real grid");
            Click(snapButton);Pump();Choose(snapButton,0);Pump();Check(!window.Viewport.SnapEnabled&&window.Session.Project.GridSubdiv==8,"No-snap changed stored grid or failed to disable snapping");
            Click(toolButton);Pump();Check(tools.Items.Cast<string>().SequenceEqual(new[]{Lang.S("ed.draw"),Lang.S("ed.paint"),Lang.S("ed.erase")}),"Tool choices lost their own localized contents");
            if(output!=null)Capture(window,Path.Combine(output,$"editor-tools-{theme}-{language}.png"));
            KeyPress(Key.End);KeyPress(Key.Enter);Pump();Check(tools.SelectedIndex==2&&window.Viewport.Tool==RollTool.Erase&&!toolButton.IsOpen,"Keyboard tool selection failed");
            Click(toolButton);Pump();Choose(toolButton,1);Pump();Check(window.Viewport.Tool==RollTool.Paint,"Mouse tool selection failed");
            Click(snapButton);Pump();Click(toolButton);Pump();Check(!snapButton.IsOpen&&toolButton.IsOpen,"Editor selectors overlapped");
            KeyPress(Key.Escape);Pump();Check(!toolButton.IsOpen&&!snapButton.IsOpen,"Escape did not dismiss editor selector");
            Check(PerformanceIdentity.ForProject(window.Session.Project)==identity&&window.Session.UndoCount==history,"Selector changes modified notes or edit history");
            window.Session.Create(4,2);window.HandleKey(Key.Z,ModifierKeys.Control);Check(PerformanceIdentity.ForProject(window.Session.Project)==identity,"Undo stopped working after selector change");window.HandleKey(Key.Y,ModifierKeys.Control);Check(window.Session.ActiveTrack.Notes.Count==notes+1,"Redo stopped working after selector change");
            window.Width=940;Pump();for(int i=0;i<3;i++){Click(snapButton);Pump();Check(snapButton.IsOpen,"Compact editor selector failed to reopen");Click(snapButton);Pump();Check(!snapButton.IsOpen,"Compact editor selector failed to close");}
            Click(snapButton);Pump();window.Session.MarkSaved();window.Close();Check(!snapButton.IsOpen&&!toolButton.IsOpen,"Closing editor retained dropdown lifecycle");
        }
        Console.WriteLine("EDITOR_CHOICES snap=True;tools=True;keyboard=True;history=True;joined=True");
    }
    static void PracticeReturn(string mode,string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        string file=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{80,120,220,255},4)));
        using var bytes=new MemoryStream();encoder.Save(bytes);
        File.WriteAllText(file,JsonSerializer.Serialize(new{name="Return song",author="Author A",transcribedBy="Score B",bpm=120,cover=Convert.ToBase64String(bytes.ToArray()),songNotes=new[]{new{time=0,key="1Key0"},new{time=500,key="1Key1"},new{time=1000,key="1Key2"}}}));
        try
        {
            foreach(var theme in Enum.GetValues<AppTheme>())foreach(bool read in new[]{false,true})foreach(double width in new[]{940d,1200d})
            {
                Theme.Apply(theme,persist:false);var main=new MainWindow(true){Width=width,Height=760,ShowActivated=false};main.Show();Pump();
                var song=new SongInfo{File=file,Name="Return song",Author="Author A",TranscribedBy="Score B",Fav=true,DurationMs=1000,NoteCount=3};
                typeof(MainWindow).GetField("_nowPlaying",Private)!.SetValue(main,song);typeof(MainWindow).GetField("_playCurrent",Private)!.SetValue(main,song);
                ((CheckBox)main.FindName("ReadModeSwitch")).IsChecked=read;Call(main,"ShowPractice",true);Until(()=>!(bool)Field(main,"_transitioning")!);Pump();
                var panel=(FrameworkElement)main.FindName("PracticePanel");var bg=(FrameworkElement)main.FindName("PracticeBg");var root=(FrameworkElement)main.FindName("RootScale");
                Rect Bounds(FrameworkElement e)=>e.TransformToVisual(root).TransformBounds(new Rect(e.RenderSize));
                double Effective(FrameworkElement e){double value=1;for(DependencyObject? node=e;node!=null&&!ReferenceEquals(node,root);node=VisualTreeHelper.GetParent(node))if(node is UIElement ui)value*=ui.Opacity;return value;}
                if(mode=="practice-cover")
                {
                    var body=(FrameworkElement)VisualTreeHelper.GetParent((FrameworkElement)main.FindName("RightContent"));var a=Bounds(bg);var b=Bounds(body);
                    Check(a.Left<=b.Left+.05&&a.Top<=b.Top+.05&&a.Right>=b.Right-.05&&a.Bottom>=b.Bottom-.05,"Practice background leaves underlying main-page edges exposed");
                    Check(Effective(bg)>.999,"Resting practice background is not opaque");
                }
                var cover=(Image)main.FindName("PlayerCover");Check(cover.Source!=null&&cover.Visibility==Visibility.Visible,"Practice fixture cover did not load");
                Call(main,"ShowPractice",false);
                if(mode=="practice-rebound")
                {
                    var scale=(ScaleTransform)Field(main,"_pCardScale")!;double small=(double)Field(main,"_pSmallScale")!,open=(double)Field(main,"_pOpenScale")!;
                    double span=open-small;bool visibleBounce=false;int samples=0;var frames=new List<(double scale,double opacity)>();
                    EventHandler frame=(_,_)=>{samples++;double opacity=Effective((FrameworkElement)main.FindName("PracticeCard"));double position=Math.Abs(span)>.001?(scale.ScaleX-small)/span:0;frames.Add((position,opacity));if(position<-.0001&&opacity>.2)visibleBounce=true;};
                    CompositionTarget.Rendering+=frame;try{Until(()=>!(bool)Field(main,"_transitioning")!);}finally{CompositionTarget.Rendering-=frame;}
                    if(!visibleBounce)Console.WriteLine(JsonSerializer.Serialize(new{theme,width,read,samples,frames=frames.Select(f=>new{f.scale,f.opacity}).ToArray()}));
                    Check(samples>=3&&visibleBounce,"Close overshoot is hidden before its rebound becomes visible");
                }
                if(mode=="practice-timing")
                {
                    panel.BeginAnimation(UIElement.OpacityProperty,null);
                    var scale=(ScaleTransform)Field(main,"_pCardScale")!;scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
                    double small=(double)Field(main,"_pSmallScale")!,open=(double)Field(main,"_pOpenScale")!;scale.ScaleX=small+(open-small)*.1;
                    Call(main,"PracticeBgFollow",null!,EventArgs.Empty);Pump();
                    Check(Effective(bg)<.5,"Closing practice cover does not track reveal progress");
                    foreach(string name in new[]{"PracticeCard","SheetArea","PracticeBackBtn","ReadModeBar"})
                        Check(Effective((FrameworkElement)main.FindName(name))<=Effective(bg)+.01,"Practice content lingers after its background fades: "+name);
                }
                if(mode=="practice-return")
                {
                    Check(((TextBlock)main.FindName("PlayerSongName")).Text==song.Name,"Practice exit cleared current song title");
                    Check(((TextBlock)main.FindName("PlayerAuthor")).Text==song.Author&&((TextBlock)main.FindName("PlayerAuthor")).Visibility==Visibility.Visible,"Practice exit cleared artist");
                    Check(((TextBlock)main.FindName("PlayerTranscriber")).Text==song.TranscribedBy&&((TextBlock)main.FindName("PlayerTranscriber")).Visibility==Visibility.Visible,"Practice exit cleared transcriber");
                    Check(cover.Source!=null&&cover.Visibility==Visibility.Visible&&((FrameworkElement)main.FindName("FavStar")).Visibility==Visibility.Visible,"Practice exit cleared cover or favorite");
                    Check(ReferenceEquals(Field(main,"_nowPlaying"),song)&&ReferenceEquals(Field(main,"_playCurrent"),song),"Practice return changed playback context");
                }
                Until(()=>!(bool)Field(main,"_transitioning")!);Pump();Check(panel.Visibility==Visibility.Collapsed&&!(bool)Field(main,"_pRenderingHooked")!&&!((DispatcherTimer)Field(main,"_pFinishTimer")!).IsEnabled,"Practice exit retained panel, timer or rendering subscription");
                if(output!=null)Capture(main,Path.Combine(output,$"{mode}-{theme}-{read}-{width}.png"));
                Call(main,"ShowPractice",true);var time=Stopwatch.StartNew();Until(()=>time.ElapsedMilliseconds>=45);Call(main,"ShowPractice",false);Call(main,"ShowPractice",true);Call(main,"ShowPractice",false);
                Until(()=>!(bool)Field(main,"_transitioning")!);Check(panel.Visibility==Visibility.Collapsed,"Interrupted practice return reopened stale panel");main.Close();
            }
            if(mode=="practice-return")
            {
                var selected=new MainWindow(true){ShowActivated=false};selected.Show();Pump();
                var song=new SongInfo{File=file,Name="Selected practice",Author="Author A",DurationMs=1000};
                ((List<SongInfo>)Field(selected,"_all")!).Add(song);Call(selected,"ApplyFilter");((ListBox)selected.FindName("SongList")).SelectedItem=song;
                Call(selected,"ShowPractice",true);Until(()=>!(bool)Field(selected,"_transitioning")!);Call(selected,"ShowPractice",false);
                Check(((TextBlock)selected.FindName("PlayerSongName")).Text==song.Name,"Returning lost a song chosen only for practice");selected.Close();
                var empty=new MainWindow(true){ShowActivated=false};empty.Show();Pump();string idle=((TextBlock)empty.FindName("PlayerSongName")).Text;
                Call(empty,"ShowPractice",true);Until(()=>!(bool)Field(empty,"_transitioning")!);Call(empty,"ShowPractice",false);
                Check(((TextBlock)empty.FindName("PlayerSongName")).Text==idle,"Empty practice return invented a current song");empty.Close();
            }
        }
        finally{File.Delete(file);}
        Console.WriteLine(mode.Replace('-','_').ToUpperInvariant()+" passed");
    }
    static void SpeedPill(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var speeds=new[]{2.0,1.75,1.5,1.25,1.0,.75,.5};
        string file=(string)typeof(MainWindow).GetProperty("PrefsFile",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        byte[]? before=File.Exists(file)?File.ReadAllBytes(file):null;
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var main=new MainWindow(true){ShowActivated=false};main.Show();Pump();
            var speed=(PillChoiceButton)main.FindName("SpeedPill");var pitch=(PillChoiceButton)main.FindName("PitchPill");var instrument=(PillChoiceButton)main.FindName("InstrumentPill");
            speed.ReduceMotion=pitch.ReduceMotion=instrument.ReduceMotion=true;
            void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Choose(int index)=>speed.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=speed.Options.Items[index]});
            Click(speed);Pump();
            Check(speed.Options.Items.Count==8&&Equals(((ListBoxItem)speed.Options.Items[0]).Tag,"random")&&Equals(((ListBoxItem)speed.Options.Items[0]).Content,Lang.S("speed.random")),"Random speed option changed or was untranslated");
            Check(speed.Options.Items.OfType<ListBoxItem>().Skip(1).Select(i=>(double)i.Tag).SequenceEqual(speeds),"Speed values changed during component reuse");
            Check(speed.IsAncestorOf(speed.Options)&&ReferenceEquals(PresentationSource.FromVisual(speed),PresentationSource.FromVisual(speed.Options)),"Speed remains in a separate popup");
            Check(Math.Abs(speed.Chrome.ActualWidth-200)<.1&&SmoothScroll.GetEnabled(speed.Options)&&VirtualizingPanel.GetScrollUnit(speed.Options)==ScrollUnit.Pixel,"Speed lost shared geometry or smooth scrolling");
            Choose(2);Pump();var player=(SkyPlayer)Field(main,"_player")!;
            Check(!speed.IsOpen&&Math.Abs(player.SpeedFactor-1.75)<.001&&Math.Abs((double)Field(main,"_speed")!-1.75)<.001&&!player.RandomSpeed,"Numeric speed choice did not update player");
            Click(speed);Pump();Check(Equals(((ListBoxItem)speed.Options.SelectedItem).Tag,1.75),"Reopen lost selected numeric speed");Choose(0);Pump();
            Check(player.RandomSpeed&&Equals(speed.Content,Lang.S("speed.random")),"Random speed did not reach player or capsule label");
            Click(speed);Pump();Check(Equals(((ListBoxItem)speed.Options.SelectedItem).Tag,"random"),"Reopen lost random selection");
            Choose(5);Pump();Check(!player.RandomSpeed&&Math.Abs(player.SpeedFactor-1)<.001,"Numeric speed did not leave random mode");
            Click(speed);Pump();if(output!=null)Capture(main,Path.Combine(output,$"speed-pill-{theme}-{language}.png"));
            for(int i=0;i<3;i++){Click(speed);Pump();Check(!speed.IsOpen,"Speed did not close");Click(speed);Pump();Check(speed.IsOpen,"Speed did not reopen");}
            Click(pitch);Pump();Check(pitch.IsOpen&&!speed.IsOpen,"Pitch did not dismiss speed capsule");
            Click(speed);Pump();Check(speed.IsOpen&&!pitch.IsOpen&&!instrument.IsOpen,"Speed did not dismiss other capsules");
            Click(instrument);Pump();Check(!speed.IsOpen&&instrument.IsOpen,"Instrument did not dismiss speed capsule");
            Click(speed);Pump();Call(main,"OpenSettings");Pump();Check(!speed.IsOpen,"Page exit retained speed capsule");
            Click(speed);Pump();main.Close();Check(!speed.IsOpen,"Owner exit retained speed capsule");
        }
        Check(before==null?!File.Exists(file):File.ReadAllBytes(file).SequenceEqual(before),"Speed verification changed real preferences");
        Console.WriteLine("SPEED_PILL original_options=True;selection=True;random=True;joined=True;exclusive=True");
    }
    static void PitchPill(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var original=new[]{12,11,10,9,8,7,6,5,4,3,2,1,0,-12};
        string prefs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SMAP");
        var snapshots=Directory.GetFiles(prefs,"*.json").ToDictionary(p=>p,File.ReadAllBytes);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var main=new MainWindow(true){ShowActivated=false};main.Show();Pump();
            var pitch=(PillChoiceButton)main.FindName("PitchPill");var instrument=(PillChoiceButton)main.FindName("InstrumentPill");pitch.ReduceMotion=instrument.ReduceMotion=true;
            void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click(pitch);Pump();
            var offsets=pitch.Options.Items.OfType<ListBoxItem>().Select(i=>(int)i.Tag).ToArray();
            Check(pitch.IsOpen&&offsets.Where(original.Contains).SequenceEqual(original),"Existing pitch choices or their relative order changed");
            Check(offsets.Length==25&&offsets.First()==12&&offsets.Last()==-12&&offsets.Zip(offsets.Skip(1),(a,b)=>a-b).All(step=>step==1),"Pitch menu has missing semitone choices");
            Check(pitch.Options.Items.OfType<ListBoxItem>().Single(i=>Equals(i.Tag,-1)).Content.Equals("-1 B")&&pitch.Options.Items.OfType<ListBoxItem>().Single(i=>Equals(i.Tag,-11)).Content.Equals("-11 Db"),"Negative pitch labels have wrong note names");
            int expected=AudioEngine.GetOffset((string)Field(main,"_instrumentName")!);
            if(original.Contains(expected))Check(Equals(((ListBoxItem)pitch.Options.SelectedItem).Tag,expected),"Pitch did not restore selected offset");
            Check(pitch.IsAncestorOf(pitch.Options)&&ReferenceEquals(PresentationSource.FromVisual(pitch.Options),PresentationSource.FromVisual(pitch)),"Pitch choices use a separate window");
            Check(Math.Abs(pitch.Chrome.ActualWidth-200)<.1&&VirtualizingPanel.GetScrollUnit(pitch.Options)==ScrollUnit.Pixel&&SmoothScroll.GetEnabled(pitch.Options),"Pitch did not reuse fixed width and smooth pixel scrolling");
            for(int cycle=0;cycle<3;cycle++){Click(pitch);Pump();Check(!pitch.IsOpen,"Pitch did not close");Click(pitch);Pump();Check(pitch.IsOpen,"Pitch did not reopen");}
            pitch.Options.ScrollIntoView(pitch.Options.Items[0]);Pump();
            var item=(ListBoxItem)pitch.Options.Items[0];var point=item.TranslatePoint(new Point(item.ActualWidth/2,item.ActualHeight/2),main);
            Check(main.InputHitTest(point) is DependencyObject hit&&ItemsControl.ContainerFromElement(pitch.Options,hit) is ListBoxItem,"Pitch options cannot receive input");
            main.Left+=15;main.Top+=8;Pump();var corner=pitch.Chrome.TranslatePoint(new Point(pitch.Chrome.ActualWidth,pitch.Chrome.ActualHeight),pitch);
            Check(Math.Abs(corner.X-pitch.ActualWidth)<.1&&Math.Abs(corner.Y-pitch.ActualHeight)<.1,"Moving window detached pitch capsule");
            if(output!=null)Capture(main,Path.Combine(output,$"pitch-pill-{theme}-{language}.png"));
            Click(instrument);Pump();Check(instrument.IsOpen&&!pitch.IsOpen,"Instrument and pitch were open together");
            Click(pitch);Pump();Check(pitch.IsOpen&&!instrument.IsOpen,"Pitch did not dismiss instrument capsule");
            Click((Button)main.FindName("FilterChoiceBtn"));Pump();Check(!pitch.IsOpen,"Library choice retained pitch capsule");
            Click(pitch);Pump();Call(main,"OpenSettings");Pump();Check(!pitch.IsOpen,"Settings page retained pitch capsule");
            Click(pitch);Pump();main.Close();Check(!pitch.IsOpen,"Owner close retained pitch choices");
        }
        Check(snapshots.All(p=>File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Pitch UI verification changed JSON preferences");
        Console.WriteLine("PITCH_PILL original_options=True;joined=True;fixed_width=True;exclusive=True;prefs_unchanged=True");
    }
    static void InstrumentScroll()
    {
        foreach(var theme in Enum.GetValues<AppTheme>())
        {
            Theme.Apply(theme,persist:false);var main=new MainWindow(true){ShowActivated=false};main.Show();Pump();
            Until(()=>!(bool)Field(main,"_previewLoading")!);main.ReviewPreviewLayout("Piano");
            var pill=(PillChoiceButton)main.FindName("InstrumentPill");pill.ReduceMotion=true;
            void Click()=>pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click();Pump();var viewer=Descendants<ScrollViewer>(pill.Options).Single();
            Check(VirtualizingPanel.GetScrollUnit(pill.Options)==ScrollUnit.Pixel,"Instrument scroll still uses whole items");
            var offsets=new List<double>();var rendering=new List<double>();
            viewer.ScrollChanged+=(_,e)=>{if(e.VerticalChange!=0)offsets.Add(viewer.VerticalOffset);};
            void Wheel(int delta)=>viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=UIElement.PreviewMouseWheelEvent});
            EventHandler sample=(_,e)=>{if(e is RenderingEventArgs frame)rendering.Add(frame.RenderingTime.TotalMilliseconds);};
            CompositionTarget.Rendering+=sample;Wheel(-120);var clock=Stopwatch.StartNew();Until(()=>clock.ElapsedMilliseconds>=260);CompositionTarget.Rendering-=sample;
            Check(offsets.Distinct().Count()>=3&&offsets.Any(x=>x>1&&x<119),"Wheel scroll has no intermediate pixel positions");
            Check(Math.Abs(viewer.VerticalOffset-120)<1,"Wheel scroll did not settle at its target");
            Wheel(-120);Wheel(-120);clock.Restart();Until(()=>clock.ElapsedMilliseconds>=260);Check(Math.Abs(viewer.VerticalOffset-360)<1,$"Rapid wheel input lost accumulated distance: offset={viewer.VerticalOffset}, extent={viewer.ScrollableHeight}, elapsed={clock.ElapsedMilliseconds}");
            Wheel(120);clock.Restart();Until(()=>clock.ElapsedMilliseconds>=260);Check(Math.Abs(viewer.VerticalOffset-240)<1,$"Reverse scrolling jumped to a stale target: offset={viewer.VerticalOffset}, elapsed={clock.ElapsedMilliseconds}");
            Wheel(-120);clock.Restart();Until(()=>clock.ElapsedMilliseconds>=50);
            Console.WriteLine(JsonSerializer.Serialize(new{stage="before-close",offset=viewer.VerticalOffset,selected=((ListBoxItem)pill.Options.SelectedItem).Tag}));
            Click();Pump();Console.WriteLine(JsonSerializer.Serialize(new{stage="closed",pill.IsOpen,offset=viewer.VerticalOffset}));
            Click();Pump();Console.WriteLine(JsonSerializer.Serialize(new{stage="reopened",pill.IsOpen,offset=viewer.VerticalOffset}));clock.Restart();Until(()=>clock.ElapsedMilliseconds>=260);
            Check(viewer.VerticalOffset<1,$"Closing/reopening resumed stale wheel animation instead of selected instrument: {viewer.VerticalOffset}");
            Check(Math.Abs(pill.Chrome.ActualWidth-200)<.1&&pill.IsOpen,"Scrolling changed pill width or closed it");
            var intervals=rendering.Zip(rendering.Skip(1),(a,b)=>b-a).Where(t=>t>0).Order().ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new{theme=theme.ToString(),scroll_updates=offsets.Count,render_frames=rendering.Distinct().Count(),render_interval_median_ms=intervals.Length==0?0:intervals[intervals.Length/2]}));
            main.Close();
        }
        var list=new ListBox{ItemsSource=Enumerable.Range(0,100).Select(i=>$"Item {i}"),Height=200};
        VirtualizingPanel.SetScrollUnit(list,ScrollUnit.Pixel);SmoothScroll.SetEnabled(list,true);
        var owner=new Window{Content=list,Width=300,Height=250,ShowActivated=false};owner.Show();Pump();
        SmoothScroll.Attach(list);SmoothScroll.Attach(list);var shared=Descendants<ScrollViewer>(list).Single();
        shared.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});
        var settled=Stopwatch.StartNew();Until(()=>settled.ElapsedMilliseconds>=260);
        Check(Math.Abs(shared.VerticalOffset-120)<1,"Shared smooth scrolling registered duplicate handlers");
        shared.ScrollToVerticalOffset(300);Pump();
        shared.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120){RoutedEvent=UIElement.PreviewMouseWheelEvent});
        settled.Restart();Until(()=>settled.ElapsedMilliseconds>=260);
        Check(Math.Abs(shared.VerticalOffset-180)<1,"Scrollbar movement did not synchronize shared wheel target");owner.Close();
        Console.WriteLine("INSTRUMENT_SCROLL pixels=True;intermediate=True;reverse=True;reopen=True");
    }
    static void InstrumentPill(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var main=new MainWindow(true){ShowActivated=false};main.Show();Pump();
            var pill=(PillChoiceButton)main.FindName("InstrumentPill");pill.ReduceMotion=true;
            void Click()=>pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Anchored()
            {
                var corner=pill.Chrome.TranslatePoint(new Point(pill.Chrome.ActualWidth,pill.Chrome.ActualHeight),pill);
                Check(Math.Abs(corner.X-pill.ActualWidth)<.1&&Math.Abs(corner.Y-pill.ActualHeight)<.1,"Pill detached from bottom-right anchor");
            }
            Click();Pump();Check(pill.IsOpen&&Math.Abs(pill.Chrome.ActualWidth-200)<.1,"Expanded pill width is not fixed");
            Check(pill.IsAncestorOf(pill.Options)&&ReferenceEquals(PresentationSource.FromVisual(pill),PresentationSource.FromVisual(pill.Options)),"Instrument options still use a separate popup");
            Check(pill.Options.Items.OfType<ListBoxItem>().Select(i=>(string)i.Tag).SequenceEqual(AudioEngine.Instruments.Append("Custom4")),"Instrument identities or order changed");
            Anchored();double width=pill.Chrome.ActualWidth;
            pill.Options.ScrollIntoView(pill.Options.Items[^1]);Pump();Check(Math.Abs(pill.Chrome.ActualWidth-width)<.05,"Long names changed scrolled popup width");
            var last=(ListBoxItem)pill.Options.Items[^1];var point=last.TranslatePoint(new Point(last.ActualWidth/2,last.ActualHeight/2),main);
            Check(main.InputHitTest(point) is DependencyObject hit&&ItemsControl.ContainerFromElement(pill.Options,hit) is ListBoxItem,"Expanded pill options are covered or cannot receive input");
            main.Left+=17;main.Top+=9;Pump();Anchored();main.Width+=100;Pump();Anchored();
            for(int i=0;i<3;i++){Click();Pump();Check(!pill.IsOpen,"Pill did not close");Click();Pump();Check(pill.IsOpen,"Pill did not reopen");Anchored();}
            if(output!=null)Capture(main,Path.Combine(output,$"instrument-pill-{theme}-{language}.png"));
            pill.Close(true);pill.ReduceMotion=false;Click();Pump();
            pill.Options.ScrollIntoView(pill.Options.Items[0]);Pump();
            var header=(FrameworkElement)pill.Template.FindName("PART_Header",pill);var option=(ListBoxItem)pill.Options.Items[0];
            var head=header.TranslatePoint(new Point(),pill);var label=option.TranslatePoint(new Point(),pill);int frames=0;
            SizeChangedEventHandler sample=(_,_)=>
            {
                Anchored();var h=header.TranslatePoint(new Point(),pill);var t=option.TranslatePoint(new Point(),pill);
                Check(Math.Abs(h.X-head.X)<.1&&Math.Abs(h.Y-head.Y)<.1,"Pill title bounced with the shell");
                Check(Math.Abs(t.X-label.X)<.1&&Math.Abs(t.Y-label.Y)<.1,"Pill option text bounced with the shell");
                Check(pill.Chrome.Clip is RectangleGeometry clip&&Math.Abs(clip.Rect.Height-pill.Chrome.ActualHeight)<.1,"Pill animation lost clipping");frames++;
            };
            pill.Chrome.SizeChanged+=sample;var clock=Stopwatch.StartNew();Until(()=>clock.ElapsedMilliseconds>=390);pill.Chrome.SizeChanged-=sample;
            if(SystemParameters.ClientAreaAnimation)Check(frames>=3,"Pill reveal did not animate");
            Click();Click();clock.Restart();Until(()=>clock.ElapsedMilliseconds>=390);Check(pill.IsOpen,"Stale close completion hid reopened pill");
            pill.ReduceMotion=true;pill.Close(true);Click();Pump();last=(ListBoxItem)pill.Options.Items[^1];pill.Options.ScrollIntoView(last);Pump();
            pill.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=last});
            Until(()=>!(bool)Field(main,"_previewLoading")!);Pump();
            Check(!pill.IsOpen&&Equals(Field(main,"_instrumentName"),"Custom4"),"Instrument selection failed or did not close");
            Click();Pump();Check(Equals(((ListBoxItem)pill.Options.SelectedItem).Tag,"Custom4"),"Reopen lost selected instrument");
            Call(main,"OpenSettings");Pump();Check(!pill.IsOpen,"Changing page retained instrument options");
            Click();Pump();main.Close();Check(!pill.IsOpen,"Owner exit retained instrument choices");
        }
        Console.WriteLine("INSTRUMENT_PILL fixed_width=True;joined=True;reopen=True;motion=True;selection=True");
    }
    static void ChoiceMotion()
    {
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(double height in new[]{36d,40d})foreach(double font in new[]{13d,15.6d})
        {
            Theme.Apply(theme,persist:false);
            var button=new JoinedChoiceButton{Width=260,Height=height,FontSize=font,Content="简体中文",VerticalAlignment=VerticalAlignment.Top,HorizontalAlignment=HorizontalAlignment.Left,ReduceMotion=true};
            var owner=new Window{Width=400,Height=500,Content=button,ShowActivated=false};owner.Show();Pump();
            var model=new ComboBox{ItemsSource=new[]{"简体中文","繁體中文","English","日本語"},SelectedIndex=0};button.Toggle(model);Pump();
            var header=(FrameworkElement)button.Template.FindName("PART_Header",button);var item=(ListBoxItem)button.Options.Items[0];
            Point Header()=>header.TranslatePoint(new Point(),button);Point Text()=>item.TranslatePoint(new Point(),button);
            var anchor=Header();var text=Text();double full=button.Chrome.ActualHeight;
            void Stable()
            {
                Check(Math.Abs(Header().Y-anchor.Y)<.05,$"Header moved with reveal height: {Header().Y-anchor.Y:0.###} DIP");
                Check(Math.Abs(Text().Y-text.Y)<.05,"Option text moved with reveal height");
                var transform=header.TransformToAncestor(button);var a=transform.Transform(new Point());var b=transform.Transform(new Point(10,10));
                Check(Math.Abs(b.X-a.X-10)<.05&&Math.Abs(b.Y-a.Y-10)<.05,"Reveal animation scaled header text");
            }
            foreach(double reveal in new[]{height,(height+full)/2,full+(full-height)*.03,full-(full-height)*.01,full})
            {button.Chrome.Height=reveal;Pump();Stable();}
            button.Close(true);button.ReduceMotion=false;int frames=0;double min=double.MaxValue,max=0;
            SizeChangedEventHandler sample=(_,_)=>{Stable();frames++;min=Math.Min(min,button.Chrome.ActualHeight);max=Math.Max(max,button.Chrome.ActualHeight);};
            button.Toggle(model);Pump();item=(ListBoxItem)button.Options.Items[0];Stable();button.Chrome.SizeChanged+=sample;
            var clock=Stopwatch.StartNew();Until(()=>clock.ElapsedMilliseconds>=400);button.Chrome.SizeChanged-=sample;
            if(SystemParameters.ClientAreaAnimation)Check(frames>=3&&max-min>1,"The reveal no longer animates");
            button.Close(true);owner.Close();
        }
        Console.WriteLine("CHOICE_MOTION header=fixed;text=fixed;reveal=elastic;themes=3;heights=2");
    }
    static void SettingsChoice(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        string prefs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SMAP");
        var files=new[]{"lang.txt","theme.txt"}.ToDictionary(n=>Path.Combine(prefs,n),n=>File.Exists(Path.Combine(prefs,n))?File.ReadAllBytes(Path.Combine(prefs,n)):Array.Empty<byte>());
        void Unchanged()=>Check(files.All(p=>(File.Exists(p.Key)?File.ReadAllBytes(p.Key):Array.Empty<byte>()).SequenceEqual(p.Value)),"Verification changed real language or theme preferences");
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var main=new MainWindow(true){ShowActivated=false};main.Show();Call(main,"OpenSettings");var clock=Stopwatch.StartNew();Until(()=>clock.ElapsedMilliseconds>=460);
            var popup=(AnimatedChoicePopup)main.FindName("LibraryChoicePopup");
            var names=new[]{"Lang","Theme","Ui","Font"};int[] counts={4,3,9,9};
            for(int k=0;k<names.Length;k++)
            {
                var model=(ComboBox)main.FindName("Set"+names[k]+"Combo");var button=(JoinedChoiceButton)main.FindName("Set"+names[k]+"Choice");
                Check(model.Items.Count==counts[k]&&button.ActualHeight==40&&Equals(button.Content,model.SelectedItem),"Setting choice changed its options, size or selected label");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));clock.Restart();Until(()=>clock.ElapsedMilliseconds>=380);
                Check(popup.IsOpen&&button.IsAncestorOf(popup.Chrome)&&popup.Options.Items.Count==counts[k],"Setting did not use the joined component");
                Check(popup.Options.MaxHeight==180,"Settings options exceeded the compact viewport");
                int index=k switch{0=>((int)Lang.Current+1)%4,1=>((int)Theme.Current+1)%3,2=>6,_=>8};
                var item=(ListBoxItem)popup.Options.Items[index];popup.Options.ScrollIntoView(item);Pump();
                var point=item.TranslatePoint(new Point(item.ActualWidth/2,item.ActualHeight/2),main);
                Check(main.InputHitTest(point) is DependencyObject hit&&ItemsControl.ContainerFromElement(popup.Options,hit) is ListBoxItem,"A later settings row covered expanded options");
                popup.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=item});popup.Close(true);Pump();
                Check(model.SelectedIndex==index&&Equals(button.Content,model.SelectedItem)&&System.Windows.Data.BindingOperations.IsDataBound(button,ContentControl.ContentProperty),"Setting selection broke its display binding");
                if(k==0)Check((int)Lang.Current==index,"Language setting lost its effect");
                if(k==1)Check((int)Theme.Current==index,"Theme setting lost its effect");
                if(k==2)Check(Math.Abs(((ScaleTransform)main.FindName("UiScale")).ScaleX-1.1)<.001,"UI scale selection lost its effect");
                if(k==3)Check(Math.Abs((double)((FrameworkElement)main.FindName("RootScale")).GetValue(System.Windows.Documents.TextElement.FontSizeProperty)-15.6)<.001,"Font scale selection lost its effect");
                Unchanged();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(popup.IsOpen&&popup.Options.SelectedIndex==index,"Setting could not reopen with its selected value");popup.Close(true);
            }
            main.ApplyLanguage();
            var lang=(ComboBox)main.FindName("SetLangCombo");var themeModel=(ComboBox)main.FindName("SetThemeCombo");
            Check(((JoinedChoiceButton)main.FindName("SetThemeChoice")).Content!.Equals(themeModel.SelectedItem),"Language refresh left a stale theme label");
            var languageButton=(JoinedChoiceButton)main.FindName("SetLangChoice");languageButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));clock.Restart();Until(()=>clock.ElapsedMilliseconds>=360);
            if(output!=null)Capture(main,Path.Combine(output,$"settings-{theme}-{language}.png"));
            Call(main,"CloseSettings");Check(!popup.IsOpen,"Closing settings retained an expanded selector");main.Close();Unchanged();
        }
        Console.WriteLine("SETTINGS_CHOICE controls=4;languages=4;themes=3;effects=True;bindings=True;prefs_unchanged=True");
    }
    static void CloudChoice(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            Theme.Apply(theme,persist:false);typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);
            var main=new MainWindow(true){ShowActivated=false};main.Show();Pump();
            ((FrameworkElement)main.FindName("LocalFilterRow")).Visibility=Visibility.Collapsed;
            ((FrameworkElement)main.FindName("CloudFilterRow")).Visibility=Visibility.Visible;
            ((FrameworkElement)main.FindName("SongList")).Visibility=Visibility.Collapsed;
            ((FrameworkElement)main.FindName("CloudList")).Visibility=Visibility.Visible;
            ((TextBlock)main.FindName("LibHeader")).Text=Lang.S("nav.cloud");Pump();
            var sort=(ComboBox)main.FindName("CloudSortCombo");var diff=(ComboBox)main.FindName("CloudDiffCombo");var localSort=(ComboBox)main.FindName("SortCombo");var localFilter=(ComboBox)main.FindName("FilterCombo");
            var popup=(AnimatedChoicePopup)main.FindName("LibraryChoicePopup");var sortButton=(JoinedChoiceButton)main.FindName("CloudSortChoiceBtn");var diffButton=(JoinedChoiceButton)main.FindName("CloudDiffChoiceBtn");
            foreach(double width in new[]{1080d,1400d})
            {
                main.Width=width;Pump();var search=(FrameworkElement)main.FindName("SearchBox");
                var left=sortButton.TranslatePoint(new Point(),search);var right=diffButton.TranslatePoint(new Point(diffButton.ActualWidth,0),search);
                Check(Math.Abs(left.X)<.1&&Math.Abs(right.X-search.ActualWidth)<.1,"Cloud choices do not fill the search width");
                Check(Math.Abs(sortButton.ActualWidth-diffButton.ActualWidth)<.1&&Math.Abs(diffButton.TranslatePoint(new Point(),sortButton).X-sortButton.ActualWidth-12)<.1,"Cloud choices are not equal with a twelve-DIP gap");
                Check(Math.Abs(left.Y-search.ActualHeight-12)<.1,"Cloud vertical spacing does not match the horizontal gap");
                Check(sortButton.ActualHeight==search.ActualHeight&&diffButton.ActualHeight==search.ActualHeight,"Cloud choice height changed");
            }
            var originalLocalSort=localSort.Items.Cast<object>().ToArray();var originalLocalFilter=localFilter.Items.Cast<object>().ToArray();int si=localSort.SelectedIndex,fi=localFilter.SelectedIndex;
            string[] expectedSort={Lang.S("cloud.newest"),Lang.S("cloud.hot"),Lang.S("cloud.downs")};string[] expectedDiff={Lang.S("cloud.diffall"),"★","★★","★★★","★★★★","★★★★★"};
            foreach(var pair in new[]{(model:sort,button:sortButton,expected:expectedSort),(model:diff,button:diffButton,expected:expectedDiff)})
            {
                for(int i=0;i<pair.expected.Length;i++)
                {
                    pair.button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                    Check(popup.IsOpen&&pair.button.IsAncestorOf(popup.Chrome),"Cloud choice bypassed the joined component");
                    Check(popup.Options.Items.Cast<ListBoxItem>().Select(o=>o.Content?.ToString()).SequenceEqual(pair.expected),"Cloud choices reused local contents or lost translation");
                    if(i==0)
                    {
                        var clock=Stopwatch.StartNew();Until(()=>clock.ElapsedMilliseconds>=360);
                        var option=(ListBoxItem)popup.Options.Items[1];var point=option.TranslatePoint(new Point(option.ActualWidth/2,option.ActualHeight/2),main);
                        Check(main.InputHitTest(point) is DependencyObject hit&&ItemsControl.ContainerFromElement(popup.Options,hit) is ListBoxItem,"Cloud list covered the dropdown options");
                    }
                    var item=(ListBoxItem)popup.Options.Items[i];popup.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=item});
                    Check(pair.model.SelectedIndex==i&&Equals(pair.button.Content,pair.expected[i]),"Cloud choice did not preserve its own selected value");popup.Close(true);
                }
            }
            main.ApplyLanguage();Check(sort.SelectedIndex==2&&diff.SelectedIndex==5,"Language refresh reset cloud choices");
            Check(localSort.SelectedIndex==si&&localFilter.SelectedIndex==fi&&localSort.Items.Cast<object>().SequenceEqual(originalLocalSort)&&localFilter.Items.Cast<object>().SequenceEqual(originalLocalFilter),"Cloud selection modified local options");
            diffButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(popup.IsOpen&&popup.Options.SelectedIndex==5,"Cloud reopen lost selection");
            if(output!=null)
            {
                ((TextBlock)main.FindName("LibHeader")).Text=Lang.S("nav.cloud");var elapsed=Stopwatch.StartNew();Until(()=>elapsed.ElapsedMilliseconds>=360);
                Capture(main,Path.Combine(output,$"cloud-choice-{theme}-{language}.png"));
            }
            Call(main,"SetCloudMode",false);Check(!popup.IsOpen,"Switching to local left cloud choices expanded");main.Close();
        }
        Console.WriteLine("CLOUD_CHOICE sort=3;difficulty=6;independent=True;languages=4;themes=3");
    }
    static void ChoiceLifecycle(string? output=null)
    {
        if(output!=null)Directory.CreateDirectory(output);
        var main=new MainWindow(true);main.Show();Pump();var target=(Button)main.FindName("FilterChoiceBtn");var model=(ComboBox)main.FindName("FilterCombo");var popup=(AnimatedChoicePopup)main.FindName("LibraryChoicePopup");
        for(int i=0;i<5;i++)
        {
            popup.Toggle(model,target);var elapsed=Stopwatch.StartNew();Until(()=>elapsed.ElapsedMilliseconds>=380);
            Check(popup.IsOpen&&target.IsAncestorOf(popup.Chrome)&&ReferenceEquals(PresentationSource.FromVisual(target),PresentationSource.FromVisual(popup.Chrome)),"Choices were implemented as a separate floating window instead of the trigger's visual tree");
            var option=(ListBoxItem)popup.Options.Items[1];var optionPoint=option.TranslatePoint(new Point(option.ActualWidth/2,option.ActualHeight/2),main);
            Check(main.InputHitTest(optionPoint) is DependencyObject hit&&ItemsControl.ContainerFromElement(popup.Options,hit) is ListBoxItem,"Expanded options were covered by the song list or could not receive input");
            if(output!=null&&i==0)Capture(main,Path.Combine(output,"native-before-move.png"));
            var before=popup.Chrome.PointToScreen(new Point());main.Left+=17;main.Top+=9;Pump();var after=popup.Chrome.PointToScreen(new Point());var anchor=target.PointToScreen(new Point());
            Check(after.X>before.X&&after.Y>before.Y&&Math.Abs(after.X-anchor.X)<2&&Math.Abs(after.Y-anchor.Y)<2,"Moving the window detached the choices");
            main.Width=main.Width==1200?1400:1200;Pump();Check(Math.Abs(popup.Chrome.ActualWidth-target.ActualWidth)<.1,"Resizing detached choice width");
            if(output!=null&&i==0)Capture(main,Path.Combine(output,"native-after-move-resize.png"));
            target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));elapsed.Restart();Until(()=>elapsed.ElapsedMilliseconds>=240);Check(!popup.IsOpen,"Header click did not finish closing");
        }
        popup.Toggle(model,target);Pump();var item=(ListBoxItem)popup.Options.Items[1];popup.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=item});Check(model.SelectedIndex==1,"Choice selection stopped working");main.Close();Check(!popup.IsOpen,"Owner exit retained choices");
        Console.WriteLine("CHOICE_LIFECYCLE same_tree=True;cycles=5;move=True;resize=True;selection=True");
    }
    static void JoinedChoice(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())
        {
            Theme.Apply(theme,persist:false);var main=new MainWindow(true);main.Show();Pump();
            var target=(Button)main.FindName("FilterChoiceBtn");var model=(ComboBox)main.FindName("FilterCombo");var popup=(AnimatedChoicePopup)main.FindName("LibraryChoicePopup");
            popup.Toggle(model,target);var time=Stopwatch.StartNew();Until(()=>time.ElapsedMilliseconds>=120);Pump();
            var clip=popup.Chrome.Clip as RectangleGeometry;Check(clip!=null&&Math.Abs(clip.Rect.Height-popup.Chrome.ActualHeight)<.1&&clip.RadiusX==10,"Animation lost rounded clipping");
            var a=target.PointToScreen(new Point());var b=popup.Chrome.PointToScreen(new Point());Check(Math.Abs(a.X-b.X)<2&&Math.Abs(a.Y-b.Y)<2,"Joined popup separated from its anchor");
            if(output!=null)Capture(popup.Chrome,Path.Combine(output,$"joined-mid-{theme}.png"));
            popup.Close();popup.Toggle(model,target);time.Restart();Until(()=>time.ElapsedMilliseconds>=400);
            Check(popup.IsOpen&&popup.Chrome.IsHitTestVisible,"Stale close hid a new joined popup");
            if(output!=null)Capture(popup.Chrome,Path.Combine(output,$"joined-open-{theme}.png"));
            popup.Close(true);popup.ReduceMotion=true;popup.Toggle(model,target);Pump();Check(!popup.Chrome.HasAnimatedProperties,"Reduced motion still animated");
            popup.Close();Check(!popup.IsOpen,"Reduced motion close was delayed");main.Close();
        }
        Console.WriteLine("JOINED_CHOICE clipping=True;anchor=True;reopen=True;reduced=True");
    }
    static void CursorKeys(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);Theme.Apply(theme,persist:false);
            var t=new ProjectTrack();t=t with{Notes=new(){new(){TrackId=t.Id,Key=1,Beat=1,Action=NoteAction.Hold,LengthBeats=2},new(){TrackId=t.Id,Key=3,Beat=1,Action=NoteAction.Hold,LengthBeats=1},new(){TrackId=t.Id,Key=1,Beat=2.5m,Action=NoteAction.Hold,LengthBeats=1.5m},new(){TrackId=t.Id,Key=4,Beat=4}}};
            var other=new ProjectTrack();other=other with{Notes=new(){new(){TrackId=other.Id,Key=5,Beat=1,Action=NoteAction.Hold,LengthBeats=5}}};
            var window=new EditorWindow(new(){Tracks=new(){t,other}}){AudioEnabled=false};int audio=0;window.KeyOutputFactory=_=>{audio++;return new Notes();};window.Show();Pump();
            string identity=PerformanceIdentity.ForProject(window.Session.Project);int history=window.Session.UndoCount;
            void At(decimal beat,params int[] expected)
            {
                window.Viewport.PlayheadBeat=beat;Call(window,"RefreshStatus");Pump();
                Check(window.Viewport.KeysAtPlayhead().SetEquals(expected),"Cursor note intervals were incorrect");
                var keys=(Button[])Field(window,"_keys")!;var accent=((SolidColorBrush)window.FindResource("Accent")).Color;
                Check(Enumerable.Range(0,15).All(k=>(((SolidColorBrush)keys[k].Background).Color==accent)==expected.Contains(k)),"Mapped keys did not follow cursor");
            }
            At(1.5m,1,3);if(output!=null)Capture(window,Path.Combine(output,$"cursor-keys-{theme}-{language}.png"));At(2,1);At(3.5m,1);At(4,4);At(4.25m);At(1.5m,1,3);
            Check(PerformanceIdentity.ForProject(window.Session.Project)==identity&&window.Session.UndoCount==history&&audio==0,"Cursor highlighting edited or auditioned notes");
            window.Session.Select(new[]{t.Notes[1].Id});window.Session.DeleteSelected();At(1.5m,1);window.Session.Undo();At(1.5m,1,3);
            Call(window,"SwitchTrack",other.Id);At(1.5m,5);window.Session.MarkSaved();window.Close();
        }
        Console.WriteLine("CURSOR_KEYS chord=True;hold=True;seek=True;undo=True;audio=0");
    }
    static void ChoiceLayout(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null,language);Theme.Apply(theme,persist:false);
            var main=new MainWindow(true);main.Show();Pump();
            foreach(double width in new[]{1080d,1400d})
            {
                main.Width=width;Pump();
                var search=(FrameworkElement)main.FindName("SearchBox");var left=(FrameworkElement)main.FindName("FilterChoiceBtn");var right=(FrameworkElement)main.FindName("SortChoiceBtn");
                Rect Bounds(FrameworkElement e)=>e.TransformToAncestor(search.Parent as Visual ?? main).TransformBounds(new Rect(e.RenderSize));
                var s=Bounds(search);var a=Bounds(left);var b=Bounds(right);
                Check(Math.Abs(s.Left-a.Left)<.1&&Math.Abs(s.Right-b.Right)<.1,"Choice row does not align with search bounds");
                Check(Math.Abs(left.ActualWidth-right.ActualWidth)<.1&&Math.Abs(right.TranslatePoint(new Point(),(UIElement)left.Parent).X-left.ActualWidth-12)<.1,"Choice widths or spacing changed");
                Check(Math.Abs(a.Top-s.Bottom-12)<.1,"Local vertical spacing does not match the horizontal gap");
                Check(search.ActualHeight==36&&left.ActualHeight==36&&right.ActualHeight==36,"Choice height does not match search");
            }
            if(output!=null)Capture(main,Path.Combine(output,$"choice-{theme}-{language}.png"));main.Close();
        }
        Console.WriteLine("CHOICE_LAYOUT themes=3;languages=4;widths=2;aligned=True");
    }
    static void ChoiceReuse()
    {
        foreach(var theme in Enum.GetValues<AppTheme>())
        {
            Theme.Apply(theme,persist:false);
            var button=new JoinedChoiceButton{Content="One",Width=140,Height=44,VerticalAlignment=VerticalAlignment.Top};var window=new Window{Content=button,Width=400,Height=300};window.Show();Pump();
            var model=new ComboBox{ItemsSource=new[]{"One","Two","Three"},SelectedIndex=0};var popup=new AnimatedChoicePopup();
            popup.Toggle(model,button);Pump();Check(popup.IsOpen&&popup.Options.Items.Count==3,"Standalone choices did not open");
            var item=(ListBoxItem)popup.Options.Items[1];
            popup.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonUpEvent,Source=item});
            Check(model.SelectedIndex==1&&Equals(button.Content,"Two"),"Standalone choice did not commit selection");
            popup.Toggle(model,button);Pump();Check(popup.IsOpen&&popup.Options.SelectedIndex==1,"Reopening lost selection");
            popup.Close();popup.Toggle(model,button);var elapsed=Stopwatch.StartNew();Until(()=>elapsed.ElapsedMilliseconds>=300);
            Check(popup.IsOpen&&popup.Chrome.IsHitTestVisible,"A stale close animation hid the reopened popup");
            window.Close();Pump();Check(!popup.IsOpen,"Owner close left the choice popup open");
        }
        Console.WriteLine("CHOICE_REUSE independent=True;selection=True;reopen=True;cleanup=True");
    }
    static void RegistrationRollback()
    {
        using var one=new HwndSource(new HwndSourceParameters("shortcut-one"){Width=1,Height=1,WindowStyle=0});
        using var two=new HwndSource(new HwndSourceParameters("shortcut-two"){Width=1,Height=1,WindowStyle=0});
        using var first=new ShortcutRegistrations(new MainWindow.Registrar(one.Handle));using var second=new ShortcutRegistrations(new MainWindow.Registrar(two.Handle));
        var chord=new ShortcutChord(0x86,ShortcutModifiers.Ctrl|ShortcutModifiers.Alt|ShortcutModifiers.Shift);
        var old=new FunctionShortcut(ShortcutAction.PlayStop,new(0x87,chord.Modifiers),ShortcutScope.Global);
        first.Apply(new[]{old with{Chord=chord}});second.Apply(new[]{old});
        try{second.Apply(new[]{old with{Chord=chord}});throw new InvalidOperationException("Duplicate native shortcut was accepted");}
        catch(System.ComponentModel.Win32Exception error)
        {
            Check(error.NativeErrorCode==1409,"Unexpected native registration error");
            Check(error.Message.Contains("error=1409")&&error.Message.Contains($"pid={Environment.ProcessId}")&&error.Message.Contains($"hwnd={two.Handle.ToInt64()}")&&error.Message.Contains("thread="),"Registration failure lost its context");
            Check(second.Registered.ContainsKey(old.Chord!.Value),"Native registration failure lost the previous binding");
            Console.WriteLine("NATIVE_REGISTRATION_FAILURE "+error.Message);
        }
        first.Dispose();second.Dispose();Check(first.Registered.Count==0&&second.Registered.Count==0,"Native registrations survived disposal");
        second.Apply(new[]{old with{Chord=chord}});Check(second.Registered.Count==1,"Disposed native shortcut was not released");second.Dispose();
        Console.WriteLine("REGISTRATION_ROLLBACK native_error=1409;previous=retained;released=True");
    }
    static void Capture(FrameworkElement element,string path)
    {
        element.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var nested in Descendants<T>(child))yield return nested;}
    }
    static void Windows(string? output)
    {
        if(output!=null)Directory.CreateDirectory(output);int renders=0;
        foreach(var theme in Enum.GetValues<AppTheme>())foreach(var language in Enum.GetValues<AppLang>())
        {
            typeof(Lang).GetField("<Current>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,language);Theme.Apply(theme,persist:false);
            var registrar=new Registrar();var observer=new Observer();var main=CreateMain(registrar,observer);
            var menu=((Button)main.FindName("KeyEditBtn")).ContextMenu!;
            var entry=(MenuItem)menu.Items[1];Check(!entry.IsEnabled&&entry.Header.ToString()==Lang.S("map.deferredTitle"),"Mapping menu was enabled or untranslated");
            Check(((MenuItem)menu.Items[0]).IsEnabled,"Ordinary shortcut settings were disabled");
            Check(!((MenuItem)((Button)main.FindName("PracticeBtn")).ContextMenu!.Items[0]).IsEnabled,"Practice context menu bypassed isolation");
            var preferences=new InputPreferences();preferences=preferences with{Functions=preferences.Functions.Select(s=>s.Action==ShortcutAction.PauseMapping?s with{Chord=new(0x78,ShortcutModifiers.Ctrl|ShortcutModifiers.Alt),Scope=ShortcutScope.AppForeground}:s).ToList()};
            var retained=preferences.Functions.Where(s=>!FeatureAvailability.ShortcutAvailable(s.Action)).ToArray();
            var settings=new InputSettingsWindow(preferences,SkyPlayer.DefaultVk,_=>true,_=>{});settings.Show();Pump();
            var rows=(StackPanel)Field(settings,"_rows")!;
            Check(rows.Children.Count==preferences.Functions.Count,"Shortcut rows disappeared");
            for(int i=0;i<preferences.Functions.Count;i++)
            {
                var row=(StackPanel)rows.Children[i];bool available=FeatureAvailability.ShortcutAvailable(preferences.Functions[i].Action);
                Check(row.IsEnabled==available,"Wrong shortcut row availability");
                Check(row.Children.OfType<Control>().All(c=>c.IsEnabled==available),"Disabled mapping row retained an active field");
            }
            Check(((TextBlock)Field(settings,"_status")!).Text.Contains(Lang.S("map.deferred")),"Missing retained-config explanation");
            Descendants<Button>(settings).Single(b=>Equals(b.Content,Lang.S("map.defaults"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(((InputPreferences)Field(settings,"_draft")!).Functions.Where(s=>!FeatureAvailability.ShortcutAvailable(s.Action)).SequenceEqual(retained),"Restoring active defaults overwrote deferred mapping settings");
            if(output!=null)
            {
                Capture(settings,Path.Combine(output,$"shortcuts-{theme}-{language}.png"));
                Descendants<ScrollViewer>(settings).Single(s=>ReferenceEquals(s.Content,rows)).ScrollToBottom();Pump();
                Capture(settings,Path.Combine(output,$"deferred-rows-{theme}-{language}.png"));
                menu.PlacementTarget=(Button)main.FindName("KeyEditBtn");menu.IsOpen=true;Pump();Capture(menu,Path.Combine(output,$"menu-{theme}-{language}.png"));menu.IsOpen=false;
            }
            renders+=2;settings.Close();main.Close();Pump();
        }
        Console.WriteLine($"VERSION_WINDOWS renders={renders};themes=3;languages=4;mapping_rows=disabled;settings=retained");
    }
    static void Manual(Application app,string[] args)
    {
        if(args.Length<2)throw new ArgumentException("Manual input observation requires an external output directory");
        string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Theme.Apply(AppTheme.Dark,persist:false);
        using var observer=new GlobalKeyboardObserver();var main=new MainWindow(true);int yDown=0,yUp=0,outputAttempts=0;var functionDown=new Dictionary<ushort,int>();var observations=new List<object>();
        var status=new TextBlock{Text="请切换到游戏，按实体 Y；再测试 F2、F3、F4。关闭此窗口结束。\n仅观察指定键，不捕获游戏画面，不发送自动演奏输入。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(20)};
        var panel=new Window{Title="v3.0 实体输入验证",Width=600,Height=220,Content=status};
        main.GameInputFactory=()=>{outputAttempts++;throw new InvalidOperationException("ManualVerificationCannotSendGameInput");};main.Show();Pump();main.InitializeInputRouting(observer:observer);
        var registrations=(ShortcutRegistrations)Field(main,"_registrations")!;
        int registered=registrations.Registered.Count;Check(registered==8,"Manual verification requires all eight default shortcuts to register first");
        void Record(bool closed)=>File.WriteAllText(Path.Combine(output,"physical-input.json"),JsonSerializer.Serialize(new{yDown,yUp,functionDown,registered,mapping=FeatureAvailability.SheetMappingEnabled,outputAttempts,closed,observations}));
        Record(false);
        observer.Key+=e=>
        {
            if(e.Key==0x59){if(e.Down)yDown++;else yUp++;}
            else if(e.Key is >=0x70 and <=0x75 && e.Down)functionDown[e.Key]=functionDown.GetValueOrDefault(e.Key)+1;
            else return;
            main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>
            {
                observations.Add(new{e.Key,e.Down,gameForeground=GameWindows.IsKnownGame(e.Foreground),speed=(double)Field(main,"_speed")!,paused=(bool)Field(main,"_practicePaused")!});
                status.Text=$"Y 按下 {yDown} / 松开 {yUp} · 倍速 {(double)Field(main,"_speed")!:0.0}x\nF1～F6 观察次数：{string.Join(", ",functionDown.OrderBy(p=>p.Key).Select(p=>$"F{p.Key-0x6F}={p.Value}"))}\n全局绑定 {registered}/8；映射禁用；自动演奏输入调用 {outputAttempts}。";Record(false);
            }));
        };
        panel.Closed+=(_,_)=>
        {
            main.Close();observer.Dispose();Check(registrations.Registered.Count==0,"Manual shortcut registrations survived closure");Record(true);app.Shutdown();
        };
        panel.Show();app.Run();
    }
}
