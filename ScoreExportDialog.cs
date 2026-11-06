using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class ScoreExportDialog : ChromeWindow
{
    readonly SmapProject _project;
    readonly Dictionary<Guid, CheckBox> _tracks = new();
    readonly ComboBox _orientation, _format;
    readonly CheckBox _midi;
    readonly Image _preview = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _status, _pageLabel;
    readonly StackPanel _settings;
    readonly Button _export, _previous, _next;
    readonly CancellationTokenSource _cancel = new();
    ScoreExportDocument? _document;
    int _page;
    bool _busy, _closed;
    static string S(string key) => Lang.S("score." + key);

    public ScoreExportDialog(Window owner, SmapProject project, Guid activeTrack) : base(S("title"), 980)
    {
        Owner = owner; _project = project;
        var root = new Grid { Margin = new Thickness(12), Height = 610 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _settings = new StackPanel(); _settings.Children.Add(Label(S("tracks"), 18));
        var list = new StackPanel();
        foreach (var track in project.Tracks)
        {
            var check = new CheckBox { Content = string.IsNullOrWhiteSpace(track.Name) ? S("track") : track.Name,
                IsChecked = track.Id == activeTrack, Foreground = B("TextFg"), Margin = new Thickness(0, 5, 0, 5) };
            check.Checked += (_, _) => InvalidatePreview(); check.Unchecked += (_, _) => InvalidatePreview();
            _tracks[track.Id] = check; list.Children.Add(check);
        }
        _settings.Children.Add(new ScrollViewer { Content = list, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _settings.Children.Add(Label(S("orientation"))); _orientation = Combo(new[] { S("portrait"), S("landscape") }); _settings.Children.Add(_orientation);
        _settings.Children.Add(Label(S("format"))); _format = Combo(new[] { "SVG + PNG", "SVG", "PNG" }); _settings.Children.Add(_format);
        _midi = new CheckBox { Content = new TextBlock { Text = S("includeMidi"), TextWrapping = TextWrapping.Wrap }, Foreground = B("TextFg"),
            IsChecked = false, Visibility = project.OriginalMidi == null ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 12, 0, 8) };
        _midi.Checked += (_, _) => InvalidatePreview(); _midi.Unchecked += (_, _) => InvalidatePreview(); _settings.Children.Add(_midi);
        _settings.Children.Add(Label(S("dataNotice")));
        var prepare = Neutral(S("preview")); prepare.Margin = new Thickness(0, 12, 0, 8); prepare.Click += async (_, _) => await Prepare(); _settings.Children.Add(prepare);
        _export = Neutral(S("export")); _export.IsEnabled = false; _export.SetResourceReference(BackgroundProperty, "PracticeActionBg"); _export.SetResourceReference(ForegroundProperty, "ActionFg");
        _export.Click += async (_, _) => await Export(); _settings.Children.Add(_export);
        root.Children.Add(new ScrollViewer { Content = _settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var viewer = new Border { Background = B("PanelBg"), BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = _preview };
        Grid.SetColumn(viewer, 2); root.Children.Add(viewer);
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _previous = Neutral("‹"); _previous.Width = 48; _previous.IsEnabled = false; _previous.Click += (_, _) => { _page--; ShowPage(); }; navigation.Children.Add(_previous);
        _pageLabel = Label("—"); _pageLabel.VerticalAlignment = VerticalAlignment.Center; _pageLabel.Margin = new Thickness(16, 0, 16, 0); navigation.Children.Add(_pageLabel);
        _next = Neutral("›"); _next.Width = 48; _next.IsEnabled = false; _next.Click += (_, _) => { _page++; ShowPage(); }; navigation.Children.Add(_next);
        bottom.Children.Add(navigation); _status = Label(S("choose")); _status.MinHeight = 42; bottom.Children.Add(_status);
        Grid.SetRow(bottom, 1); Grid.SetColumnSpan(bottom, 3); root.Children.Add(bottom); SetBody(root);
    }
    TextBlock Label(string text, double size = 13) => new() { Text = text, FontSize = size, Foreground = B("TextFg"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 5) };
    ComboBox Combo(string[] values)
    {
        var box = new ComboBox { ItemsSource = values, SelectedIndex = 0, Height = 34, Background = B("BoxBg"), Foreground = B("TextFg"), Template = ComboTpl() };
        box.SelectionChanged += (_, _) => InvalidatePreview(); return box;
    }
    void InvalidatePreview()
    {
        _document = null; if (_export != null) _export.IsEnabled = false;
        if (_previous != null) _previous.IsEnabled = _next.IsEnabled = false;
        _preview.Source = null;
    }
    void Busy(bool value)
    {
        _busy = value; _settings.IsEnabled = !value; _previous.IsEnabled = _next.IsEnabled = false;
        if (!value && _document != null) { _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < _document.Pages.Count; }
    }
    async Task Prepare()
    {
        var options = new ScoreExportOptions { TrackIds = _tracks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToArray(), Orientation = (ScoreOrientation)_orientation.SelectedIndex, IncludeOriginalMidi = _midi.IsChecked == true };
        Busy(true); _status.Text = S("working");
        try
        {
            var document = await Task.Run(() => ScoreExport.Prepare(_project, options, _cancel.Token));
            if (_closed) return;
            _document = document; _page = 0; ShowPage(); _export.IsEnabled = true;
            _status.Text = string.Format(S("ready"), document.Pages.Count) + (document.Notices.Count > 0 ? "\n" + string.Join("\n", document.Notices.Select(ErrorText)) : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) _status.Text = ErrorText(error.Message); }
        finally { if (!_closed) Busy(false); }
    }
    void ShowPage()
    {
        if (_document == null) return;
        _preview.Source = ScoreImageExporter.Preview(_document, _document.Pages[_page]); _pageLabel.Text = $"{_page + 1} / {_document.Pages.Count}";
        _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < _document.Pages.Count;
    }
    async Task Export()
    {
        if (_document == null || _busy) return;
        var folder = new Microsoft.Win32.OpenFolderDialog { Title = S("folder"), Multiselect = false }; if (folder.ShowDialog(this) != true) return;
        var formats = _format.SelectedIndex == 1 ? ScoreImageFormats.Svg : _format.SelectedIndex == 2 ? ScoreImageFormats.Png : ScoreImageFormats.Both;
        var document = _document; var culture = CultureInfo.CurrentCulture;
        Busy(true); _status.Text = S("working");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            CultureInfo.CurrentCulture = culture;
            try { completion.SetResult(ScoreImageExporter.Export(document, folder.FolderName, formats, _cancel.Token)); }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        try { string path = await completion.Task; if (!_closed) _status.Text = S("exported") + "\n" + path; }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) _status.Text = ErrorText(error.Message); }
        finally { if (!_closed) Busy(false); }
    }
    public static string ErrorText(string error)
    { string key = "score." + error; string translated = Lang.S(key); return translated == key ? Lang.S("score.failed") + " " + error : translated; }
    protected override void OnClosing(CancelEventArgs e) { _closed = true; _cancel.Cancel(); base.OnClosing(e); }
}
