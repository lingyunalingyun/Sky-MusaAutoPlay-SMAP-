using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    sealed class ImportReply(bool delay = false, bool html = false) : HttpMessageHandler
    {
        public int Requests;
        public bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (delay)
            {
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            return new HttpResponseMessage(html ? HttpStatusCode.BadGateway : HttpStatusCode.NotFound)
            { Content = new StringContent(html ? "<html>gateway error</html>" : "{\"success\":false,\"code\":\"not_found\"}", Encoding.UTF8, html ? "text/html" : "application/json") };
        }
    }
    static MainWindow ImportOwner()
    {
        var owner = (MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null)!.Invoke([true]);
        owner.WindowStartupLocation = WindowStartupLocation.Manual; owner.Left = -12000; owner.Top = -12000; owner.ShowInTaskbar = false; owner.Show(); Pump(); return owner;
    }
    static ImportDialog ImportWindow(Window owner, NativeSharingClient client, string directory, Func<string[], Window, string>? import = null, Func<int, string[]?>? files = null)
    {
        var window = new ImportDialog(owner, import ?? ((_, _) => "synthetic import"), client,
            new NativeSharingStore(Path.Combine(directory, "links")), Path.Combine(directory, "songs"), files)
        { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowInTaskbar = false };
        window.Show(); Pump(); return window;
    }
    static void CaptureImport(ImportDialog window, string name)
    {
        var views = PrivateField<List<FrameworkElement>>(window, "_views"); int mode = PrivateField<SharingModeTabs>(window, "_tabs").SelectedIndex;
        WaitUntil(() => views.Where((_, i) => i != mode).All(v => v.Visibility == Visibility.Collapsed));
        window.UpdateLayout();var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));var path = Path.Combine(Output, "import-unified/actual", name + ".png"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);encoder.Save(file);
    }
    static void ImportReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark); Language(AppLang.ZhCN); var owner = ImportOwner();
        Check("IMPORT-REMOVES-SEPARATE-CODE-ENTRY",owner.FindName("NativeCodeButton")==null);
        var response = new ImportReply(); using var transport = new CloudTransport(new Uri("https://missing.example.invalid/"), response);var client = new NativeSharingClient(transport, () => null);
        string dir = Path.Combine(Output,"private/import-ui-"+Guid.NewGuid().ToString("N"));
        foreach (AppTheme theme in Enum.GetValues<AppTheme>()) foreach (AppLang language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language); int calls = 0; Window? callbackOwner = null; string[]? selection = null;
            var window = ImportWindow(owner, client, dir, (files, w) => { calls++; callbackOwner = w; return "synthetic imported"; }, _ => selection);
            var tabs = PrivateField<SharingModeTabs>(window,"_tabs");var code = PrivateField<TextBox>(window,"_code");code.Text="8J1H-JZRR-GCP5";
            double height=window.ActualHeight;
            foreach(int mode in Enumerable.Range(0,4))
            {
                tabs.Select(mode); CaptureImport(window,theme+"-"+language+"-"+mode);
                var buttons=Descendants<Button>(tabs).ToArray();
                Check("IMPORT-THEME-LANGUAGE-"+theme+language+mode,window.Title==Lang.Import("title")&&buttons.Length==4&&buttons.All(b=>Descendants<TextBlock>(b).All(t=>t.ActualWidth<=b.ActualWidth))&&window.ActualHeight==height);
                if(mode==0)continue;
                selection=[mode switch{1=>"synthetic.smap",2=>"synthetic.json",_=>"synthetic.mid"}];
                PrivateField<Button>(window,"_action").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("IMPORT-FILE-ROUTING-"+theme+language+mode,calls==mode&&ReferenceEquals(callbackOwner,window)&&PrivateField<TextBlock>(window,"_status").Text=="synthetic imported");
            }
            tabs.Select(0);tabs.Select(3);tabs.Select(1);tabs.Select(2);Pump();
            WaitUntil(()=>Math.Abs(PrivateField<TranslateTransform>(tabs,"_offset").X-2*(tabs.ActualWidth-6)/4)<.1);
            Check("IMPORT-RAPID-SWITCH-KEEPS-CODE-"+theme+language,tabs.SelectedIndex==2&&code.Text=="8J1H-JZRR-GCP5"&&window.ActualHeight==height);
            window.Close();Pump();
        }
        Check("IMPORT-OFFLINE-AND-STARTUP-NO-NETWORK",response.Requests==0);
        Palette(AppTheme.Dark);Language(AppLang.ZhCN);
        int imports=0;string[]? picked=["wrong.mid"];
        var checkWindow=ImportWindow(owner,client,dir,(_,_)=>{imports++;return "imported";},_=>picked);
        var checkTabs=PrivateField<SharingModeTabs>(checkWindow,"_tabs");checkTabs.Select(1);var action=PrivateField<Button>(checkWindow,"_action");action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("IMPORT-WRONG-TAB-FORMAT-NO-WRITE",imports==0&&PrivateField<TextBlock>(checkWindow,"_status").Text==Lang.Import("wrongFormat"));
        picked=null;action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("IMPORT-FILE-CANCEL-NO-WRITE",imports==0);
        checkTabs.Select(0);PrivateField<TextBox>(checkWindow,"_code").Text="invalid";action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(checkWindow,"_busy"));
        Check("IMPORT-INVALID-CODE-LOCAL-REJECTION",response.Requests==0&&!checkWindow.Imported);
        PrivateField<TextBox>(checkWindow,"_code").Text="8J1H-JZRR-GCP5";action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(checkWindow,"_busy"));
        Check("IMPORT-MISSING-API-NO-WRITE",response.Requests==1&&PrivateField<TextBlock>(checkWindow,"_status").Text==Lang.Sharing("apiUnavailable")&&!checkWindow.Imported);
        checkWindow.Close();Pump();
        var waiting=new ImportReply(true);using var waitTransport=new CloudTransport(new Uri("https://missing.example.invalid/"),waiting);
        checkWindow=ImportWindow(owner,new NativeSharingClient(waitTransport,()=>null),dir);
        PrivateField<TextBox>(checkWindow,"_code").Text="8J1H-JZRR-GCP5";action=PrivateField<Button>(checkWindow,"_action");action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WaitUntil(()=>waiting.Requests==1);PrivateField<SharingModeTabs>(checkWindow,"_tabs").Select(1);WaitUntil(()=>!PrivateField<bool>(checkWindow,"_busy"));
        Check("IMPORT-SWITCH-CANCELS-ONE-REQUEST",waiting.Cancelled&&waiting.Requests==1&&PrivateField<TextBlock>(checkWindow,"_status").Text==Lang.Import("offlineStatus"));
        PrivateField<SharingModeTabs>(checkWindow,"_tabs").Select(0);action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>waiting.Requests==2);checkWindow.Close();Pump();
        Check("IMPORT-CLOSE-CANCELS-REQUEST",waiting.Cancelled&&!checkWindow.Imported);
        var gateway=new ImportReply(html:true);using var htmlTransport=new CloudTransport(new Uri("https://missing.example.invalid/"),gateway);
        checkWindow=ImportWindow(owner,new NativeSharingClient(htmlTransport,()=>null),dir);PrivateField<TextBox>(checkWindow,"_code").Text="8J1H-JZRR-GCP5";
        PrivateField<Button>(checkWindow,"_action").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(checkWindow,"_busy"));
        Check("IMPORT-HTML-GATEWAY-SAFE-FAILURE",!checkWindow.Imported&&PrivateField<SharingVersion?>(checkWindow,"_version")==null);checkWindow.Close();
        string previous = SongLibrary.SongsDir;
        try
        {
            string inputs=Path.Combine(dir,"inputs");Directory.CreateDirectory(inputs);SongLibrary.SongsDir=Path.Combine(dir,"actual-library");
            var project=Sample();string native=Path.Combine(inputs,"project.smap");SmapFile.Save(project,native);
            var doc=ScoreExport.Prepare(project,new ScoreExportOptions{TrackIds=project.Tracks.Select(t=>t.Id).ToArray()});
            string images=ScoreImageExporter.Export(doc,inputs,ScoreImageFormats.Both);
            string[] recovered=[native,Directory.GetFiles(images,"*.svg").First(),Directory.GetFiles(images,"*.png").First()];
            var method=typeof(MainWindow).GetMethod("ImportFiles",BindingFlags.NonPublic|BindingFlags.Instance)!;
            string[]? choices=recovered;
            checkWindow=ImportWindow(owner,client,dir,(files,w)=>(string)method.Invoke(owner,[files,w])!,_=>choices);
            PrivateField<SharingModeTabs>(checkWindow,"_tabs").Select(1);PrivateField<Button>(checkWindow,"_action").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            var nativeFiles=Directory.GetFiles(SongLibrary.SongsDir,"*.smap");
            Check("IMPORT-REAL-SMAP-SVG-PNG-TO-LIBRARY",nativeFiles.Length==3&&nativeFiles.All(f=>PerformanceIdentity.ForProject(SmapFile.Load(f))==(Path.GetFileName(f)=="project.smap"?PerformanceIdentity.ForProject(project):doc.PerformanceHash)));
            var track=new ProjectTrack();track.Notes.Add(new(){TrackId=track.Id,Key=1,Beat=0,Action=NoteAction.Tap});var oldProject=new SmapProject{Tracks=[track]};
            var txt=Path.Combine(inputs,"old.txt");var json=Path.Combine(inputs,"old.json");LegacyProjectAdapter.SaveLegacy(oldProject,txt);LegacyProjectAdapter.SaveLegacy(oldProject,json);
            choices=[txt,json];PrivateField<SharingModeTabs>(checkWindow,"_tabs").Select(2);PrivateField<Button>(checkWindow,"_action").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
            Check("IMPORT-REAL-LEGACY-READABLE-LIBRARY",SongLibrary.Scan().Count==5&&File.ReadAllBytes(txt).SequenceEqual(File.ReadAllBytes(Path.Combine(SongLibrary.SongsDir,"old.txt")))&&File.ReadAllBytes(json).SequenceEqual(File.ReadAllBytes(Path.Combine(SongLibrary.SongsDir,"old.json"))));
            checkWindow.Close();
        }
        finally { SongLibrary.SongsDir=previous; }
        owner.Close();app.Shutdown();
    }
    static void ImportNetworkReview()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark);Language(AppLang.ZhCN);var owner=ImportOwner();
        var login=Login();Wait(login);var logged=login.Result;using var transport=logged.Transport;
        var worksTask=logged.Client.MyWorksAsync();Wait(worksTask);var work=worksTask.Result.First(w=>w.LatestVersionId!=null);
        var versionTask=logged.Client.VersionAsync(work.LatestVersionId!.Value);Wait(versionTask);var version=versionTask.Result;
        var directory=Path.Combine(Output,"private/import-network-"+Guid.NewGuid().ToString("N"));var window=ImportWindow(owner,logged.Client,directory);
        var code=PrivateField<TextBox>(window,"_code");code.Text=version.FormattedCode.ToLowerInvariant();var action=PrivateField<Button>(window,"_action");
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("IMPORT-REAL-TLS-RESOLVE-PREVIEW",PrivateField<SharingVersion>(window,"_version").VersionId==version.VersionId&&!Directory.Exists(Path.Combine(directory,"songs")));
        CaptureImport(window,"real-online-preview");action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("IMPORT-REAL-TLS-DOWNLOAD",window.Imported&&PrivateField<TextBlock>(window,"_status").Text==Lang.Sharing("imported"));
        var files=Directory.GetFiles(Path.Combine(directory,"songs"),"*.smap");var project=SmapFile.Load(files.Single());
        Check("IMPORT-REAL-PROJECT-RECOVERY",PerformanceIdentity.ForProject(project)==version.PerformanceHash);
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitUntil(()=>!PrivateField<bool>(window,"_busy"));
        Check("IMPORT-REPEAT-NO-DUPLICATE",Directory.GetFiles(Path.Combine(directory,"songs"),"*.smap").Length==1);
        PrivateField<Button>(window,"_back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        Check("IMPORT-BACK-TO-CODE-RETAINS-INPUT",PrivateField<SharingVersion?>(window,"_version")==null&&code.IsVisible&&code.Text==version.FormattedCode.ToLowerInvariant());
        window.Close();owner.Close();app.Shutdown();
    }
}
