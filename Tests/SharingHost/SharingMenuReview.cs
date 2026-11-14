using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;

static partial class Program
{
    static void SharingMenuReview()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Palette(AppTheme.Dark); Language(AppLang.ZhCN);
        var owner = (MainWindow)typeof(MainWindow).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(bool)], null)!.Invoke([true]);
        owner.WindowStartupLocation = WindowStartupLocation.Manual; owner.Left = -12000; owner.Top = -12000; owner.ShowInTaskbar = false; owner.Show();
        var output = Path.Combine(Output, "unified-sharing/menu-actual"); Directory.CreateDirectory(output);
        using var transport = new CloudTransport(new Uri("https://missing.example.invalid/"), new SharingRouteMissing());
        foreach (AppTheme theme in Enum.GetValues<AppTheme>()) foreach (AppLang language in Enum.GetValues<AppLang>())
        {
            Palette(theme); Language(language);
            var window = new NativeSharingWindow(owner, Sample(), client: new NativeSharingClient(transport, () => null),
                store: new NativeSharingStore(Path.Combine(Output, "private/menu-ui")), user: new(() => false, () => 0, _ => false))
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, ShowInTaskbar = false };
            window.Show(); WaitUntil(() => !PrivateField<bool>(window, "_busy"));
            var menu = PrivateField<ContextMenu>(window, "_publicationMenu"); menu.IsOpen = true; Pump(); menu.UpdateLayout();
            var item = menu.Items.Cast<MenuItem>().First();
            var root = (FrameworkElement)PresentationSource.FromVisual(item)!.RootVisual;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, theme + "-" + language + ".png"))) encoder.Save(file);
            string id = theme + "-" + language;
            Check("SHARE-MENU-USES-OWNER-STYLES-" + id, ReferenceEquals(menu.Style, owner.FindResource(typeof(ContextMenu))) && menu.Items.Cast<MenuItem>().All(i => ReferenceEquals(i.Style, owner.FindResource(typeof(MenuItem)))));
            var border = Descendants<Border>(menu).First();
            Check("SHARE-MENU-THEME-AND-ROUNDING-" + id, border.CornerRadius == new CornerRadius(8) && ((SolidColorBrush)border.Background).Color == ((SolidColorBrush)app.Resources["BoxBg"]).Color && menu.Items.Cast<MenuItem>().All(i => ((SolidColorBrush)i.Foreground).Color == ((SolidColorBrush)app.Resources["TextFg"]).Color));
            Check("SHARE-MENU-LOCALIZED-PADDING-" + id, item.Header as string == Lang.Sharing("retry") && menu.Items.Cast<MenuItem>().All(i => i.Padding == new Thickness(16, 7, 16, 7)));
            PrivateField<SharingModeTabs>(window, "_modes").Select(1); Pump();
            Check("SHARE-MENU-CLOSES-ON-MODE-CHANGE-" + id, !menu.IsOpen);
            window.Close(); Pump();
        }
        owner.Close(); app.Shutdown();
    }
}
