using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[CollectionDefinition("MIDI UI", DisableParallelization = true)]
public sealed class MidiUiCollection;

[Collection("MIDI UI")]
public class MidiUiTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("12.375", 12375)] [InlineData("00:12.375", 12375)] [InlineData("1:02", 62000)] [InlineData("1:02:03.5", 3723500)]
    public void ComparisonRegionInputUsesMusicalMinutesAndSeconds(string value, double expected)
    { Assert.True(MidiImportDialog.TryTime(value, out double actual)); Assert.Equal(expected, actual); }

    [Theory]
    [InlineData("NaN")] [InlineData("-1")] [InlineData("1:60")] [InlineData("1::2")] [InlineData("1.5:02")]
    public void InvalidComparisonTimesAreRejected(string value) => Assert.False(MidiImportDialog.TryTime(value, out _));

    [Fact]
    public void NewInterfaceStringsHaveFourCompleteTranslations()
    {
        _ = Lang.S("midi3.title");
        var strings = (Dictionary<string, string[]>)typeof(Lang).GetField("T", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var entries = strings.Where(p => p.Key.StartsWith("midi3.", StringComparison.Ordinal)).ToArray(); Assert.True(entries.Length > 60);
        foreach (var entry in entries)
        {
            Assert.Equal(4, entry.Value.Length);
            Assert.All(entry.Value, s => { Assert.False(string.IsNullOrWhiteSpace(s)); Assert.DoesNotContain("???", s); _ = string.Format(s, Enumerable.Repeat<object>(1, 10).ToArray()); });
        }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root)) if (child is DependencyObject node)
            foreach (var descendant in Descendants(node)) yield return descendant;
    }
    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    static void Click(MidiImportDialog dialog, string label) => Descendants(dialog).OfType<Button>().First(b => Equals(b.Content, Lang.S("midi3." + label))).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    static void PumpUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        Assert.True(condition(), "界面计算没有完成。");
    }

    [Fact]
    public void ApprovedDialogRendersWithThreePalettesAndFourLanguagesAndKeepsIndependentSettings()
    {
        Exception? failure = null; int renders = 0; string? screenshot = null;
        var thread = new Thread(() =>
        {
            Application? app = null; var languageProperty = typeof(Lang).GetProperty(nameof(Lang.Current))!; AppLang previous = Lang.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var source = new MidiFixture(2).Note(60, 0, 960).Note(64, 240, 1200, channel: 2, track: 1).Read();
                // 使用主题的颜色资源契约，不调用会写入用户偏好的主题切换入口。
                foreach (var palette in new[] { (name: "Dark", bg: "#121214", box: "#262626", fg: "#e0e0e0", sub: "#9a9ab0"),
                    (name: "Light", bg: "#ffffff", box: "#eef0f2", fg: "#222222", sub: "#6a6a72"),
                    (name: "SunsetPink", bg: "#FFF3FC", box: "#FFFFFF", fg: "#2E2530", sub: "#756977") })
                foreach (AppLang language in Enum.GetValues<AppLang>())
                {
                    languageProperty.SetValue(null, language);
                    SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
                    foreach (string key in new[] { "WindowBg", "PanelBg" }) app.Resources[key] = Brush(palette.bg);
                    foreach (string key in new[] { "BoxBg", "ComboBg", "NeutralBtnBg" }) app.Resources[key] = Brush(palette.box);
                    foreach (string key in new[] { "TextFg", "NeutralBtnFg" }) app.Resources[key] = Brush(palette.fg);
                    app.Resources["SubTextFg"] = Brush(palette.sub);
                    foreach (string key in new[] { "WindowBorder", "BoxBorder", "BtnBorder" }) app.Resources[key] = Brush(palette.sub);
                    app.Resources["TitleGrad"] = Brush("#35305a"); app.Resources["CaptionFg"] = Brushes.White;
                    var dialog = new MidiImportDialog(source, "Synthetic MIDI");
                    try
                    {
                        void Render(bool advanced)
                        {
                            var root = (FrameworkElement)dialog.Content; double width = advanced ? 1060 : 440;
                            root.Measure(new Size(width, 1100)); root.Arrange(new Rect(0, 0, width, Math.Min(1050, root.DesiredSize.Height))); root.UpdateLayout();
                            Assert.True(root.ActualWidth > 300); Assert.True(root.ActualHeight > 300);
                            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); renders++;
                            if (language == AppLang.ZhCN && palette.name == "Dark" && advanced)
                            {
                                string directory = Path.Combine(AppContext.BaseDirectory, "UiChecks"); Directory.CreateDirectory(directory);
                                screenshot = Path.Combine(directory, "midi-dialog-dark-zh-advanced.png");
                                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(screenshot); encoder.Save(file);
                            }
                        }
                        Click(dialog, "recalculate"); PumpUntil(() => Field<MidiConversion?>(dialog, "_result") != null && !Field<bool>(dialog, "_busy")); Render(false);
                        Click(dialog, "advanced"); Assert.NotNull(Field<DataGrid>(dialog, "_mapping"));
                        var select = Descendants(dialog).OfType<Button>().Where(b => Equals(b.Content, Lang.S("midi3.mapping"))).ToArray();
                        select[1].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Field<ComboBox>(dialog, "_instrument").SelectedValue = "Harp";
                        Field<CheckBox>(dialog, "_auto").IsChecked = false;
                        Click(dialog, "recalculate"); PumpUntil(() => Field<MidiConversion?>(dialog, "_result") != null && !Field<bool>(dialog, "_busy"));
                        var options = Field<List<SapmVoiceOptions>>(dialog, "_options"); Assert.Equal("Piano", options[0].Profile.Instrument);
                        Assert.Equal("Harp", options[1].Profile.Instrument); Assert.False(options[1].AutoTranspose);
                        var grid = Field<DataGrid>(dialog, "_mapping"); Assert.NotEmpty(grid.Items.Cast<object>());
                        var row = grid.Items[0]; row.GetType().GetProperty("Key")!.SetValue(row, 2);
                        Click(dialog, "recalculate"); PumpUntil(() => Field<MidiConversion?>(dialog, "_result") != null && !Field<bool>(dialog, "_busy"));
                        Assert.Empty(options[0].ManualKeys); Assert.Single(options[1].ManualKeys); Render(true);
                        Click(dialog, "back"); Click(dialog, "recalculate"); PumpUntil(() => Field<MidiConversion?>(dialog, "_result") != null && !Field<bool>(dialog, "_busy"));
                        Assert.Equal("Harp", options[1].Profile.Instrument); Assert.False(options[1].AutoTranspose); Assert.Single(options[1].ManualKeys);
                    }
                    finally { dialog.Close(); }
                }
                var settings = MidiFixture.Options(source); settings[0] = settings[0] with { Profile = settings[0].Profile with { Version = 7, Verification = PitchVerification.UserProvided } };
                settings[0].ManualKeys[source.Notes[0].Id] = 3;
                var saved = MidiProjectConverter.Create(SapmMapper.Convert(source, settings), "Saved project"); var reopened = new MidiImportDialog(saved);
                try
                {
                    reopened.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    PumpUntil(() => Field<MidiConversion?>(reopened, "_result") != null && !Field<bool>(reopened, "_busy"));
                    var restored = Field<List<SapmVoiceOptions>>(reopened, "_options"); Assert.Equal(7, restored[0].Profile.Version);
                    Assert.Single(restored[0].ManualKeys); Assert.NotSame(saved.MidiConversion!.Voices[0].ManualKeys, restored[0].ManualKeys);
                    Assert.Equal(Lang.S("midi3.remapTitle"), reopened.Title);
                }
                finally { reopened.Close(); }
            }
            catch (Exception error) { failure = error; }
            finally { languageProperty.SetValue(null, previous); app?.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "界面渲染线程未退出。");
        Assert.Null(failure); Assert.Equal(24, renders); output.WriteLine($"UI_RENDER palettes=3; languages=4; modes=2; renders={renders}; settings_preserved=True; screenshot={screenshot}");
    }
}
