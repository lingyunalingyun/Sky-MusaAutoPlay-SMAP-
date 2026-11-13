using System.Windows;
using System.Windows.Controls;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class MainWindow
{
    async Task ShareNativeSong(SongInfo song)
    {
        try
        {
            var project = await Task.Run(() => IsNative(song) ? SmapFile.Load(song.File) : LegacyProjectAdapter.Load(song.File, 0));
            var link = await Task.Run(() => new NativeSharingStore(NativeSharingStore.DefaultDirectory).Find(song.File, CloudTransport.ApiOrigin));
            var window = new NativeSharingWindow(this, project, initialVersion: link?.VersionId); window.ShowDialog();
            if (window.Imported) RefreshLibrary(); UpdateLoginButton();
        }
        catch (Exception error) { ShowToast(NativeSharingWindow.ErrorText(error)); }
    }
    void AddNativeSharingMenu(ContextMenu menu, SongInfo song)
    {
        var share = new MenuItem { Header = Lang.Sharing("publish") }; share.Click += async (_, _) => await ShareNativeSong(song); menu.Items.Add(share);
    }
}
