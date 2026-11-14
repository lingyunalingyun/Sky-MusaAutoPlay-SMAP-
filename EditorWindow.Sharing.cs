namespace SMAP_WPF;

public partial class EditorWindow
{
    async Task OpenNativeShare()
    {
        PauseForEdit();
        try
        {
            NativeCloudLink? link = _path != null
                ? await Task.Run(() => new NativeSharingStore(NativeSharingStore.DefaultDirectory).Find(_path, CloudTransport.ApiOrigin)) : null;
            var window = new NativeSharingWindow(DialogOwner, _session.Project, _session.ActiveTrackId, initialVersion: link?.VersionId);
            window.ShowDialog();
            if (window.ImportedFile is { } file) Saved?.Invoke(file);
        }
        catch (Exception error) { StatusText.Text = NativeSharingWindow.ErrorText(error); }
        finally { Roll.Focus(); }
    }
}
