using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    static void LibraryRefreshReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark);
        string original = SongLibrary.SongsDir;
        string root = Path.Combine(Output, "synthetic-library-" + Guid.NewGuid().ToString("N"));
        SongLibrary.SongsDir = Path.Combine(root, "songs");
        var window = (MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null)!.Invoke([true]);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        List<SongInfo> Songs() => (List<SongInfo>)Field("_all")!;
        void Until(Func<bool> predicate)
        {
            var end = DateTime.UtcNow.AddSeconds(12);
            while (!predicate() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(20); }
            if (!predicate()) throw new InvalidOperationException("Library refresh timed out");
            Pump();
        }
        void Settle()
        {
            var end = DateTime.UtcNow.AddMilliseconds(1600);
            while (DateTime.UtcNow < end) { Pump(); Thread.Sleep(20); }
            Until(() => !(bool)Field("_libraryScanRunning")!);
        }
        static string Json(string title) => System.Text.Json.JsonSerializer.Serialize(new[] { new { name = title, bpm = 120, songNotes = new[] { new { time = 0, key = "1Key0" } } } });
        try
        {
            window.ShowInTaskbar = false; window.Show(); window.Left = -12000; window.Top = -12000;
            Call("StartLibraryWatcher");
            Check("LIBRARY-MISSING-DIRECTORY-CREATED", Directory.Exists(SongLibrary.SongsDir));
            string file = Path.Combine(SongLibrary.SongsDir, "external.txt");
            File.WriteAllText(file, Json("External song"));
            Until(() => Songs().Count == 1);
            Check("LIBRARY-EXTERNAL-COPY-APPEARS", Songs()[0].Name == "External song");
            File.WriteAllText(file, Json("Changed title"));
            Until(() => Songs().SingleOrDefault()?.Name == "Changed title");
            Check("LIBRARY-EXTERNAL-EDIT-APPEARS", true);
            File.WriteAllText(file, "["); Settle();
            File.WriteAllText(file, Json("Completed copy"));
            Until(() => Songs().SingleOrDefault()?.Name == "Completed copy");
            Check("LIBRARY-PARTIAL-COPY-RECOVERS", true);
            string renamed = Path.Combine(SongLibrary.SongsDir, "renamed.json"); File.Move(file, renamed);
            Until(() => Songs().SingleOrDefault()?.File == renamed);
            Check("LIBRARY-RENAME-APPEARS", true);
            File.Move(renamed, renamed + ".bak"); Until(() => Songs().Count == 0);
            Check("LIBRARY-RENAME-OUT-OF-SUPPORTED-FORMAT", true);
            File.Move(renamed + ".bak", file); Until(() => Songs().Count == 1);
            Check("LIBRARY-RENAME-INTO-SUPPORTED-FORMAT", true);
            File.Delete(file); Until(() => Songs().Count == 0);
            Check("LIBRARY-DELETE-APPEARS", true);
            var search = (TextBox)window.FindName("SearchBox"); search.Text = "match";
            var sort = (ComboBox)window.FindName("SortCombo"); sort.SelectedIndex = 1;
            for (int i = 0; i < 20; i++) File.WriteAllText(Path.Combine(SongLibrary.SongsDir, "batch-" + i + ".txt"), Json(i == 3 ? "Match me" : "Batch " + i));
            Until(() => Songs().Count == 20);
            Check("LIBRARY-BATCH-COPY-APPEARS", true);
            var list = (ListView)window.FindName("SongList");
            Check("LIBRARY-SEARCH-AND-SORT-PRESERVED", search.Text == "match" && sort.SelectedIndex == 1 && list.Items.Count == 1);
            var project = new SmapProject { Tracks = [new ProjectTrack { Name = "Native" }] };
            string native = Path.Combine(SongLibrary.SongsDir, "native.smap"); SmapFile.Save(project, native);
            Until(() => Songs().Count == 21);
            Check("LIBRARY-NATIVE-PROJECT-APPEARS", Songs().Any(s => s.File == native));
            Settle(); int generation = (int)Field("_libraryRefreshVersion")!;
            File.WriteAllText(Path.Combine(SongLibrary.SongsDir, "unrelated.tmp"), "ignored"); Settle();
            Check("LIBRARY-UNRELATED-FILES-NOT-RESCANNED", (int)Field("_libraryRefreshVersion")! == generation);
            ((FileSystemWatcher)Field("_libraryWatcher")!).Dispose();
            File.WriteAllText(Path.Combine(SongLibrary.SongsDir, "fallback.json"), Json("Fallback"));
            Call("ShowLocal_Click", window, new RoutedEventArgs()); Until(() => Songs().Count == 22);
            Check("LIBRARY-LOCAL-TAB-RESCANS-WITHOUT-WATCHER", true);
            window.Close();
            Check("LIBRARY-WATCHER-DISPOSED-ON-CLOSE", Field("_libraryWatcher") == null && !(bool)((DispatcherTimer)Field("_libraryRefreshDelay")!).IsEnabled);
        }
        finally
        {
            if (window.IsVisible) window.Close();
            SongLibrary.SongsDir = original;
            app.Shutdown();
        }
    }
}
