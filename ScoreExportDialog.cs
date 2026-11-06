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
    readonly CheckBox _midi, _simplify;
    readonly Image _preview = new() { Stretch = Stretch.Uniform };
    readonly TextBlock _status, _pageLabel, _layoutNote;
    readonly StackPanel _settings;
    readonly Button _export, _prepare, _previous, _next, _standard, _compact;
    readonly CancellationTokenSource _cancel = new();
    ScoreExportDocument? _document;
    int _page;
    bool _busy, _closed;
    ScoreSpacing _spacing;
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
            var check = new CheckBox { Content = new TextBlock { Text = string.IsNullOrWhiteSpace(track.Name) ? S("track") : track.Name, TextWrapping = TextWrapping.Wrap },
                IsChecked = track.Id == activeTrack, Foreground = B("TextFg"), Margin = new Thickness(0, 5, 0, 5) };
            check.Checked += (_, _) => InvalidatePreview(); check.Unchecked += (_, _) => InvalidatePreview();
            _tracks[track.Id] = check; list.Children.Add(check);
        }
        _settings.Children.Add(new ScrollViewer { Content = list, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _settings.Children.Add(Label(S("orientation"))); _orientation = Combo(new[] { S("portrait"), S("landscape") }); _settings.Children.Add(_orientation);
        _settings.Children.Add(Label(S("layout")));
        var modes = new Grid(); modes.ColumnDefinitions.Add(new ColumnDefinition()); modes.ColumnDefinitions.Add(new ColumnDefinition());
        _standard = Neutral(S("standard")); _compact = Neutral(S("compact")); _standard.Margin = new Thickness(0, 0, 4, 0); _compact.Margin = new Thickness(4, 0, 0, 0);
        _standard.Click += (_, _) => SetSpacing(ScoreSpacing.Standard); _compact.Click += (_, _) => SetSpacing(ScoreSpacing.Compact);
        modes.Children.Add(_standard); Grid.SetColumn(_compact, 1); modes.Children.Add(_compact); _settings.Children.Add(modes);
        _layoutNote = Label(S("standardNote")); _settings.Children.Add(_layoutNote);
        _simplify = new CheckBox { Content = new TextBlock { Text = S("simplifySpeed"), TextWrapping = TextWrapping.Wrap }, IsChecked = true,
            Foreground = B("TextFg"), Margin = new Thickness(0, 8, 0, 5), ToolTip = S("displayOnly") };
        _simplify.Checked += (_, _) => InvalidatePreview(); _simplify.Unchecked += (_, _) => InvalidatePreview(); _settings.Children.Add(_simplify);
        _settings.Children.Add(Label(S("format"))); _format = Combo(new[] { "SVG + PNG", "SVG", "PNG" }); _settings.Children.Add(_format);
        _midi = new CheckBox { Content = new TextBlock { Text = S("includeMidi"), TextWrapping = TextWrapping.Wrap }, Foreground = B("TextFg"),
            IsChecked = false, Visibility = project.OriginalMidi == null ? Visibility.Collapsed : Visibility.Visible, Margin = new Thickness(0, 12, 0, 8) };
        _midi.Checked += (_, _) => InvalidatePreview(); _midi.Unchecked += (_, _) => InvalidatePreview(); _settings.Children.Add(_midi);
        _settings.Children.Add(Label(S("dataNotice")));
        var actions = new StackPanel(); _prepare = Neutral(S("preview")); _prepare.Margin = new Thickness(0, 12, 0, 8); _prepare.Click += async (_, _) => await Prepare(); actions.Children.Add(_prepare);
        _export = Neutral(S("export")); _export.IsEnabled = false; _export.SetResourceReference(BackgroundProperty, "PracticeActionBg"); _export.SetResourceReference(ForegroundProperty, "ActionFg");
        _export.Click += async (_, _) => await Export(); actions.Children.Add(_export);
        var left = new Grid(); left.RowDefinitions.Add(new RowDefinition()); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(new ScrollViewer { Content = _settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetRow(actions, 1); left.Children.Add(actions); root.Children.Add(left);
        var viewer = new Border { Background = B("PanelBg"), BorderBrush = B("BoxBorder"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = _preview };
        Grid.SetColumn(viewer, 2); root.Children.Add(viewer);
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _previous = Neutral("‹"); _previous.Width = 48; _previous.IsEnabled = false; _previous.Click += (_, _) => { _page--; ShowPage(); }; navigation.Children.Add(_previous);
        _pageLabel = Label("—"); _pageLabel.VerticalAlignment = VerticalAlignment.Center; _pageLabel.Margin = new Thickness(16, 0, 16, 0); navigation.Children.Add(_pageLabel);
        _next = Neutral("›"); _next.Width = 48; _next.IsEnabled = false; _next.Click += (_, _) => { _page++; ShowPage(); }; navigation.Children.Add(_next);
        bottom.Children.Add(navigation); _status = Label(S("choose")); _status.MinHeight = 42; bottom.Children.Add(_status);
        Grid.SetRow(bottom, 1); Grid.SetColumnSpan(bottom, 3); root.Children.Add(bottom); SetBody(root); SetSpacing(ScoreSpacing.Standard);
    }
    void SetSpacing(ScoreSpacing spacing)
    {
        _spacing = spacing;
        foreach (var (button, selected) in new[] { (_standard, spacing == ScoreSpacing.Standard), (_compact, spacing == ScoreSpacing.Compact) })
        { button.Background = B(selected ? "PracticeActionBg" : "NeutralBtnBg"); button.Foreground = B(selected ? "ActionFg" : "NeutralBtnFg"); }
        _layoutNote.Text = S(spacing == ScoreSpacing.Standard ? "standardNote" : "compactNote"); InvalidatePreview();
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
        _preview.Source = null; if (_pageLabel != null) _pageLabel.Text = "—";
    }
    void Busy(bool value)
    {
        _busy = value; _settings.IsEnabled = _prepare.IsEnabled = !value; _export.IsEnabled = !value && _document != null; _previous.IsEnabled = _next.IsEnabled = false;
        if (!value && _document != null) { _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < _document.Pages.Count; }
    }
    async Task Prepare()
    {
        var options = new ScoreExportOptions { TrackIds = _tracks.Where(t => t.Value.IsChecked == true).Select(t => t.Key).ToArray(), Orientation = (ScoreOrientation)_orientation.SelectedIndex,
            Spacing = _spacing, SimplifySpeed = _simplify.IsChecked == true, IncludeOriginalMidi = _midi.IsChecked == true };
        InvalidatePreview();
        Busy(true); _status.Text = S("working");
        try
        {
            var document = await Task.Run(() => ScoreExport.Prepare(_project, options, _cancel.Token));
            if (_closed) return;
            _document = document; _page = 0; ShowPage(); _export.IsEnabled = true;
            _status.Text = string.Format(S("ready"), document.Pages.Count) + (document.Notices.Count > 0 ? "\n" + string.Join("\n", document.Notices.Select(ErrorText)) : "");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_closed) { InvalidatePreview(); _status.Text = ErrorText(error.Message); } }
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
