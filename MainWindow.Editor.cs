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
        _workspaceHidden = RootScale.Children.OfType<FrameworkElement>()
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
        RootScale.UpdateLayout();
        double top = 52, width = RootScale.ActualWidth, height = RootScale.ActualHeight;
        if (!IsVisible || width < 1 || height <= top) return null;
        var dpi = VisualTreeHelper.GetDpi(RootScale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(RootScale) { Stretch = Stretch.Fill }, null, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        int firstRow = (int)Math.Round(top * dpi.DpiScaleY);
        var page = new CroppedBitmap(bitmap, new Int32Rect(0, firstRow, bitmap.PixelWidth, bitmap.PixelHeight - firstRow));
        page.Freeze(); return page;
    }
    BitmapSource? PrepareWorkspaceTransition(bool motion)
    {
        var outgoing = motion ? CaptureWorkspacePage() : null;
        ++_workspaceTransitionGen; _workspaceFinishTimer?.Stop();
        RestoreWorkspacePage();
        return outgoing;
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
        AnimateWorkspaceCaption(animate);
        if (!animate) { FinishWorkspaceTransition(generation); return; }
        WindowRoot.Effect = null;
        WorkspaceOutgoing.Source = outgoing; WorkspaceOutgoing.Opacity = 1; WorkspaceIncoming.Opacity = 0;
        WorkspaceTransitionLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _workspaceTransitionGen || !_workspaceTransitioning) return;
            WorkspaceTransitionLayer.Visibility = Visibility.Collapsed;
            WorkspaceIncoming.Source = CaptureWorkspacePage();
            _workspaceIncoming = incoming.Where(e => e.Visibility == Visibility.Visible).Select(e => (e, e.Visibility)).ToArray();
            foreach (var (element, _) in _workspaceIncoming) element.Visibility = Visibility.Hidden;
            WorkspaceTransitionLayer.Visibility = Visibility.Visible;
            var duration = TimeSpan.FromMilliseconds(220);
            WorkspaceOutgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            WorkspaceIncoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }, DispatcherPriority.ContextIdle);
        _workspaceFinishTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(360) };
        _workspaceFinishTimer.Tick += (_, _) => FinishWorkspaceTransition(generation);
        _workspaceFinishTimer.Start();
    }
    void AnimateWorkspaceCaption(bool animate)
    {
        bool on = _workspaceEditor != null;
        EditorBackButton.ApplyTemplate();
        var shape = (Rectangle)EditorBackButton.Template.FindName("MorphShape", EditorBackButton);
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
        Number(shape, Rectangle.RadiusXProperty, on ? 6 : 7); Number(shape, Rectangle.RadiusYProperty, on ? 6 : 7);
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
        EditorBackButton.ApplyTemplate();
        _workspaceFinishTimer?.Stop(); _workspaceFinishTimer = null; _workspaceTransitioning = false;
        RestoreWorkspacePage();
        EditorBackButton.BeginAnimation(WidthProperty, null); EditorBackButton.BeginAnimation(HeightProperty, null);
        EditorBackButton.BeginAnimation(Canvas.LeftProperty, null); EditorBackButton.BeginAnimation(Canvas.TopProperty, null);
        CaptionTitles.BeginAnimation(MarginProperty, null);
        HomeCaption.BeginAnimation(OpacityProperty, null); EditorCaption.BeginAnimation(OpacityProperty, null);
        if (EditorBackButton.Template.FindName("MorphShape", EditorBackButton) is Rectangle shape)
        { shape.BeginAnimation(Rectangle.RadiusXProperty, null); shape.BeginAnimation(Rectangle.RadiusYProperty, null); shape.BeginAnimation(Shape.StrokeThicknessProperty, null); }
        if (EditorBackButton.Template.FindName("BackLabel", EditorBackButton) is TextBlock label) label.BeginAnimation(OpacityProperty, null);
        if (EditorBackButton.Background is SolidColorBrush fill) fill.BeginAnimation(SolidColorBrush.ColorProperty, null);
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
