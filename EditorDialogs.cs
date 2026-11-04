using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SMAP_WPF.Core;

namespace SMAP_WPF;

sealed class EditorFieldsDialog : ChromeWindow
{
    readonly List<TextBox> _fields = new();
    readonly ComboBox? _choice;
    public string[] Values => _fields.Select(f => f.Text.Trim()).ToArray();
    public int Selection => _choice?.SelectedIndex ?? 0;
    EditorFieldsDialog(string title, (string label, string value)[] fields, string[]? choices, int selected) : base(title, 440)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        foreach (var (label, value) in fields)
        {
            panel.Children.Add(new TextBlock { Text = label, Foreground = B("TextFg"), Margin = new Thickness(0, 7, 0, 4), TextWrapping = TextWrapping.Wrap });
            var box = new TextBox { Text = value, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Foreground = B("TextFg"), Background = B("BoxBg"), BorderBrush = B("BoxBorder"), Template = TextBoxTpl() };
            _fields.Add(box); panel.Children.Add(box);
        }
        if (choices != null)
        {
            _choice = new ComboBox { ItemsSource = choices, SelectedIndex = selected, Height = 32, Margin = new Thickness(0, 12, 0, 0), Foreground = B("TextFg"), Background = B("BoxBg"), Template = ComboTpl() }; panel.Children.Add(_choice);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
        var cancel = Neutral(Lang.S("d.cancel")); cancel.IsCancel = true; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        var ok = Neutral(Lang.S("ed.apply")); ok.IsDefault = true; ok.Margin = new Thickness(8, 0, 0, 0); ok.Click += (_, _) => DialogResult = true; buttons.Children.Add(ok); panel.Children.Add(buttons); SetBody(panel);
    }
    public static string[]? Ask(Window owner, string title, (string label, string value)[] fields) => Ask(owner, title, fields, null, 0, out _);
    public static int ConfirmSave(Window owner)
    {
        var dialog = new ChromeWindow(Lang.S("ed.saveChanges"), 430) { Owner = owner };
        var panel = new StackPanel { Margin = new Thickness(18) }; int result = 2;
        panel.Children.Add(new TextBlock { Text = Lang.S("ed.saveChanges"), Foreground = owner.TryFindResource("TextFg") as System.Windows.Media.Brush, Margin = new Thickness(0, 0, 0, 14) });
        foreach (var (label, index) in new[] { (Lang.S("ed.save"), 0), (Lang.S("ed.discard"), 1), (Lang.S("d.cancel"), 2) })
        {
            var button = new Button { Content = label, MinHeight = 34, Margin = new Thickness(0, 3, 0, 3), Template = BtnTpl() };
            button.Click += (_, _) => { result = index; dialog.Close(); }; panel.Children.Add(button);
        }
        dialog.SetBody(panel); dialog.ShowDialog(); return result;
    }
    public static string[]? Ask(Window owner, string title, (string label, string value)[] fields, string[]? choices, int selected, out int selection)
    {
        var dialog = new EditorFieldsDialog(title, fields, choices, selected) { Owner = owner }; bool accepted = dialog.ShowDialog() == true; selection = dialog.Selection; return accepted ? dialog.Values : null;
    }
}

