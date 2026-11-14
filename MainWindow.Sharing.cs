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
            var presentation = link?.Presentation ?? await Task.Run(() => new NativeSharingStore(NativeSharingStore.DefaultDirectory).PresentationFor(song.File, CloudTransport.ApiOrigin));
            var window = new NativeSharingWindow(this, project, initialVersion: link?.VersionId, presentation: presentation); window.ShowDialog();
            if (window.Imported) RefreshLibrary(); UpdateLoginButton();
        }
        catch (Exception error) { ShowToast(NativeSharingWindow.ErrorText(error)); }
    }
    void AddNativeSharingMenu(ContextMenu menu, SongInfo song)
    {
        var share = new MenuItem { Header = Lang.Sharing("publish") }; share.Click += async (_, _) => await ShareNativeSong(song); menu.Items.Add(share);
    }
    void CloudMetadataMenu(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(CloudList, e.OriginalSource as DependencyObject) as ListViewItem;
        if (item?.DataContext is not CloudSheet sheet || !CloudApi.LoggedIn) return;
        var menu = new ContextMenu(); var edit = new MenuItem { Header = Lang.Sharing("metadataEdit") }; menu.Items.Add(edit);
        edit.Click += async (_, _) =>
        {
            var window = new NativeSharingWindow(this, metadataTarget: sheet.MetadataTarget);
            window.ShowDialog(); if (window.MetadataSaved) await LoadCloud(false); UpdateLoginButton();
        };
        OpenRowMenu(menu, item); e.Handled = true;
    }
}
