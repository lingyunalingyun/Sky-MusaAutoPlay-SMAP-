using System.IO;
using System.Text.Json;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    static void CloudCatalogQueueReview()
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var window = (MainWindow)typeof(MainWindow).GetConstructor(flags, null, [typeof(bool)], null)!.Invoke([true]);
        var requests = new List<(string Query, string Sort, int Difficulty, int Page, TaskCompletionSource<CloudApi.ListResult> Reply)>();
        Func<string, string, int, int, int, Task<CloudApi.ListResult>> query = (q, sort, diff, page, size) =>
        {
            var reply = new TaskCompletionSource<CloudApi.ListResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add((q, sort, diff, page, reply)); return reply.Task;
        };
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object Get(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        Task Load(bool append) => (Task)typeof(MainWindow).GetMethod("LoadCloud", flags)!.Invoke(window, [append])!;
        void Until(Func<bool> predicate)
        {
            var end = DateTime.UtcNow.AddSeconds(8);
            while (!predicate() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(10); }
            if (!predicate()) throw new InvalidOperationException("Cloud queue test timed out");
        }
        var rows = (System.Collections.ObjectModel.ObservableCollection<CloudSheet>)Get("_cloud");
        CloudApi.ListResult Result(params CloudSheet[] songs) => new(true, null, 3, 2, songs.ToList(), true);
        try
        {
            window.ShowInTaskbar = false; window.Show(); window.Left = -12000; window.Top = -12000;
            Set("_cloudQuery", query); Set("_cloudMode", true);
            var first = Load(false);
            ((System.Windows.Controls.ComboBox)window.FindName("CloudSortCombo")).SelectedIndex = 2;
            ((System.Windows.Controls.TextBox)window.FindName("SearchBox")).Text = "Latest query";
            _ = Load(false);
            ((System.Windows.Controls.ComboBox)window.FindName("CloudDiffCombo")).SelectedIndex = 4;
            Check("CLOUD-QUEUE-LATEST-QUERY-NOT-DROPPED", requests.Count == 1 && (bool)Get("_cloudReloadPending"));
            requests[0].Reply.SetResult(Result(new CloudSheet { Id = 1, Title = "Stale" })); Until(() => requests.Count == 2);
            Check("CLOUD-QUEUE-STALE-RESULT-NOT-APPLIED", rows.Count == 0);
            Check("CLOUD-QUEUE-LATEST-FILTERS-RESUBMITTED", requests[1].Query == "Latest query" && requests[1].Sort == "downloads" && requests[1].Difficulty == 4 && requests[1].Page == 1);
            requests[1].Reply.SetResult(Result(new CloudSheet { Id = 2, Title = "Latest" })); Until(() => !(bool)Get("_cloudLoading"));
            Check("CLOUD-QUEUE-LATEST-RESULT-APPLIED", rows.Count == 1 && rows[0].Title == "Latest");
            _ = Load(true); requests[2].Reply.SetResult(new(false, "Synthetic network failure", 0, 0, [])); Until(() => !(bool)Get("_cloudLoading"));
            Check("CLOUD-QUEUE-FAILED-PAGE-NOT-SKIPPED", requests[2].Page == 2 && (int)Get("_cloudPage") == 1);
            _ = Load(true); requests[3].Reply.SetResult(Result(new CloudSheet { Id = 2, Title = "Duplicate" }, new CloudSheet { Id = 3, Title = "Next page" })); Until(() => !(bool)Get("_cloudLoading"));
            Check("CLOUD-QUEUE-RETRY-PAGE-AND-DEDUPLICATE", requests[3].Page == 2 && rows.Count == 2 && rows.Count(s => s.Id == 2) == 1 && (int)Get("_cloudPage") == 2);
            _ = Load(false); Set("_cloudMode", false); requests[4].Reply.SetResult(Result(new CloudSheet { Id = 99, Title = "Late response" })); Until(() => !(bool)Get("_cloudLoading"));
            Check("CLOUD-QUEUE-LEAVING-CLOUD-IGNORES-LATE-RESULT", rows.Count == 2 && rows.All(s => s.Id != 99));
        }
        finally { window.Close(); app.Shutdown(); }
    }

    static async Task CloudCatalogReview()
    {
        using var context = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "private/catalog-context.json")));
        var c = context.RootElement;
        if (!c.GetProperty("database").GetString()!.StartsWith("smap_rc_")) throw new InvalidOperationException("Synthetic database required");
        var origin = new Uri(c.GetProperty("origin").GetString()!);
        if (origin.Host != "localhost" || origin.Scheme != "https" || origin.Port == 443) throw new InvalidOperationException("Isolated origin required");
        using var transport = new CloudTransport(origin, Handler());
        Task<CloudApi.ListResult> Query(string query, string order) => (Task<CloudApi.ListResult>)typeof(CloudApi).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Single(m => m.Name == "ListAsync" && m.GetParameters().Length == 6).Invoke(null, [query, order, 0, 1, 20, transport])!;
        string marker = c.GetProperty("marker").GetString()!;
        Guid work = c.GetProperty("workId").GetGuid(), version = c.GetProperty("versionId").GetGuid();
        var catalog = await Query(marker, "newest");
        Check("CATALOG-NETWORK-MIXED-LIST", catalog.Ok && catalog.IncludesNative && catalog.Items.Count == 2 && catalog.Items.Any(s => !s.IsNative));
        var native = catalog.Items.Single(s => s.IsNative);
        Check("CATALOG-NETWORK-CURRENT-WORK-VERSION", native.WorkId == work && native.VersionId == version);
        Check("CATALOG-NETWORK-EDIT-USES-WORK-ID", native.MetadataTarget == new SongMetadataTarget("work", work.ToString()));
        Check("CATALOG-NETWORK-METADATA-AND-STATISTICS", native.Title == marker + " edited" && native.Difficulty == 4 && native.StatisticsAvailable && native.Likes == 2);
        var directory = Path.Combine(Output, "synthetic-downloads-" + Guid.NewGuid().ToString("N"));
        var store = new NativeSharingStore(Path.Combine(directory, "links"));
        string? error = null;
        Task<string?> Download(CloudSheet sheet) => (Task<string?>)typeof(CloudApi).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Single(m => m.Name == "DownloadAsync" && m.GetParameters().Length == 5).Invoke(null, [sheet, directory, (Action<string>)(e => error = e), transport, store])!;
        string? path = await Download(native);
        Check("CATALOG-NETWORK-DOWNLOAD-NATIVE-AS-SMAP", error == null && path != null && Path.GetExtension(path) == ".smap");
        var project = SmapFile.Load(path!); var client = new NativeSharingClient(transport, () => null);
        var published = await client.VersionAsync(version);
        Check("CATALOG-NETWORK-FINGERPRINT-AND-PAYLOAD", PerformanceIdentity.ForProject(project) == published.PerformanceHash && Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path!))) == published.PayloadSha256);
        string? repeated = await Download(native);
        Check("CATALOG-NETWORK-REIMPORT-REUSES-IDENTICAL-FILE", repeated == path && Directory.GetFiles(directory, "*.smap").Length == 1);
        var editor = new EditorSession(project); int before = editor.ActiveTrack.Notes.Count;
        editor.Create(3, 8);
        Check("CATALOG-NETWORK-RESTORED-ENGINE-CONTINUES-EDITING", editor.ActiveTrack.Notes.Count == before + 1 && editor.IsDirty);
        SmapFile.Save(editor.Project, path!); byte[] edited = File.ReadAllBytes(path!);
        string? importedAgain = await Download(native);
        Check("CATALOG-NETWORK-REIMPORT-PRESERVES-LOCAL-EDITS", importedAgain != path && File.ReadAllBytes(path!).SequenceEqual(edited) && PerformanceIdentity.ForProject(SmapFile.Load(importedAgain!)) == published.PerformanceHash);
        var legacy = catalog.Items.Single(s => !s.IsNative);
        string? oldPath = await Download(legacy);
        Check("CATALOG-NETWORK-LEGACY-STILL-DOWNLOADS", error == null && oldPath != null && Path.GetExtension(oldPath) == ".txt" && LegacyProjectAdapter.Load(oldPath, 0).Tracks.Count > 0);
        var refreshed = await Query(marker, "downloads");
        Check("CATALOG-NETWORK-DOWNLOAD-COUNTER-REFLECTS-TRANSFER", refreshed.Ok && refreshed.Items.Single(s => s.IsNative).Downloads >= native.Downloads + 3);
        var historical = await client.ResolveAsync((await client.VersionAsync(version)).ShareCode);
        Check("CATALOG-NETWORK-SHARE-CODE-REMAINS-VALID", historical.VersionId == version && historical.WorkId == work);
    }
}
