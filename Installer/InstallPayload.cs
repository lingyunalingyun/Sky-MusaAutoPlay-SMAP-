using System.IO;
using System.IO.Compression;

namespace SMAP_Installer;

internal static class InstallPayload
{
    const long MaxBytes = 1024L * 1024 * 1024;
    public static void Extract(Stream payload, string target, Action<int, string>? progress = null)
    {
        string root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
        if (root.Length == 0 || string.Equals(root, Path.GetPathRoot(Path.GetFullPath(target))?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择独立的安装目录。");
        using var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
        var entries = new List<(ZipArchiveEntry entry, string relative, string destination, bool song)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        foreach (var entry in zip.Entries)
        {
            string relative = entry.FullName.Replace('\\', '/');
            string[] parts = relative.Split('/');
            if (relative.StartsWith('/') || parts.Any(p => p is "." or ".." || p.Contains(':')) || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000)
                throw new InvalidDataException("安装包包含不安全路径。");
            if (relative.EndsWith('/')) continue;
            if (parts.Any(string.IsNullOrEmpty) || !names.Add(relative)) throw new InvalidDataException("安装包包含重复或无效路径。");
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包路径越界。");
            bool song = parts[0].Equals("songs", StringComparison.OrdinalIgnoreCase);
            bool resource = parts[0] is "Assets" or "runtimes";
            bool application = parts.Length == 1 && (Path.GetExtension(relative).ToLowerInvariant() is ".exe" or ".dll" or ".pdb"
                || relative is "SMAP.deps.json" or "SMAP.runtimeconfig.json" or "LICENSE.txt" or "ThirdPartyNotices.txt");
            if (!song && !resource && !application) throw new InvalidDataException("安装包不能修改用户设置或私人数据。");
            bytes += entry.Length;
            if (bytes > MaxBytes || entries.Count >= 20000) throw new InvalidDataException("安装包超过大小限制。");
            CheckLinks(root, destination); entries.Add((entry, relative, destination, song));
        }
        if (!names.Contains("SMAP.exe")) throw new InvalidDataException("安装包缺少 SMAP.exe。");
        string parent = Path.GetDirectoryName(root)!; Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".smap-install-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var replaced = new List<(string destination, string? backup)>(); bool retainRecovery = false;
        try
        {
            foreach (var item in entries)
            {
                string temp = Path.Combine(staging, "new", item.relative); Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                item.entry.ExtractToFile(temp, overwrite: false);
            }
            Directory.CreateDirectory(root); int done = 0;
            foreach (var item in entries)
            {
                CheckLinks(root, item.destination);
                if (item.song && File.Exists(item.destination)) { progress?.Invoke(++done * 100 / entries.Count, "保留: " + item.relative); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(item.destination)!);
                string? backup = null;
                if (!item.song && File.Exists(item.destination))
                {
                    backup = Path.Combine(staging, "old", item.relative); Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(item.destination, backup, overwrite: false);
                }
                File.Move(Path.Combine(staging, "new", item.relative), item.destination, overwrite: !item.song);
                // New sample songs are persistent data; a code rollback must leave them intact.
                if (!item.song) replaced.Add((item.destination, backup));
                progress?.Invoke(++done * 100 / entries.Count, "解压: " + item.relative);
            }
        }
        catch (Exception original)
        {
            var errors = new List<Exception>();
            foreach (var item in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.backup != null) File.Copy(item.backup, item.destination, overwrite: true);
                    else File.Delete(item.destination);
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            if (errors.Count > 0) { retainRecovery = true; throw new AggregateException("自动恢复未完成，旧程序备份保留于: " + staging, new[] { original }.Concat(errors)); }
            throw;
        }
        finally
        {
            if (!retainRecovery)
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }
    static void CheckLinks(string root, string destination)
    {
        for (string? path = destination; path != null && (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)); path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("安装目录中不能包含重解析链接。");
    }
}
