using Border = SMAP_WPF.G2Border;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed record SharingUserContext(Func<bool> IsAuthenticated, Func<int> ActorId, Func<Window, bool> SignIn);

public sealed partial class NativeSharingWindow : ChromeWindow
{
    readonly SmapProject? _project;
    readonly Guid _activeTrack;
    readonly CloudTransport? _ownedTransport;
    readonly NativeSharingClient _client;
    readonly NativeSharingStore _store;
    readonly string _importDirectory;
    readonly SharingUserContext _user;
    readonly CancellationTokenSource _cancel = new();
    readonly StackPanel _navigation = new() { Orientation = Orientation.Horizontal };
    readonly WrapPanel _actions = new() { HorizontalAlignment = HorizontalAlignment.Right };
    readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly TextBlock _status;
    StackPanel _body = new();
    bool _closed, _busy;
    SharingVersion? _sourceVersion;
    public bool Imported { get; private set; }
    public string? ImportedFile { get; private set; }
    static string S(string key) => Lang.Sharing(key);

    public NativeSharingWindow(Window owner, SmapProject? project = null, Guid activeTrack = default,
        NativeSharingClient? client = null, NativeSharingStore? store = null, bool startWithHistory = false, SharingUserContext? user = null, Guid? initialVersion = null, string? importDirectory = null) : base(S("title"), 1000)
    {
        Owner = owner; _project = project; _activeTrack = activeTrack;
        _store = store ?? new NativeSharingStore(NativeSharingStore.DefaultDirectory);
        _importDirectory = importDirectory ?? SongLibrary.SongsDir;
        _user = user ?? new(() => CloudApi.LoggedIn, () => CloudApi.UserId, window => new LoginDialog(window).ShowDialog() == true);
        if (client == null)
        {
            _ownedTransport = new(CloudTransport.ApiOrigin);
            _client = new(_ownedTransport, () => CloudApi.SharingToken, CloudApi.SharingAuthenticationFailed);
        }
        else _client = client;
        Width = Math.Min(1000, Math.Max(720, SystemParameters.WorkArea.Width - 40));
        foreach (var type in new[] { typeof(System.Windows.Controls.Primitives.ScrollBar), typeof(ContextMenu), typeof(MenuItem) })
            if (owner.TryFindResource(type) is Style style) Resources[type] = style;
        _status = Label("", 13); _status.MinHeight = 32; _status.Margin = new Thickness(0, 7, 0, 0);
        InitializeSharingLayout();
        if (initialVersion != null) { Reset(S("history")); Loaded += async (_, _) => await Run(async () => { var version = await _client.VersionAsync(initialVersion.Value, _cancel.Token); if (!_closed) { _sourceVersion = version; if (startWithHistory) await ShowHistory(version.WorkId); else { ShowPublish(null); await RefreshCloudSelection(); } } }); }
        else if (startWithHistory) { Reset(S("history")); Loaded += async (_, _) => await Run(ShowMyWorks); }
        else if (project == null) ShowCode();
        else { ShowPublish(null); Loaded += async (_, _) => await Run(RefreshCloudSelection); }
    }
    TextBlock Label(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = B("TextFg"), Margin = new Thickness(0, 7, 0, 7) };
    void Reset(string heading)
    {
        foreach (var choice in _body.Children.OfType<JoinedChoiceButton>()) choice.Close(true);
        _body = new StackPanel { Margin = new Thickness(0, 12, 6, 0) }; _body.Children.Add(Label(heading, 21)); _scroll.Content = _body;
        _actions.Children.Clear();
    }
    TextBox Input(string label, string text = "", int length = 150, bool multiline = false, Panel? target = null)
    {
        target ??= _body; target.Children.Add(Label(label, 13));
        var box = new TextBox { Text = text, MaxLength = length, MinHeight = multiline ? 70 : 38, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap,
            Background = B("BoxBg"), Foreground = B("TextFg"), BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1), Padding = new Thickness(9), Margin = new Thickness(0, 0, 0, 6) };
        box.Template = multiline ? (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate TargetType='TextBox' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:g2='clr-namespace:SMAP_WPF;assembly=SMAP'><g2:G2Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8'><ScrollViewer x:Name='PART_ContentHost' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Margin='10,8'/></g2:G2Border></ControlTemplate>") : TextBoxTpl();
        target.Children.Add(box); return box;
    }
    void AddNavigation(string label, Action action)
    { var button = Neutral(label); button.Height = 36; button.Margin = new Thickness(0, 0, 10, 0); button.Click += (_, _) => action(); _navigation.Children.Add(button); }
    Button ActionButton(string text, Func<Task> action, bool primary = false, Panel? target = null)
    {
        var button = Neutral(text); button.Margin = new Thickness(0, 8, 0, 0);
        if (primary) { button.SetResourceReference(BackgroundProperty, "PracticeActionBg"); button.SetResourceReference(ForegroundProperty, "ActionFg"); }
        if (target is WrapPanel) { button.MinWidth = 160; button.Margin = new Thickness(6, 8, 0, 0); }
        button.Click += async (_, _) => await Run(action); (target ?? _body).Children.Add(button); return button;
    }
    async Task Run(Func<Task> action)
    {
        if (_busy || _closed) return; _busy = true; _scroll.IsEnabled = _navigation.IsEnabled = _actions.IsEnabled = false; _status.Text = S("working");
        try { await action(); if (!_closed && _status.Text == S("working")) _status.Text = ""; }
        catch (OperationCanceledException) { if (!_closed) _status.Text = S("network"); }
        catch (Exception error) { if (!_closed) _status.Text = ErrorText(error); }
        finally { if (!_closed) { _scroll.IsEnabled = _navigation.IsEnabled = _actions.IsEnabled = true; _busy = false; } }
    }
    public static string ErrorText(Exception error)
    {
        if (error is HttpRequestException or TaskCanceledException) return S("network");
        if (error is SharingException sharing)
        {
            if (sharing.Status == 401 || sharing.Code is "AUTH_REQUIRED" or "invalid_session") return S("login");
            if (sharing.Code == "SHARING_API_UNAVAILABLE") return S("apiUnavailable");
            if (sharing.Code == "SERVICE_UNAVAILABLE") return S("unavailable");
            if (sharing.Status == 403) return S("forbidden");
            if (sharing.Status == 404) return S("notFound");
            if (sharing.Status == 410) return S("withdrawn");
            if (sharing.Status == 412) return S("changed");
            if (sharing.Status == 429) return S("limited");
            return S(sharing.Code switch { "INVALID_SHARE_CODE" => "invalidCode", "INTEGRITY_MISMATCH" or "INVALID_PROJECT" or "INVALID_RESPONSE" => "integrity", "PENDING_PUBLICATION" => "pending", _ => "unavailable" });
        }
        return S(error is IOException or UnauthorizedAccessException ? "saveFailed" : "unavailable");
    }
    bool Login()
    {
        if (_user.IsAuthenticated()) return true;
        return _user.SignIn(this);
    }
    void ShowCode()
    {
        Reset(S("get")); var input = Input(S("codeHint"), length: 64);
        if (_project != null) ActionButton(S("newWork"), async () => { ShowPublish(null);await RefreshCloudSelection(); });
        ActionButton(S("resolve"), async () => { var version = await _client.ResolveAsync(input.Text, _cancel.Token); if (!_closed) ShowResult(version); }, true);
        _body.Children.Add(Label(S("notice"), 13));
    }
    void ShowPublish(SharingWork? existing)
    {
        if (_project == null) { ShowCode(); return; }
        Reset(existing == null ? S("newWork") : (existing.Title ?? existing.WorkId.ToString()));
        Button? publishButton = null;
        void SelectionChanged() { if (publishButton != null) publishButton.IsEnabled = false; _status.Text = S("selectionChanged"); }
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel(); var right = new StackPanel();
        foreach (var pair in new[] { (left, 0), (right, 2) }) { var card = new Border { Background = B("PanelBg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Child = pair.Item1 }; Grid.SetColumn(card, pair.Item2); columns.Children.Add(card); }
        _body.Children.Add(columns);
        right.Children.Add(Label(S("choose"))); var tracks = new Dictionary<Guid, CheckBox>(); var trackList = new StackPanel();
        foreach (var track in _project.Tracks)
        {
            var box = new CheckBox { Content = new TextBlock { Text = track.Name, TextWrapping = TextWrapping.Wrap }, IsChecked = _activeTrack == Guid.Empty ? !track.Muted : track.Id == _activeTrack,
                Foreground = B("TextFg"), Margin = new Thickness(0, 4, 0, 4) }; tracks.Add(track.Id, box); trackList.Children.Add(box);
            box.Checked += (_, _) => SelectionChanged(); box.Unchecked += (_, _) => SelectionChanged();
        }
        right.Children.Add(new ScrollViewer { Content = trackList, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var title = Input(S("name"), _project.Metadata.Name, target: left); var artist = Input(S("artist"), _project.Metadata.Author, 100, target: left);
        var transcriber = Input(S("transcriber"), _project.Metadata.TranscribedBy, 100, target: left); var description = Input(S("description"), _project.Metadata.Description, 2000, true, left);
        var midi = new CheckBox { Content = new TextBlock { Text = S("midi"), TextWrapping = TextWrapping.Wrap }, Foreground = B("TextFg"), IsChecked = false, Margin = new Thickness(0, 8, 0, 8), Visibility = _project.OriginalMidi == null ? Visibility.Collapsed : Visibility.Visible };
        right.Children.Add(midi);
        midi.Checked += (_, _) => SelectionChanged(); midi.Unchecked += (_, _) => SelectionChanged();
        var visibility = new ComboBox { ItemsSource = new[] { S("public"), S("private") }, SelectedIndex = 0 };
        var choice = new JoinedChoiceButton { Height = 38, FontSize = 14, Margin = new Thickness(0, 6, 0, 6) };
        choice.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("SelectedItem") { Source = visibility });
        choice.Click += (_, _) => { Panel.SetZIndex(choice, 20); choice.Toggle(visibility); }; right.Children.Add(choice);
        var sourceCodes = Input(S("sourceCodes"), existing == null && _sourceVersion?.Visibility == "public" ? _sourceVersion.FormattedCode : "", length: 1024, multiline: true, target: right);
        right.Children.Add(Label(S("notice"), 13));
        async Task<bool> CheckCloud()
        {
            var ids = tracks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToArray();
            if (ids.Length == 0) { _status.Text = S("tracksRequired"); return false; }
            var options = new ScoreExportOptions { TrackIds = ids, IncludeOriginalMidi = midi.IsChecked == true };
            using var lookup = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token); _lookupCancel = lookup;
            if (publishButton != null) publishButton.IsEnabled = false;_status.Text = S("checkingCloud");
            try
            {
                var snapshot = await Task.Run(() => ScoreExport.ProjectSelection(_project, options), lookup.Token);
                var matches = await _client.FindMatchesAsync(snapshot, lookup.Token);
                lookup.Token.ThrowIfCancellationRequested();if (_closed) return false;
                if (matches.Items.Count > 0 && existing == null) { ShowMatchingVersions(matches); return false; }
                if (existing == null && !matches.LegacyCatalogReady && _project.Tracks.Any(t => t.LegacyFields != null))
                { _status.Text = S("legacyPending"); return false; }
                if (publishButton != null) publishButton.IsEnabled = true;_status.Text = S("cloudAbsent");return true;
            }
            finally { if (ReferenceEquals(_lookupCancel, lookup)) _lookupCancel = null; }
        }
        _refreshCloud = async () => { await CheckCloud(); };
        ActionButton(S("checkCloud"), RefreshCloudSelection, target: _actions);
        publishButton = ActionButton(existing == null ? S("uploadShare") : S("confirm"), async () =>
        {
            if (!await CheckCloud()) return;
            if (!Login()) return;
            string chosenTitle = title.Text.Trim(), chosenArtist = artist.Text.Trim(), chosenTranscriber = transcriber.Text.Trim(), chosenDescription = description.Text.Trim();
            if (chosenTitle.Length == 0 || chosenTitle.EnumerateRunes().Count() > 150 || chosenArtist.EnumerateRunes().Count() > 100 || chosenTranscriber.EnumerateRunes().Count() > 100 || chosenDescription.EnumerateRunes().Count() > 2000)
            { _status.Text = S("textLimits"); return; }
            string chosenVisibility = visibility.SelectedIndex == 0 ? "public" : "private";
            int actorId = _user.ActorId();
            var ids = tracks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToArray();
            if (ids.Length == 0) { _status.Text = S("tracksRequired"); return; }
            if (_store.Pending(actorId) != null) throw new SharingException("PENDING_PUBLICATION");
            var sources = new List<SharingSource>();
            foreach (var code in sourceCodes.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            { if (sources.Count >= 16) throw new SharingException("INVALID_SHARE_CODE"); var source = await _client.ResolveAsync(code.Trim(), _cancel.Token); sources.Add(new(source.VersionId)); }
            var options = new ScoreExportOptions { TrackIds = ids, IncludeOriginalMidi = midi.IsChecked == true };
            var payload = await Task.Run(() =>
            {
                var snapshot = ScoreExport.ProjectSelection(_project, options);
                return SmapFile.ToBytes(snapshot with { Metadata = snapshot.Metadata with { Name = chosenTitle, Author = chosenArtist, TranscribedBy = chosenTranscriber, Description = chosenDescription } });
            }, _cancel.Token);
            var draft = await Task.Run(() => _store.Stage(actorId, existing, payload, chosenTitle, chosenArtist, chosenTranscriber, chosenDescription,
                chosenVisibility, sources), _cancel.Token);
            var version = await _store.PublishAsync(_client, draft, _cancel.Token); if (!_closed) { ShowResult(version); _status.Text = S("published"); }
        }, true, _actions);publishButton.IsEnabled = false;
        var more = Neutral(S("more"));more.Margin = new Thickness(6, 8, 0, 0);_actions.Children.Add(more);
        _publicationMenu = new ContextMenu { PlacementTarget = more, Style = TryFindResource(typeof(ContextMenu)) as Style };
        void MoreAction(string text, Func<Task> action)
        { var item = new MenuItem { Header = text, Style = TryFindResource(typeof(MenuItem)) as Style };item.Click += async (_, _) => await Run(action);_publicationMenu.Items.Add(item); }
        MoreAction(S("retry"), async () =>
        {
            if (!Login()) return; var draft = _store.Pending(_user.ActorId()); if (draft == null) { _status.Text = S("empty"); return; }
            if (draft.Work == null)
            {
                var snapshot = await Task.Run(() => SmapFile.FromBytes(_store.Payload(draft)), _cancel.Token);
                var matches = await _client.FindMatchesAsync(snapshot, _cancel.Token);
                if (matches.Items.Count > 0) { if (!_closed) ShowMatchingVersions(matches); return; }
                if (!matches.LegacyCatalogReady && _project?.Tracks.Any(t => t.LegacyFields != null) == true) { _status.Text = S("legacyPending"); return; }
            }
            var version = await _store.PublishAsync(_client, draft, _cancel.Token); if (!_closed) { ShowResult(version); _status.Text = S("published"); }
        });
        MoreAction(S("discard"), () =>
        {
            if (_user.IsAuthenticated() && _store.Pending(_user.ActorId()) is { } draft && MsgBox.Confirm(this, S("pending"), S("discard"))) _store.Complete(draft);
            return Task.CompletedTask;
        });
        more.Click += (_, _) => _publicationMenu.IsOpen = true;
    }
    public void ShowResult(SharingVersion version)
    {
        Reset(version.Title); _body.Children.Add(Label(string.Format(S("version"), version.VersionNumber), 17));
        var codeLabel = Label(version.FormattedCode, 36); codeLabel.Foreground = B("PracticeActionBg"); _body.Children.Add(codeLabel); _body.Children.Add(Label(version.Visibility == "private" ? S("private") : S("public")));
        _body.Children.Add(Label(version.Artist + (string.IsNullOrEmpty(version.TranscribedBy) ? "" : " · " + version.TranscribedBy)));
        if (!string.IsNullOrEmpty(version.Description)) _body.Children.Add(Label(version.Description));
        _body.Children.Add(new Expander { Header = S("identity"), Foreground = B("SubTextFg"), Content = Label(version.WorkId + "\n" + version.VersionId, 11), Margin = new Thickness(0, 8, 0, 8) });
        var copy = new WrapPanel(); _body.Children.Add(copy);
        ActionButton(S("copyCode"), () => Copy(version.FormattedCode), target: copy);
        ActionButton(S("copyVersion"), () => Copy(new Uri(_client.Origin, "smap.php?v=" + version.VersionId).AbsoluteUri), target: copy);
        ActionButton(S("copyWork"), () => Copy(new Uri(_client.Origin, "smap.php?w=" + version.WorkId).AbsoluteUri), target: copy);
        var download = ActionButton(S("import"), async () =>
        {
            var result = await _client.DownloadAsync(version, _cancel.Token);
            string path = await Task.Run(() => _store.Import(result, _importDirectory), _cancel.Token);
            if (!_closed) { Imported = true; ImportedFile = path; _status.Text = S("imported"); }
        }, true, _actions); download.IsEnabled = version.Lifecycle == "published" && version.WorkState == "active";
        ActionButton(S("history"), () => ShowHistory(version.WorkId), target: _actions);
        ActionButton(S("sources"), () => ShowSources(version.VersionId), target: _actions);
        if (version.Lifecycle == "published" && version.WorkState == "active") ActionButton(S("image"), async () =>
        {
            var full = await _client.DownloadAsync(version, _cancel.Token);
            if (!_closed) { SetOfflineSource(full.Project, full.Project.Tracks[0].Id, version); _modes.Select(1); }
        }, target: _actions);
        if (_user.IsAuthenticated() && version.OwnerUserId == _user.ActorId() && version.Lifecycle != "removed")
        {
            ActionButton(version.WorkState == "active" ? S("withdrawWork") : S("restoreWork"), async () =>
            {
                if (!MsgBox.Confirm(this, S(version.WorkState == "active" ? "withdrawWork" : "restoreWork"), S("title"))) return;
                var work = await _client.GetWorkAsync(version.WorkId, _cancel.Token);
                await _client.SetWorkStateAsync(work, work.State == "active" ? "withdrawn" : "active", Guid.NewGuid(), _cancel.Token);
                if (!_closed) ShowResult(await _client.VersionAsync(version.VersionId, _cancel.Token));
            });
            if (_project != null) ActionButton(S("confirm"), async () => { var work = await _client.GetWorkAsync(version.WorkId, _cancel.Token); if (!_closed) ShowPublish(work); });
            ActionButton(version.Lifecycle == "published" ? S("withdraw") : S("restore"), async () =>
            { var result = await _client.SetAccessAsync(version, "public", version.Lifecycle == "published" ? "withdrawn" : "published", Guid.NewGuid(), _cancel.Token); if (!_closed) ShowResult(result); });
            ActionButton(version.Visibility == "public" ? S("makePrivate") : S("restore"), async () =>
            { var result = await _client.SetAccessAsync(version, version.Visibility == "public" ? "private" : "public", version.Lifecycle, Guid.NewGuid(), _cancel.Token); if (!_closed) ShowResult(result); });
            if (version.Visibility == "public" && version.Lifecycle == "published") ActionButton(S("latest"), async () =>
            { var work = await _client.GetWorkAsync(version.WorkId, _cancel.Token); await _client.SetLatestAsync(work, version.VersionId, Guid.NewGuid(), _cancel.Token); if (!_closed) ShowResult(await _client.VersionAsync(version.VersionId, _cancel.Token)); });
        }
    }
    Task Copy(string text) { Clipboard.SetText(text); _status.Text = S("copied"); return Task.CompletedTask; }
    async Task ShowMyWorks()
    {
        if (!Login()) return; var works = await _client.MyWorksAsync(_cancel.Token); if (_closed) return; Reset(S("myWorks"));
        if (works.Count == 0) _body.Children.Add(Label(S("empty")));
        foreach (var work in works)
        {
            _body.Children.Add(Label(work.Title ?? work.WorkId.ToString(), 17));
            ActionButton(S("history"), () => ShowHistory(work.WorkId));
            if (_project != null) ActionButton(S("confirm"), async () => { var fresh = await _client.GetWorkAsync(work.WorkId, _cancel.Token); if (!_closed) ShowPublish(fresh with { Title = work.Title }); });
        }
    }
    async Task ShowHistory(Guid id, int? before = null)
    {
        var history = await _client.HistoryAsync(id, before, _cancel.Token); if (_closed) return; Reset(S("history"));
        foreach (var row in history.Items)
        {
            var button = ActionButton(string.Format(S("version"), row.VersionNumber) + " · " + (row.Title.Length > 0 ? row.Title : S("withdrawn")), async () =>
            { var version = await _client.VersionAsync(row.VersionId, _cancel.Token); if (!_closed) ShowResult(version); });
            button.IsEnabled = row.Lifecycle == "published" || (_user.IsAuthenticated() && row.OwnerUserId == _user.ActorId());
        }
        if (history.NextBefore != null) ActionButton(S("loadMore"), () => ShowHistory(id, history.NextBefore));
    }
    async Task ShowSources(Guid id)
    {
        var sources = await _client.SourcesAsync(id, _cancel.Token); if (_closed) return; Reset(S("sources"));
        _body.Children.Add(Label(S("disclaimer"), 13)); if (sources.Count == 0) _body.Children.Add(Label(S("noSources")));
        foreach (var source in sources)
        {
            _body.Children.Add(Label(!source.Available ? S("sourceUnavailable") : (source.Attribution ?? "") + (source.Provenance == "user_declared" ? " · " + S("declared") : "")));
            if (source.Available && source.VersionId is { } versionId) ActionButton(S("history"), async () => { var version = await _client.VersionAsync(versionId, _cancel.Token); if (!_closed) ShowResult(version); });
            else if (!string.IsNullOrEmpty(source.Url)) _body.Children.Add(Label(source.Url, 12));
        }
    }
    protected override void OnClosed(EventArgs e)
    { _closed = true; _lookupCancel?.Cancel(); _offlinePane?.Dispose(); _publicationMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); _cancel.Cancel(); _ownedTransport?.Dispose(); _cancel.Dispose(); base.OnClosed(e); }
}
