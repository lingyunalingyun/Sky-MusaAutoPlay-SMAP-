using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class ImportDialog : ChromeWindow
{
    readonly SharingModeTabs _tabs = new([S("smap"), S("offline"), S("legacy"), S("midi")], double.NaN, 44);
    readonly Grid _pages = new();
    readonly StackPanel _online = new();
    readonly List<FrameworkElement> _views = [];
    readonly TextBox _code;
    readonly TextBlock _status;
    readonly Button _action, _back;
    readonly Func<string[], Window, string> _importFiles;
    readonly Func<int, string[]?>? _chooseFiles;
    readonly NativeSharingClient _client;
    readonly NativeSharingStore _store;
    readonly CloudTransport? _transport;
    readonly string _directory;
    readonly string[] _messages = [S("onlineStatus"), S("offlineStatus"), S("offlineStatus"), S("offlineStatus")];
    CancellationTokenSource? _operation;
    SharingVersion? _version;
    bool _busy, _closed, _saving;
    public bool Imported { get; private set; }
    static string S(string key) => Lang.Import(key);

    public ImportDialog(Window owner, Func<string[], Window, string> importFiles, NativeSharingClient? client = null,
        NativeSharingStore? store = null, string? importDirectory = null, Func<int, string[]?>? chooseFiles = null) : base(S("title"), 960)
    {
        Owner = owner; Width = Math.Min(960, Math.Max(720, SystemParameters.WorkArea.Width - 40));
        _importFiles = importFiles; _chooseFiles = chooseFiles;
        _store = store ?? new NativeSharingStore(NativeSharingStore.DefaultDirectory); _directory = importDirectory ?? SongLibrary.SongsDir;
        if (client == null) { _transport = new(CloudTransport.ApiOrigin); _client = new(_transport, () => CloudApi.SharingToken, CloudApi.SharingAuthenticationFailed); }
        else _client = client;
        if (owner.TryFindResource(typeof(System.Windows.Controls.Primitives.ScrollBar)) is Style scrollStyle) Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollStyle;
        var root = new Grid { Margin = new Thickness(14), Height = Math.Min(556, Math.Max(430, SystemParameters.WorkArea.Height - 150)) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(_tabs); Grid.SetRow(_pages, 1); _pages.Margin = new Thickness(0, 18, 0, 12); root.Children.Add(_pages);
        _code = new TextBox { MaxLength = 64, Height = 50, FontSize = 20, Foreground = B("TextFg"), Background = B("BoxBg"),
            BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1), Template = TextBoxTpl(), Margin = new Thickness(0, 8, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(_code, S("codeLabel"));
        ShowCode(); AddView(_online);
        foreach (var mode in new[] { "offline", "legacy", "midi" }) AddView(FileView(mode));
        _status = Text(_messages[0], 13, true); _status.MinHeight = 32; Grid.SetRow(_status, 2); root.Children.Add(_status);
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition());
        _back = Neutral(S("close")); _back.MinWidth = 108; _back.HorizontalAlignment = HorizontalAlignment.Left;
        _back.Click += (_, _) => { if (_tabs.SelectedIndex == 0 && _version != null) { _version = null; ShowCode(); _status.Text = _messages[0] = S("onlineStatus"); UpdateActions(); } else Close(); };
        _action = Neutral(S("onlineAction")); _action.MinWidth = 260; _action.HorizontalAlignment = HorizontalAlignment.Right; _action.IsDefault = true;
        _action.SetResourceReference(BackgroundProperty, "PracticeActionBg"); _action.SetResourceReference(ForegroundProperty, "ActionFg");
        _action.Click += async (_, _) => { if (_tabs.SelectedIndex == 0) await RunOnline(); else ImportLocal(); };
        footer.Children.Add(_back); Grid.SetColumn(_action, 1); footer.Children.Add(_action); Grid.SetRow(footer, 3); root.Children.Add(footer);
        _tabs.SelectionChanged += ChangeMode; SetBody(root); UpdateActions();
    }
    TextBlock Text(string value, double size = 14, bool subdued = false) => new()
    { Text = value, FontSize = size, Foreground = B(subdued ? "SubTextFg" : "TextFg"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    Border Card(UIElement content) => new() { Child = content, Background = B("PanelBg"), CornerRadius = new CornerRadius(10), Padding = new Thickness(24), Margin = new Thickness(0, 18, 0, 0) };
    void AddView(FrameworkElement view)
    { if (_views.Count > 0) view.Visibility = Visibility.Collapsed; _views.Add(view); _pages.Children.Add(view); }
    void ShowCode()
    {
        (_code.Parent as Panel)?.Children.Remove(_code);
        _online.Children.Clear(); _online.Children.Add(Text(S("onlineTitle"), 23)); _online.Children.Add(Text(S("onlineText"), subdued: true));
        var body = new StackPanel(); body.Children.Add(Text(S("codeLabel"))); body.Children.Add(_code);
        body.Children.Add(Text(S("codeHint"), 13, true)); body.Children.Add(new Border { Height = 16 }); body.Children.Add(Text(S("onlineNote"), subdued: true));
        _online.Children.Add(Card(body));
    }
    FrameworkElement FileView(string mode)
    {
        var body = new StackPanel(); body.Children.Add(Text(S(mode + "Title"), 23)); body.Children.Add(Text(S(mode + "Text"), subdued: true));
        var panel = new StackPanel(); var formats = Text(mode switch { "offline" => ".smap   SVG   PNG", "legacy" => "TXT   JSON", _ => ".mid   .midi" }, 30);
        formats.Foreground = B("Accent"); formats.HorizontalAlignment = HorizontalAlignment.Center; formats.Margin = new Thickness(0, 12, 0, 14); panel.Children.Add(formats);
        var empty = Text(S("choose"), 16, true); empty.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(empty);
        var hint = Text(mode == "midi" ? "SAPM" : S("multiple"), 13, true); hint.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(hint);
        var note = Text(S(mode + "Note"), 13, true); note.Margin = new Thickness(0, 24, 0, 4); panel.Children.Add(note); body.Children.Add(Card(panel)); return body;
    }
    void ChangeMode(int mode)
    {
        _operation?.Cancel();
        for (int i = 0; i < _views.Count; i++)
        {
            var view = _views[i]; bool selected = i == mode; view.IsHitTestVisible = selected;
            if (selected)
            { view.Visibility = Visibility.Visible; view.BeginAnimation(OpacityProperty, SystemParameters.ClientAreaAnimation ? new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) : null); view.Opacity = 1; }
            else if (view.Visibility == Visibility.Visible && SystemParameters.ClientAreaAnimation)
            {
                int index = i; var fade = new DoubleAnimation(view.Opacity, 0, TimeSpan.FromMilliseconds(150));
                fade.Completed += (_, _) => { if (_tabs.SelectedIndex != index) view.Visibility = Visibility.Collapsed; }; view.BeginAnimation(OpacityProperty, fade);
            }
            else view.Visibility = Visibility.Collapsed;
        }
        _status.Text = _messages[mode]; UpdateActions();
    }
    void UpdateActions()
    {
        int mode = _tabs.SelectedIndex;
        _action.Content = S(mode switch { 0 => _version == null ? "onlineAction" : "download", 3 => "midiAction", _ => "offlineAction" });
        _action.IsEnabled = !_busy; _back.IsEnabled = !_saving; _tabs.IsEnabled = !_saving;
        _back.Content = S(mode == 0 && _version != null ? "back" : "close");
    }
    async Task RunOnline()
    {
        if (_busy || _closed) return;
        using var operation = new CancellationTokenSource(); _operation = operation; _busy = true; _code.IsEnabled = false; UpdateActions();
        _status.Text = _messages[0] = Lang.Sharing("working");
        try
        {
            if (_version == null)
            {
                var version = await _client.ResolveAsync(_code.Text, operation.Token); operation.Token.ThrowIfCancellationRequested();
                if (_closed) return; _version = version;
                _online.Children.Clear(); _online.Children.Add(Text(S("resultTitle"), 23));
                var card = new StackPanel(); card.Children.Add(Text(version.Title, 22));
                card.Children.Add(Text(string.Format(Lang.Sharing("version"), version.VersionNumber) + " · " + (version.PublisherName ?? version.Artist), subdued: true));
                var code = Text(version.FormattedCode, 25); code.Foreground = B("Accent"); card.Children.Add(code);
                card.Children.Add(Text(S("resultNote"), subdued: true)); _online.Children.Add(Card(card)); _messages[0] = S("onlineStatus");
            }
            else
            {
                var result = await _client.DownloadAsync(_version, operation.Token); operation.Token.ThrowIfCancellationRequested();
                _saving = true; UpdateActions();
                try { await Task.Run(() => _store.Import(result, _directory)); }
                finally { _saving = false; }
                Imported = true; _messages[0] = Lang.Sharing("imported");
            }
        }
        catch (OperationCanceledException) { _messages[0] = S("onlineStatus"); }
        catch (Exception error) { _messages[0] = NativeSharingWindow.ErrorText(error); }
        finally
        {
            _operation = null; _busy = false;
            if (!_closed) { _code.IsEnabled = true; _status.Text = _messages[_tabs.SelectedIndex]; UpdateActions(); }
        }
    }
    void ImportLocal()
    {
        if (_busy || _closed) return; int mode = _tabs.SelectedIndex;
        var files = _chooseFiles != null ? _chooseFiles(mode) : PickFiles(mode);
        if (files == null || files.Length == 0) return;
        if (files.Any(f => !Accepts(mode, f))) { _status.Text = _messages[mode] = S("wrongFormat"); return; }
        _busy = true; UpdateActions();
        try { _status.Text = _messages[mode] = _importFiles(files, this); }
        catch (Exception error) { _status.Text = _messages[mode] = NativeSharingWindow.ErrorText(error); }
        finally { _busy = false; UpdateActions(); }
    }
    internal static bool Accepts(int mode, string file) => (mode, Path.GetExtension(file).ToLowerInvariant()) switch
    { (1, ".smap" or ".svg" or ".png") or (2, ".txt" or ".json") or (3, ".mid" or ".midi") => true, _ => false };
    string[]? PickFiles(int mode)
    {
        var filter = mode switch { 1 => "SMAP / SVG / PNG|*.smap;*.svg;*.png", 2 => "TXT / JSON|*.txt;*.json", _ => "MIDI|*.mid;*.midi" };
        var picker = new Microsoft.Win32.OpenFileDialog { Title = S("title"), Filter = filter, Multiselect = true };
        return picker.ShowDialog(this) == true ? picker.FileNames : null;
    }
    protected override void OnClosed(EventArgs e)
    { _closed = true; _operation?.Cancel(); _transport?.Dispose(); base.OnClosed(e); }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    { if (_saving) e.Cancel = true; base.OnClosing(e); }
}