sealed class EditorTrackDialog : ChromeWindow
{
    public ProjectTrack? Result { get; private set; }
    public EditorTrackDialog(ProjectTrack track) : base(Lang.S("ed.trackSettings"), 475)
    {
        string S(string key) => Lang.S("ed." + key);
        var panel = new StackPanel { Margin = new Thickness(18) };
        void Label(string text) => panel.Children.Add(new TextBlock { Text = text, Foreground = B("TextFg"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        ComboBox Combo(IEnumerable<string> values, string selected, Func<string,string>? display = null)
        {
            var combo = new ComboBox { ItemsSource = values.Select(v=>new ComboBoxItem{Content=display?.Invoke(v)??v,Tag=v}).ToArray(), SelectedValuePath="Tag", SelectedValue = selected, Height = 34, Foreground = B("TextFg"), Background = B("BoxBg"), Template = ComboTpl() }; panel.Children.Add(combo); return combo;
        }
        Label(S("previewInstrument")); var preview = Combo(AudioEngine.Instruments, track.PreviewInstrument,Lang.Instrument);
        Label(string.Format(S("previewOffset"), PitchConfig.Get(track.PreviewInstrument)));
        var offset = new TextBox { Text = (track.PreviewSemitoneOffset ?? PitchConfig.Get(track.PreviewInstrument)).ToString(CultureInfo.CurrentCulture), Height = 32, Foreground = B("TextFg"), Background = B("BoxBg"), Template = TextBoxTpl() }; panel.Children.Add(offset);
        Label(S("target")); var target = Combo(new[] { S("keepTarget") }.Concat(GamePitchProfiles.Instruments).Append("Custom4"), S("keepTarget"),Lang.Instrument);
        Label(S("targetKey")); var key = Combo(Enumerable.Range(0, 12).Select(n => n.ToString()), "0");
        Label(S("pitchNotice"));
        var apply = Neutral(S("apply")); apply.Margin = new Thickness(0, 16, 0, 0); apply.Click += (_, _) =>
        {
            try
            {
                int semitone = int.Parse(offset.Text, CultureInfo.CurrentCulture); if (semitone is < -48 or > 48) throw new FormatException();
                GamePitchTarget? config = track.GameTarget;
                if (target.SelectedIndex > 0)
                {
                    if ((string)target.SelectedValue=="Custom4")
                    {
                        var capability=InstrumentCapabilities.CustomFour();
                        track=track with{Capability=capability,KeyDomain=track.Capability==null?NoteKeyDomain.StableLogical:track.KeyDomain,
                            GameTarget=new(){InstrumentId=capability.InstrumentId,ProfileId=capability.Id,ProfileVersion=capability.Version}};
                        config=track.GameTarget;
                    }
                    else
                    {
                    var profile = GamePitchProfiles.Get((string)target.SelectedValue, int.Parse((string)key.SelectedValue, CultureInfo.InvariantCulture));
                    if (profile.Verification == PitchVerification.ConflictingSources && !MsgBox.Choice(this, Lang.S("midi3.conflict"), S("target"), Lang.S("d.ok"), Lang.S("d.cancel"))) return;
                    track = InstrumentCapabilities.WithTarget(track, profile); config = track.GameTarget;
                    }
                }
                Result = track with { PreviewInstrument = (string)preview.SelectedValue, PreviewSemitoneOffset = semitone, GameTarget = config }; DialogResult = true;
            }
            catch (Exception error) { Logger.Exception("轨道配置无效", error); MsgBox.Info(this, S("error"), S("trackSettings")); }
        };
        panel.Children.Add(apply); SetBody(panel);
    }
}

sealed class EditorRemapDialog : ChromeWindow
{
    public EditorRemapPlan? Plan { get; private set; }
    sealed record Row(string Track, string Before, string After);
    public EditorRemapDialog(EditorSession session, SmapProject result) : base(Lang.S("ed.difference"), 790)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        var preserve = new CheckBox { Content = Lang.S("ed.preserveManual"), IsChecked = true, Foreground = B("TextFg"), Margin = new Thickness(0, 5, 0, 10) }; panel.Children.Add(preserve);
        var summary = new TextBlock { Foreground = B("TextFg"), Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(summary);
        var grid = new DataGrid { Height = 290, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Foreground = B("TextFg"), Background = B("BoxBg"), RowBackground = B("BoxBg"), EnableRowVirtualization = true };
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)); header.Setters.Add(new Setter(Control.ForegroundProperty, B("TextFg"))); header.Setters.Add(new Setter(Control.BackgroundProperty, B("NeutralBtnBg"))); header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6))); grid.ColumnHeaderStyle = header;
        void Column(string header, string property) => grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Column(Lang.S("ed.track"), nameof(Row.Track)); Column(Lang.S("ed.before"), nameof(Row.Before)); Column(Lang.S("ed.after"), nameof(Row.After)); panel.Children.Add(grid);
        var apply = Neutral(Lang.S("ed.apply")); apply.Margin = new Thickness(0, 14, 0, 0); apply.Click += (_, _) => { if (Plan != null) DialogResult = true; }; panel.Children.Add(apply);
        string Describe(ProjectNote? n) => n == null ? "—" : $"K{n.Key} · {n.Beat:0.######} · {(n.Action == NoteAction.Tap ? Lang.S("ed.tap") : n.LengthBeats.ToString("0.######", CultureInfo.CurrentCulture))}";
        void Refresh()
        {
            try
            {
                Plan = session.PreviewRemap(result, preserve.IsChecked == true); apply.IsEnabled = true;
                summary.Text = string.Format(Lang.S("ed.diffCounts"), Plan.Differences.Count, Plan.PreservedSources);
                string Profiles(SmapProject p) => string.Join(" / ", p.Tracks.Take(2).Select(t => t.GameTarget is { } g ? $"{Lang.Instrument(g.InstrumentId)} {g.ProfileId} v{g.ProfileVersion}" : "—")) + (p.Tracks.Count > 2 ? " …" : "");
                summary.Text += "\n" + string.Format(Lang.S("ed.diffSettings"), Profiles(session.Project), Profiles(Plan.Project), session.Project.Tempos.Count, Plan.Project.Tempos.Count, session.Project.Meters.Count, Plan.Project.Meters.Count);
                if (Plan.Differences.Count > 2000) summary.Text += "\n" + string.Format(Lang.S("ed.diffTruncated"), 2000, Plan.Differences.Count);
                var rows = new List<Row>();
                string Target(ProjectTrack? t) => t == null ? "—" : $"{t.GameTarget?.ProfileId ?? "—"} v{t.GameTarget?.ProfileVersion} · {Lang.Instrument(t.PreviewInstrument)} {t.PreviewSemitoneOffset:+0;-0;0}";
                foreach (Guid id in session.Project.Tracks.Select(t => t.Id).Union(Plan.Project.Tracks.Select(t => t.Id)))
                {
                    var before = session.Project.Tracks.SingleOrDefault(t => t.Id == id); var after = Plan.Project.Tracks.SingleOrDefault(t => t.Id == id);
                    if (before?.GameTarget != after?.GameTarget || before?.PreviewInstrument != after?.PreviewInstrument || before?.PreviewSemitoneOffset != after?.PreviewSemitoneOffset)
                        rows.Add(new((after ?? before)!.Name + " · " + Lang.S("ed.target"), Target(before), Target(after)));
                }
                foreach (decimal beat in session.Project.Tempos.Select(t => t.Beat).Union(Plan.Project.Tempos.Select(t => t.Beat)).Order())
                {
                    var before = session.Project.Tempos.SingleOrDefault(t => t.Beat == beat); var after = Plan.Project.Tempos.SingleOrDefault(t => t.Beat == beat);
                    if (before != after) rows.Add(new($"BPM · {beat:0.######}", before == null ? "—" : $"{60_000_000m / before.MicrosecondsPerBeat:0.######}", after == null ? "—" : $"{60_000_000m / after.MicrosecondsPerBeat:0.######}"));
                }
                foreach (decimal beat in session.Project.Meters.Select(t => t.Beat).Union(Plan.Project.Meters.Select(t => t.Beat)).Order())
                {
                    var before = session.Project.Meters.SingleOrDefault(t => t.Beat == beat); var after = Plan.Project.Meters.SingleOrDefault(t => t.Beat == beat);
                    if (before != after) rows.Add(new($"{Lang.S("ed.meter")} · {beat:0.######}", before == null ? "—" : $"{before.Numerator}/{before.Denominator}", after == null ? "—" : $"{after.Numerator}/{after.Denominator}"));
                }
                rows.AddRange(Plan.Differences.Take(2000).Select(d => new Row(session.Project.Tracks.FirstOrDefault(t => t.Id == d.TrackId)?.Name ?? Plan.Project.Tracks.FirstOrDefault(t => t.Id == d.TrackId)?.Name ?? "—", Describe(d.Before), Describe(d.After))));
                grid.ItemsSource = rows;
            }
            catch (Exception error) { Plan = null; apply.IsEnabled = false; summary.Text = Lang.S("ed.error"); Logger.Exception("重新映射预览失败", error); }
        }
        preserve.Checked += (_, _) => Refresh(); preserve.Unchecked += (_, _) => Refresh(); Refresh(); SetBody(panel);
    }
}
