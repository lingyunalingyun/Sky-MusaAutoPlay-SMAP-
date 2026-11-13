using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class MainWindow
{
    EditorWindow? _workspaceEditor;
    (FrameworkElement element, Visibility visibility)[] _workspaceHidden = Array.Empty<(FrameworkElement, Visibility)>();
    (FrameworkElement element, Visibility visibility)[] _workspaceIncoming = Array.Empty<(FrameworkElement, Visibility)>();
    int _workspaceTransitionGen;
    bool _workspaceTransitioning;
    DispatcherTimer? _workspaceFinishTimer;

    void EnterEditorWorkspace(EditorWindow editor)
    {
        var outgoing = PrepareWorkspaceTransition(editor.WorkspaceMotionAllowed);
        CloseLibraryChoice(true);
        _rowMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        _workspaceHidden = WorkspaceScene.Children.OfType<FrameworkElement>()
            .Where(e => Grid.GetRow(e) > 0 && !ReferenceEquals(e, EditorWorkspaceHost) && !ReferenceEquals(e, WorkspaceTransitionLayer))
            .Select(e => (e, e.Visibility)).ToArray();
        foreach (var (element, _) in _workspaceHidden) element.Visibility = Visibility.Collapsed;
        _workspaceEditor = editor;
        EditorBackButton.Content = Lang.S("practice.back");
        EditorCaptionText.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = editor });
        EditorCaption.Visibility = EditorWorkspaceHost.Visibility = Visibility.Visible;
        EditorWorkspaceHost.Content = editor.Embed(this);
        RefreshShortcutFocus();
        AnimateWorkspaceTransition(outgoing, new FrameworkElement[] { EditorWorkspaceHost });
        Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_workspaceEditor, editor)) editor.Viewport.Focus(); });
    }
    void LeaveEditorWorkspace(EditorWindow editor)
    {
        if (!ReferenceEquals(_workspaceEditor, editor)) return;
        var outgoing = PrepareWorkspaceTransition(editor.WorkspaceMotionAllowed);
        EditorWorkspaceHost.Content = null;
        EditorWorkspaceHost.Visibility = Visibility.Collapsed;
        HomeCaption.Visibility = Visibility.Visible;
        foreach (var (element, visibility) in _workspaceHidden) element.Visibility = visibility;
        var incoming = _workspaceHidden.Where(e => e.visibility == Visibility.Visible).Select(e => e.element).ToArray();
        _workspaceHidden = Array.Empty<(FrameworkElement, Visibility)>();
        _workspaceEditor = null;
        RefreshShortcutFocus();
        AnimateWorkspaceTransition(outgoing, incoming);
    }
    void EditorBack_Click(object sender, RoutedEventArgs e) => _workspaceEditor?.Close();

    BitmapSource? CaptureWorkspacePage()
    {
        WorkspaceScene.UpdateLayout();
        double top = 12, width = WorkspaceScene.ActualWidth, height = WorkspaceScene.ActualHeight;
        if (!IsVisible || width < 1 || height <= top) return null;
        var dpi = VisualTreeHelper.GetDpi(WorkspaceScene);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(WorkspaceScene) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        int firstRow = (int)Math.Round(top * dpi.DpiScaleY);
        var page = new CroppedBitmap(bitmap, new Int32Rect(0, firstRow, bitmap.PixelWidth, bitmap.PixelHeight - firstRow));
        page.Freeze(); return page;
    }
    BitmapSource? PrepareWorkspaceTransition(bool motion)
    {
        var outgoing = motion ? _workspaceTransitioning && WorkspaceTransitionLayer.IsVisible ? CaptureWorkspaceTransitionFrame() : CaptureWorkspacePage() : null;
        ++_workspaceTransitionGen; _workspaceFinishTimer?.Stop();
        RestoreWorkspacePage();
        return outgoing;
    }
    BitmapSource CaptureWorkspaceTransitionFrame()
    {
        var size = WorkspaceTransitionLayer.RenderSize;
        var dpi = VisualTreeHelper.GetDpi(WorkspaceTransitionLayer);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bounds = new Rect(size);
            dc.DrawRectangle((Brush)FindResource("WindowBg"), null, bounds);
            foreach (var image in new[] { WorkspaceOutgoing, WorkspaceIncoming })
            {
                dc.PushOpacity(image.Opacity); dc.DrawImage(image.Source, bounds); dc.Pop();
            }
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    void RestoreWorkspacePage()
    {
        foreach (var (element, visibility) in _workspaceIncoming) element.Visibility = visibility;
        _workspaceIncoming = Array.Empty<(FrameworkElement, Visibility)>();
        WorkspaceOutgoing.BeginAnimation(OpacityProperty, null); WorkspaceIncoming.BeginAnimation(OpacityProperty, null);
        WorkspaceOutgoing.Source = WorkspaceIncoming.Source = null;
        WorkspaceTransitionLayer.Visibility = Visibility.Collapsed;
    }
    void AnimateWorkspaceTransition(BitmapSource? outgoing, FrameworkElement[] incoming)
    {
        int generation = _workspaceTransitionGen;
        bool animate = outgoing != null;
        _workspaceTransitioning = animate;
        if (!animate) { FinishWorkspaceTransition(generation); return; }
        WindowRoot.Effect = null;
        WorkspaceOutgoing.Source = outgoing; WorkspaceOutgoing.Opacity = 1; WorkspaceIncoming.Opacity = 0;
        WorkspaceTransitionLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _workspaceTransitionGen || !_workspaceTransitioning) return;
            WorkspaceIncoming.Source = CaptureWorkspacePage();
            _workspaceIncoming = incoming.Where(e => e.Visibility == Visibility.Visible).Select(e => (e, e.Visibility)).ToArray();
            foreach (var (element, _) in _workspaceIncoming) element.Visibility = Visibility.Hidden;
            WorkspaceTransitionLayer.Visibility = Visibility.Visible;
            AnimateWorkspaceCaption(true);
            WorkspaceOutgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            WorkspaceIncoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)) { BeginTime = TimeSpan.FromMilliseconds(110), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _workspaceFinishTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(360) };
            _workspaceFinishTimer.Tick += (_, _) => FinishWorkspaceTransition(generation);
            _workspaceFinishTimer.Start();
        }, DispatcherPriority.ContextIdle);
    }
    void AnimateWorkspaceCaption(bool animate)
    {
        bool on = _workspaceEditor != null;
        EditorBackButton.ApplyTemplate();
        var shape = (G2Rectangle)EditorBackButton.Template.FindName("MorphShape", EditorBackButton);
        var label = (TextBlock)EditorBackButton.Template.FindName("BackLabel", EditorBackButton);
        var text = new TextBlock { Text = Lang.S("practice.back"), FontFamily = EditorBackButton.FontFamily, FontSize = EditorBackButton.FontSize };
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double width = on ? Math.Ceiling(text.DesiredSize.Width) + 22 : 14;
        var duration = TimeSpan.FromMilliseconds(280);
        void Number(UIElement element, DependencyProperty property, double target)
        {
            double from = (double)element.GetValue(property);
            element.BeginAnimation(property, null); element.SetValue(property, target);
            if (animate) element.BeginAnimation(property, new DoubleAnimation(from, target, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        }
        HomeCaption.Visibility = EditorCaption.Visibility = Visibility.Visible;
        EditorBackButton.IsHitTestVisible = EditorBackButton.Focusable = on;
        Number(EditorBackButton, WidthProperty, width); Number(EditorBackButton, HeightProperty, on ? 28 : 14);
        Number(EditorBackButton, Canvas.LeftProperty, on ? 6 : 14); Number(EditorBackButton, Canvas.TopProperty, on ? 6 : 13);
        Number(shape, G2Rectangle.RadiusXProperty, on ? 6 : 7); Number(shape, G2Rectangle.RadiusYProperty, on ? 6 : 7);
        Number(shape, Shape.StrokeThicknessProperty, on ? 1 : 0);
        Number(label, OpacityProperty, on ? 1 : 0); Number(EditorCaption, OpacityProperty, on ? 1 : 0); Number(HomeCaption, OpacityProperty, on ? 0 : 1);
        var fromMargin = CaptionTitles.Margin; var targetMargin = new Thickness(on ? width + 18 : 36, 0, 140, 0);
        CaptionTitles.BeginAnimation(MarginProperty, null); CaptionTitles.Margin = targetMargin;
        if (animate) CaptionTitles.BeginAnimation(MarginProperty, new ThicknessAnimation(fromMargin, targetMargin, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
        var fill = ((SolidColorBrush)EditorBackButton.Background).CloneCurrentValue();
        var fromColor = fill.Color; var targetColor = on ? ((SolidColorBrush)FindResource("NeutralBtnBg")).Color : Color.FromRgb(232, 232, 245);
        EditorBackButton.Background = fill; fill.Color = targetColor;
        if (animate) fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(fromColor, targetColor, duration));
    }
    void FinishWorkspaceTransition(int generation)
    {
        if (generation != _workspaceTransitionGen) return;
        _workspaceFinishTimer?.Stop(); _workspaceFinishTimer = null; _workspaceTransitioning = false;
        RestoreWorkspacePage();
        AnimateWorkspaceCaption(false);
        HomeCaption.Visibility = _workspaceEditor == null ? Visibility.Visible : Visibility.Collapsed;
        EditorCaption.Visibility = _workspaceEditor == null ? Visibility.Collapsed : Visibility.Visible;
        if (_workspaceEditor == null) BindingOperations.ClearBinding(EditorCaptionText, TextBlock.TextProperty);
        else _workspaceEditor.Viewport.Focus();
        if (!_transitioning) WindowRoot.Effect = _rootShadow;
    }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_workspaceTransitioning) FinishWorkspaceTransition(_workspaceTransitionGen);
    }
}
