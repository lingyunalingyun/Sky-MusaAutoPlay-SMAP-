using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMAP_WPF.Updates;

namespace SMAP_WPF;

public sealed class UpdateSettingsPane : G2Border
{
    public event Action? ActionRequested;
    public event Action? RestartRequested;
    readonly TextBlock _title = Label(19), _state = Label(16), _hint = Label(12), _stats = Label(12), _downloadLabel = Label(12), _localLabel = Label(12);
    readonly G2Border _downloadFill = new() { HorizontalAlignment = HorizontalAlignment.Left }, _localFill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Grid _download = new() { Height = 7 }, _local = new() { Height = 5 };
    readonly Button _action = new() { MinWidth = 112, Height = 36, Padding = new Thickness(14, 0, 14, 0), Template = ChromeWindow.BtnTpl("0") };
    readonly SpeedGraph _graph = new() { Height = 44, Margin = new Thickness(0, 8, 0, 8) };
    readonly StackPanel _details = new();
    DeltaUpdateState _currentState;
    double _downloadRatio, _localRatio;
    static TextBlock Label(int size) => new() { FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    public UpdateSettingsPane()
    {
        CornerRadius = new(12); Padding = new(16); SetResourceReference(BackgroundProperty, "PanelBg");
        var content = new StackPanel(); Child = content;
        content.Children.Add(_details);
        foreach (var item in new[] { _title, _state }) { item.SetResourceReference(TextBlock.ForegroundProperty, "TextFg"); _details.Children.Add(item); }
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); _hint.Text = Lang.Update("continueUse"); _details.Children.Add(_hint);
        void Bar(Grid host, G2Border fill)
        {
            var background = new G2Border { CornerRadius = new(3) }; background.SetResourceReference(BackgroundProperty, "BoxBg"); host.Children.Add(background);
            fill.CornerRadius = new(3); fill.SetResourceReference(BackgroundProperty, "Accent"); host.Children.Add(fill);
        }
        Bar(_download, _downloadFill); Bar(_local, _localFill);
        _downloadLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); _details.Children.Add(_downloadLabel); _details.Children.Add(_download); _details.Children.Add(_graph);
        _stats.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); _details.Children.Add(_stats);
        _localLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); _details.Children.Add(_localLabel); _details.Children.Add(_local);
        var footer = Label(12); footer.SetResourceReference(TextBlock.ForegroundProperty, "SubTextFg"); footer.Margin = new(0, 12, 0, 12); footer.Text = Lang.Update("preserved"); _details.Children.Add(footer);
        _footer = footer;
        _action.HorizontalAlignment = HorizontalAlignment.Stretch; _action.SetResourceReference(Control.BackgroundProperty, "NeutralBtnBg"); _action.SetResourceReference(Control.ForegroundProperty, "NeutralBtnFg");
        _action.Click += (_, _) => { if (_currentState == DeltaUpdateState.Ready) RestartRequested?.Invoke(); else ActionRequested?.Invoke(); }; content.Children.Add(_action);
        SizeChanged += (_, _) => Fill(); Refresh(null);
    }
    readonly TextBlock _footer;
    static string Bytes(long value) => value >= 1024 * 1024 ? $"{value / (1024d * 1024):0.0} MB" : $"{value / 1024d:0.0} KB";
    public void Refresh(DeltaUpdateService? service)
    {
        _currentState = service?.State ?? DeltaUpdateState.Idle; _title.Text = Lang.Update("title");
        bool compact = _currentState is DeltaUpdateState.Idle or DeltaUpdateState.Current;
        _details.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Padding = compact ? new Thickness(0) : new Thickness(16);
        if (compact) Background = Brushes.Transparent; else SetResourceReference(BackgroundProperty, "PanelBg");
        string key = _currentState switch { DeltaUpdateState.Checking => "checking", DeltaUpdateState.Planning => "planning", DeltaUpdateState.Downloading => "downloading", DeltaUpdateState.Paused => "paused", DeltaUpdateState.Reconstructing => "reconstructing", DeltaUpdateState.Ready => "ready", DeltaUpdateState.Current => "current", DeltaUpdateState.Error => "error", _ => "idle" };
        _state.Text = Lang.Update(key) + (service?.Version is { } version ? " · " + version : "");
        _hint.Text = Lang.Update("continueUse"); _footer.Text = Lang.Update("preserved");
        _action.Content = Lang.Update(_currentState switch { DeltaUpdateState.Downloading or DeltaUpdateState.Reconstructing => "pause", DeltaUpdateState.Paused => "resume", DeltaUpdateState.Error => "retry", DeltaUpdateState.Ready => "restart", _ => "check" });
        _action.IsEnabled = _currentState is not (DeltaUpdateState.Checking or DeltaUpdateState.Planning);
        var p = service?.Progress; var plan = service?.Plan;
        _downloadRatio = _currentState == DeltaUpdateState.Ready || p?.Stage == "reconstruct" ? 1 : p?.Stage == "download" && p.Total > 0 ? (double)p.Completed / p.Total : 0;
        _localRatio = _currentState == DeltaUpdateState.Ready ? 1 : p?.Stage == "reconstruct" && p.Total > 0 ? (double)p.Completed / p.Total : 0;
        _downloadLabel.Text = Lang.Update("download") + (plan != null ? "  " + Bytes(_currentState == DeltaUpdateState.Ready || p?.Stage == "reconstruct" ? plan.DownloadBytes : p?.Stage == "download" ? p.Completed : 0) + " / " + Bytes(plan.DownloadBytes) : "");
        _localLabel.Text = Lang.Update("local");
        _stats.Text = (_currentState == DeltaUpdateState.Downloading && service?.BytesPerSecond is > 0 ? Bytes((long)service.BytesPerSecond) + "/s · " : "") + Lang.Update("reuse") + "  " + Bytes(plan?.ReusedBytes ?? 0);
        if (_currentState == DeltaUpdateState.Downloading) _graph.Add(service?.BytesPerSecond ?? 0); _graph.InvalidateVisual(); Fill();
    }
    void Fill() { _downloadFill.Width = Math.Max(0, _download.ActualWidth * Math.Clamp(_downloadRatio, 0, 1)); _localFill.Width = Math.Max(0, _local.ActualWidth * Math.Clamp(_localRatio, 0, 1)); }

    sealed class SpeedGraph : FrameworkElement
    {
        readonly Queue<double> _values = new();
        public void Add(double value) { _values.Enqueue(value); while (_values.Count > 36) _values.Dequeue(); InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            var values = _values.ToArray(); double max = Math.Max(1, values.DefaultIfEmpty().Max()), step = ActualWidth / 36;
            var brush = (Brush)FindResource("Accent");
            for (int i = 0; i < values.Length; i++)
            { double height = ActualHeight * values[i] / max; if (height > 0) G2Geometry.Draw(dc, brush, null, new Rect(i * step, ActualHeight - height, Math.Max(0, step - 3), height), 2, 2); }
        }
    }
}
