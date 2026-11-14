using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Reflection;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    static readonly List<object> Results = [];
    static string Output = "";
    static string ResultFile = "client-integration-results.json";
    static void Check(string id, bool ok)
    {
        Results.Add(new { id, status = ok ? "PASS" : "FAIL" });
        File.WriteAllText(Path.Combine(Output, ResultFile), JsonSerializer.Serialize(new { tests = Results }, NativeSharingClient.Json));
        if (!ok) throw new InvalidOperationException("Integration check failed: " + id);
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Output = Path.GetFullPath(args[0]);
            if (args.Contains("--updater-row")) { ResultFile = "updater-row-results.json"; UpdaterRowReview(); }
            else if (args.Contains("--updater-ui")) { ResultFile = "updater-ui-results.json"; UpdaterUiReview(); }
            else if (args.Contains("--cloud-catalog-queue")) { ResultFile = "catalog-ui-queue-results.json"; CloudCatalogQueueReview(); }
            else if (args.Contains("--cloud-catalog")) { ResultFile = "catalog-client-network-results.json"; CloudCatalogReview().GetAwaiter().GetResult(); }
            else if (args.Contains("--library-refresh")) { ResultFile = "library-refresh-results.json"; LibraryRefreshReview(); }
            else if (args.Contains("--metadata-edit-ui")) { ResultFile = "metadata-edit-ui-results.json"; MetadataEditingReview(); }
            else if (args.Contains("--metadata-edit-network")) { ResultFile = "metadata-edit-network-results.json"; MetadataEditingNetwork().GetAwaiter().GetResult(); }
            else if (args.Contains("--metadata-network")) { ResultFile = "metadata-network-results.json"; MetadataNetwork().GetAwaiter().GetResult(); }
            else if (args.Contains("--metadata-ui")) { ResultFile = "metadata-ui-results.json"; MetadataReview(); }
            else if (args.Contains("--import-ui")) { ResultFile = "import-unified/ui-results.json"; ImportReview(); }
            else if (args.Contains("--import-network")) { ResultFile = "import-unified/network-results.json"; ImportNetworkReview(); }
            else if (args.Contains("--share-menu")) { ResultFile = "unified-sharing/menu-results.json"; SharingMenuReview(); }
            else if (args.Contains("--unified-share")) { ResultFile = "unified-sharing/ui-results.json"; UnifiedSharingReview(); }
            else if (args.Contains("--login-render")) { ResultFile = "login-ui-results.json"; LoginReview(); }
            else if (args.Contains("--render")) { ResultFile = "gui-render-results.json"; Render(); }
            else if (args.Contains("--demo")) Demo();
            else if (args.Contains("--performance")) { ResultFile="performance-checks.json"; Performance().GetAwaiter().GetResult(); }
            else Network().GetAwaiter().GetResult();
            Console.WriteLine(JsonSerializer.Serialize(new { checks = Results.Count, status = "PASS" })); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); if (args.Contains("--unified-share") && error is TargetInvocationException { InnerException: { } inner }) Console.Error.WriteLine(inner); return 1; }
    }
    static HttpClientHandler Handler()
    {
        var ca = X509Certificate2.CreateFromPem(File.ReadAllText(Path.Combine(Output, "private/certs/ca.pem")));
        return new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                if (cert == null || request.RequestUri?.Host != "localhost" || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
                using var trusted = new X509Chain(); trusted.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                trusted.ChainPolicy.CustomTrustStore.Add(ca); trusted.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return trusted.Build(cert);
            }
        };
    }
    static SmapProject Sample()
    {
        var melody = new ProjectTrack { Name = "旋律 / Melody" };
        melody.Notes.Add(new() { TrackId = melody.Id, Key = 1, Beat = 0, LengthBeats = 4, Action = NoteAction.Hold });
        melody.Notes.Add(new() { TrackId = melody.Id, Key = 2, Beat = 1.0000000000000000000000000001m, Action = NoteAction.Tap });
        var harmony = new ProjectTrack { Name = "伴奏 / Harmony", Muted = true };
        harmony.Notes.Add(new() { TrackId = harmony.Id, Key = 5, Beat = 0, Action = NoteAction.Tap });
        return new() { Tracks = [melody, harmony], Metadata = new() { Name = "RC · 多轨长按验证", Author = "合成测试", TranscribedBy = "fixture" } };
    }
    static async Task<(CloudTransport Transport, NativeSharingClient Client)> Login(HttpMessageHandler? handler = null)
    {
        using var state = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "private/state.json")));
        if (!state.RootElement.GetProperty("database").GetString()!.StartsWith("smap_rc_")) throw new InvalidOperationException("Synthetic database required.");
        var uri = new Uri("https://localhost:" + state.RootElement.GetProperty("tlsPort").GetInt32() + "/");
        var transport = new CloudTransport(uri, handler ?? Handler());
        using var reply = await transport.SendSensitiveAsync("/api/game_login.php", new StringContent(JsonSerializer.Serialize(new { username = "s0_alpha", password = state.RootElement.GetProperty("loginPassword").GetString() }), Encoding.UTF8, "application/json"));
        using var body = JsonDocument.Parse(await reply.Content.ReadAsByteArrayAsync());
        string token = body.RootElement.GetProperty("user").GetProperty("device_token").GetString()!;
        return (transport, new NativeSharingClient(transport, () => token));
    }
    sealed class LosePublishResponse(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        bool _lost;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var response = await base.SendAsync(request, token);
            if (!_lost && request.RequestUri!.AbsolutePath.EndsWith("/publish.php") && response.IsSuccessStatusCode)
            { _lost = true; response.Dispose(); throw new HttpRequestException("Synthetic lost response after real commit."); }
            return response;
        }
    }
    static async Task Network()
    {
        var login = await Login(new LosePublishResponse(Handler())); using var transport = login.Transport;
        var client = login.Client; var source = Sample(); string originalIdentity = PerformanceIdentity.ForProject(source);
        var options = new ScoreExportOptions { TrackIds = [source.Tracks[0].Id] };
        var selected = ScoreExport.ProjectSelection(source, options); byte[] payload = SmapFile.ToBytes(selected);
        var directory = Path.Combine(Output, "private/client-workflow-" + Guid.NewGuid().ToString("N")); var store = new NativeSharingStore(directory);
        var draft = store.Stage(1, null, payload, selected.Metadata.Name, selected.Metadata.Author, "fixture", "真实隔离链路", "public", []);
        bool failed = false; try { await store.PublishAsync(client, draft, default); } catch (HttpRequestException) { failed = true; }
        Check("REAL-COMMIT-LOST-RESPONSE", failed && store.Pending(1)?.Work != null);
        var restarted = new NativeSharingStore(directory); var good = await Login(); using var goodTransport = good.Transport; client = good.Client;
        var published = await restarted.PublishAsync(client, restarted.Pending(1)!, default);
        Check("RESTART-IDEMPOTENT-RETRY", restarted.Pending(1) == null && published.VersionNumber == 1);
        var history = await client.HistoryAsync(published.WorkId);
        Check("RETRY-NO-DUPLICATE-VERSION", history.Items.Count == 1);
        var resolved = await client.ResolveAsync(published.FormattedCode.ToLowerInvariant());
        var downloaded = await client.DownloadAsync(resolved);
        Check("DOWNLOAD-EXACT-RAW-BYTES", downloaded.Payload.SequenceEqual(payload));
        Check("SELECTION-AND-HOLD-RESTORED", downloaded.Project.Tracks.Count == 1 && downloaded.Project.Tracks[0].Notes[0].LengthBeats == 4 && downloaded.Project.Tracks[0].Notes[1].Beat == selected.Tracks[0].Notes[1].Beat);
        Check("SOURCE-PROJECT-PRESERVED", PerformanceIdentity.ForProject(source) == originalIdentity && source.Tracks.Count == 2);
        var songs = Path.Combine(directory, "songs"); string imported = restarted.Import(downloaded, songs);
        Check("IMPORT-EDITABLE-NATIVE", PerformanceIdentity.ForProject(SmapFile.Load(imported)) == published.PerformanceHash);
        Check("REPEATED-IMPORT-NO-DUPLICATE", restarted.Import(downloaded, songs) == imported && Directory.GetFiles(songs, "*.smap").Length == 1);
        SmapFile.Save(downloaded.Project with { Metadata = downloaded.Project.Metadata with { Name = "Locally edited" } }, imported);
        string other = restarted.Import(downloaded, songs);
        Check("IMPORT-PRESERVES-LOCAL-EDIT", other != imported && SmapFile.Load(imported).Metadata.Name == "Locally edited");
        var second = await client.PublishAsync(await client.GetWorkAsync(published.WorkId), payload, "Title changed", "fixture", "fixture", "", "public", [], Guid.NewGuid());
        Check("TITLE-NOT-PERFORMANCE-IDENTITY", second.VersionNumber == 2 && second.PerformanceHash == published.PerformanceHash);
        Check("OLD-SHARE-CODE-FIXED", (await client.ResolveAsync(published.ShareCode)).VersionId == published.VersionId);
        var hidden = await client.SetAccessAsync(second, "private", "published", Guid.NewGuid());
        using var anonymousTransport = new CloudTransport(goodTransport.Origin, Handler()); var anonymous = new NativeSharingClient(anonymousTransport, () => null);
        bool rejected = false; try { await anonymous.ResolveAsync(hidden.ShareCode); } catch (SharingException error) when (error.Status == 404) { rejected = true; }
        Check("ANONYMOUS-PRIVATE-REJECTED", rejected);
        var restored = await client.SetAccessAsync(hidden, "public", "published", Guid.NewGuid());
        await client.SetLatestAsync(await client.GetWorkAsync(published.WorkId), published.VersionId, Guid.NewGuid());
        Check("LATEST-EXPLICIT-UPDATE", (await anonymous.GetWorkAsync(published.WorkId)).LatestVersionId == published.VersionId);
        var derived = await client.CreateWorkAsync(Guid.NewGuid());
        var adaptation = await client.PublishAsync(derived, payload, "Adaptation", "fixture", "fixture", "", "public", [new(published.VersionId)], Guid.NewGuid());
        var sources = await client.SourcesAsync(adaptation.VersionId);
        Check("SOURCE-SERVER-RESOLVED", sources.Count == 1 && sources[0].VersionId == published.VersionId && sources[0].Provenance == "server_resolved");
        Check("OWNED-WORKS-QUERY", (await client.MyWorksAsync()).Any(w => w.WorkId == published.WorkId));
        var imageDoc = ScoreExport.Prepare(downloaded.Project, new ScoreExportOptions { TrackIds = downloaded.Project.Tracks.Select(t => t.Id).ToArray() });
        Check("IMAGE-PAYLOAD-MATCHES-VERSION", Convert.ToHexStringLower(SHA256.HashData(imageDoc.Payload)) == published.PayloadSha256);
        File.WriteAllBytes(Path.Combine(Output, "private/ui-version.json"), JsonSerializer.SerializeToUtf8Bytes(published, NativeSharingClient.Json));
        File.WriteAllBytes(Path.Combine(Output, "private/ui-project.smap"), payload);
        Check("GAME-MAPPING-STILL-DISABLED", !FeatureAvailability.SheetMappingEnabled);
        var editor = new EditorSession(SmapFile.Load(other));int notesBefore=editor.ActiveTrack.Notes.Count;
        editor.Create(3,8);Check("IMPORTED-PROJECT-CONTINUES-EDITING",editor.ActiveTrack.Notes.Count==notesBefore+1&&editor.IsDirty);editor.Undo();
        Check("IMPORTED-EDIT-UNDO-PRESERVES-PERFORMANCE",PerformanceIdentity.ForProject(editor.Project)==published.PerformanceHash);
    }
    static void Pump()
    { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
    static void Language(AppLang value) => typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null, value);
    static void Palette(AppTheme value) => typeof(Theme).GetMethod("Apply", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, null, [typeof(AppTheme), typeof(bool)], null)!.Invoke(null, [value, false]);
    static void Render()
    {
        var login = Login().GetAwaiter().GetResult(); using var transport = login.Transport;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var owner = new Window { Width = 300, Height = 100, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000 }; owner.Show();
        var dir = Path.Combine(Output, "gui/wpf"); Directory.CreateDirectory(dir);
        var version = JsonSerializer.Deserialize<SharingVersion>(File.ReadAllBytes(Path.Combine(Output, "private/ui-version.json")), NativeSharingClient.Json)!;
        var project = SmapFile.Load(Path.Combine(Output, "private/ui-project.smap")); int renders = 0;
        foreach (AppTheme theme in Enum.GetValues<AppTheme>()) foreach (AppLang language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language);
            foreach (string page in new[] { "publish", "result", "code", "history", "sources", "error" })
            {
                var window = new NativeSharingWindow(owner, page == "publish" ? project : null, client: login.Client,
                    store: new NativeSharingStore(Path.Combine(Output,"private/ui-click-store")), user: new(() => true, () => 1, _ => true), importDirectory: Path.Combine(Output,"private/ui-click-songs"))
                    { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowInTaskbar = false };
                if (page == "result") window.ShowResult(version);
                window.Show(); Pump(); window.UpdateLayout();
                if (page is "history" or "sources")
                {
                    var method = typeof(NativeSharingWindow).GetMethod(page == "history" ? "ShowHistory" : "ShowSources", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    Wait((Task)method.Invoke(window, page == "history" ? [version.WorkId, null] : [version.VersionId])!);
                }
                if (page == "error") ((TextBlock)typeof(NativeSharingWindow).GetField("_status",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!).Text = NativeSharingWindow.ErrorText(new SharingException("VERSION_WITHDRAWN",410));
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));using (var file = File.Create(Path.Combine(dir, theme+"-"+language+"-"+page+".png"))) encoder.Save(file);
                Check("WPF-"+theme+"-"+language+"-"+page, window.ActualWidth >= 800 && window.ActualHeight > 600);
                if (theme == AppTheme.Dark && language == AppLang.ZhCN && page == "code")
                {
                    Descendants<TextBox>(window).Single().Text = version.FormattedCode;
                    Descendants<Button>(window).Single(b => b.Content as string == Lang.Sharing("resolve")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); WaitBusy(window);
                    Check("WPF-REAL-CODE-LOOKUP-CLICK", Descendants<TextBlock>(window).Any(b => b.Text == version.FormattedCode));
                    Descendants<Button>(window).Single(b => b.Content as string == Lang.Sharing("import")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitBusy(window);
                    Check("WPF-REAL-IMPORT-CLICK",window.Imported && File.Exists(window.ImportedFile));
                    var link = new NativeSharingStore(Path.Combine(Output,"private/ui-click-store")).Find(window.ImportedFile!,login.Client.Origin);
                    Check("WPF-IMPORTED-VERSION-LINK", link?.VersionId == version.VersionId);
                }
                if (theme == AppTheme.Dark && language == AppLang.ZhCN && page == "publish")
                {
                    Descendants<Button>(window).Single(b => b.Content as string == Lang.Sharing("confirm")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WaitBusy(window);
                    Check("WPF-REAL-PUBLISH-CLICK",Descendants<TextBlock>(window).Any(b => b.Text == Lang.Sharing("published")));
                }
                window.Close(); Pump(); renders++;
            }
        }
        Palette(AppTheme.Dark);Language(AppLang.ZhCN);
        foreach (ScoreOrientation orientation in Enum.GetValues<ScoreOrientation>()) foreach(ScoreSpacing spacing in Enum.GetValues<ScoreSpacing>())
        {
            var doc=ScoreExport.Prepare(project,new ScoreExportOptions{TrackIds=project.Tracks.Select(t=>t.Id).ToArray(),Orientation=orientation,Spacing=spacing});
            Check("IMAGE-EXACT-PAYLOAD-"+orientation+spacing,Convert.ToHexStringLower(SHA256.HashData(doc.Payload))==version.PayloadSha256);
            var shared=doc with{VerifiedShareCode=version.FormattedCode};var page=shared.Pages[0];
            var imageDir=Path.Combine(Output,"images");Directory.CreateDirectory(imageDir);
            string stem=Path.Combine(imageDir,"shared-"+orientation+"-"+spacing);File.WriteAllText(stem+".svg",ScoreImageExporter.Svg(shared,page));File.WriteAllBytes(stem+".png",ScoreImageExporter.Png(shared,page));
            Check("IMAGE-SVG-RECOVERY-"+orientation+spacing,PerformanceIdentity.ForProject(ScoreImageData.Read(stem+".svg"))==version.PerformanceHash);
            Check("IMAGE-PNG-RECOVERY-"+orientation+spacing,PerformanceIdentity.ForProject(ScoreImageData.Read(stem+".png"))==version.PerformanceHash);
            var visible=System.Xml.Linq.XDocument.Parse(File.ReadAllText(stem+".svg")).Descendants(System.Xml.Linq.XName.Get("text","http://www.w3.org/2000/svg")).Select(x=>x.Value).ToArray();
            Check("IMAGE-VISIBLE-CODE-NO-SHA-"+orientation+spacing,visible.Contains(version.FormattedCode)&&!visible.Any(t=>t.Contains(version.PerformanceHash)));
        }
        owner.Close(); app.Shutdown(); Console.WriteLine("Rendered " + renders + " native WPF views.");
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T item)yield return item;foreach(var item2 in Descendants<T>(child))yield return item2;}
    }
    static void Wait(Task task)
    {
        var until=DateTime.UtcNow.AddSeconds(40);while(!task.IsCompleted&&DateTime.UtcNow<until){Pump();Thread.Sleep(10);}task.GetAwaiter().GetResult();Pump();
    }
    static void WaitBusy(NativeSharingWindow window)
    {
        var field=typeof(NativeSharingWindow).GetField("_busy",BindingFlags.Instance|BindingFlags.NonPublic)!;var until=DateTime.UtcNow.AddSeconds(40);
        while((bool)field.GetValue(window)!&&DateTime.UtcNow<until){Pump();Thread.Sleep(10);}Pump();
    }
    static void Demo()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Palette(AppTheme.Dark); Language(AppLang.ZhCN);
        var logged = Login().GetAwaiter().GetResult(); using var transport = logged.Transport;
        var owner = new Window { Width = 300, Height = 100, ShowInTaskbar = false }; owner.Show();
        var window = new NativeSharingWindow(owner, Sample(), client: logged.Client,
            store: new NativeSharingStore(Path.Combine(Output, "private/manual-demo")), user: new(() => true, () => 1, _ => true),importDirectory:Path.Combine(Output,"private/manual-demo-songs"));
        window.ShowDialog(); owner.Close(); app.Shutdown();
    }
    static async Task Performance()
    {
        var started=System.Diagnostics.Stopwatch.StartNew();
        var tracks=new List<ProjectTrack>();
        for(int track=0;track<4;track++)
        {
            var t=new ProjectTrack{Name="Track "+track};
            for(int i=0;i<5000;i++)t.Notes.Add(new(){TrackId=t.Id,Key=i%15,Beat=i/4m,Action=i%20==0?NoteAction.Hold:NoteAction.Tap,LengthBeats=i%20==0?2:0});
            tracks.Add(t);
        }
        var p=new SmapProject{Tracks=tracks,Metadata=new(){Name="RC 20,000 notes",Author="Synthetic fixture"}};
        byte[] bytes=SmapFile.ToBytes(p);long serializationMs=started.ElapsedMilliseconds;
        var login=await Login();using var transport=login.Transport;var work=await login.Client.CreateWorkAsync(Guid.NewGuid());
        started.Restart();var version=await login.Client.PublishAsync(work,bytes,p.Metadata.Name,"fixture","fixture","large synthetic","public",[],Guid.NewGuid());long publishMs=started.ElapsedMilliseconds;
        started.Restart();var download=await login.Client.DownloadAsync(version);long downloadMs=started.ElapsedMilliseconds;
        Check("LARGE-MULTITRACK-NOTE-COUNT",download.Project.Tracks.Count==4&&download.Project.Tracks.Sum(t=>t.Notes.Count)==20000);
        Check("LARGE-PAYLOAD-EXACT",download.Payload.SequenceEqual(bytes));
        var values=new{notes=20000,tracks=4,payloadBytes=bytes.Length,serializeMilliseconds=serializationMs,publishMilliseconds=publishMs,downloadAndValidateMilliseconds=downloadMs,
            clientPeakWorkingSetBytes=System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64,scope="One isolated Windows request; not production capacity or Linux peak memory"};
        File.WriteAllText(Path.Combine(Output,"performance-measurements.json"),JsonSerializer.Serialize(values,NativeSharingClient.Json));
    }
}
