using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SMAP_WPF;

static partial class Program
{
    static (CloudTransport Transport, NativeSharingClient Client, SongMetadataTarget Target, Guid Version, int Owner) MetadataEditingConnection()
    {
        using var context = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "private/editor-test-context.json")));
        var root = context.RootElement; var origin = new Uri(root.GetProperty("origin").GetString()!);
        if (origin.Host != "localhost" || origin.Scheme != "https" || origin.Port == 443) throw new InvalidOperationException("Loopback test environment required.");
        string token = root.GetProperty("ownerToken").GetString()!;
        var transport = new CloudTransport(origin, Handler());
        return (transport, new(transport, () => token), new("work", root.GetProperty("id").GetString()!), root.GetProperty("versionId").GetGuid(), root.GetProperty("ownerUserId").GetInt32());
    }
    static async Task MetadataEditingNetwork()
    {
        var c = MetadataEditingConnection(); using var transport = c.Transport; var client = c.Client;
        Check("EDIT-NETWORK-CAPABILITY", await client.MetadataSupportedAsync());
        var original = await client.VersionAsync(c.Version); var bytes = await client.DownloadAsync(original);
        var profile = await client.MetadataAsync(c.Target);
        Check("EDIT-NETWORK-OWNER-PROFILE", profile.CanEdit && profile.Authority == "publisher" && profile.ShareCode == original.ShareCode);
        var key = Guid.NewGuid(); var values = profile.Metadata with { Title = "桌面资料修改 " + Guid.NewGuid().ToString("N")[..8], Tags = ["回归", "Desktop"] };
        var saved = await client.SaveMetadataAsync(profile, values, "keep", null, key);
        var repeat = await client.SaveMetadataAsync(profile, values, "keep", null, key);
        Check("EDIT-NETWORK-SAVE-AND-REPLAY", saved.Revision == profile.Revision + 1 && repeat.Revision == saved.Revision && repeat.Metadata.Title == values.Title);
        bool conflict = false;
        try { await client.SaveMetadataAsync(profile, values, "keep", null, Guid.NewGuid()); } catch (SharingException e) { conflict = e.Code == "METADATA_CONFLICT" && e.Status == 409; }
        Check("EDIT-NETWORK-CONFLICT", conflict);
        var image = await client.MetadataCoverAsync(saved);
        Check("EDIT-NETWORK-EXISTING-COVER-RETAINED", image is { Length: > 0 });
        var history = await client.MetadataHistoryAsync(c.Target);
        Check("EDIT-NETWORK-AUDIT", history.Items[0].After.Title == values.Title && history.Items[0].ActorId == c.Owner);
        var version = await client.VersionAsync(c.Version); var download = await client.DownloadAsync(version);
        Check("EDIT-NETWORK-IMMUTABLE-BYTES-AND-CODE", version.ManifestBytes == original.ManifestBytes && version.ShareCode == original.ShareCode && bytes.Payload.SequenceEqual(download.Payload));
        var publicClient = new NativeSharingClient(transport, () => null);
        Check("EDIT-NETWORK-PUBLIC-DISPLAY-NOT-AUTHORITY", (await publicClient.MetadataAsync(c.Target)) is { CanEdit: false } p && p.Metadata.Title == values.Title);
        bool denied = false;
        try { await publicClient.SaveMetadataAsync(saved, values, "keep", null, Guid.NewGuid()); } catch (SharingException e) { denied = e.Status == 401; }
        Check("EDIT-NETWORK-ANONYMOUS-WRITE-REFUSED", denied);
    }
    static void MetadataEditingReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark); Language(AppLang.ZhCN);
        var owner = (MainWindow)typeof(MainWindow).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [typeof(bool)], null)!.Invoke([true]);
        owner.Left = -12000; owner.Top = -12000; owner.ShowInTaskbar = false; owner.Show();
        var c = MetadataEditingConnection(); using var transport = c.Transport;
        var images = Path.Combine(Output, "actual-editor"); Directory.CreateDirectory(images);
        NativeSharingWindow Open()
        {
            var window = new NativeSharingWindow(owner, client: c.Client, user: new(() => true, () => c.Owner, _ => true), metadataTarget: c.Target);
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.Top = -12000; window.ShowInTaskbar = false; window.Show(); Pump(); WaitBusy(window); return window;
        }
        foreach (var theme in Enum.GetValues<AppTheme>()) foreach (var language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language); var window = Open(); string id = theme + "-" + language;
            Check("EDIT-UI-FIELDS-" + id, Descendants<TextBlock>(window).Any(t => t.Text == Lang.Sharing("metadataNotice")) && Descendants<TextBox>(window).Count() == 5);
            Check("EDIT-UI-NO-PERFORMANCE-CONTROLS-" + id, !Descendants<TrackMultiChoiceButton>(window).Any() && !Descendants<SharingModeTabs>(window).Any());
            Check("EDIT-UI-G2-" + id, Descendants<System.Windows.Controls.Border>(window).Where(b => b.CornerRadius != default).All(b => b is G2Border));
            Check("EDIT-UI-TRANSLATIONS-" + id, new[] { "metadataEdit", "metadataNotice", "metadataCoverHint", "metadataSave", "metadataReload", "metadataHistory", "metadataConflict", "cancel" }.All(k => Lang.Sharing(k) != k));
            CaptureMetadata(window, Path.Combine(images, id + ".png"));
            var cancel = Descendants<Button>(window).Single(b => Equals(b.Content, Lang.Sharing("cancel"))); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, cancel)); Pump();
            Check("EDIT-UI-CANCEL-" + id, !window.IsVisible);
        }
        Palette(AppTheme.Dark); Language(AppLang.ZhCN);
        var editWindow = Open(); var title = Descendants<TextBox>(editWindow).First(); string initial = title.Text;
        title.Text = "桌面窗口保存 " + Guid.NewGuid().ToString("N")[..8]; string expected = title.Text;
        var save = Descendants<Button>(editWindow).Single(b => Equals(b.Content, Lang.Sharing("metadataSave"))); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save)); WaitBusy(editWindow);
        Console.WriteLine("Metadata editor save status: " + PrivateField<TextBlock>(editWindow, "_status").Text);
        Check("EDIT-UI-ACTUAL-SAVE", editWindow.MetadataSaved && Descendants<TextBox>(editWindow).First().Text == expected);
        var currentTask = c.Client.MetadataAsync(c.Target); Wait(currentTask); var current = currentTask.Result;
        var external = c.Client.SaveMetadataAsync(current, current.Metadata with { Title = "External change" }, "keep", null, Guid.NewGuid()); Wait(external);
        title = Descendants<TextBox>(editWindow).First(); title.Text = "Keep my unsaved input";
        save = Descendants<Button>(editWindow).Single(b => Equals(b.Content, Lang.Sharing("metadataSave"))); save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, save)); WaitBusy(editWindow);
        Check("EDIT-UI-CONFLICT-PRESERVES-INPUT", title.Text == "Keep my unsaved input" && Descendants<TextBlock>(editWindow).Any(t => t.Text == Lang.Sharing("metadataConflict")));
        title.Text = expected; // Return to the form baseline before closing; no unsaved test data is discarded implicitly.
        var history = Descendants<Button>(editWindow).Single(b => Equals(b.Content, Lang.Sharing("metadataHistory"))); history.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, history)); WaitBusy(editWindow);
        Check("EDIT-UI-HISTORY", Descendants<TextBlock>(editWindow).Any(t => t.Text.Contains(Lang.Sharing("metadataPublisher"))) && !Descendants<TextBox>(editWindow).Any());
        CaptureMetadata(editWindow, Path.Combine(images, "history.png")); editWindow.Close();
        var versionTask = c.Client.VersionAsync(c.Version); Wait(versionTask);
        var result = new NativeSharingWindow(owner, client: c.Client, user: new(() => true, () => c.Owner, _ => true));
        result.Left = -12000; result.Top = -12000; result.ShowInTaskbar = false; result.Show(); result.ShowResult(versionTask.Result);
        for (int i = 0; i < 120 && !Descendants<Button>(result).Any(b => Equals(b.Content, Lang.Sharing("metadataEdit"))); i++) { Pump(); Thread.Sleep(20); }
        Check("EDIT-UI-RESULT-CURRENT-DISPLAY", Descendants<TextBlock>(result).Any(t => t.Text == "External change") && Descendants<Button>(result).Any(b => Equals(b.Content, Lang.Sharing("metadataEdit")))); result.Close();
        using var context = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "private/editor-test-context.json")));
        var legacy = new NativeSharingWindow(owner, client: c.Client, user: new(() => true, () => c.Owner, _ => true), metadataTarget: new("legacy", context.RootElement.GetProperty("legacyId").GetString()!));
        legacy.Left = -12000; legacy.Top = -12000; legacy.ShowInTaskbar = false; legacy.Show(); Pump(); WaitBusy(legacy);
        Check("EDIT-UI-LEGACY-LIMIT", Descendants<TextBox>(legacy).Single(t => t.AcceptsReturn).MaxLength == 500); CaptureMetadata(legacy, Path.Combine(images, "legacy.png")); legacy.Close();
        var denied = new NativeSharingWindow(owner, client: new(c.Transport, () => null), user: new(() => true, () => c.Owner, _ => true), metadataTarget: c.Target);
        denied.Left = -12000; denied.Top = -12000; denied.ShowInTaskbar = false; denied.Show(); Pump(); WaitBusy(denied);
        Check("EDIT-UI-SERVER-DENIAL-NO-EDITABLE-FORM", !Descendants<TextBox>(denied).Any() && !denied.MetadataSaved); denied.Close();
        owner.Close(); app.Shutdown();
    }
}
