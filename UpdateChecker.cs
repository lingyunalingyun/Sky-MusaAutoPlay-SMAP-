using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SMAP_WPF;

/// <summary>启动时查 GitHub Releases, 有更高版本则返回信息。</summary>
public static class UpdateChecker
{
    public const string AppVersion = "1.1";   // C# WPF 版
    const string Repo = "lingyunalingyun/Sky-MusaAutoPlay-SMAP-";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public readonly record struct Release(string Tag, string Name, string Url);

    /// <summary>有新版返回 Release; 无新版/网络失败返回 null。</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.Add("Accept", "application/vnd.github+json");
            req.Headers.Add("User-Agent", "SMAP-Updater");   // GitHub API 必须带 UA, 否则 403
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = (root.TryGetProperty("tag_name", out var t) ? t.GetString() : "")?.TrimStart('v', 'V') ?? "";
            if (tag.Length == 0 || Compare(tag, AppVersion) <= 0) return null;

            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "v" + tag;
            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            return new Release(tag, name, url);
        }
        catch { return null; }
    }

    // 数值化逐段比较版本号: >0 表示 a 更新
    static int Compare(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int va = i < pa.Length && int.TryParse(pa[i], out var x) ? x : 0;
            int vb = i < pb.Length && int.TryParse(pb[i], out var y) ? y : 0;
            if (va != vb) return va.CompareTo(vb);
        }
        return 0;
    }
}
