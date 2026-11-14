using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    sealed class MetadataUiReply(string hash) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = Array.Empty<object>(), performanceHash = hash, canonicalVersion = 1, more = false, legacyCatalogReady = true, legacyItems = Array.Empty<object>(), legacyCatalog = new { status = "complete", converterVersion = 1, scope = "published-song-sheets", totalRecords = 0, coveredRecords = 0, unresolvedRecords = 0 } }), Encoding.UTF8, "application/json") });
    }

    static void MetadataReview()
    {
        Directory.CreateDirectory(Output); var images = Path.Combine(Output, "actual-gui"); Directory.CreateDirectory(images);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark); Language(AppLang.ZhCN);
        var owner = (MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(bool)], null)!.Invoke([true]);
        owner.WindowStartupLocation = WindowStartupLocation.Manual; owner.Left = -12000; owner.Top = -12000; owner.ShowInTaskbar = false; owner.Show();
        var tracks = Enumerable.Range(0, 128).Select(i => new ProjectTrack { Name = "Track " + i.ToString("000"), Muted = i > 1 }).ToList();
        foreach (var track in tracks) track.Notes.Add(new() { TrackId = track.Id, Key = 1, Beat = 12, LengthBeats = 2, Action = NoteAction.Hold });
        var project = new SmapProject { Tracks = tracks, Metadata = new() { Name = "被生命厌恶 · Long mixed title for metadata verification", Author = "まふまふ", TranscribedBy = "合成制谱人" } };
        var audibleHash = PerformanceIdentity.ForProject(NativeSharingClient.SharingSelection(project, new ScoreExportOptions { TrackIds = tracks.Take(2).Select(t => t.Id).ToArray() }));
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new MetadataUiReply(audibleHash));
        foreach (var theme in Enum.GetValues<AppTheme>()) foreach (var language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language); string id = theme + "-" + language;
            var window = new NativeSharingWindow(owner, project, client: new(transport, () => null), store: new(Path.Combine(Output, "private/ui-drafts")), user: new(() => false, () => 0, _ => false), presentation: new() { Difficulty = 3, Tags = ["流行", "二次元"] });
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.Top = -12000; window.ShowInTaskbar = false;
            window.Show(); WaitBusy(window); Pump();
            var labels = Descendants<TextBlock>(window).Select(t => t.Text).ToArray();
            Check("METADATA-FIELDS-" + id, new[] { "artist", "transcriber", "difficulty", "tags", "description", "performanceInfo" }.All(k => labels.Contains(Lang.Sharing(k))));
            Check("METADATA-G2-BORDERS-" + id, Descendants<System.Windows.Controls.Border>(window).Where(b => b.CornerRadius != default).All(b => b is G2Border));
            Check("METADATA-TRASH-ICON-" + id, Descendants<Button>(window).Any(b => b.ToolTip?.ToString() == Lang.Sharing("coverRemove") && b.Content is System.Windows.Shapes.Path));
            var remove = Descendants<Button>(window).Single(b => b.ToolTip?.ToString() == Lang.Sharing("coverRemove"));
            var icon = (System.Windows.Shapes.Path)remove.Content;
            var iconPresenter = Descendants<ContentPresenter>(remove).Single();
            bool iconFits = iconPresenter.ActualWidth >= icon.Width && iconPresenter.ActualHeight >= icon.Height;
            var difficulty = Descendants<JoinedChoiceButton>(window).Single(b => b is not TrackMultiChoiceButton && Equals(b.Content, Lang.Sharing("difficulty3")));
            difficulty.ReduceMotion = true; difficulty.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, difficulty)); Pump();
            bool optionsAboveForm = difficulty.Options.Items.Cast<ListBoxItem>().All(option =>
            {
                var point = option.TranslatePoint(new Point(option.ActualWidth / 2, option.ActualHeight / 2), window);
                var hit = window.InputHitTest(point) as DependencyObject;
                return hit != null && (ReferenceEquals(hit, option) || option.IsAncestorOf(hit));
            });
            File.WriteAllText(Path.Combine(Output, "layout-" + id + ".json"), JsonSerializer.Serialize(new { iconFits, iconWidth = icon.Width, availableWidth = iconPresenter.ActualWidth, optionsAboveForm }));
            CaptureMetadata(window, Path.Combine(images, id + "-difficulty-open.png")); difficulty.Close(true);
            Check("METADATA-TRASH-NOT-CLIPPED-" + id, iconFits);
            Check("METADATA-DIFFICULTY-ABOVE-FORM-" + id, optionsAboveForm);
            var picker = Descendants<TrackMultiChoiceButton>(window).Single();
            Check("TRACK-INITIAL-SELECTION-" + id, picker.SelectedIds.SequenceEqual(tracks.Take(2).Select(t => t.Id)));
            CaptureMetadata(window, Path.Combine(images, id + ".png"));
            picker.ReduceMotion = true; picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, picker)); Pump();
            Check("TRACK-JOINED-OPEN-" + id, picker.IsOpen && picker.Chrome.Clip is StreamGeometry && ReferenceEquals(PresentationSource.FromVisual(picker.Chrome), PresentationSource.FromVisual(window)));
            var list = Descendants<ListBox>(picker.Chrome).Single(l => l.Items.Count == 128);
            Check("TRACK-VIRTUALIZATION-" + id, VirtualizingPanel.GetIsVirtualizing(list) && Descendants<ListBoxItem>(list).Count() < 30);
            var search = Descendants<TextBox>(picker.Chrome).Single(); search.Text = "127"; Pump();
            Check("TRACK-SEARCH-PRESERVES-SELECTION-" + id, list.Items.Count == 1 && picker.SelectedIds.Length == 2);
            picker.SelectAll(true); Check("TRACK-ALL-INCLUDES-HIDDEN-" + id, picker.SelectedIds.Length == 128);
            var check = Descendants<CheckBox>(list).Single(); check.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false); check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, check)); Pump();
            Check("TRACK-MULTI-DOES-NOT-CLOSE-" + id, picker.IsOpen && picker.SelectedIds.Length == 127);
            search.Clear(); Pump(); CaptureMetadata(window, Path.Combine(images, id + "-tracks-open.png"));
            picker.Close(true); picker.SelectAll(false);
            Check("TRACK-CLEAR-AND-CLOSE-" + id, !picker.IsOpen && picker.SelectedIds.Length == 0);
            if (theme == AppTheme.Dark && language == AppLang.ZhCN)
            {
                var root = Descendants<Grid>(window).Single(g => g.RowDefinitions.Count == 2 && g.Height >= 440);
                root.Height = 440; Pump(); picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, picker)); Pump();
                var viewport = Descendants<ScrollViewer>(window).Single(s => ReferenceEquals(s.Content, PrivateField<StackPanel>(window, "_body")));
                var bounds = picker.Chrome.TransformToAncestor(viewport).TransformBounds(new Rect(picker.Chrome.RenderSize));
                Console.WriteLine($"COMPACT_TRACKS maxList={list.MaxHeight:F2};actualList={list.ActualHeight:F2};bottom={bounds.Bottom:F2};viewport={viewport.ActualHeight:F2};chrome={picker.Chrome.ActualHeight:F2}");
                Check("TRACK-COMPACT-HEIGHT-BOUNDED", list.MaxHeight < 220 && bounds.Bottom <= viewport.ActualHeight + 1);
                picker.Close(true);
            }
            window.Close();
        }
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            Palette(theme); Language(AppLang.ZhCN);
            var main = (MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(bool)], null)!.Invoke([true]);
            main.WindowStartupLocation = WindowStartupLocation.Manual; main.Left = -12000; main.Top = -12000; main.ShowInTaskbar = false; main.Show(); Pump();
            Check("MAIN-G2-BORDERS-" + theme, Descendants<System.Windows.Controls.Border>(main).Where(b => b.CornerRadius != default).All(b => b is G2Border));
            CaptureMetadata(main, Path.Combine(images, "main-" + theme + ".png"));
            main.Close();
        }
        owner.Close(); app.Shutdown();
    }

    static void CaptureMetadata(Window window, string file)
    {
        Pump(); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(file); encoder.Save(output);
    }
}
