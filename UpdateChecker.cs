using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SMAP_WPF;

/// <summary>启动时查 GitHub Releases, 有更高版本则返回信息。</summary>
public static class UpdateChecker
{
    public static readonly string AppVersion = typeof(UpdateChecker).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
    const string Repo = Updates.GitHubUpdates.Repository;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly HttpClient DownloadHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };

    public readonly record struct Release(string Tag, string Name, string Url, string AssetUrl, long AssetSize);

    /// <summary>有新版返回 Release; 无新版/网络失败返回 null。</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            // 取全部 releases 找版本号最高的(GitHub 的 /latest 是"最近发布"而非最高版本, 会漏)
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=50");
            req.Headers.Add("Accept", "application/vnd.github+json");
            req.Headers.Add("User-Agent", "SMAP-Updater");   // GitHub API 必须带 UA, 否则 403
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string bestTag = "", bestName = "", bestUrl = "", bestAssetUrl = "";
            long bestAssetSize = 0;
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True) continue;
                if (!AppVersion.Contains("-alpha.", StringComparison.Ordinal) && rel.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) continue;
                var tag = (rel.TryGetProperty("tag_name", out var t) ? t.GetString() : "")?.TrimStart('v', 'V') ?? "";
                if (tag.Length == 0) continue;
                if (bestTag.Length == 0 || Compare(tag, bestTag) > 0)
                {
                    string assetUrl = "";
                    long assetSize = 0;
                    if (rel.TryGetProperty("assets", out var assets))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                            if (!assetName.Equals("SMAP-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
                            assetUrl = asset.TryGetProperty("browser_download_url", out var ad) ? ad.GetString() ?? "" : "";
                            assetSize = asset.TryGetProperty("size", out var az) ? az.GetInt64() : 0;
                            break;
                        }
                    }
                    if (assetUrl.Length == 0) continue;
                    bestTag = tag;
                    bestName = rel.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "v" + tag;
                    bestUrl = rel.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
                    bestAssetUrl = assetUrl;
                    bestAssetSize = assetSize;
                }
            }
            if (bestTag.Length == 0 || Compare(bestTag, AppVersion) <= 0) return null;
            return new Release(bestTag, bestName, bestUrl, bestAssetUrl, bestAssetSize);
        }
        catch { return null; }
    }

    public static bool IsMajorUpdate(string tag) => Num(tag.TrimStart('v', 'V').Split('.')[0]) > Num(AppVersion.Split('.')[0]);

    public static async Task<string> DownloadAsync(Release release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => await DownloadToAsync(release, DownloadHttp, Path.Combine(Path.GetTempPath(), "SMAP", "updates"), progress, cancellationToken);

    internal static async Task<string> DownloadToAsync(Release release, HttpClient http, string dir, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        const long limit = 512L * 1024 * 1024;
        if (string.IsNullOrEmpty(release.Tag) || release.Tag.Length > 80 || release.Tag.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
            || release.AssetSize is < 0 or > limit || !Uri.TryCreate(release.AssetUrl, UriKind.Absolute, out var source)
            || source.Scheme != Uri.UriSchemeHttps || !source.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !source.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
            throw new IOException("安装包地址或版本无效。");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"SMAP-Setup-v{release.Tag}.exe");
        var partial = path + ".download";
        try
        {
            using var resp = await DownloadResponse(http, source, cancellationToken);
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentType?.MediaType is "text/html" or "application/json" || resp.Content.Headers.ContentLength > limit)
                throw new IOException("安装包响应格式或大小无效。");
            var total = release.AssetSize > 0 ? release.AssetSize : resp.Content.Headers.ContentLength ?? 0;
            await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (received > limit) throw new IOException("安装包超过大小限制。");
                if (total > 0) progress?.Report(received * 100.0 / total);
            }
            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();
            if (release.AssetSize > 0 && received != release.AssetSize)
                throw new IOException($"安装包大小不正确（应为 {release.AssetSize}，实际为 {received}）");
            using (var check = File.OpenRead(partial))
                if (check.ReadByte() != 'M' || check.ReadByte() != 'Z') throw new IOException("下载文件不是有效的 Windows 安装程序。");
            File.Move(partial, path, true);
            return path;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }
            throw;
        }
    }

    static async Task<HttpResponseMessage> DownloadResponse(HttpClient http, Uri source, CancellationToken cancellation)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            request.Headers.Add("User-Agent", "SMAP-Updater");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is < 300 or >= 400) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location == null) throw new IOException("安装包重定向无效。");
            var next = location.IsAbsoluteUri ? location : new Uri(source, location);
            if (next.Scheme != Uri.UriSchemeHttps || !new[] { "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(next.Host, StringComparer.OrdinalIgnoreCase))
                throw new IOException("安装包不能重定向到不受信任或明文地址。");
            source = next;
        }
        throw new IOException("安装包重定向次数过多。");
    }

    // 数值化逐段比较版本号: >0 表示 a 更新。每段只取前导数字, 容忍 "1.1-wpf" 这类后缀
    internal static int Compare(string a, string b)
    {
        var aa = a.Split('-', 2); var bb = b.Split('-', 2);
        var pa = aa[0].Split('.');
        var pb = bb[0].Split('.');
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int va = i < pa.Length ? Num(pa[i]) : 0;
            int vb = i < pb.Length ? Num(pb[i]) : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        bool Pre(string[] parts) => parts.Length > 1 && new[] { "alpha", "beta", "rc" }.Any(p => parts[1].StartsWith(p, StringComparison.OrdinalIgnoreCase));
        bool preA = Pre(aa), preB = Pre(bb);
        if (preA != preB) return preA ? -1 : 1;
        if (!preA) return 0; // Preserve the existing stable meaning of legacy -wpf suffixes.
        var sa = aa[1].Split('.'); var sb = bb[1].Split('.');
        for (int i = 0; i < Math.Min(sa.Length, sb.Length); i++)
        {
            bool na = int.TryParse(sa[i], out int va), nb = int.TryParse(sb[i], out int vb);
            int compare = na && nb ? va.CompareTo(vb) : na != nb ? (na ? -1 : 1) : StringComparer.OrdinalIgnoreCase.Compare(sa[i], sb[i]);
            if (compare != 0) return compare;
        }
        return sa.Length.CompareTo(sb.Length);
    }

    // 取字符串前导数字(如 "1-wpf" -> 1, "2" -> 2)
    static int Num(string s)
    {
        int v = 0, i = 0;
        while (i < s.Length && char.IsDigit(s[i])) { v = v * 10 + (s[i] - '0'); i++; }
        return v;
    }
}
