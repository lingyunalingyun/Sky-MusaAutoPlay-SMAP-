using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SMAP_WPF;

public static class AvatarUtil
{
    public static async Task<BitmapImage?> LoadAsync()
    {
        var bytes = await CloudApi.DownloadAvatarAsync();
        if (bytes is not { Length: > 0 }) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
