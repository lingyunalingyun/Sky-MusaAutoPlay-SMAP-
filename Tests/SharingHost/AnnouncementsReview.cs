using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Announcements;

static partial class Program
{
    sealed class AnnouncementReplies : HttpMessageHandler
    {
        public bool Fail;
        public static Announcement Item(long id) => new(id, 1, id <= 2 ? new string('曲', 160) : "Synthetic announcement " + id,
            id == 2 ? new string('S', 240) : "Synthetic summary · 公告摘要", id <= 2, 1700000000 + id);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            if (request.Headers.Authorization != null || request.Headers.Contains("Cookie")) throw new InvalidOperationException("Public announcements must not carry credentials.");
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            object Summary(Announcement a) => new { id = a.Id, revision = a.Revision, title = a.Title, summary = a.Summary, important = a.Important, publishedAt = a.PublishedAt };
            string query = request.RequestUri!.Query;
            if (query.StartsWith("?id="))
            {
                long id = long.Parse(query[4..], System.Globalization.CultureInfo.InvariantCulture);
                if (id == 99999) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                var a = Item(id);
                return Task.FromResult(AnnouncementJson(new { schemaVersion = 1, announcement = new { id = a.Id, revision = 1, title = a.Title, summary = a.Summary, important = a.Important, publishedAt = a.PublishedAt,
                    content = "<script>plain text only</script>\n" + string.Join("\n", Enumerable.Repeat("长正文验证 / Long announcement body. Local features remain available.", 85)) } }));
            }
            int page = query.Contains("page=2") ? 2 : 1;
            return Task.FromResult(AnnouncementJson(new { schemaVersion = 1, items = Enumerable.Range(page == 1 ? 1 : 21, page == 1 ? 20 : 2).Select(i => Summary(Item(i))), page, pageSize = 20, total = 22 }));
        }
    }
    static HttpResponseMessage AnnouncementJson(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    static void AnnouncementsUiReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Await(Task task)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew(); while (!task.IsCompleted && clock.ElapsedMilliseconds < 10000) Pump();
            if (!task.IsCompleted) throw new TimeoutException("Announcement UI task timed out."); task.GetAwaiter().GetResult(); Pump();
        }
        void Until(Func<bool> test)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew(); while (!test() && clock.ElapsedMilliseconds < 10000) Pump();
            if (!test()) throw new TimeoutException("Announcement UI condition timed out.");
        }
        string images = Path.Combine(Output, "announcement-ui"); Directory.CreateDirectory(images);
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
        string[] files = ["theme.txt", "lang.txt", "nowplaying.txt", "prefs.txt", "playlist.txt", "playmode.txt", "update-channel.txt", "announcements-v1.json"];
        string Hash(string f) => File.Exists(Path.Combine(profile, f)) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(profile, f)))) : "absent";
        var before = files.ToDictionary(x => x, Hash);
        foreach (var theme in Enum.GetValues<AppTheme>()) foreach (var language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language); string key = theme + "-" + language;
            var main = (MainWindow)typeof(MainWindow).GetConstructor(flags, null, [typeof(bool)], null)!.Invoke([true]);
            main.WindowStartupLocation = WindowStartupLocation.Manual; main.Left = -12000; main.Top = -12000; main.ShowActivated = false; main.ShowInTaskbar = false;
            typeof(MainWindow).GetField("_importantOffered", flags)!.SetValue(main, true); main.Show(); Pump();
            var service = (AnnouncementService)typeof(MainWindow).GetField("_announcements", flags)!.GetValue(main)!;
            var network = new AnnouncementReplies(); var store = new AnnouncementReadStore(Path.Combine(Output, "private", "announcement-ui-" + Guid.NewGuid() + ".json"));
            ((AnnouncementClient)typeof(AnnouncementService).GetField("_client", flags)!.GetValue(service)!).Dispose();
            typeof(AnnouncementService).GetField("_client", flags)!.SetValue(service, new AnnouncementClient(CloudTransport.ApiOrigin, network));
            typeof(AnnouncementService).GetField("_store", flags)!.SetValue(service, store);
            Await(service.Refresh());
            Check("ANNOUNCEMENT-ENTRY-AND-UNREAD-" + key, ((TextBlock)main.FindName("AnnouncementsLabel")).Text == Lang.Announcement("title") && ((G2Border)main.FindName("AnnouncementUnreadDot")).Visibility == Visibility.Visible
                && ((Grid)main.FindName("SettingsPanel")).IsAncestorOf((Button)main.FindName("AnnouncementsBtn")));
            var window = new AnnouncementWindow(main, service) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowActivated = false, ShowInTaskbar = false };
            window.Show(); Until(() => store.IsRead(AnnouncementReplies.Item(1))); Pump();
            var list = (ListBox)typeof(AnnouncementWindow).GetField("_list", flags)!.GetValue(window)!;
            var detail = (StackPanel)typeof(AnnouncementWindow).GetField("_detail", flags)!.GetValue(window)!;
            var scroll = (ScrollViewer)typeof(AnnouncementWindow).GetField("_detailScroll", flags)!.GetValue(window)!;
            var refresh = (Button)typeof(AnnouncementWindow).GetField("_refresh", flags)!.GetValue(window)!;
            Check("ANNOUNCEMENT-LIST-AND-DETAIL-" + key, list.Items.Count == 20 && detail.Children.OfType<TextBlock>().Any(t => t.Text.StartsWith("<script>plain text only</script>")));
            Check("ANNOUNCEMENT-OPEN-MARKS-ONLY-SELECTED-READ-" + key, store.IsRead(AnnouncementReplies.Item(1)) && !store.IsRead(AnnouncementReplies.Item(2)));
            Check("ANNOUNCEMENT-G2-" + key, Descendants<System.Windows.Controls.Border>(window).Where(b => b.CornerRadius != default).All(b => b is G2Border));
            Check("ANNOUNCEMENT-LOCALIZATION-" + key, new[] { "title", "loading", "empty", "error", "open", "later", "more" }.All(k => Lang.Announcement(k) != k) && refresh.Content.Equals(Lang.Announcement("refresh")));
            CaptureMetadata(window, Path.Combine(images, key + "-list-detail.png"));
            window.Width = 720; Pump(); window.UpdateLayout();
            var refreshPoint = refresh.TranslatePoint(new Point(), window);
            Check("ANNOUNCEMENT-NARROW-MARGINS-" + key, refreshPoint.X >= 22 && refreshPoint.X + refresh.ActualWidth <= window.ActualWidth - 22
                && scroll.ViewportWidth > 180 && detail.Children.OfType<TextBlock>().All(t => t.ActualWidth <= scroll.ViewportWidth + 1));
            Check("ANNOUNCEMENT-LONG-BODY-SCROLLS-" + key, scroll.ExtentHeight > scroll.ViewportHeight && scroll.ViewportHeight > 100);
            CaptureMetadata(window, Path.Combine(images, key + "-narrow.png"));
            Await(service.More()); Check("ANNOUNCEMENT-MORE-" + key, service.Items.Count == 22 && !service.HasMore);
            Await(window.Open(99999)); Check("ANNOUNCEMENT-WITHDRAWN-SAFE-" + key, detail.Children.OfType<TextBlock>().Any(t => t.Text == Lang.Announcement("unavailable")) && !store.IsRead(AnnouncementReplies.Item(99999)));
            network.Fail = true; Await(service.Refresh());
            Check("ANNOUNCEMENT-OFFLINE-KEEPS-LOCAL-APP-" + key, main.IsEnabled && main.IsVisible && service.Items.Count == 22 && service.Failed);
            window.Close();
            typeof(MainWindow).GetField("_importantOffered", flags)!.SetValue(main, false);
            typeof(MainWindow).GetMethod("TryShowImportantAnnouncement", flags)!.Invoke(main, null); Pump();
            var notice = (AnnouncementNoticeWindow)typeof(MainWindow).GetField("_announcementNotice", flags)!.GetValue(main)!;
            Until(() => store.WasNotified(AnnouncementReplies.Item(2)));
            Check("ANNOUNCEMENT-IMPORTANT-NONMODAL-" + key, notice.IsVisible && !notice.ShowActivated && main.IsEnabled && notice.ActualWidth <= 470);
            Check("ANNOUNCEMENT-IMPORTANT-MARGINS-" + key, Descendants<Button>(notice).All(b => { var p = b.TranslatePoint(new Point(), notice); return p.X >= 20 && p.X + b.ActualWidth <= notice.ActualWidth - 20 && p.Y + b.ActualHeight <= notice.ActualHeight - 20; }));
            CaptureMetadata(notice, Path.Combine(images, key + "-important.png"));
            Descendants<Button>(notice).Single(b => Equals(b.Content, Lang.Announcement("later"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            typeof(MainWindow).GetMethod("TryShowImportantAnnouncement", flags)!.Invoke(main, null); Pump();
            Check("ANNOUNCEMENT-IMPORTANT-ONCE-NOT-READ-" + key, !notice.IsVisible && !store.IsRead(AnnouncementReplies.Item(2)) && typeof(MainWindow).GetField("_announcementNotice", flags)!.GetValue(main) == null);
            main.Close();
        }
        Check("ANNOUNCEMENT-USER-PROFILE-UNCHANGED", files.All(f => before[f] == Hash(f))); app.Shutdown();
    }

    static async Task AnnouncementsNetworkReview()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "public-fixture.json")));
        var root = doc.RootElement; var origin = new Uri(root.GetProperty("siteOrigin").GetString()!);
        if (origin.Host != "localhost" || origin.Scheme != "https" || origin.Port == 443) throw new InvalidOperationException("Synthetic HTTPS only.");
        using var client = new AnnouncementClient(origin, Handler());
        var page = await client.List(1); Check("ANNOUNCEMENT-REAL-HTTPS-FEED", page.Items.Length == 20 && page.Total == 23);
        var detail = await client.Detail(root.GetProperty("firstImportantId").GetInt64()); Check("ANNOUNCEMENT-REAL-HTTPS-DETAIL", detail.Important && detail.Content!.StartsWith("Synthetic body"));
        var next = await client.List(2); Check("ANNOUNCEMENT-REAL-HTTPS-PAGE2", next.Items.Length == 3 && next.Items.All(a => page.Items.All(b => a.Id != b.Id)));
        try { await client.Detail(root.GetProperty("withdrawnId").GetInt64()); Check("ANNOUNCEMENT-REAL-HTTPS-WITHDRAWN", false); }
        catch (HttpRequestException error) { Check("ANNOUNCEMENT-REAL-HTTPS-WITHDRAWN", error.StatusCode == HttpStatusCode.NotFound); }
        string statePath = Path.Combine(Output, "private", "network-read-state.json"); var state = new AnnouncementReadStore(statePath);
        state.MarkRead(detail); Check("ANNOUNCEMENT-NETWORK-READ-STATE-RELOAD", new AnnouncementReadStore(statePath).IsRead(detail));
    }

    static void AnnouncementsDemo()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "public-fixture.json")));
        var origin = new Uri(fixture.RootElement.GetProperty("siteOrigin").GetString()!);
        if (origin.Host != "localhost" || origin.Scheme != "https" || origin.Port == 443) throw new InvalidOperationException("Synthetic HTTPS only.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Palette(Theme.Load()); Lang.Load();
        using var service = new AnnouncementService(new AnnouncementClient(origin, Handler()), new AnnouncementReadStore(Path.Combine(Output, "private", "demo-read-state.json")));
        var owner = new Window { Width = 1, Height = 1, Left = -12000, Top = -12000, ShowInTaskbar = false, ShowActivated = false };
        owner.Show();
        var window = new AnnouncementWindow(owner, service) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        window.Closed += (_, _) => { owner.Close(); app.Shutdown(); };
        app.Run(window);
    }
}
