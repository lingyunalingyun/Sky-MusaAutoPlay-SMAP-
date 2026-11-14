using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Updates;

static partial class Program
{
    static void UpdaterUiReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
        string[] protectedFiles = ["theme.txt", "lang.txt", "nowplaying.txt", "prefs.txt", "playlist.txt", "playmode.txt"];
        string Digest(string name) => File.Exists(Path.Combine(profile, name)) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(profile, name)))) : "absent";
        var before = protectedFiles.ToDictionary(f => f, Digest);
        string images = Path.Combine(Output, "updater-ui"); Directory.CreateDirectory(images);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var theme in Enum.GetValues<AppTheme>()) foreach (var language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language); string id = theme + "-" + language;
            var main = (MainWindow)typeof(MainWindow).GetConstructor(flags, null, [typeof(bool)], null)!.Invoke([true]);
            main.WindowStartupLocation = WindowStartupLocation.Manual; main.Left = -12000; main.Top = -12000;
            main.ShowActivated = false; main.ShowInTaskbar = false; main.Width = 1280; main.Height = 860; main.Show(); Pump();
            typeof(MainWindow).GetMethod("Settings_Click", flags)!.Invoke(main, [main, new RoutedEventArgs()]);
            var until = System.Diagnostics.Stopwatch.StartNew(); while (until.ElapsedMilliseconds < 500) Pump();
            var pane = (UpdateSettingsPane)main.FindName("UpdatePane"); pane.Visibility = Visibility.Visible;
            var service = (DeltaUpdateService)typeof(MainWindow).GetField("_deltaUpdates", flags)!.GetValue(main)!;
            void State(DeltaUpdateState state) { typeof(DeltaUpdateService).GetProperty(nameof(DeltaUpdateService.State))!.SetValue(service, state); pane.Refresh(service); Pump(); }
            var settings = (Grid)main.FindName("SettingsPanel");
            Check("UPDATE-EMBEDDED-IN-SETTINGS-" + id, settings.IsAncestorOf(pane) && Window.GetWindow(pane) == main && app.Windows.Count == 1);
            foreach (var idle in new[] { DeltaUpdateState.Idle, DeltaUpdateState.Current })
            {
                State(idle);
                var actions = Descendants<Button>(pane).Where(b => b.IsVisible).ToArray();
                Check("UPDATE-ONLY-CHECK-BUTTON-" + idle + "-" + id,
                    actions.Length == 1 && actions[0].IsEnabled && Equals(actions[0].Content, Lang.Update("check"))
                    && !Descendants<TextBlock>(pane).Any(t => t.IsVisible && !actions[0].IsAncestorOf(t))
                    && pane.ActualHeight <= 40 && pane.Padding == default);
            }
            CaptureMetadata(main, Path.Combine(images, id + "-current.png"));
            State(DeltaUpdateState.Downloading);
            Check("UPDATE-DETAILS-RESTORED-" + id, pane.ActualHeight > 100 && Descendants<TextBlock>(pane).Any(t => t.IsVisible && t.Text == Lang.Update("title")));
            Check("UPDATE-CONTINUE-USING-MAIN-" + id, main.IsEnabled && pane.IsVisible && Descendants<Button>(pane).Any(b => Equals(b.Content, Lang.Update("pause"))));
            Check("UPDATE-LOCALIZATION-" + id, new[] { "title", "ready", "restart", "found", "continueUse", "preserved" }.All(k => Lang.Update(k) != k));
            Check("UPDATE-G2-" + id, Descendants<System.Windows.Controls.Border>(pane).Where(b => b.CornerRadius != default).All(b => b is G2Border));
            typeof(MainWindow).GetMethod("ShowToast", flags)!.Invoke(main, [Lang.Update("found")]); Pump();
            var capsuleClock = System.Diagnostics.Stopwatch.StartNew(); while (capsuleClock.ElapsedMilliseconds < 220) Pump();
            var capsule = (G2Border)main.FindName("Toast");
            Check("UPDATE-CAPSULE-" + id, ((TextBlock)main.FindName("ToastText")).Text == Lang.Update("found") && capsule.IsVisible && capsule.Opacity > .9 && capsule.ActualWidth > 100);
            CaptureMetadata(main, Path.Combine(images, id + "-downloading.png"));
            State(DeltaUpdateState.Paused); Check("UPDATE-RESUME-ACTION-" + id, Descendants<Button>(pane).Any(b => Equals(b.Content, Lang.Update("resume"))));
            State(DeltaUpdateState.Error); CaptureMetadata(main, Path.Combine(images, id + "-retry.png"));
            State(DeltaUpdateState.Ready); typeof(MainWindow).GetMethod("ShowUpdateReady", flags)!.Invoke(main, null); Pump();
            var ready = (ChromeWindow)typeof(MainWindow).GetField("_updateReadyNotice", flags)!.GetValue(main)!;
            Check("UPDATE-SMALL-NONMODAL-READY-" + id, ready != null && ready.ActualWidth < 470 && main.IsEnabled && !ready.ShowActivated);
            CaptureMetadata(ready!, Path.Combine(images, id + "-ready.png"));
            var later = Descendants<Button>(ready!).Single(b => Equals(b.Content, Lang.Update("later"))); later.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, later)); Pump();
            Check("UPDATE-LATER-KEEPS-APP-RUNNING-" + id, !ready!.IsVisible && main.IsVisible && service.State == DeltaUpdateState.Ready);
            main.Close();
        }
        Check("UPDATE-VERIFICATION-USER-STATE-UNCHANGED", protectedFiles.All(f => Digest(f) == before[f]));
        app.Shutdown();
    }
}
