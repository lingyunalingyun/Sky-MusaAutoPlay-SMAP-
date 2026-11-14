using System.IO;
using System.Text;

namespace SMAP_WPF.Updates;

public static class UpdateChannelSettings
{
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "update-channel.txt");
    public static string DefaultFor(string version) => version.Contains("-alpha.", StringComparison.Ordinal) ? "alpha" : "stable";
    public static void Validate(string channel)
    {
        if (channel is not ("stable" or "alpha")) throw new ArgumentException("Unknown update channel.", nameof(channel));
    }
    public static string Load(string path, string fallback)
    {
        Validate(fallback);
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 32) return fallback;
            string value = File.ReadAllText(path).Trim();
            return value is "stable" or "alpha" ? value : fallback;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return fallback; }
    }
    public static void Save(string path, string channel)
    {
        Validate(channel);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(Encoding.UTF8.GetBytes(channel)); file.Flush(true); }
            File.Move(pending, path, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
