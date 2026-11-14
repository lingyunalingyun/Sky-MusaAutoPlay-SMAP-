using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    sealed class SharingRequestAudit(HttpMessageHandler inner, bool holdFirstMatch = false) : DelegatingHandler(inner)
    {
        public int Writes, Matches;
        public bool LookupCancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.StartsWith("/api/smap/v1/")) Writes++;
            if (path.EndsWith("/matches.php") && ++Matches == 1 && holdFirstMatch)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { LookupCancelled = true;throw; }
            }
            return await base.SendAsync(request, cancellationToken);
        }
    }
    sealed class SharingRouteMissing : HttpMessageHandler
    {
        public int Writes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post) Writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            { Content = new StringContent("{\"success\":false,\"code\":\"not_found\"}", Encoding.UTF8, "application/json") });
        }
    }
    static T PrivateField<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    static void WaitUntil(Func<bool> ready)
    { var end = DateTime.UtcNow.AddSeconds(40);while (!ready() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(10); }Pump();if (!ready()) throw new TimeoutException("Unified sharing UI did not settle."); }

    static void UnifiedSharingReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark);Language(AppLang.ZhCN);
        var owner = new Window { Width = 200, Height = 100, Left = -12000, Top = -12000, ShowInTaskbar = false };owner.Show();
        var output = Path.Combine(Output, "unified-sharing/actual");Directory.CreateDirectory(output);
        void Capture(Window window, string name)
        {
            if (window is NativeSharingWindow sharing)
            {
                bool offline = PrivateField<SharingModeTabs>(sharing,"_modes").SelectedIndex==1;
                WaitUntil(()=>PrivateField<Grid>(sharing,offline?"_onlineHost":"_offlineHost").Visibility==Visibility.Collapsed);
            }
            Pump();window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file = File.Create(Path.Combine(output, name + ".png"));encoder.Save(file);
        }
        NativeSharingWindow Make(SmapProject project, NativeSharingClient client, string directory)
        {
            var window = new NativeSharingWindow(owner, project, client: client, store: new NativeSharingStore(directory),
                user: new(() => true, () => 1, _ => throw new InvalidOperationException("UI fixture must not prompt real credentials")), importDirectory: Path.Combine(directory,"songs"))
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowInTaskbar = false };
            window.Show();Pump();return window;
        }
        var audit = new SharingRequestAudit(Handler());var login=Login(audit);Wait(login);var logged = login.Result;using var transport = logged.Transport;
        var project = Sample(); project.Tracks[0].Notes.Add(new() { TrackId=project.Tracks[0].Id,Key=6,Beat=2m+(decimal)Random.Shared.NextDouble()/10,Action=NoteAction.Tap });
        string directory = Path.Combine(Output,"private/unified-ui-"+Guid.NewGuid().ToString("N"));
        var window = Make(project,logged.Client,directory);WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        var publish = Descendants<Button>(window).Single(b=>b.Content as string==Lang.Sharing("uploadShare"));
        Check("NATIVE-NO-MATCH-ALLOWS-EXPLICIT-UPLOAD",publish.IsEnabled&&audit.Writes==0);
        var title = Descendants<TextBox>(window).First();title.Text="Unified sharing fixture";
        var tabs = PrivateField<SharingModeTabs>(window,"_modes");double height=window.ActualHeight;
        tabs.Select(1);
        var motion = new List<object>();bool sawIntermediate=false;var clock=System.Diagnostics.Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<320){Pump();double x=PrivateField<TranslateTransform>(tabs,"_offset").X;motion.Add(new{ms=clock.ElapsedMilliseconds,x});sawIntermediate|=x>0&&x<132;Thread.Sleep(10);}
        File.WriteAllText(Path.Combine(Output,"unified-sharing/native-motion-samples.json"),System.Text.Json.JsonSerializer.Serialize(motion));
        Check("NATIVE-SLIDING-BACKGROUND-MOVES",!SystemParameters.ClientAreaAnimation||sawIntermediate);
        WaitUntil(()=>PrivateField<ScoreExportPane>(window,"_offlinePane").IsVisible);
        var pane = PrivateField<ScoreExportPane>(window,"_offlinePane");
        Check("OFFLINE-EMBEDDED-SAME-WINDOW",ReferenceEquals(Window.GetWindow(pane),window)&&Application.Current.Windows.Count==2);
        int readsBefore=audit.Matches;
        var prepare = (Task)typeof(ScoreExportPane).GetMethod("Prepare",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pane,null)!;Wait(prepare);
        var document = PrivateField<ScoreExportDocument>(pane,"_document");
        Check("OFFLINE-PREVIEW-NO-NETWORK",document.Pages.Count>0&&audit.Matches==readsBefore&&audit.Writes==0);
        string files = Path.Combine(Output,"unified-sharing/offline-files");Directory.CreateDirectory(files);
        string exported = ScoreImageExporter.Export(document,files,ScoreImageFormats.Both);
        string svg=Directory.GetFiles(exported,"*.svg").First(),png=Directory.GetFiles(exported,"*.png").First();
        Check("OFFLINE-SVG-RECOVERY",PerformanceIdentity.ForProject(ScoreImageData.Read(svg))==document.PerformanceHash);
        Check("OFFLINE-PNG-RECOVERY",PerformanceIdentity.ForProject(ScoreImageData.Read(png))==document.PerformanceHash);
        Capture(window,"offline-preview");tabs.Select(0);WaitUntil(()=>PrivateField<Grid>(window,"_onlineHost").IsVisible);
        Check("TAB-KEEPS-DRAFT-AND-WINDOW-SIZE",title.Text=="Unified sharing fixture"&&window.ActualHeight==height&&ReferenceEquals(pane,PrivateField<ScoreExportPane>(window,"_offlinePane")));
        Capture(window,"online-new");publish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("EXPLICIT-PUBLISH-REAL-SERVER",audit.Writes==2&&PrivateField<TextBlock>(window,"_status").Text==Lang.Sharing("published"));
        string code=Descendants<TextBlock>(window).Select(t=>t.Text).Single(t=>Regex.IsMatch(t,"^[0123456789ABCDEFGHJKMNPQRSTVWXYZ]{4}(?:-[0123456789ABCDEFGHJKMNPQRSTVWXYZ]{4}){2}$"));
        var resolve=logged.Client.ResolveAsync(code);Wait(resolve);var version=resolve.Result;
        Check("PUBLISHED-CODE-REAL-VERSION",version.Title=="Unified sharing fixture");window.Close();Pump();

        int writes=audit.Writes;window=Make(project,logged.Client,Path.Combine(directory,"reuse"));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("EXISTING-CONTENT-NO-SECOND-UPLOAD",audit.Writes==writes&&Descendants<TextBlock>(window).Any(t=>t.Text==Lang.Sharing("cloudFound"))&&Descendants<TextBlock>(window).Any(t=>t.Text==version.FormattedCode));
        Check("MATCH-SHOWS-REAL-PUBLISHER",Descendants<TextBlock>(window).Any(t=>t.Text.Contains("s0_alpha")));
        Capture(window,"online-existing");window.Close();Pump();

        foreach(AppTheme theme in Enum.GetValues<AppTheme>()) foreach(AppLang language in Enum.GetValues<AppLang>())
        {
            Palette(theme);Language(language);window=Make(project,logged.Client,Path.Combine(directory,theme+"-"+language));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
            Capture(window,theme+"-"+language+"-online");tabs=PrivateField<SharingModeTabs>(window,"_modes");tabs.Select(1);Pump();
            Capture(window,theme+"-"+language+"-offline");
            Check("UNIFIED-THEME-LANGUAGE-"+theme+language,window.Title==Lang.Sharing("title")&&PrivateField<ScoreExportPane>(window,"_offlinePane").ActualWidth>500&&window.ActualHeight==height);
            tabs.Select(0);tabs.Select(1);tabs.Select(0);tabs.Select(1);Pump();
            WaitUntil(()=>Math.Abs(PrivateField<TranslateTransform>(tabs,"_offset").X-132)<.1);
            Check("RAPID-TAB-SELECTION-"+theme+language,tabs.SelectedIndex==1&&window.ActualHeight==height);window.Close();Pump();
        }
        Palette(AppTheme.Dark);Language(AppLang.ZhCN);
        var missing=new SharingRouteMissing();using var unavailableTransport=new CloudTransport(new Uri("https://missing.example.invalid/"),missing);
        window=Make(project,new NativeSharingClient(unavailableTransport,()=>null),Path.Combine(directory,"missing"));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("MISSING-API-NOT-MISREAD-AS-NO-MATCH",missing.Writes==0&&PrivateField<TextBlock>(window,"_status").Text==Lang.Sharing("apiUnavailable")&&!Descendants<Button>(window).Single(b=>b.Content as string==Lang.Sharing("uploadShare")).IsEnabled);
        tabs=PrivateField<SharingModeTabs>(window,"_modes");tabs.Select(1);Pump();Check("MISSING-API-OFFLINE-STILL-AVAILABLE",PrivateField<ScoreExportPane>(window,"_offlinePane").IsVisible);Capture(window,"unavailable-offline");window.Close();Pump();

        var delayedAudit=new SharingRequestAudit(Handler(),true);var delayedTask=Login(delayedAudit);Wait(delayedTask);var delayedLogin=delayedTask.Result;using var delayedTransport=delayedLogin.Transport;
        window=Make(project,delayedLogin.Client,Path.Combine(directory,"cancel"));WaitUntil(()=>delayedAudit.Matches==1);
        PrivateField<SharingModeTabs>(window,"_modes").Select(1);WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("SWITCH-OFFLINE-CANCELS-LOOKUP",delayedAudit.LookupCancelled&&delayedAudit.Writes==0&&PrivateField<ScoreExportPane>(window,"_offlinePane").IsVisible);window.Close();Pump();
        var legacy = project with { Tracks = [project.Tracks[0] with { LegacyFields = new System.Text.Json.Nodes.JsonObject(), Notes = project.Tracks[0].Notes.Select(n=>n with { Beat=n.Beat+64 }).ToList() }] };
        writes=audit.Writes;window=Make(legacy,logged.Client,Path.Combine(directory,"legacy"));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("LEGACY-UNCHECKED-CATALOG-NO-DUPLICATE-UPLOAD",PrivateField<TextBlock>(window,"_status").Text==Lang.Sharing("legacyPending")&&audit.Writes==writes&&!Descendants<Button>(window).Single(b=>b.Content as string==Lang.Sharing("uploadShare")).IsEnabled);
        Capture(window,"legacy-pending");window.Close();Pump();
        var main=(MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(bool)],null)!.Invoke([true]);
        foreach(string menuName in new[]{"BuildLibraryMenu","BuildPlaylistMenu"})
        {
            var menu=(ContextMenu)typeof(MainWindow).GetMethod(menuName,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(main,[new SongInfo{File="synthetic.txt",Name="Synthetic"}])!;
            var labels=menu.Items.OfType<MenuItem>().Select(i=>i.Header as string).ToArray();
            Check("SINGLE-SHARE-ENTRY-"+menuName,labels.Count(t=>t==Lang.Sharing("publish"))==1&&!labels.Contains(Lang.S("score.title"))&&!labels.Contains(Lang.S("m.uploadCloud")));
        }
        main.Close();Pump();
        owner.Close();app.Shutdown();
    }
}
