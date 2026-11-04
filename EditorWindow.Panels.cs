using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class EditorWindow
{
    void RefreshTracks()
    {
        _commandMenu?.Close(true);
        var focusTag = (Keyboard.FocusedElement as ToggleButton)?.Tag;
        ToggleButton? restoreFocus = null;
        var panel = new StackPanel { Margin = new Thickness(5) };
        panel.Children.Add(Label(S("tracks"), bold: true));
        foreach (var track in _session.Project.Tracks)
        {
            var card = new StackPanel { Margin = new Thickness(8, 5, 5, 5) };
            var top = new DockPanel(); var more = new CommandMenuButton { Content = "⋯" }; more.Click += (_, _) => TrackMenu(track, more); more.MinWidth = 28; more.Padding = new Thickness(6, 1, 6, 1); more.ToolTip = S("more");
            DockPanel.SetDock(more, Dock.Right); top.Children.Add(more);
            var select = MakeButton(TrackName(track), () => SwitchTrack(track.Id)); select.HorizontalContentAlignment = HorizontalAlignment.Left;
            var title = new TextBlock { Text = TrackName(track), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 125 };
            select.Content = title; select.ToolTip = TrackName(track); select.Background = Brushes.Transparent; select.BorderThickness = new Thickness(0); top.Children.Add(select); card.Children.Add(top);
            card.Children.Add(Label(Lang.Instrument(track.PreviewInstrument), muted: true));
            var states = new WrapPanel { Margin = new Thickness(4, 3, 4, 3) };
            ToggleButton StateButton(string label, string key, bool active, Action<bool> update)
            {
                var button = new ToggleButton { Content = label, IsChecked = active, Style = (Style)FindResource(key == "mute" ? "TrackMuteToggle" : "TrackSoloToggle"),
                    Tag = $"{track.Id:N}:{key}", ToolTip = F("toggleStatus", S(key), S(active ? "stateOn" : "stateOff")) };
                System.Windows.Automation.AutomationProperties.SetName(button, TrackName(track) + " · " + S(key));
                void ApplyState()
                {
                    if (button.IsChecked == active) return;
                    if (_busy) { button.IsChecked = active; return; }
                    Edit(() => update(button.IsChecked == true));
                }
                button.Checked += (_, _) => ApplyState(); button.Unchecked += (_, _) => ApplyState();
                button.Click += (_, e) => e.Handled = true;
                if (Equals(focusTag, button.Tag)) restoreFocus = button;
                return button;
            }
            var mute = StateButton("M", "mute", track.Muted, active => _session.UpdateTrack(track with { Muted = active }));
            var solo = StateButton("S", "solo", track.Solo, active => _session.UpdateTrack(track with { Solo = active }));
            mute.Margin = new Thickness(0, 0, 6, 0); states.Children.Add(mute); states.Children.Add(solo); card.Children.Add(states);
            if (track.Id == _session.ActiveTrackId) card.Children.Add(Label(S("editing").Replace("{0}", ""), bold: true));
            var border = new Border { Child = card, BorderThickness = new Thickness(4, 1, 1, 1), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 3, 0, 5), BorderBrush = new SolidColorBrush(_view.Color(track.Id, _session.Project.Tracks.IndexOf(track))) };
            border.SetResourceReference(Border.BackgroundProperty, "BoxBg"); panel.Children.Add(border);
        }
        panel.Children.Add(MakeButton(S("newTrack"), () => Edit(() => _session.AddTrack(S("track") + " " + (_session.Project.Tracks.Count + 1)))));
        var reference = new CheckBox { Content = S("reference"), IsChecked = Roll.References, Margin = new Thickness(5, 10, 5, 4) };
        reference.SetResourceReference(CheckBox.ForegroundProperty, "TextFg");
        reference.Checked += (_, _) => Roll.References = true; reference.Unchecked += (_, _) => Roll.References = false; panel.Children.Add(reference);
        panel.Children.Add(Label(S("referenceNotice"), muted: true));
        _trackScroll.Content = panel;
        if (restoreFocus != null) Dispatcher.BeginInvoke(() => { if (_trackScroll.IsAncestorOf(restoreFocus)) restoreFocus.Focus(); }, System.Windows.Threading.DispatcherPriority.Loaded);
        Overview.InvalidateNotes();
    }
    void TrackMenu(ProjectTrack track, CommandMenuButton anchor)
    {
        var menu = NewMenu();
        AddMenu(menu, S("rename"), () =>
        {
            if (EditorFieldsDialog.Ask(this, S("rename"), new[] { (S("name"), TrackName(track)) }) is { } fields)
                Edit(() => _session.UpdateTrack(track with { Name = fields[0] }));
        });
        AddMenu(menu, S("trackSettings"), () => EditTrack(track));
        var colors = new MenuItem { Header = S("color") };
        foreach (string hex in EditorViewPreferences.Palette)
        {
            var color = new MenuItem { Header = hex, Icon = new Border { Width = 15, Height = 15, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) } };
            color.Click += (_, _) => { _view.Colors[track.Id] = hex; RefreshTracks(); Roll.InvalidateLayers(); }; colors.Items.Add(color);
        }
        menu.Items.Add(colors);
        AddMenu(menu, S("exportTrack"), () => ExportTrack(track.Id));
        AddMenu(menu, S("deleteTrack"), () =>
        {
            if (MsgBox.Choice(this, F("deleteConfirm", TrackName(track), track.Notes.Count), S("deleteTrack"), S("delete"), Lang.S("d.cancel")))
                Edit(() => _session.DeleteTrack(track.Id));
        });
        ToggleCommandMenu(anchor, menu);
    }
    void RefreshInspector()
    {
        var panel = new StackPanel { Margin = new Thickness(9) };
        panel.Children.Add(Label(S("source"), bold: true));
        panel.Children.Add(Label(F("editing", TrackName(_session.ActiveTrack))));
        var selected = _session.SelectedNotes();
        if (selected.Length == 1)
        {
            var n = selected[0];
            var compatibility=PerformanceCompatibility.Evaluate(_session.ActiveTrack,n);
            panel.Children.Add(Label(Lang.S("map."+(compatibility.Availability==KeyAvailability.Unknown?"unknown":compatibility.Availability==KeyAvailability.Available?"available":"unavailable")),muted:true));
            panel.Children.Add(Label($"K{n.Key} · {S(n.Action == NoteAction.Tap ? "tap" : "hold")}\n{S("position")}: {n.Beat:0.############}\n{S("length")}: {n.LengthBeats:0.############}"));
            var conversion = _session.Project.MidiConversion;
            var original = conversion?.Source.Notes.FirstOrDefault(o => o.Id == n.SourceMidiNoteId);
            if (original != null)
            {
                var mapping = conversion!.Notes.First(m => m.SourceId == original.Id);
                var target = _session.ActiveTrack.GameTarget;
                panel.Children.Add(Label(F("sourceInfo", MidiImportDialog.Pitch(original.Pitch), original.Track + 1, original.Channel,
                    InstrumentCapabilities.ForTrack(_session.ActiveTrack)?.Resolve(n.Key,_session.ActiveTrack.KeyDomain)?.MidiPitch is {} actual ? MidiImportDialog.Pitch(actual) : "—", n.Key)));
                bool edited = n.Key != mapping.Key || n.Beat != conversion.Source.Beat(original.StartTick) || n.LengthBeats != conversion.Source.Beat(original.EndTick - original.StartTick);
                panel.Children.Add(Label(edited ? S("manual") : Lang.S("midi3.cause." + mapping.Reason.Split(';')[0]), muted: true));
                panel.Children.Add(MakeButton(S("restore"), () => RestoreNote(n)));
            }
        }
        else panel.Children.Add(Label(F("status", TrackName(_session.ActiveTrack), selected.Length, _session.ActiveTrack.Notes.Count), muted: true));
        panel.Children.Add(MakeButton(S("properties"), EditNoteProperties));
        panel.Children.Add(MakeButton(S("trackSettings"), () => EditTrack(_session.ActiveTrack)));
        panel.Children.Add(Label(S("pitchNotice"), muted: true));
        if (_session.Project.MidiConversion != null)
        {
            panel.Children.Add(Label(S("qualitySnapshot"), muted: true));
            panel.Children.Add(MakeButton(S("remap"), Remap));
            panel.Children.Add(Label(S("sourceReadonly"), muted: true));
            panel.Children.Add(Label(S("originalNotice"), muted: true));
            panel.Children.Add(MakeButton(S("original"), async () => await PreviewOriginal()));
            var unmapped = Roll.UnmappedNotes().Take(40).ToArray();
            if (unmapped.Length > 0)
            {
                panel.Children.Add(Label(S("unmapped"), bold: true));
                foreach (var (source, _) in unmapped)
                    panel.Children.Add(MakeButton($"! {MidiImportDialog.Pitch(source.Pitch)} · {source.StartTick}", () =>
                    { Roll.PlayheadBeat = _session.Project.MidiConversion.Source.Beat(source.StartTick); Roll.EnsurePlayheadVisible(true); Remap(); }));
            }
        }
        panel.Children.Add(Label(F("outOfRange", Roll.ReferenceOutOfRange), muted: true));
        _inspectorScroll.Content = panel;
    }
    void EditNoteProperties()
    {
        var notes = _session.SelectedNotes(); if (notes.Length == 0) return;
        var first = notes[0];
        var fields = EditorFieldsDialog.Ask(this, S("properties"), new[]{(S("position"),first.Beat.ToString(CultureInfo.CurrentCulture)),(S("key"),first.Key.ToString()),
            (S("length"),first.LengthBeats.ToString(CultureInfo.CurrentCulture))}, new[] { S("tap"), S("hold") }, first.Action == NoteAction.Tap ? 0 : 1, out int action);
        if (fields == null) return;
        Edit(() =>
        {
            decimal beat = Decimal(fields[0]), length = Decimal(fields[2]); int key = int.Parse(fields[1], CultureInfo.CurrentCulture);
            var move = _session.MovePreview(notes, beat - first.Beat, key - first.Key);
            var changed = move.Select(n => n with { Action = action == 0 ? NoteAction.Tap : NoteAction.Hold, LengthBeats = action == 0 ? 0 : length, RetriggerOnOverlap = action != 0 && n.RetriggerOnOverlap }).ToArray();
            _session.PutNotes(changed);
        });
    }
    void EditTrack(ProjectTrack track)
    {
        var dialog = new EditorTrackDialog(track) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } result) Edit(() => _session.UpdateTrack(result));
    }
    void RestoreNote(ProjectNote note)
    {
        if (note.SourceMidiNoteId is not { } id || _session.Project.MidiConversion is not { } c) return;
        var mapping = c.Notes.First(n => n.SourceId == id); if (mapping.Key == null) return;
        var source = c.Source.Notes.First(n => n.Id == id); decimal length = c.Source.Beat(source.EndTick - source.StartTick);
        var voice=c.Voices.Single(v=>v.VoiceId==mapping.VoiceId);
        int key=_session.ActiveTrack.KeyDomain==NoteKeyDomain.StableLogical?InstrumentCapabilities.FromProfile(voice.Profile).Resolve(mapping.Key.Value,NoteKeyDomain.LegacyOrdinal)!.LogicalKey:mapping.Key.Value;
        Edit(() => _session.PutNotes(new[]{note with { Key=key,Beat=c.Source.Beat(source.StartTick),LengthBeats=length,
            Action=length>0?NoteAction.Hold:NoteAction.Tap,RetriggerOnOverlap=length>0 }},
            _session.Project.Tracks.SelectMany(t => t.Notes).Where(n => n.SourceMidiNoteId == id).Select(n => n.Id)));
    }
    void Remap()
    {
        if (_busy || _session.Project.MidiConversion == null) return; PauseForEdit();
        try
        {
            var dialog = new MidiImportDialog(_session.Project) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.ResultProject is not { } remapped) return;
            var preview = new EditorRemapDialog(_session, remapped) { Owner = this };
            if (preview.ShowDialog() == true && preview.Plan is { } plan) Edit(() => _session.ReplaceProject(plan.Project));
        }
        catch (Exception error) { ShowError(error); }
    }
    void AnimatePanel(FrameworkElement panel)
    {
        if (!_view.ReduceMotion && SystemParameters.ClientAreaAnimation) panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }
    static decimal Decimal(string value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
        || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed) ? parsed : throw new FormatException();
}
