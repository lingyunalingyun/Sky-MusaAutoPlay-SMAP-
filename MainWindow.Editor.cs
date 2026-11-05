using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SMAP_WPF;

public partial class MainWindow
{
    EditorWindow? _workspaceEditor;
    (FrameworkElement element, Visibility visibility)[] _workspaceHidden = Array.Empty<(FrameworkElement, Visibility)>();

    void EnterEditorWorkspace(EditorWindow editor)
    {
        CloseLibraryChoice(true);
        _rowMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false);
        _workspaceHidden = RootScale.Children.OfType<FrameworkElement>()
            .Where(e => Grid.GetRow(e) > 0 && !ReferenceEquals(e, EditorWorkspaceHost))
            .Select(e => (e, e.Visibility)).ToArray();
        foreach (var (element, _) in _workspaceHidden) element.Visibility = Visibility.Collapsed;
        _workspaceEditor = editor;
        EditorBackButton.Content = Lang.S("practice.back");
        EditorCaptionText.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = editor });
        HomeCaption.Visibility = Visibility.Collapsed;
        EditorCaption.Visibility = EditorWorkspaceHost.Visibility = Visibility.Visible;
        EditorWorkspaceHost.Content = editor.Embed(this);
        RefreshShortcutFocus();
        Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_workspaceEditor, editor)) editor.Viewport.Focus(); });
    }
    void LeaveEditorWorkspace(EditorWindow editor)
    {
        if (!ReferenceEquals(_workspaceEditor, editor)) return;
        EditorWorkspaceHost.Content = null;
        EditorWorkspaceHost.Visibility = EditorCaption.Visibility = Visibility.Collapsed;
        BindingOperations.ClearBinding(EditorCaptionText, TextBlock.TextProperty);
        HomeCaption.Visibility = Visibility.Visible;
        foreach (var (element, visibility) in _workspaceHidden) element.Visibility = visibility;
        _workspaceHidden = Array.Empty<(FrameworkElement, Visibility)>();
        _workspaceEditor = null;
        RefreshShortcutFocus();
    }
    void EditorBack_Click(object sender, RoutedEventArgs e) => _workspaceEditor?.Close();
}
