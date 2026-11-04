using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class EditorWindow
{
    bool _currentOnly;
    double? _regionEnd;
    decimal? _regionEndBeat;
    double _playOffsetMs;
    ProjectTimeline? _displayTimeline;
    long _playGeneration;
    internal bool AudioEnabled { get; set; } = true;

    async Task TogglePlayback()
    {
        if (_busy) return;
        if (_playing) { PauseForEdit(); return; }
        await StartPreview(_currentOnly);
    }
    SmapProject Snapshot(bool onlyCurrent)
    {
        var p = _session.Project;
        return onlyCurrent ? p with { Tracks = new() { _session.ActiveTrack with { Muted = false, Solo = false, Notes = _session.ActiveTrack.Notes.ToList() } } }
            : p with { Tracks = p.Tracks.Select(t => t with { Notes = t.Notes.ToList() }).ToList() };
    }
    internal static PlaybackSequence PreviewSequence(SmapProject project)
    {
        var sequence = PlaybackSequence.FromProject(project);
        if (sequence.Notes.Count == 0) return sequence;
        var notes = project.AudibleTracks().SelectMany(t => t.Notes).ToArray();
        decimal last = notes.Max(n => n.Beat + n.LengthBeats);
        bool tapAtEnd = notes.Any(n => n.Action == NoteAction.Tap && n.Beat == last);
        decimal end = new EditorMeterMap(project.Meters).EndOfBar(last, tapAtEnd);
        return new(sequence.Notes, sequence.Preview, new ProjectTimeline(project.Tempos).BeatToMicroseconds(end));
    }
    async Task StartPreview(bool onlyCurrent)
    {
        if (_busy || !AudioEnabled) return;
        _audition.Stop(); StopKeyPreview(); long generation = ++_playGeneration;
        var snapshot = Snapshot(onlyCurrent); decimal beat = Roll.PlayheadBeat;
        var timeline = new ProjectTimeline(snapshot.Tempos); double start = (double)(timeline.BeatToMicroseconds(beat) / 1000);
        _displayTimeline = timeline; _playOffsetMs = 0; _regionEnd = _regionEndBeat is { } endBeat ? (double)(timeline.BeatToMicroseconds(endBeat) / 1000) : null;
        SetBusy(true);
        try
        {
            var prepared = await Task.Run(() => (sequence: PreviewSequence(snapshot), output: (INoteOutput)new EditorAudioOutput(snapshot)));
            if (_closed || generation != _playGeneration) { prepared.output.StopAll(); return; }
            if (start >= prepared.sequence.DurationMs) { start = 0; Roll.PlayheadBeat = 0; }
            _audition.Play(prepared.sequence, 1, () => Dispatcher.BeginInvoke(() => FinishPlayback(generation)), prepared.output, start);
            _playing = true; _playTimer.Start(); RefreshLabels();
        }
        catch (Exception error) { if (!_closed) ShowError(error); }
        finally { if (!_closed) SetBusy(false); }
    }
    void SetBusy(bool busy)
    {
        if (busy) _commandMenu?.Close(true);
        _busy = busy; ToolBar.IsEnabled = MoreButton.IsEnabled = ModeButton.IsEnabled = SaveButton.IsEnabled = TrackSelect.IsEnabled = Roll.IsEnabled = !busy;
        if (busy) StatusText.Text = S("working");
    }
    void StopPlayback()
    {
        _playGeneration++; _playing = false; _playTimer.Stop();
        try { _audition.Stop(); StopKeyPreview(); } catch (Exception error) { Logger.Exception("编辑试听停止失败", error); }
        if (_session != null) RefreshLabels();
    }
    void FinishPlayback(long generation)
    {
        if (_closed || generation != _playGeneration) return;
        Roll.PlayheadBeat = (_displayTimeline ?? new ProjectTimeline(_session.Project.Tempos)).MicrosecondsToBeat((decimal)(_audition.PositionMs + _playOffsetMs) * 1000);
        if (_follow) Roll.EnsurePlayheadVisible();
        _playing = false; _playTimer.Stop(); RefreshLabels();
    }
    void TickPlayback()
    {
        if (!_playing) return;
        double ms = _audition.PositionMs + _playOffsetMs;
        if (_regionEnd is { } end && ms >= end) { StopPlayback(); return; }
        Roll.PlayheadBeat = (_displayTimeline ?? new ProjectTimeline(_session.Project.Tempos)).MicrosecondsToBeat((decimal)ms * 1000);
        if (_follow) Roll.EnsurePlayheadVisible(); RefreshStatus();
    }
    void QuickNote(int key)
    {
        if (_busy || _keyHeld.Contains(key) || key >= _session.KeyCount) return;
        if (!_playing)
        {
            Edit(() =>
            {
                decimal beat = Roll.SnapBeat(Roll.PlayheadBeat);
                var existing = _session.ActiveTrack.Notes.FirstOrDefault(n => n.Key == key && n.Beat == beat);
                if (existing == null) _session.Create(key, beat);
                else if (existing.Action == NoteAction.Tap) _session.PutNotes(Array.Empty<ProjectNote>(), new[] { existing.Id });
                else _session.Select(new[] { existing.Id });
            });
        }
        StartKeyPreview(key);
    }
    async Task PreviewOriginal()
    {
        if (_busy || _session.Project.MidiConversion is not { } c || !AudioEnabled) return;
        StopPlayback(); long generation = ++_playGeneration; SetBusy(true);
        try
        {
            var timeline = c.Source.Timeline(); double start = (double)(timeline.BeatToMicroseconds(Roll.PlayheadBeat) / 1000);
            _displayTimeline = timeline; _playOffsetMs = start; _regionEnd = _regionEndBeat is { } end ? (double)(timeline.BeatToMicroseconds(end) / 1000) : null;
            string[]? voices = _currentOnly ? c.Voices.Where(v => MidiSource.StableId($"{c.Source.Sha256}:track:{v.VoiceId}") == _session.ActiveTrackId).Select(v => v.VoiceId).ToArray() : null;
            var plan = await Task.Run(() => MidiAudition.Original(c.Source, voices, start, _regionEnd));
            if (_closed || generation != _playGeneration) return;
            plan.Play(_audition, 1, () => Dispatcher.BeginInvoke(() => FinishPlayback(generation)));
            _playing = true; _playTimer.Start(); StatusText.Text = S("originalNotice");
        }
        catch (Exception error) { if (!_closed) ShowError(error); }
        finally { if (!_closed) SetBusy(false); }
    }
    void ShowMore()
    {
        var menu = NewMenu(MoreButton);
        AddMenu(menu, S("saveAs"), () => Save(true)); AddMenu(menu, S("metadata"), EditMetadata);
        AddMenu(menu, S("exportTrack"), () => ExportTrack(_session.ActiveTrackId));
        AddMenu(menu, S("exportLegacy"), ExportLegacy); AddMenu(menu, S("exportMidi"), ExportMidi);
        if (_session.Project.OriginalMidi != null) AddMenu(menu, S("exportOriginal"), ExportOriginal);
        menu.Items.Add(new Separator());
        AddMenu(menu, S("selectAll"), () => _session.Select(_session.ActiveTrack.Notes.Select(n => n.Id)));
        AddMenu(menu, S("delete"), () => Edit(_session.DeleteSelected));
        AddMenu(menu, S("quantize"), () => { if (_session.Selected.Count > 0 && MsgBox.Choice(this, F("quantizeConfirm", _session.Selected.Count), S("quantize"), S("apply"), Lang.S("d.cancel"))) Edit(_session.QuantizeSelected); });
        AddMenu(menu, S("split"), () => Edit(() => _session.SplitSelected(Roll.PlayheadBeat)));
        AddMenu(menu, S("properties"), EditNoteProperties); AddMenu(menu, S("trackSettings"), () => EditTrack(_session.ActiveTrack));
        menu.Items.Add(new Separator());
        var current = new MenuItem { Header = S("currentOnly"), IsCheckable = true, IsChecked = _currentOnly }; current.Click += (_, _) => { PauseForEdit(); _currentOnly = current.IsChecked; }; menu.Items.Add(current);
        AddMenu(menu, S("region"), PreviewRegion);
        if (_session.Project.MidiConversion != null) { AddMenu(menu, S("original"), async () => await PreviewOriginal()); AddMenu(menu, S("remap"), Remap); }
        AddMenu(menu, S("home"), () => { StopPlayback(); Roll.PlayheadBeat = 0; Roll.SetView(0); });
        AddMenu(menu, S("zoomIn"), () => Roll.SetZoom(Roll.Zoom * 1.25, Roll.VerticalZoom)); AddMenu(menu, S("zoomOut"), () => Roll.SetZoom(Roll.Zoom / 1.25, Roll.VerticalZoom));
        var motion = new MenuItem { Header = S("reduceMotion"), IsCheckable = true, IsChecked = _view.ReduceMotion }; motion.Click += (_, _) => _view.ReduceMotion = motion.IsChecked; menu.Items.Add(motion);
        AddMenu(menu, S("help"), () => MsgBox.Info(this, S("helpText"), S("help")));
        ToggleCommandMenu(MoreButton, menu);
    }
    void PreviewRegion()
    {
        var values = EditorFieldsDialog.Ask(this, S("region"), new[] { (S("startSeconds"), "0"), (S("endSeconds"), "") }); if (values == null) return;
        try
        {
            if (!MidiImportDialog.TryTime(values[0], out double start) || (!string.IsNullOrWhiteSpace(values[1]) && !MidiImportDialog.TryTime(values[1], out _))) throw new FormatException();
            double? end = string.IsNullOrWhiteSpace(values[1]) ? null : MidiImportDialog.TryTime(values[1], out var last) ? last : null;
            if (end is { } finish && finish <= start) throw new FormatException();
            var timeline = new ProjectTimeline(_session.Project.Tempos); PauseForEdit(); Roll.PlayheadBeat = timeline.MicrosecondsToBeat((decimal)start * 1000);
            _regionEndBeat = end is { } lastBeat ? timeline.MicrosecondsToBeat((decimal)lastBeat * 1000) : null; _ = StartPreview(_currentOnly);
        }
        catch (Exception error) { ShowError(error); }
    }
    internal bool Save(bool saveAs = false)
    {
        if (_busy) return false; PauseForEdit();
        try
        {
            string? path = _path;
            if (saveAs || path == null)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "SMAP (*.smap)|*.smap", DefaultExt = ".smap", AddExtension = true, FileName = SongLibrary.NativeFileStem(string.IsNullOrWhiteSpace(_session.Project.Metadata.Name) ? S("untitled") : _session.Project.Metadata.Name), InitialDirectory = Directory.Exists(SongLibrary.SongsDir) ? SongLibrary.SongsDir : null };
                if (dialog.ShowDialog(this) != true) return false; path = dialog.FileName;
            }
            SmapFile.Save(_session.Project, path); _path = path; _session.MarkSaved(); RefreshTitle(); StatusText.Text = F("saved", Path.GetFileName(path)); Saved?.Invoke(path); return true;
        }
        catch (Exception error) { ShowError(error); return false; }
    }
    bool ConfirmClose()
    {
        var result = EditorFieldsDialog.ConfirmSave(this);
        return result == 0 ? Save() : result == 1;
    }
    void ExportTrack(Guid id)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "SMAP (*.smap)|*.smap", DefaultExt = ".smap", FileName = SongLibrary.NativeFileStem(TrackName(_session.Project.Tracks.Single(t => t.Id == id))) };
        if (dialog.ShowDialog(this) != true) return;
        try { var p = _session.Project; SmapFile.Save(p with { Tracks = new() { p.Tracks.Single(t => t.Id == id) with { Muted = false, Solo = false } } }, dialog.FileName); } catch (Exception error) { ShowError(error); }
    }
    void ExportLegacy()
    {
        try
        {
            _ = LegacyProjectAdapter.ToDocument(_session.Project, _session.ActiveTrackId);
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "TXT (*.txt)|*.txt|JSON (*.json)|*.json", FileName = SongLibrary.NativeFileStem(TrackName(_session.ActiveTrack)) };
            if (dialog.ShowDialog(this) == true) LegacyProjectAdapter.SaveLegacy(_session.Project, dialog.FileName, _session.ActiveTrackId);
        }
        catch (Exception error) { Logger.Exception("旧格式导出失败", error); MsgBox.Info(this, Lang.S("midi3.lossy") + "\n" + S("saveAs"), S("exportLegacy")); }
    }
    void ExportMidi()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "MIDI (*.mid)|*.mid", DefaultExt = ".mid" };
        if (dialog.ShowDialog(this) != true) return; try { MidiProjectConverter.ExportPerformance(_session.Project, dialog.FileName); } catch (Exception error) { ShowError(error); }
    }
    void ExportOriginal()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "MIDI (*.mid)|*.mid", DefaultExt = ".mid" };
        if (dialog.ShowDialog(this) != true) return; try { MidiProjectConverter.ExportOriginal(_session.Project, dialog.FileName); } catch (Exception error) { ShowError(error); }
    }
    void EditMetadata()
    {
        var m = _session.Project.Metadata; var values = EditorFieldsDialog.Ask(this, S("metadata"), new[] { (S("name"), m.Name), (S("author"), m.Author), (S("transcriber"), m.TranscribedBy), (S("description"), m.Description) });
        if (values != null) Edit(() => _session.ReplaceProject(_session.Project with { Metadata = m with { Name = values[0], Author = values[1], TranscribedBy = values[2], Description = values[3] } }));
    }
    void ShowTempoMenu()
    {
        var point = _session.Project.Tempos.Where(t => t.Beat <= Roll.PlayheadBeat).OrderBy(t => t.Beat).Last();
        var fields = EditorFieldsDialog.Ask(this, "BPM", new[] { (S("position"), point.Beat.ToString(CultureInfo.CurrentCulture)), ("BPM", (60_000_000m / point.MicrosecondsPerBeat).ToString("0.######", CultureInfo.CurrentCulture)) });
        if (fields == null) return;
        Edit(() => { decimal beat = Decimal(fields[0]), bpm = Decimal(fields[1]); if (beat < 0 || bpm <= 0) throw new FormatException(); var points = _session.Project.Tempos.Where(t => t.Beat != beat).Append(new TempoPoint(beat, 60_000_000m / bpm)).OrderBy(t => t.Beat).ToList(); _session.ReplaceProject(_session.Project with { Tempos = points }); });
    }
    void ShowMeterMenu()
    {
        var point = _session.Project.Meters.Where(t => t.Beat <= Roll.PlayheadBeat).OrderBy(t => t.Beat).Last();
        var fields = EditorFieldsDialog.Ask(this, "♩", new[] { (S("position"), point.Beat.ToString(CultureInfo.CurrentCulture)), (S("meter"), $"{point.Numerator}/{point.Denominator}") }); if (fields == null) return;
        Edit(() => { decimal beat = Decimal(fields[0]); var parts = fields[1].Split('/'); if (parts.Length != 2) throw new FormatException(); int n = int.Parse(parts[0]), d = int.Parse(parts[1]); var points = _session.Project.Meters.Where(m => m.Beat != beat).Append(new MeterPoint(beat, n, d)).OrderBy(m => m.Beat).ToList(); _session.ReplaceProject(_session.Project with { Meters = points }); });
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (_commandMenu is { IsOpen: true } command) { e.Handled = command.HandleKey(key); return; }
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && key == Key.S) { Save(); e.Handled = true; return; }
        if (_snapChoice.IsOpen || _toolChoice.IsOpen || _trackChoice.IsOpen)
        {
            var choice = _snapChoice.IsOpen ? _snapChoice : _toolChoice.IsOpen ? _toolChoice : _trackChoice;
            var model = ReferenceEquals(choice, _snapChoice) ? _snap : ReferenceEquals(choice, _toolChoice) ? _tools : TrackSelect;
            if (key == Key.Escape) { CloseEditorChoices(); Roll.Focus(); e.Handled = true; }
            else if (key is Key.Up or Key.Down or Key.Home or Key.End)
            {
                int index = choice.Options.SelectedIndex;
                choice.Options.SelectedIndex = key == Key.Home ? 0 : key == Key.End ? choice.Options.Items.Count - 1
                    : Math.Clamp(index + (key == Key.Up ? -1 : 1), 0, choice.Options.Items.Count - 1);
                choice.Options.ScrollIntoView(choice.Options.SelectedItem); e.Handled = true;
            }
            else if (key is Key.Enter or Key.Space)
            {
                if (choice.Options.SelectedIndex >= 0) model.SelectedIndex = choice.Options.SelectedIndex;
                choice.Close(); Roll.Focus(); e.Handled = true;
            }
            else base.OnPreviewKeyDown(e);
            return;
        }
        if (Keyboard.FocusedElement is TextBox or PasswordBox or JoinedChoiceButton or CommandMenuButton || Keyboard.FocusedElement is ComboBox) { base.OnPreviewKeyDown(e); return; }
        if (HandleKey(key, Keyboard.Modifiers, e.IsRepeat)) e.Handled = true; else base.OnPreviewKeyDown(e);
    }
    internal bool HandleKey(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0, shift = (modifiers & ModifierKeys.Shift) != 0;
        if (key == Key.Escape) { Roll.CancelGesture(); if (Drawer.Visibility == Visibility.Visible) DismissDrawer(); else _session.Select(Array.Empty<Guid>()); return true; }
        if (_busy) return false;
        if (ctrl && key == Key.A) _session.Select(_session.ActiveTrack.Notes.Select(n => n.Id));
        else if (ctrl && key == Key.C) _session.Copy();
        else if (ctrl && key == Key.X) Edit(_session.Cut);
        else if (ctrl && key == Key.V) Edit(() => _session.Paste(Roll.SnapBeat(Roll.PlayheadBeat)));
        else if (ctrl && key == Key.Z) Edit(shift ? _session.Redo : _session.Undo);
        else if (ctrl && key == Key.Y) Edit(_session.Redo);
        else if (key == Key.Delete || shift && key == Key.X) Edit(_session.DeleteSelected);
        else if (key == Key.Space) { if (!repeat) _ = TogglePlayback(); }
        else if (key == Key.Home) { StopPlayback(); Roll.PlayheadBeat = 0; Roll.SetView(0); }
        else if (key is Key.Left or Key.Right)
        {
            decimal delta = (key == Key.Left ? -1 : 1) / (decimal)_session.Project.GridSubdiv;
            if (_session.Selected.Count > 0) Edit(() => _session.MoveSelected(delta, 0)); else { Roll.PlayheadBeat = Math.Max(0, Roll.PlayheadBeat + delta); Roll.EnsurePlayheadVisible(); Roll.PlayheadChanged?.Invoke(); }
        }
        else if (key is Key.Up or Key.Down)
        { if (_session.Selected.Count > 0) Edit(() => _session.MoveSelected(0, key == Key.Up ? 1 : -1)); else Roll.SetView(Roll.ViewBeat, Roll.VerticalOffset + (key == Key.Up ? -22 : 22)); }
        else if (key == Key.Enter && (modifiers & ModifierKeys.Alt) != 0) EditNoteProperties();
        else if (!ctrl && !shift && KeyIndex(key) is int index && index >= 0) { if (!repeat) QuickNote(index); }
        else return false;
        return true;
    }
}
