using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class MidiImportDialog : ChromeWindow
{
    readonly MidiSource _source;
    readonly SmapProject? _previous;
    readonly List<SapmVoiceOptions> _options;
    readonly SkyPlayer _audition = new();
    readonly CancellationTokenSource _cancellation = new();
    readonly CancellationToken _token;
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    bool _advanced, _loading, _busy, _closed, _formDirty, _dirty = true;
    int _selected;
    string _name;
    SapmScope _scope;
    MidiConversion? _result;
    MidiAuditionPlan? _originalPlan, _mappedPlan;
    string _planRegion = "";
    StackPanel _work = null!;
    TextBlock _quality = null!, _profileInfo = null!, _diagnostics = null!, _status = null!;
    TextBlock? _voiceCaption;
    TextBox _nameBox = null!, _semitone = null!, _octave = null!, _from = null!, _to = null!;
    ComboBox _instrument = null!, _key = null!, _mode = null!;
    CheckBox _auto = null!, _solo = null!;
    Button _import = null!, _toggle = null!;
    DataGrid? _mapping;
    public SmapProject? ResultProject { get; private set; }

    sealed record Choice<T>(T Value, string Label) { public override string ToString() => Label; }
    static string S(string key) => Lang.S("midi3." + key);
    static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, S(key), args);
    internal static string Pitch(int? pitch)
    {
        if (pitch is not { } p) return "—";
        return new[] { "C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B" }[(p % 12 + 12) % 12] + ((int)Math.Floor(p / 12.0) - 1);
    }

    public MidiImportDialog(MidiSource source, string baseName) : this(source, baseName, null) { }
    public MidiImportDialog(SmapProject project) : this(OriginalSource(project), project.Metadata.Name, project) { }
    static MidiSource OriginalSource(SmapProject project)
    {
        ProjectValidation.Validate(project); MidiProjectConverter.ValidateOriginal(project);
        return (project.MidiConversion ?? throw new ArgumentException("工程没有 MIDI 来源。", nameof(project))).Source with { Bytes = project.OriginalMidi!.ToArray() };
    }
    MidiImportDialog(MidiSource source, string baseName, SmapProject? previous) : base(S(previous == null ? "title" : "remapTitle"), 450)
    {
        source.Validate(); if (source.Notes.Count == 0) throw new ArgumentException("MIDI 没有音符。", nameof(source));
        _token = _cancellation.Token;
        _source = source; _name = baseName; _previous = previous; _scope = previous?.MidiConversion?.Scope ?? SapmScope.WholeSong;
        _options = source.Voices.Select(v => previous?.MidiConversion?.Voices.SingleOrDefault(o => o.VoiceId == v.Id) is { } old
            ? old with { Profile = old.Profile with { Pitches = old.Profile.Pitches.ToList(), Sources = old.Profile.Sources.ToList() }, ManualKeys = new(old.ManualKeys) }
            : new SapmVoiceOptions { VoiceId = v.Id, Included = v.Channel != 10 }).ToList();
        _audition.Failed = error => Dispatcher.BeginInvoke(() => { if (!_closed) ShowError(error); });
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await Recalculate(); };
        BuildBody();
        Loaded += async (_, _) =>
        {
            if (_previous != null) { await Recalculate(); return; }
            SetBusy(true);
            try
            {
                var recommendation = await Task.Run(() => SapmMapper.Recommend(_source, "Piano", _token)[0], _token);
                if (_closed) return;
                for (int i = 0; i < _options.Count; i++) _options[i] = _options[i] with { Profile = GamePitchProfiles.Get("Piano", recommendation.Profile.Tonic) };
                LoadEditor();
            }
            catch (Exception error) { ShowError(error); }
            finally { if (!_closed) SetBusy(false); }
            if (!_closed) await Recalculate();
        };
    }

    void BuildBody()
    {
        _loading = true; _mapping = null; _voiceCaption = null;
        Width = Math.Min(_advanced ? 1080 : 450, SystemParameters.WorkArea.Width - 40);
        var body = new StackPanel { Margin = new Thickness(14) };
        var header = new DockPanel();
        _toggle = Button(_advanced ? "back" : "advanced", () =>
        {
            if (!ReadEditor()) return;
            _name = _nameBox.Text; StopAudition(); _advanced = !_advanced; BuildBody();
        });
        _toggle.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(_toggle, Dock.Right); header.Children.Add(_toggle);
        header.Children.Add(Text(_advanced ? S("title") : S("simple"), true)); body.Children.Add(header);
        body.Children.Add(Text(S("subtitle"), muted: true));
        _work = new StackPanel(); body.Children.Add(_work);
        _work.Children.Add(Text(Lang.S("midi.name"))); _nameBox = Field(_name); _work.Children.Add(_nameBox);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new() { Width = _advanced ? new GridLength(300) : new GridLength(1, GridUnitType.Star) });
        if (_advanced) columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 6, _advanced ? 18 : 0, 0) }; columns.Children.Add(left);
        left.Children.Add(Text(S("voices"), true));
        var parts = new StackPanel();
        for (int i = 0; i < _options.Count; i++)
        {
            int index = i; var voice = _source.Voices[i];
            string label = $"{voice.Track + 1}: {voice.Name} · Ch {voice.Channel} · Program {voice.Program} · Bank {voice.BankMsb}:{voice.BankLsb}";
            var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                IsChecked = _options[i].Included, Foreground = B("TextFg"), Margin = new Thickness(4, 5, 4, 5) };
            check.Checked += (_, _) => IncludePart(index, true); check.Unchecked += (_, _) => IncludePart(index, false); parts.Children.Add(check);
            if (_advanced)
            {
                var select = Button("mapping", () => { if (ReadEditor()) { StopAudition(); _selected = index; LoadEditor(); RefreshResult(); } });
                select.ToolTip = label; select.FontSize = 11; parts.Children.Add(select);
            }
        }
        left.Children.Add(new ScrollViewer { Content = parts, MaxHeight = _advanced ? 300 : 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var editor = _advanced ? new StackPanel() : left;
        if (_advanced)
        {
            Grid.SetColumn(editor, 1); columns.Children.Add(editor);
            var scopes = Combo(new[] { new Choice<SapmScope>(SapmScope.WholeSong, S("whole")), new(SapmScope.IndependentVoices, S("independent")) }, _scope);
            left.Children.Add(scopes); scopes.SelectionChanged += (_, _) => { if (!_loading) { _scope = (SapmScope)scopes.SelectedValue; Dirty(); } };
            left.Children.Add(Button("compare", async () => await CompareScopes()));
            _voiceCaption = Text(_source.Voices[_selected].Name, true); editor.Children.Add(_voiceCaption);
        }
        editor.Children.Add(Text(S("target")));
        _instrument = Combo(GamePitchProfiles.Instruments.Concat(_options.Select(v => v.Profile.Instrument)).Distinct(StringComparer.Ordinal).Select(i => new Choice<string>(i, Lang.Instrument(i))), _options[_selected].Profile.Instrument); editor.Children.Add(_instrument);
        editor.Children.Add(Text(S("key")));
        _key = Combo(Enumerable.Range(0, 12).Select(i => new Choice<int>(i, F("major", Pitch(i + 60)[..^1]))), _options[_selected].Profile.Tonic); editor.Children.Add(_key);
        editor.Children.Add(Text(S("mode")));
        _mode = Combo(new[] { new Choice<SapmMode>(SapmMode.Melody, S("melody")), new(SapmMode.Strict, S("strict")), new(SapmMode.Harmony, S("harmony")) }, _options[_selected].Mode); editor.Children.Add(_mode);
        if (_advanced)
        {
            _auto = new CheckBox { Content = S("auto"), IsChecked = true, Foreground = B("TextFg"), Margin = new Thickness(0, 8, 0, 4) }; editor.Children.Add(_auto);
            var shifts = new StackPanel { Orientation = Orientation.Horizontal };
            shifts.Children.Add(Text(S("semitone"))); _semitone = Field("0", 62); shifts.Children.Add(_semitone);
            shifts.Children.Add(Text(S("octave"))); _octave = Field("0", 62); shifts.Children.Add(_octave); editor.Children.Add(shifts);
            _auto.Checked += (_, _) => FormChanged(); _auto.Unchecked += (_, _) => FormChanged();
            _semitone.LostKeyboardFocus += (_, _) => FormChanged(); _octave.LostKeyboardFocus += (_, _) => FormChanged();
        }
        _instrument.SelectionChanged += (_, _) => FormChanged(); _key.SelectionChanged += (_, _) => FormChanged(); _mode.SelectionChanged += (_, _) => FormChanged();
        _profileInfo = Text("", muted: true); editor.Children.Add(_profileInfo); editor.Children.Add(Text(S("reason"), muted: true));
        if (_advanced) editor.Children.Add(Button("sourceLink", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_options[_selected].Profile.KeySourceUrl) { UseShellExecute = true })));
        _work.Children.Add(columns);
        var details = _advanced ? editor : _work;
        details.Children.Add(Text(S("quality"), true)); _quality = Text(S("calculating")); details.Children.Add(_quality);
        if (_advanced) details.Children.Add(Button("formula", () => MsgBox.Info(this, S("formulaText"), S("formula"))));
        _diagnostics = Text("", muted: true);
        details.Children.Add(new Expander { Header = S("diagnostics"), Foreground = B("TextFg"), IsExpanded = _source.Issues.Count > 0,
            Content = new ScrollViewer { Content = _diagnostics, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        details.Children.Add(Text(S("region")));
        var region = new Grid(); region.ColumnDefinitions.Add(new()); region.ColumnDefinitions.Add(new());
        var start = new StackPanel(); start.Children.Add(Text(S("start"), muted: true)); _from = Field("0"); start.Children.Add(_from);
        var end = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; end.Children.Add(Text(S("end"), muted: true)); _to = Field(""); end.Children.Add(_to);
        region.Children.Add(start); Grid.SetColumn(end, 1); region.Children.Add(end); details.Children.Add(region);
        _from.TextChanged += (_, _) => InvalidatePlans(); _to.TextChanged += (_, _) => InvalidatePlans();
        _solo = new CheckBox { Content = S("solo"), Foreground = B("TextFg"), Visibility = _advanced ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 2) };
        _solo.Checked += (_, _) => InvalidatePlans(); _solo.Unchecked += (_, _) => InvalidatePlans(); details.Children.Add(_solo);
        var preview = new WrapPanel(); preview.Children.Add(Button("original", async () => await Preview(true))); preview.Children.Add(Button("mapped", async () => await Preview(false)));
        preview.Children.Add(Button("stop", StopAudition)); details.Children.Add(preview); details.Children.Add(Text(S("referenceNotice"), muted: true));
        if (_advanced)
        {
            _work.Children.Add(Text(S("mapping"), true));
            _mapping = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, Height = 210,
                Foreground = B("TextFg"), Background = B("BoxBg"), RowBackground = B("BoxBg"), BorderBrush = B("BoxBorder"),
                EnableRowVirtualization = true, EnableColumnVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column };
            var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(ForegroundProperty, B("TextFg"))); headerStyle.Setters.Add(new Setter(BackgroundProperty, B("NeutralBtnBg")));
            headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6))); _mapping.ColumnHeaderStyle = headerStyle;
            var editingStyle = new Style(typeof(TextBox)); editingStyle.Setters.Add(new Setter(ForegroundProperty, B("TextFg")));
            editingStyle.Setters.Add(new Setter(BackgroundProperty, B("BoxBg"))); editingStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, B("TextFg")));
            var elementStyle = new Style(typeof(TextBlock)); elementStyle.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(MappingRow.Detail))));
            void Column(string title, string path, double width, bool editable = false) => _mapping.Columns.Add(new DataGridTextColumn
            { Header = S(title), Binding = new Binding(path) { Mode = editable ? BindingMode.TwoWay : BindingMode.OneWay, ValidatesOnExceptions = editable },
                IsReadOnly = !editable, ElementStyle = elementStyle, EditingElementStyle = editingStyle, Width = new DataGridLength(width, DataGridLengthUnitType.Star) });
            Column("time", nameof(MappingRow.Time), .7); Column("pitch", nameof(MappingRow.Original), .7); Column("final", nameof(MappingRow.Final), .7);
            Column("keyNumber", nameof(MappingRow.Key), .8, true); Column("why", nameof(MappingRow.Reason), 2);
            _work.Children.Add(_mapping);
        }
        _status = Text("", muted: true); body.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Button("recalculate", async () => await Recalculate()));
        var cancel = Button("", () => Close()); cancel.Content = Lang.S("d.cancel"); cancel.IsCancel = true; buttons.Children.Add(cancel);
        _import = Button(_previous == null ? "import" : "saveVersion", async () => await Import()); _import.Background = new SolidColorBrush(Color.FromRgb(0x2f, 0x6f, 0xd0)); _import.Foreground = Brushes.White;
        buttons.Children.Add(_import);
        var container = new StackPanel();
        container.Children.Add(new ScrollViewer { Content = body, MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 195), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        buttons.Margin = new Thickness(14, 4, 14, 10); container.Children.Add(buttons); SetBody(container);
        _loading = false; LoadEditor(); RefreshResult(); SetBusy(_busy);
    }

    TextBlock Text(string value, bool bold = false, bool muted = false) => new()
    { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = B(muted ? "SubTextFg" : "TextFg"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        FontSize = muted ? 12 : 14, Margin = new Thickness(0, bold ? 12 : 6, 4, 4) };
    TextBox Field(string value, double width = double.NaN) => new()
    { Text = value, Width = width, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Background = B("BoxBg"),
        Foreground = B("TextFg"), CaretBrush = B("TextFg"), BorderBrush = B("BoxBorder"), Margin = new Thickness(0, 2, 6, 4), Template = TextBoxTpl() };
    ComboBox Combo<T>(IEnumerable<Choice<T>> choices, T value) => new()
    { ItemsSource = choices.ToArray(), DisplayMemberPath = nameof(Choice<T>.Label), SelectedValuePath = nameof(Choice<T>.Value), SelectedValue = value,
        MinHeight = 32, Foreground = B("TextFg"), Background = B("BoxBg"), BorderBrush = B("BoxBorder"), Margin = new Thickness(0, 2, 0, 4), Template = ComboTpl() };
    Button Button(string label, Action action)
    {
        var button = new Button { Content = S(label), Padding = new Thickness(12, 6, 12, 6), MinHeight = 32, Margin = new Thickness(0, 4, 8, 4),
            Foreground = B("NeutralBtnFg"), Background = B("NeutralBtnBg"), BorderBrush = B("BtnBorder"), BorderThickness = new Thickness(1), Template = BtnTpl() };
        button.Click += (_, _) => action(); return button;
    }
}
