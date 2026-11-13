using Border = SMAP_WPF.G2Border;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class EditorWindow : Window
{
    readonly SkyPlayer _audition = new();
    readonly DispatcherTimer _playTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly ScrollViewer _trackScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly ScrollViewer _inspectorScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Button[] _keys = new Button[15];
    readonly EditorViewPreferences _view;
    readonly bool _persistView;
    EditorSession _session = null!;
    bool _advanced, _compact, _drawerInspector, _follow = true, _keyboard = true, _keyboardExplicit, _refreshing, _scrolling, _playing, _busy, _closed;
    decimal _extentBeat = 16;
    string? _path;
    Button _play = null!, _undo = null!, _redo = null!, _tempo = null!, _meter = null!;
    ComboBox _snap = null!, _tools = null!;
    JoinedChoiceButton _snapChoice = null!, _toolChoice = null!, _trackChoice = null!;
    ContextMenu? _editorMenu;
    CommandMenuButton? _commandMenu;
    static string S(string key) => Lang.S("ed." + key);
    static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, S(key), args);
    internal EditorSession Session => _session;
    internal PianoRoll Viewport => Roll;
    internal bool Advanced => _advanced;
    public event Action<string>? Saved;

    public EditorWindow() : this(EditorSession.NewProject(), true) { }
    internal EditorWindow(SmapProject project, bool persistView = false)
    {
        InitializeComponent(); _persistView = persistView; _view = persistView ? EditorViewPreferences.Load() : new();
        Overview.Roll=Roll;Overview.TrackColor=id=>_view.Color(id,_session.Project.Tracks.FindIndex(t=>t.Id==id));
        BuildToolbar();
        MoreButton.Chrome.CornerRadius = new CornerRadius(6);
        TrackSelect.DisplayMemberPath = nameof(TrackChoice.Name); TrackSelect.SelectedValuePath = nameof(TrackChoice.Id);
        TrackSelect.SelectionChanged += (_, _) => { if (!_refreshing && TrackSelect.SelectedValue is Guid id) SwitchTrack(id); };
        _trackChoice = MakeEditorChoice(TrackSelect, 185, TrackChoiceButton);
        Roll.EditStarting = PauseForEdit; Roll.Status = text => StatusText.Text = text;
        Roll.TrackColor = id => _view.Color(id, _session.Project.Tracks.FindIndex(t => t.Id == id));
        Roll.PlayheadChanged = () => { if (_playing) _audition.Seek(Math.Max(0, (double)((_displayTimeline ?? new ProjectTimeline(_session.Project.Tempos)).BeatToMicroseconds(Roll.PlayheadBeat) / 1000) - _playOffsetMs)); RefreshStatus(); };
        Roll.ViewChanged = SyncScrollbars;
        HorizontalScroll.ValueChanged += (_, _) => { if (!_scrolling) Roll.SetView((decimal)HorizontalScroll.Value, Roll.VerticalOffset); };
        VerticalScroll.ValueChanged += (_, _) => { if (!_scrolling) Roll.SetView(Roll.ViewBeat, VerticalScroll.Value); };
        ModeButton.Click += (_, _) => ToggleAdvanced(); MoreButton.Click += (_, _) => ShowMore();
        SaveButton.Click += (_, _) => Save(); PropertiesButton.Click += (_, _) => ShowProperties();
        FollowButton.Click += (_, _) => { _follow = !_follow; RefreshLabels(); };
        KeyboardButton.Click += (_, _) => { _keyboard = MapGrid.Visibility != Visibility.Visible; _keyboardExplicit = true; RefreshLayoutMode(); RefreshLabels(); };
        BuildKeyboard();
        Deactivated+=(_,_)=>{StopKeyPreview();if(!_closed)RefreshEditorKeys();};
        _playTimer.Tick += (_, _) => TickPlayback();
        _audition.Failed = error => Dispatcher.BeginInvoke(() => { if (!_closed) { StopPlayback(); ShowError(error); } });
        SizeChanged += (_, _) => { _commandMenu?.Close(true); if(_editorMenu!=null)_editorMenu.IsOpen=false;RefreshLayoutMode(); };
        Theme.Changed += ThemeChanged;
        Attach(project); Loaded += (_, _) => { RefreshLayoutMode(); Roll.Focus(); _=PrepareKeyOutput(); };
    }
    void Attach(SmapProject project)
    {
        if (_session != null) { _session.Changed -= Changed; _session.SelectionChanged -= SelectionChanged; }
        _session = new(project) { AllowUnsupportedEditing = true }; Roll.Session = _session; _session.Changed += Changed; _session.SelectionChanged += SelectionChanged;
        Changed();
    }
    public void LoadSong(SongInfo song)
    {
        StopPlayback();
        if (Path.GetExtension(song.File).Equals(".smap", StringComparison.OrdinalIgnoreCase))
        { Attach(SmapFile.Load(song.File)); _path = song.File; }
        else { Attach(LegacyProjectAdapter.FromDocument(SongLibrary.LoadDocument(song))); _path = null; }
        Roll.PlayheadBeat = 0; Roll.SetView(0); RefreshTitle();
    }
    void Changed()
    {
        if (_closed) return;
        _extentBeat = _session.Project.Tracks.SelectMany(t => t.Notes).Select(n => n.Beat + n.LengthBeats).DefaultIfEmpty(16).Max();
        RefreshTitle(); RefreshTracks(); RefreshInspector(); RefreshLabels(); SyncScrollbars();
    }
    void SelectionChanged() { if (!_closed) { RefreshInspector(); RefreshLabels(); } }
    void ThemeChanged() { if (!_closed) { Roll.InvalidateLayers(); RefreshTracks(); RefreshInspector(); } }
    void RefreshTitle() => Title = S("title") + " · " + (string.IsNullOrWhiteSpace(_session.Project.Metadata.Name) ? S("untitled") : _session.Project.Metadata.Name) + (_session.IsDirty ? " *" : "");
    sealed record TrackChoice(Guid Id, string Name) { public override string ToString() => Name; }
    sealed record SnapChoice(int Subdiv, string Name) { public override string ToString() => Name; }
    void BuildToolbar()
    {
        _play = MakeButton(S("play"), async () => await TogglePlayback()); ToolBar.Children.Add(_play);
        _play.SetResourceReference(Control.BackgroundProperty, "PracticeActionBg"); _play.SetResourceReference(Control.ForegroundProperty, "ActionFg");
        _play.Height = 32;
        ToolBar.Children.Add(MakeButton("■", StopPlayback, S("stop")));
        _undo = MakeButton("↶", () => Edit(_session.Undo), S("undo")); _redo = MakeButton("↷", () => Edit(_session.Redo), S("redo"));
        ToolBar.Children.Add(_undo); ToolBar.Children.Add(_redo);
        _snap = new ComboBox { Visibility = Visibility.Collapsed, SelectedValuePath = nameof(SnapChoice.Subdiv), ToolTip = S("snapTip") };
        _snap.SelectionChanged += (_, _) => { if (_refreshing || _snap.SelectedValue is not int grid) return; Roll.SnapEnabled = grid != 0; if (grid > 0) _session.SetGrid(grid); };
        ToolBar.Children.Add(_snap);
        _snapChoice = MakeEditorChoice(_snap, 115); ToolBar.Children.Add(_snapChoice);
        _tools = new ComboBox { Visibility = Visibility.Collapsed, ItemsSource = new[] { S("draw"), S("paint"), S("erase") }, SelectedIndex = 0 };
        _tools.SelectionChanged += (_, _) => Roll.Tool = (RollTool)_tools.SelectedIndex; ToolBar.Children.Add(_tools);
        _toolChoice = MakeEditorChoice(_tools, 105); ToolBar.Children.Add(_toolChoice);
        _tempo = MakeButton("120 BPM ▾", ShowTempoMenu); _meter = MakeButton("4/4 ▾", ShowMeterMenu);
        ToolBar.Children.Add(_tempo); ToolBar.Children.Add(_meter);
    }
    JoinedChoiceButton MakeEditorChoice(ComboBox model, double width, JoinedChoiceButton? existing = null)
    {
        var label = (DataTemplate)FindResource("EditorChoiceLabel");
        var button = existing ?? new JoinedChoiceButton();
        button.Width = width; button.Height = 32; button.Margin = existing == null ? new Thickness(3) : new Thickness(0); button.FontSize = 12; button.ContentTemplate = label;
        button.Chrome.CornerRadius = new CornerRadius(6);
        button.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("SelectedItem") { Source = model });
        button.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding("IsEnabled") { Source = model });
        button.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("SelectedItem") { Source = model });
        var items = new Style(typeof(ListBoxItem), button.Options.ItemContainerStyle);
        items.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, label));
        items.Setters.Add(new Setter(ToolTipProperty, new System.Windows.Data.Binding("Content") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.Self) }));
        button.Options.ItemContainerStyle = items;
        button.IsEnabledChanged += (_, _) => { if (!button.IsEnabled) button.Close(true); };
        VirtualizingPanel.SetScrollUnit(button.Options, ScrollUnit.Pixel); SmoothScroll.SetEnabled(button.Options, true);
        button.Click += (_, _) =>
        {
            if (!ReferenceEquals(button, _snapChoice)) _snapChoice.Close(true);
            if (!ReferenceEquals(button, _toolChoice)) _toolChoice.Close(true);
            if (!ReferenceEquals(button, _trackChoice)) _trackChoice?.Close(true);
            if (_editorMenu != null) _editorMenu.IsOpen = false;
            _commandMenu?.Close(true);
            button.Toggle(model);
            if (button.IsOpen && button.Options.SelectedItem is { } selected)
                Dispatcher.BeginInvoke(() => button.Options.ScrollIntoView(selected), DispatcherPriority.Loaded);
        };
        return button;
    }
    void CloseEditorChoices() { _snapChoice.Close(true); _toolChoice.Close(true); _trackChoice.Close(true); }
    void ToggleCommandMenu(CommandMenuButton button, ContextMenu model)
    {
        CloseEditorChoices();
        if (_editorMenu != null) _editorMenu.IsOpen = false;
        if (!ReferenceEquals(_commandMenu, button)) _commandMenu?.Close(true);
        _commandMenu = button; button.ReduceMotion = _view.ReduceMotion;
        button.Closed -= CommandMenuClosed; button.Closed += CommandMenuClosed;
        button.Toggle(model, EditorMenuLayer);
    }
    void CommandMenuClosed(object? sender, EventArgs e)
    {
        if (sender is CommandMenuButton button) button.Closed -= CommandMenuClosed;
        if (ReferenceEquals(_commandMenu, sender)) _commandMenu = null;
    }
    internal void ToggleAdvanced()
    {
        _advanced = !_advanced; Roll.References = _advanced; RefreshLayoutMode(); RefreshLabels();
    }
    void RefreshLayoutMode()
    {
        double width = _workspaceRoot?.ActualWidth ?? ActualWidth;
        _compact = width > 0 && width < 1060;
        bool tracks = _advanced && !_compact;
        TrackColumn.Width = new GridLength(tracks ? 268 : 0);
        TrackSurface.Visibility = tracks ? Visibility.Visible : Visibility.Collapsed;
        bool inspector = _advanced && !_compact && _session.Project.MidiConversion != null;
        InspectorColumn.Width = new GridLength(inspector ? 262 : 0);
        InspectorSurface.Visibility = inspector ? Visibility.Visible : Visibility.Collapsed;
        if (Drawer.Visibility != Visibility.Visible)
        {
            TrackHost.Content = null; InspectorHost.Content = null;
            if (_advanced && !_compact) TrackHost.Content = _trackScroll;
            if (inspector) InspectorHost.Content = _inspectorScroll;
        }
        MapGrid.Visibility = _keyboard && (!_compact || _keyboardExplicit) && (_workspaceOwner?.ActualHeight ?? Height) > 600 ? Visibility.Visible : Visibility.Collapsed;
        HintText.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        ModeButton.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        _tempo.Visibility = _meter.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        RefreshLabels();
    }
    void RefreshLabels()
    {
        _refreshing = true;
        MoreButton.Content = S("more"); ModeButton.Content = S(_advanced ? "simple" : "advanced"); SaveButton.Content = S("save");
        PropertiesButton.Content = S(_compact ? "panels" : "properties"); FollowButton.Content = S("follow") + (_follow ? " ☑" : " ☐");
        KeyboardButton.Content = S("keyboard") + (MapGrid.Visibility == Visibility.Visible ? " ▴" : " ▸"); HintText.Text = S("hint");
        Overview.ToolTip=S("overview");System.Windows.Automation.AutomationProperties.SetName(Overview,S("overview"));
        double PlayTextWidth(string text) => new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_play.FontFamily, _play.FontStyle, _play.FontWeight, _play.FontStretch), _play.FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(_play).PixelsPerDip).WidthIncludingTrailingWhitespace;
        _play.Width = Math.Max(_play.MinWidth, Math.Ceiling(Math.Max(PlayTextWidth(S("play")), PlayTextWidth(S("pause")))
            + _play.Padding.Left + _play.Padding.Right + _play.BorderThickness.Left + _play.BorderThickness.Right));
        _play.Content = S(_playing ? "pause" : "play"); _undo.IsEnabled = _session.CanUndo; _redo.IsEnabled = _session.CanRedo;
        _snap.ItemsSource = new[] { 0, 4, 8, 3, 6, 12, _session.Project.GridSubdiv }.Distinct().Select(g => new SnapChoice(g, g == 0 ? S("noSnap") : S("snap") + " 1/" + g)).ToArray();
        _snap.SelectedValue = Roll.SnapEnabled ? _session.Project.GridSubdiv : 0;
        var tempo = _session.Project.Tempos.Where(t => t.Beat <= Roll.PlayheadBeat).OrderBy(t => t.Beat).Last();
        var meter = _session.Project.Meters.Where(t => t.Beat <= Roll.PlayheadBeat).OrderBy(t => t.Beat).Last();
        _tempo.Content = $"{60_000_000m / tempo.MicrosecondsPerBeat:0.##} BPM ▾"; _meter.Content = $"{meter.Numerator}/{meter.Denominator} ▾";
        TrackSelect.ItemsSource = _session.Project.Tracks.Select((t, i) => new TrackChoice(t.Id, $"{i + 1:00} {TrackName(t)}")).ToArray(); TrackSelect.SelectedValue = _session.ActiveTrackId;
        TrackCaption.Text = F("editing", TrackName(_session.ActiveTrack)) + " · " + Lang.Instrument(_session.ActiveTrack.PreviewInstrument);
        TrackCaption.ToolTip = TrackCaption.Text;
        RefreshEditorKeys();
        RefreshStatus(); _refreshing = false;
    }
    void RefreshStatus()
    {
        RefreshCursorKeyColors();
        Overview.RefreshView();
        StatusText.Text = F("status", TrackName(_session.ActiveTrack), _session.Selected.Count, _session.ActiveTrack.Notes.Count);
        ZoomText.Text = $"X {Roll.Zoom:P0}  Y {Roll.VerticalZoom:P0}  · {PianoRoll.TimeLabel(new ProjectTimeline(_session.Project.Tempos).BeatToMicroseconds(Roll.PlayheadBeat) / 1_000_000)}";
    }
    string TrackName(ProjectTrack track) => string.IsNullOrWhiteSpace(track.Name) ? S("track") : track.Name;
    void SyncScrollbars()
    {
        if (_session == null || _scrolling) return; _scrolling = true;
        HorizontalScroll.Maximum = (double)Math.Max(0, Math.Max(_extentBeat + 8, Roll.ViewBeat + Roll.VisibleBeats) - Roll.VisibleBeats);
        HorizontalScroll.ViewportSize = (double)Roll.VisibleBeats; HorizontalScroll.SmallChange = 1.0 / _session.Project.GridSubdiv; HorizontalScroll.Value = (double)Roll.ViewBeat;
        VerticalScroll.Maximum = Math.Max(0, 15 * Roll.RowHeight - Math.Max(1, Roll.ActualHeight - PianoRoll.RulerHeight));
        VerticalScroll.ViewportSize = Math.Max(1, Roll.ActualHeight - PianoRoll.RulerHeight); VerticalScroll.Value = Roll.VerticalOffset;
        _scrolling = false; RefreshStatus();
    }
    void SwitchTrack(Guid id)
    {
        if (_busy || id == _session.ActiveTrackId) return;
        PauseForEdit(); Roll.CancelGesture(); _session.SelectTrack(id); RefreshTracks(); RefreshLabels();
        if (Drawer.Visibility == Visibility.Visible) DismissDrawer();
    }
    void ShowDrawer(bool inspector)
    {
        _drawerInspector = inspector; TrackHost.Content = null; InspectorHost.Content = null; DrawerHost.Content = null;
        DrawerHost.Content = inspector ? _inspectorScroll : _trackScroll; Drawer.Visibility = Visibility.Visible;
        AnimatePanel(DrawerHost);
    }
    void DismissDrawer()
    {
        DrawerHost.Content = null; Drawer.Visibility = Visibility.Collapsed; RefreshLayoutMode(); Roll.Focus();
    }
    void DismissDrawer_Click(object sender, RoutedEventArgs e) => DismissDrawer();
    void ShowProperties()
    {
        _commandMenu?.Close(true);
        if (_compact)
        {
            var menu = NewMenu(); AddMenu(menu, S("tracks"), () => ShowDrawer(false)); AddMenu(menu, S("properties"), () => ShowDrawer(true));
            PropertiesButton.ContextMenu = menu; menu.IsOpen = true;
        }
        else if (_session.Selected.Count > 0) EditNoteProperties(); else EditTrack(_session.ActiveTrack);
    }
    void Edit(Action action)
    {
        if (_busy) return; PauseForEdit(); try { action(); } catch (Exception error) { ShowError(error); }
    }
    void PauseForEdit()
    {
        if (_playing) { var ms = _audition.PositionMs + _playOffsetMs; _audition.Pause(true); Roll.PlayheadBeat = (_displayTimeline ?? new ProjectTimeline(_session.Project.Tempos)).MicrosecondsToBeat((decimal)ms * 1000); _playing = false; _playTimer.Stop(); _play.Content = S("play"); RefreshStatus(); }
    }
    void ShowError(Exception error)
    {
        Logger.Exception("编辑操作失败", error); string key = "ed.error." + error.Message; string message = Lang.S(key);
        if (message == key && error.Message.StartsWith("Score", StringComparison.Ordinal))
        { key = "score." + error.Message; message = Lang.S(key); }
        if (message == key) message = S("error"); StatusText.Text = message;
        MsgBox.Info(DialogOwner, message, S("title"));
    }
    static Button MakeButton(string text, Action action, string? tooltip = null)
    {
        var button = new Button { Content = text, ToolTip = tooltip ?? text }; button.Click += (_, _) => action(); return button;
    }
    static TextBlock Label(string text, bool muted = false, bool bold = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(4, 6, 4, 4), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, muted ? "SubTextFg" : "TextFg"); return t;
    }
    ContextMenu NewMenu(FrameworkElement? anchor = null)
    {
        if(_editorMenu!=null)_editorMenu.IsOpen=false;
        var owner = DialogOwner;
        var menu = new ContextMenu { Width=320,Style=(Style)FindResource(typeof(ContextMenu)),PlacementTarget=anchor??owner,Placement=PlacementMode.Custom };
        menu.Resources[typeof(ScrollBar)]=FindResource(typeof(ScrollBar));menu.Resources[typeof(MenuItem)]=FindResource(typeof(MenuItem));menu.Resources[typeof(Separator)]=FindResource(typeof(Separator));
        var origin=anchor?.TranslatePoint(new Point(),owner)??Mouse.GetPosition(owner);
        menu.MaxHeight=Math.Max(100,Math.Min(420,owner.ActualHeight-(anchor==null?origin.Y:origin.Y+anchor.ActualHeight)-20));
        menu.CustomPopupPlacementCallback=(size,target,offset)=>
        {
            double x=anchor==null?0:anchor.ActualWidth-size.Width,y=anchor?.ActualHeight??0;
            x=Math.Clamp(x,8-origin.X,Math.Max(8-origin.X,owner.ActualWidth-8-origin.X-size.Width));
            y=Math.Clamp(y,8-origin.Y,Math.Max(8-origin.Y,owner.ActualHeight-8-origin.Y-size.Height));
            return new[]{new CustomPopupPlacement(new Point(x,y),PopupPrimaryAxis.None)};
        };
        menu.Opened+=(_,_)=>StyleMenuItems(menu.Items);
        return _editorMenu=menu;
    }
    void StyleMenuItems(ItemCollection items)
    {
        foreach(var item in items.OfType<MenuItem>())
        {item.Style=(Style)FindResource(typeof(MenuItem));item.ToolTip=item.Header;StyleMenuItems(item.Items);}
    }
    void AddMenu(ContextMenu menu, string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => { try { action(); } finally { if (!_closed && IsEnabled) Roll.Focus(); } };
        menu.Items.Add(item);
    }
    void Title_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) ToggleMaximize(); else if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    void Surface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var surface = (Border)sender;
        surface.Clip = G2Geometry.Create(new Rect(surface.RenderSize), surface.CornerRadius);
    }
    void Min_Click(object sender, MouseButtonEventArgs e) { WindowState = WindowState.Minimized; e.Handled = true; }
    void Max_Click(object sender, MouseButtonEventArgs e) { ToggleMaximize(); e.Handled = true; }
    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, MouseButtonEventArgs e) { Close(); e.Handled = true; }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_session.IsDirty && !ConfirmClose()) { e.Cancel = true; return; }
        base.OnClosing(e);
    }
    protected override void OnClosed(EventArgs e)
    {
        _closed = true; _playTimer.Stop(); Roll.CancelGesture(); Theme.Changed -= ThemeChanged;
        ReleaseWorkspace();
        Overview.EndRangeDrag();
        CloseEditorChoices();
        _commandMenu?.Close(true);
        if(_editorMenu!=null)_editorMenu.IsOpen=false;
        _session.Changed -= Changed; _session.SelectionChanged -= SelectionChanged;
        try { _audition.Dispose(); } finally { StopKeyPreview(); if (_persistView) _view.Save(); base.OnClosed(e); }
    }
}
