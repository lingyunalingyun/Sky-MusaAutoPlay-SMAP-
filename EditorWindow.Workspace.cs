using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SMAP_WPF;

public partial class EditorWindow
{
    Window? _workspaceOwner;
    FrameworkElement? _workspaceRoot;
    Window DialogOwner => _workspaceOwner ?? this;

    // Reuse the editor surface and session without showing or creating a second native window.
    internal FrameworkElement Embed(Window owner)
    {
        _workspaceOwner = owner;
        var root = (FrameworkElement)Content;
        Content = null;
        _workspaceRoot = root;
        root.Resources = Resources;
        TextElement.SetFontFamily(root, FontFamily);
        WindowSurface.Margin = WindowSurface.Padding = WindowSurface.BorderThickness = new Thickness(0);
        TitleSurface.Visibility = Visibility.Collapsed;
        EditorLayout.RowDefinitions[0].Height = new GridLength(0);
        ToolbarSurface.Margin = new Thickness(0);
        root.Loaded += WorkspaceLoaded;
        root.SizeChanged += WorkspaceSizeChanged;
        owner.Deactivated += WorkspaceDeactivated;
        return root;
    }
    void WorkspaceLoaded(object sender, RoutedEventArgs e)
    {
        RefreshLayoutMode(); Roll.Focus(); _ = PrepareKeyOutput();
    }
    void WorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _commandMenu?.Close(true);
        if (_editorMenu != null) _editorMenu.IsOpen = false;
        RefreshLayoutMode();
    }
    void WorkspaceDeactivated(object? sender, EventArgs e)
    {
        StopKeyPreview();
        if (!_closed) RefreshEditorKeys();
    }
    internal void WorkspaceKeyDown(KeyEventArgs e) => OnPreviewKeyDown(e);
    internal void WorkspaceKeyUp(KeyEventArgs e) => OnPreviewKeyUp(e);
    void ReleaseWorkspace()
    {
        if (_workspaceOwner != null) _workspaceOwner.Deactivated -= WorkspaceDeactivated;
        if (_workspaceRoot != null)
        {
            _workspaceRoot.Loaded -= WorkspaceLoaded;
            _workspaceRoot.SizeChanged -= WorkspaceSizeChanged;
        }
        _workspaceOwner = null; _workspaceRoot = null;
    }
}
