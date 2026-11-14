using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        string[] protectedFiles = ["theme.txt", "lang.txt", "nowplaying.txt", "prefs.txt", "playlist.txt", "playmode.txt", "update-channel.txt"];
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
            var pane = (UpdateSettingsPane)main.FindName("UpdatePane");
            var service = (DeltaUpdateService)typeof(MainWindow).GetField("_deltaUpdates", flags)!.GetValue(main)!;
            void State(DeltaUpdateState state) { typeof(DeltaUpdateService).GetProperty(nameof(DeltaUpdateService.State))!.SetValue(service, state); pane.Refresh(service); Pump(); }
            var settings = (Grid)main.FindName("SettingsPanel");
            var shortcuts = (Button)main.FindName("SetShortcutsBtn");
            var fallback = (Grid)main.FindName("SetUpdateRow");
            var actionHost = (ContentControl)main.FindName("UpdateActionHost");
            var channelCombo = (ComboBox)main.FindName("SetUpdateChannelCombo");
            var channelChoice = (JoinedChoiceButton)main.FindName("SetUpdateChannelChoice");
            var checkButton = (Button)actionHost.Content;
            var column = Descendants<ScrollViewer>(settings).Single(s => s.Content is StackPanel p && p.IsAncestorOf(actionHost));
            var detailsColumn = Descendants<ScrollViewer>(settings).Single(s => s.Content is StackPanel p && p.IsAncestorOf(pane));
            Check("UPDATE-ONE-SETTINGS-ENTRY-" + id, actionHost.IsVisible == service.Supported && fallback.IsVisible != service.Supported);
            Check("UPDATE-ACTIONS-LEFT-COLUMN-" + id, Grid.GetColumn(column) == 0 && column.IsAncestorOf(shortcuts)
                && Equals(shortcuts.Content, Lang.S("map.shortcuts")));
            Check("UPDATE-DETAILS-RIGHT-COLUMN-" + id, Grid.GetColumn(detailsColumn) == 1 && !detailsColumn.IsAncestorOf(checkButton));
            Check("UPDATE-CHANNEL-LOCALIZATION-" + id, channelCombo.Items.Count == 2
                && Equals(channelCombo.Items[0], Lang.Update("stableChannel")) && Equals(channelCombo.Items[1], Lang.Update("testingChannel")));
            channelCombo.SelectedIndex = 0; Pump();
            Check("UPDATE-STABLE-SELECTION-" + id, service.Channel == "stable" && fallback.IsVisible == !service.Supported
                && actionHost.IsVisible == service.Supported && service.State == DeltaUpdateState.Idle);
            channelCombo.SelectedIndex = 1; Pump();
            Check("UPDATE-TESTING-SELECTION-" + id, service.Channel == "alpha" && actionHost.IsVisible && !fallback.IsVisible
                && service.State == DeltaUpdateState.Idle);
            ((AnimatedChoicePopup)main.FindName("LibraryChoicePopup")).ReduceMotion = true;
            channelChoice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, channelChoice)); Pump(); main.UpdateLayout();
            CaptureMetadata(main, Path.Combine(images, id + "-channel-menu.png"));
            var optionsTop = channelChoice.Options.TranslatePoint(new Point(), column);
            File.WriteAllText(Path.Combine(images, id + "-channel-menu-layout.json"), System.Text.Json.JsonSerializer.Serialize(new
            { channelChoice.OpenAbove, channelChoice.IsOpen, channelChoice.ActualHeight, chromeHeight = channelChoice.Chrome.ActualHeight,
                requestedHeight = channelChoice.Chrome.Height, transformY = channelChoice.Chrome.RenderTransform.Value.OffsetY,
                optionsY = optionsTop.Y, optionsHeight = channelChoice.Options.ActualHeight, viewportHeight = column.ActualHeight }));
            Check("UPDATE-CHANNEL-MENU-NOT-CLIPPED-" + id, channelChoice.IsOpen && channelChoice.Options.Items.Count == 2
                && optionsTop.Y >= 0 && optionsTop.Y + channelChoice.Options.ActualHeight <= column.ActualHeight);
            Check("UPDATE-CHANNEL-MENU-HIT-TEST-" + id, channelChoice.Options.Items.Cast<ListBoxItem>().All(option =>
            {
                var point = option.TranslatePoint(new Point(option.ActualWidth / 2, option.ActualHeight / 2), main);
                var hit = main.InputHitTest(point) as DependencyObject;
                return hit != null && (ReferenceEquals(hit, option) || option.IsAncestorOf(hit));
            }));
            var stableOption = (ListBoxItem)channelChoice.Options.Items[0];
            channelChoice.Options.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = stableOption }); Pump();
            Check("UPDATE-CHANNEL-MENU-COMMITS-SELECTION-" + id, service.Channel == "stable" && !channelChoice.IsOpen);
            channelCombo.SelectedIndex = 1; Pump();
            var choicePopup = (AnimatedChoicePopup)main.FindName("LibraryChoicePopup"); choicePopup.ReduceMotion = false;
            channelChoice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, channelChoice));
            var motionClock = System.Diagnostics.Stopwatch.StartNew(); while (motionClock.ElapsedMilliseconds < 120) Pump();
            var header = (G2Border)typeof(JoinedChoiceButton).GetField("_header", flags)!.GetValue(channelChoice)!;
            Check("UPDATE-CHANNEL-MOTION-ANCHOR-" + id, Math.Abs(header.TranslatePoint(new Point(), main).Y - channelChoice.TranslatePoint(new Point(), main).Y) < 2);
            choicePopup.Close(true); choicePopup.ReduceMotion = true; Pump();
            fallback.Visibility = Visibility.Collapsed; actionHost.Visibility = Visibility.Visible; Pump();
            Check("UPDATE-EMBEDDED-IN-SETTINGS-" + id, settings.IsAncestorOf(pane) && Window.GetWindow(pane) == main && app.Windows.Count == 1);
            if (service.Supported)
            {
                var response = new UpdateStatusReply();
                ((GitHubUpdates)typeof(DeltaUpdateService).GetField("_remote", flags)!.GetValue(service)!).Dispose();
                typeof(DeltaUpdateService).GetField("_remote", flags)!.SetValue(service, new GitHubUpdates(response));
                void Await(Task task)
                {
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (!task.IsCompleted && wait.ElapsedMilliseconds < 5000) Pump();
                    if (!task.IsCompleted) throw new TimeoutException("Updater check did not finish.");
                    task.GetAwaiter().GetResult(); Pump();
                }
                var toastText = (TextBlock)main.FindName("ToastText");
                toastText.Text = "before-automatic-check";
                Await(service.Start());
                Check("UPDATE-AUTOMATIC-CURRENT-SILENT-" + id, service.State == DeltaUpdateState.Current && response.Requests == 1 && toastText.Text == "before-automatic-check");
                Await((Task)typeof(MainWindow).GetMethod("CheckUpdateManual", flags)!.Invoke(main, null)!);
                Check("UPDATE-MANUAL-CURRENT-NOTICE-" + id, service.State == DeltaUpdateState.Current && response.Requests == 2 && toastText.Text == Lang.Update("current"));
                var noticeClock = System.Diagnostics.Stopwatch.StartNew(); while (noticeClock.ElapsedMilliseconds < 220) Pump();
                CaptureMetadata(main, Path.Combine(images, id + "-current-notice.png"));
                response.Fail = true; toastText.Text = "before-failed-check";
                Await((Task)typeof(MainWindow).GetMethod("CheckUpdateManual", flags)!.Invoke(main, null)!);
                Check("UPDATE-FAILED-CHECK-NOT-CURRENT-" + id, service.State == DeltaUpdateState.Error && response.Requests == 3 && toastText.Text == "before-failed-check" && pane.IsVisible);
                response.Fail = false; response.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Task checking = service.Start();
                Check("UPDATE-CHANNEL-DISABLED-DURING-CHECK-" + id, !channelChoice.IsEnabled && !service.CanChangeChannel);
                channelCombo.SelectedIndex = 0; Pump();
                Check("UPDATE-BUSY-SWITCH-RETAINED-" + id, channelCombo.SelectedIndex == 1 && service.Channel == "alpha");
                response.Gate.SetResult(true); Await(checking);
                Check("UPDATE-CHANNEL-REENABLED-AFTER-CHECK-" + id, channelChoice.IsEnabled && service.CanChangeChannel);
            }
            foreach (var idle in new[] { DeltaUpdateState.Idle, DeltaUpdateState.Current })
            {
                State(idle);
                var actions = Descendants<Button>(actionHost).Where(b => b.IsVisible).ToArray();
                Check("UPDATE-ONLY-CHECK-BUTTON-" + idle + "-" + id,
                    actions.Length == 1 && actions[0].IsEnabled && Equals(actions[0].Content, Lang.Update("check"))
                    && !pane.IsVisible && actionHost.ActualHeight <= 40);
            }
            var pitch = (Button)main.FindName("SetPitchResetBtn");
            Point Position(FrameworkElement control) => control.TranslatePoint(new Point(), settings);
            Check("UPDATE-CONTROLS-ORDER-AND-ALIGNMENT-" + id,
                Position(pitch).Y < Position(shortcuts).Y && Position(shortcuts).Y < Position(checkButton).Y
                && Math.Abs(Position(pitch).X - Position(checkButton).X) < 1
                && Math.Abs(Position(channelChoice).Y - Position(actionHost).Y) < 3
                && Math.Abs(Position(pitch).X + pitch.ActualWidth - Position(channelChoice).X - channelChoice.ActualWidth) < 1);
            CaptureMetadata(main, Path.Combine(images, id + "-current.png"));
            State(DeltaUpdateState.Downloading);
            pane.BringIntoView(); Pump();
            Check("UPDATE-DETAILS-RESTORED-" + id, pane.ActualHeight > 100 && Descendants<TextBlock>(pane).Any(t => t.IsVisible && t.Text == Lang.Update("title")));
            Check("UPDATE-CONTINUE-USING-MAIN-" + id, main.IsEnabled && pane.IsVisible && checkButton.IsVisible && Equals(checkButton.Content, Lang.Update("pause")) && !Descendants<Button>(pane).Any());
            Check("UPDATE-LOCALIZATION-" + id, new[] { "title", "ready", "restart", "found", "continueUse", "preserved" }.All(k => Lang.Update(k) != k));
            Check("UPDATE-G2-" + id, Descendants<System.Windows.Controls.Border>(pane).Where(b => b.CornerRadius != default).All(b => b is G2Border));
            typeof(MainWindow).GetMethod("ShowToast", flags)!.Invoke(main, [Lang.Update("found")]); Pump();
            var capsuleClock = System.Diagnostics.Stopwatch.StartNew(); while (capsuleClock.ElapsedMilliseconds < 220) Pump();
            var capsule = (G2Border)main.FindName("Toast");
            Check("UPDATE-CAPSULE-" + id, ((TextBlock)main.FindName("ToastText")).Text == Lang.Update("found") && capsule.IsVisible && capsule.Opacity > .9 && capsule.ActualWidth > 100);
            CaptureMetadata(main, Path.Combine(images, id + "-downloading.png"));
            State(DeltaUpdateState.Paused); Check("UPDATE-RESUME-ACTION-" + id, Equals(checkButton.Content, Lang.Update("resume")));
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
    sealed class UpdateStatusReply : HttpMessageHandler
    {
        public int Requests;
        public bool Fail;
        public TaskCompletionSource<bool>? Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Gate != null) await Gate.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent("[]") };
        }
    }
}
