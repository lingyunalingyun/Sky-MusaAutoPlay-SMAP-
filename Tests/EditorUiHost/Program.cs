using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;
using SMAP.Tests;

namespace SMAP.EditorTests;

static class Program
{
    static readonly string Output = Path.Combine(Path.GetTempPath(), "SMAP-Phase4B-review");
    static int _checks;
    static void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Directory.CreateDirectory(Output);
            if (args.Contains("metrics")) Metrics(); else Verify();
            Console.WriteLine(JsonSerializer.Serialize(new { checks = _checks, screenshots = Output })); app.Shutdown(); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static void Palette(string name)
    {
        Theme.Apply(Enum.Parse<AppTheme>(name), persist: false);
    }
    static void Language(AppLang language) => typeof(Lang).GetProperty(nameof(Lang.Current))!.SetValue(null, language);
    static void Pump()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }
    static SmapProject Sample()
    {
        var source = new MidiFixture(3).Note(60, 0, 480).Note(62, 480, 960).Note(61, 800, 1000).Note(66, 1120, 1850).Note(65, 960, 1920).Note(67, 1920, 2880)
            .Note(60, 2880, 3840).Note(60, 3120, 4080).Note(48, 0, 1920, channel: 2, track: 1).Note(55, 1920, 3840, channel: 2, track: 1)
            .Note(36, 0, 3840, channel: 3, track: 2).Tempo(0, 500000).Tempo(3840, 625000).Read();
        var options = MidiFixture.Options(source, mode: SapmMode.Strict);
        options[0] = options[0] with { Mode = SapmMode.Melody }; options[0].ManualKeys[source.Notes.Single(n => n.Pitch == 66).Id] = null;
        options[1] = options[1] with { Profile = GamePitchProfiles.Get("Harp") }; options[2] = options[2] with { Profile = GamePitchProfiles.Get("Cello") };
        var project = MidiProjectConverter.Create(SapmMapper.Convert(source, options), "晨光 · Morning light");
        return project with { Tracks = project.Tracks.Select((t, i) => t with { Name = new[] { "主旋律 / Melody", "和弦铺底 / Harmony", "低音 / Bass" }[i] }).ToList() };
    }
    static void Capture(EditorWindow window, string name)
    {
        Pump(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(Output, name + ".png")); encoder.Save(output);
    }
    static void Close(EditorWindow window) { window.Session.MarkSaved(); window.Close(); Pump(); }
    static void Verify()
    {
        var project = Sample(); int renders = 0;
        foreach (string palette in new[] { "Dark", "Light", "SunsetPink" }) foreach (AppLang language in Enum.GetValues<AppLang>())
        {
            Palette(palette); Language(language);
            var window = new EditorWindow(project) { AudioEnabled = false, Width = 1232, Height = 760, Left = 20, Top = 20 };
            window.Show(); Pump(); Check(window.IsVisible, "Actual editor window did not open");
            Check(window.Viewport.ActualWidth > 600, "Simple viewport too small");
            if (palette == "Dark" && language == AppLang.ZhCN) Capture(window, "A-simple"); renders++;
            window.ToggleAdvanced(); Pump(); Check(window.Advanced, "Advanced mode did not activate");
            Check(window.Viewport.ActualWidth > 500, "Advanced viewport collapsed");
            if (language == AppLang.ZhCN) Capture(window, "D-midi-" + palette); renders++;
            window.Width = 900; window.Height = 620; Pump();
            Check(window.Viewport.ActualWidth > 700, "Compact viewport too small");
            if (palette == "Dark" && language == AppLang.ZhCN) Capture(window, "E-compact"); renders++;
            Close(window);
        }
        Palette("Dark"); Language(AppLang.ZhCN);
        var edited = new EditorWindow(project) { AudioEnabled = false, Width = 1232, Height = 760 }; edited.Show(); Pump();
        Interactions(edited); Capture(edited, "C-selection"); RemapReview(edited); Close(edited);
        var native = new EditorWindow(EditorSession.NewProject()) { AudioEnabled = false, Width = 1232, Height = 760 }; native.Show(); native.ToggleAdvanced(); Pump();
        native.Session.Create(4, 2); native.Session.PutNotes(new[] { native.Session.SelectedNotes()[0] with { Action = NoteAction.Hold, LengthBeats = 3 } });
        native.Session.AddTrack("和弦"); native.Session.Create(7, 0); native.Session.SelectTrack(native.Session.Project.Tracks[0].Id); Pump(); Capture(native, "B-advanced"); Close(native);
        var dense = new EditorWindow(DenseProject(3000)) { AudioEnabled = false, Width = 1232, Height = 740 }; dense.Show(); Pump();
        string identity = PerformanceIdentity.ForProject(dense.Session.Project);
        Check(dense.Viewport.DenseView && dense.Viewport.LastRenderedNotes < 1500, "Dense rendering did not group duplicate geometry");
        Check(dense.Session.ActiveTrack.Notes.Count == 3001, "Dense rendering changed note data");
        dense.Viewport.SetView(dense.Viewport.ViewBeat,double.MaxValue);Pump();
        var bounds = dense.Viewport.Bounds(dense.Session.ActiveTrack.Notes.First(n => n.Key == 4));
        Check(bounds.Top >= PianoRoll.RulerHeight && bounds.Top + 4 < dense.Viewport.ActualHeight,"Dense hit-test fixture is outside visible viewport");
        Check(dense.Viewport.HitNotes(new Point(bounds.Left + 3, bounds.Top + 4)).Length >= 3, "Dense overview lost overlapping hit targets");
        Capture(dense, "G-dense"); dense.Viewport.SetZoom(8, 1); Pump();
        Check(!dense.Viewport.DenseView, "Zoom did not restore individual notes");
        Check(identity == PerformanceIdentity.ForProject(dense.Session.Project), "Dense view or zoom changed timing"); Close(dense);
        Console.WriteLine($"EDITOR_UI palettes=3;languages=4;modes=3;renders={renders};actual_windows=True;game_input=False");
    }
    static void Interactions(EditorWindow window)
    {
        var roll = window.Viewport; var session = window.Session; int count = session.ActiveTrack.Notes.Count;
        int themedBuilds = roll.NoteLayerBuilds; Palette("Light"); Pump(); Palette("Dark"); Pump(); Check(roll.NoteLayerBuilds > themedBuilds, "Runtime theme change did not refresh roll");
        Point PointAt(decimal beat, int key) => new(PianoRoll.LaneWidth + (double)(beat - roll.ViewBeat) * roll.BeatWidth + 3, PianoRoll.RulerHeight + (14 - key) * roll.RowHeight + 10 - roll.VerticalOffset);
        void Click(Point p, ModifierKeys modifiers = ModifierKeys.None) { roll.PointerDown(p, MouseButton.Left, modifiers); roll.PointerUp(p, modifiers); Pump(); }
        roll.SetView(roll.ViewBeat,double.MaxValue);Pump();Check(PointAt(10,4).Y>=PianoRoll.RulerHeight&&PointAt(10,4).Y<roll.ActualHeight,"Create test point is outside visible viewport");
        Click(PointAt(10, 4)); Check(session.ActiveTrack.Notes.Count == count + 1, $"Click did not create note; pointer={PointAt(10,4)}; viewport={roll.ActualWidth}x{roll.ActualHeight}; vertical={roll.VerticalOffset}");
        var added = session.SelectedNotes().Single(); Check(added.Action == NoteAction.Tap && added.LengthBeats == 0, "Tap semantics changed");
        session.Undo(); Check(session.ActiveTrack.Notes.Count == count, "Undo create failed"); session.Redo();
        added = session.SelectedNotes().Single(); Rect bounds = roll.Bounds(added); Point edge = new(bounds.Right + 4, bounds.Top + 5); int undo = session.UndoCount;
        roll.PointerDown(edge, MouseButton.Left, ModifierKeys.None); roll.PointerMove(new Point(edge.X + 128, edge.Y), ModifierKeys.None); Check(session.UndoCount == undo, "Drag mutated history before release"); roll.PointerUp(new Point(edge.X + 128, edge.Y), ModifierKeys.None); Pump();
        var hold = session.Find(added.Id)!; Check(hold.Action == NoteAction.Hold && hold.LengthBeats > 2, "Enlarged handle did not resize"); Check(session.UndoCount == undo + 1, "Resize used multiple transactions");
        var p = PointAt(hold.Beat + .5m, hold.Key); var original = hold;
        roll.PointerDown(p, MouseButton.Left, ModifierKeys.Shift); roll.PointerMove(new Point(p.X + 64, p.Y), ModifierKeys.Shift); roll.PointerUp(new Point(p.X + 64, p.Y), ModifierKeys.Shift); Pump();
        var copy = session.SelectedNotes().Single(); Check(copy.Id != original.Id && copy.LengthBeats == original.LengthBeats, "Shift copy lost identity or length");
        Check(session.Find(original.Id) == original, "Shift copy moved original");
        var overlap = PointAt(original.Beat + 1.5m, original.Key);
        Click(overlap, ModifierKeys.Alt); Guid cycled = session.SelectedNotes().Single().Id;
        Click(overlap, ModifierKeys.Alt); Check(cycled != session.SelectedNotes().Single().Id, "Dense overlap cycling did not change selected identity");
        session.Select(new[] { copy.Id });
        p = PointAt(copy.Beat + .5m, copy.Key); roll.PointerDown(p, MouseButton.Left, ModifierKeys.None); roll.PointerMove(new Point(p.X + 64, p.Y), ModifierKeys.None); roll.CancelGesture();
        Check(session.Find(copy.Id) == copy, "Canceled gesture committed");
        session.Select(new[] { original.Id }); window.HandleKey(Key.C, ModifierKeys.Control); roll.PlayheadBeat = 14; window.HandleKey(Key.V, ModifierKeys.Control); Pump();
        Check(session.SelectedNotes().Single().LengthBeats == original.LengthBeats, "Keyboard paste lost duration");
        int before = session.ActiveTrack.Notes.Count; window.HandleKey(Key.Y, ModifierKeys.None); Check(session.ActiveTrack.Notes.Count == before + 1, "Typing keyboard did not add tap");
        window.HandleKey(Key.Y, ModifierKeys.None, repeat: true); Check(session.ActiveTrack.Notes.Count == before + 1, "Key repeat toggled note");
        Pump(); int noteBuilds = roll.NoteLayerBuilds, gridBuilds = roll.GridLayerBuilds;
        for (int i = 0; i < 100; i++) roll.PlayheadBeat = i / 10m; Pump(); Check(roll.NoteLayerBuilds == noteBuilds && roll.GridLayerBuilds == gridBuilds, "Playhead rebuilt static layers");
        string identity = PerformanceIdentity.ForProject(session.Project); roll.SetZoom(1.8, 1.4); Pump(); session.SetGrid(8); Pump(); Check(identity == PerformanceIdentity.ForProject(session.Project), "Zoom or grid changed performance");
        var snapshots = session.ActiveTrack.Notes.ToArray(); window.ToggleAdvanced(); window.ToggleAdvanced(); Check(snapshots.SequenceEqual(session.ActiveTrack.Notes), "Mode change altered notes");
        roll.SetZoom(1, 1); roll.SetView(0); Pump();
        roll.Tool = RollTool.Paint; int history = session.UndoCount; int paints = session.ActiveTrack.Notes.Count(n => n.Key == 12);
        var a = PointAt(.25m, 12); var b = PointAt(1.75m, 12);
        roll.PointerDown(a, MouseButton.Left, ModifierKeys.None); roll.PointerMove(b, ModifierKeys.None); roll.PointerUp(b, ModifierKeys.None); Pump();
        Check(session.ActiveTrack.Notes.Count(n => n.Key == 12) >= paints + 7, "Paint stroke skipped grid cells"); Check(session.UndoCount == history + 1, "Paint stroke used multiple undo entries");
        history = session.UndoCount; roll.PointerDown(a, MouseButton.Right, ModifierKeys.None); roll.PointerMove(b, ModifierKeys.None); roll.PointerUp(b, ModifierKeys.None); Pump();
        Check(session.ActiveTrack.Notes.Count(n => n.Key == 12) == paints, "Erase stroke skipped notes"); Check(session.UndoCount == history + 1, "Erase stroke used multiple undo entries"); roll.Tool = RollTool.Draw;
        roll.SetView(roll.ViewBeat,double.MaxValue);Pump();a = PointAt(9, 6); b = PointAt(14, 2); roll.PointerDown(a, MouseButton.Left, ModifierKeys.Control); roll.PointerMove(b, ModifierKeys.Control); roll.PointerUp(b, ModifierKeys.Control); Pump();
        Check(session.Selected.Count >= 2, "Ctrl marquee missed multiple holds");
        var work = (Grid)window.FindName("WorkArea"); var input = new TextBox { Text = "", Width = 120, Height = 30 }; Grid.SetColumn(input, 1); work.Children.Add(input); input.Focus(); Keyboard.Focus(input); Pump();
        before = session.ActiveTrack.Notes.Count;
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Y) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(session.ActiveTrack.Notes.Count == before, "Typing inside a text field wrote a note"); work.Children.Remove(input); roll.Focus();
        Check(roll.UnmappedNotes().Any(), "Unmapped MIDI events disappeared from their diagnostic band");
        Check(roll.HitNotes(new Point(110, roll.ActualHeight - 3)).Length == 0, "Diagnostic band acted as a playable key");
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    static void RemapReview(EditorWindow window)
    {
        var session = window.Session; var imported = session.ActiveTrack.Notes.First(n => n.SourceMidiNoteId != null);
        session.PutNotes(new[] { imported with { Beat = imported.Beat + .125m } });
        session.ReplaceProject(session.Project with { Tempos = new() { new(0, 750000) } });
        string identity = PerformanceIdentity.ForProject(session.Project);
        var source = session.Project.MidiConversion!.Source;
        var result = MidiProjectConverter.Remap(session.Project, MidiFixture.Options(source));
        var dialog = new EditorRemapDialog(session, result) { Owner = window }; Exception? failure = null;
        dialog.ContentRendered += (_, _) => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                Check(dialog.Plan != null && dialog.Plan.PreservedSources > 0, "Difference dialog lost manual protection");
                var protect = Descendants<CheckBox>(dialog).Single(); protect.IsChecked = false; Pump();
                Check(dialog.Plan != null && dialog.Plan.PreservedSources == 0 && dialog.Plan.Project.Tempos[0].MicrosecondsPerBeat == 500000, "Difference dialog did not update unchecked plan");
                protect.IsChecked = true; Pump(); Check(identity == PerformanceIdentity.ForProject(session.Project), "Difference preview mutated project");
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(Output, "F-remap-review.png"))) encoder.Save(output);
                Descendants<Button>(dialog).Single(b => Equals(b.Content, Lang.S("ed.apply"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception error) { failure = error; dialog.Close(); }
        }));
        bool accepted = dialog.ShowDialog() == true; if (failure != null) throw failure; Check(accepted, "Difference dialog did not accept reviewed plan");
        int undo = session.UndoCount; session.ReplaceProject(dialog.Plan!.Project); Check(session.UndoCount == undo + 1, "Remap was not one transaction"); session.Undo();
        Check(identity == PerformanceIdentity.ForProject(session.Project), "Remap undo did not restore manual work");
    }
    static void Metrics()
    {
        Palette("Dark"); Language(AppLang.ZhCN);
        foreach (int count in new[] { 24000, 100000 })
        {
            var p = EditorSession.NewProject(); var t = p.Tracks[0];
            p = p with { Tracks = new() { t with { Notes = Enumerable.Range(0, count).Select(i => new ProjectNote { TrackId = t.Id, Id = Guid.NewGuid(), Key = i % 15, Beat = i / 4m, Action = i % 3 == 0 ? NoteAction.Hold : NoteAction.Tap, LengthBeats = i % 3 == 0 ? 2.75m : 0 }).ToList() } } };
            var watch = Stopwatch.StartNew(); var window = new EditorWindow(p) { AudioEnabled = false, Width = 1232, Height = 740 }; window.Show(); Pump(); double open = watch.Elapsed.TotalMilliseconds;
            var roll = window.Viewport; roll.SetView(count / 8m); Pump(); var timings = new List<double>();
            for (int i = 0; i < 50; i++) { watch.Restart(); roll.SetView(count / 8m + i / 4m); Pump(); timings.Add(watch.Elapsed.TotalMilliseconds); }
            int noteBuilds = roll.NoteLayerBuilds; watch.Restart(); for (int i = 0; i < 1000; i++) roll.PlayheadBeat = count / 8m + i / 100m; Pump(); double cursor = watch.Elapsed.TotalMilliseconds;
            Check(roll.NoteLayerBuilds == noteBuilds, "Cursor performance rebuilt notes");
            var index = new NoteViewportIndex(p.Tracks[0].Notes); watch.Restart(); int found = 0; for (int i = 0; i < 1000; i++) found = index.Query(count / 8m, count / 8m + 16, tapWidth: .25m).Count(); double query = watch.Elapsed.TotalMilliseconds;
            window.Session.Select(new[] { window.Session.ActiveTrack.Notes[count / 2].Id }); long allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Restart(); window.Session.MoveSelected(.25m, 0); double commit = watch.Elapsed.TotalMilliseconds; allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            using var process = Process.GetCurrentProcess(); process.Refresh();
            timings.Sort(); Console.WriteLine(JsonSerializer.Serialize(new { notes = count, open_ms = open, viewport_mean_ms = timings.Average(), viewport_p95_ms = timings[47], cursor_1000_ms = cursor, query_1000_ms = query, visible = found, visited = index.LastVisitedNodes, commit_ms = commit, commit_allocated_bytes = allocated, history_note_versions = window.Session.HistoryNoteVersions, process_peak_working_set = process.PeakWorkingSet64 }));
            Close(window);
        }
        DenseMetrics();
    }
    static void DenseMetrics()
    {
        var p = DenseProject(24000);
        var window = new EditorWindow(p) { AudioEnabled = false, Width = 1232, Height = 740 }; window.Show(); Pump(); var roll = window.Viewport;
        var watch = new Stopwatch(); var frames = new List<double>();
        for (int i = 0; i < 30; i++) { watch.Restart(); roll.SetView(i / 100m); Pump(); frames.Add(watch.Elapsed.TotalMilliseconds); }
        window.Session.Select(window.Session.ActiveTrack.Notes.Where(n => n.Beat < 1).Take(1000).Select(n => n.Id)); Pump();
        var selected = window.Session.SelectedNotes().First(n => n.Key == 4 && n.Beat >= roll.ViewBeat + .25m); var bounds = roll.Bounds(selected); var start = new Point(bounds.Left + 10, bounds.Top + 5);
        int history = window.Session.UndoCount; roll.PointerDown(start, MouseButton.Left, ModifierKeys.None); var drag = new List<double>();
        for (int i = 1; i <= 60; i++) { watch.Restart(); roll.PointerMove(new Point(start.X + i, start.Y), ModifierKeys.None); Pump(); drag.Add(watch.Elapsed.TotalMilliseconds); }
        Check(window.Session.UndoCount == history, "Dense drag committed before release"); watch.Restart(); roll.PointerUp(new Point(start.X + 60, start.Y), ModifierKeys.None); Pump(); double commit = watch.Elapsed.TotalMilliseconds;
        Check(window.Session.UndoCount == history + 1, "Dense drag did not form a transaction");
        Check(roll.LastRenderedNotes <= 8000 + 15 * (int)Math.Ceiling(roll.ActualWidth), "Dense draw budget was not bounded");
        Settle(TimeSpan.FromSeconds(1));
        using var process = Process.GetCurrentProcess(); process.Refresh(); var cpu = process.TotalProcessorTime; int threadsBefore = process.Threads.Count; watch.Restart();
        Settle(TimeSpan.FromSeconds(3));
        process.Refresh(); double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, elapsed = watch.Elapsed.TotalMilliseconds;
        frames.Sort(); drag.Sort(); Console.WriteLine(JsonSerializer.Serialize(new { scenario = "dense-overlap-long-hold", notes = p.Tracks[0].Notes.Count, viewport_mean_ms = frames.Average(), viewport_p95_ms = frames[28], drag_mean_ms = drag.Average(), drag_p95_ms = drag[56], gesture_commit_and_render_ms = commit, drawn = roll.LastRenderedNotes, selected = window.Session.Selected.Count, idle_elapsed_ms = elapsed, idle_cpu_ms = cpuMs, idle_one_core_percent = cpuMs / elapsed * 100, threads_before = threadsBefore, threads_after = process.Threads.Count, process_peak_working_set = process.PeakWorkingSet64 }));
        Close(window);
    }
    static SmapProject DenseProject(int count)
    {
        var p = EditorSession.NewProject(); var t = p.Tracks[0];
        var notes = Enumerable.Range(0, count).Select(i => new ProjectNote { TrackId = t.Id, Key = i % 15, Beat = (i % 960) / 60m, Action = NoteAction.Hold, LengthBeats = .5m + i % 7 }).ToList();
        notes.Add(new ProjectNote { TrackId = t.Id, Key = 0, Beat = 0, Action = NoteAction.Hold, LengthBeats = 100000000000000000m });
        return p with { Tracks = new() { t with { Notes = notes } } };
    }
    static void Settle(TimeSpan duration)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
}
