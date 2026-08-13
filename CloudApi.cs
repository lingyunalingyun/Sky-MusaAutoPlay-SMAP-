using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SMAP_WPF;

// 字段必须是属性(property): WPF DisplayMemberBinding 只能绑定属性, 绑不到字段。
public class CloudSheet
{
    public int Id { get; set; }
    public int Bpm { get; set; }
    public int Difficulty { get; set; }
    public int NoteCount { get; set; }
    public int Downloads { get; set; }
    public int Likes { get; set; }
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string TranscribedBy { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Uploader { get; set; } = "";
    public string Description { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string CoverUrl { get; set; } = "";
    public string UploadTime { get; set; } = "";
    public bool Recommended { get; set; }
    public bool HasCover => !string.IsNullOrEmpty(CoverUrl);

    public string Stars => new string('★', Math.Clamp(Difficulty, 0, 5));
    public string ArtistText => string.IsNullOrWhiteSpace(Artist) ? Lang.S("song.noartist") : Artist;
    public string TranscriberText => string.IsNullOrWhiteSpace(TranscribedBy) ? Lang.S("song.notrans") : TranscribedBy;
    public string DownloadsText => $"↓{Downloads}";
}

/// <summary>缪斯树屋在线曲库 API: 登录/列表/下载/上传, 登录态持久化到 %APPDATA%\SMAP\auth.json。</summary>
public static class CloudApi
{
    const string Base = "http://musetreehouse.com";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static int UserId;
    public static string? Username;
    public static string? Mid;   // 鉴权 token
    public static bool LoggedIn => !string.IsNullOrEmpty(Mid);

    // ---- 登录态持久化 ----
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
    static readonly string AuthFile = Path.Combine(Dir, "auth.json");

    class Auth { public int UserId { get; set; } public string? Username { get; set; } public string? Mid { get; set; } }

    public static void LoadAuth()
    {
        try
        {
            if (File.Exists(AuthFile) && JsonSerializer.Deserialize<Auth>(File.ReadAllText(AuthFile)) is { } a)
            { UserId = a.UserId; Username = a.Username; Mid = a.Mid; }
        }
        catch { /* 忽略 */ }
    }

    static void SaveAuth()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(AuthFile, JsonSerializer.Serialize(new Auth { UserId = UserId, Username = Username, Mid = Mid }));
    }

    public static void Logout()
    {
        UserId = 0; Username = null; Mid = null;
        try { if (File.Exists(AuthFile)) File.Delete(AuthFile); } catch { }
    }

    /// <summary>登录; 成功返回 null 并写入登录态, 失败返回错误信息。</summary>
    public static async Task<string?> LoginAsync(string user, string pass)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { username = user, password = pass });
            using var resp = await Http.PostAsync(Base + "/api/game_login.php", new StringContent(body, Encoding.UTF8, "application/json"));
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return $"服务器错误 HTTP {(int)resp.StatusCode}";

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var s) && s.GetBoolean())
            {
                var u = root.GetProperty("user");
                UserId = u.GetProperty("id").GetInt32();
                Username = u.GetProperty("username").GetString();
                Mid = u.GetProperty("mid").GetString();
                SaveAuth();
                return null;
            }
            return root.TryGetProperty("error", out var e) ? e.GetString() : "登录失败";
        }
        catch (Exception ex) { return "网络错误: " + ex.Message; }
    }

    public record ListResult(bool Ok, string? Err, int Total, int Pages, List<CloudSheet> Items);

    public static async Task<ListResult> ListAsync(string q, string sort, int difficulty, int page, int perPage)
    {
        try
        {
            var url = $"{Base}/api/sheets/list.php?per_page={perPage}&page={page}&sort={sort}";
            if (!string.IsNullOrWhiteSpace(q)) url += "&q=" + Uri.EscapeDataString(q.Trim());
            if (difficulty is >= 1 and <= 5) url += "&difficulty=" + difficulty;

            using var resp = await Http.GetAsync(url);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return new(false, $"加载失败 HTTP {(int)resp.StatusCode}", 0, 0, new());

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "ok")
                return new(false, root.TryGetProperty("msg", out var m) ? m.GetString() : "服务端错误", 0, 0, new());

            var items = new List<CloudSheet>();
            if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                    items.Add(new CloudSheet
                    {
                        Id = GetInt(it, "id"),
                        Title = GetStr(it, "title"),
                        Artist = GetStr(it, "artist"),
                        TranscribedBy = GetStr(it, "transcribed_by"),
                        Bpm = GetInt(it, "bpm"),
                        Difficulty = GetInt(it, "difficulty"),
                        Tags = GetStr(it, "tags"),
                        NoteCount = GetInt(it, "note_count"),
                        Downloads = GetInt(it, "downloads"),
                        Likes = GetInt(it, "likes"),
                        Recommended = it.TryGetProperty("is_recommended", out var r) && r.ValueKind == JsonValueKind.True,
                        Uploader = GetStr(it, "uploader"),
                        Description = GetStr(it, "description"),
                        DownloadUrl = GetStr(it, "download_url"),
                        CoverUrl = GetStr(it, "cover_url"),
                        UploadTime = GetStr(it, "created_at")
                    });
            return new(true, null, GetInt(root, "total"), Math.Max(1, GetInt(root, "pages")), items);
        }
        catch (Exception ex) { return new(false, "网络错误: " + ex.Message, 0, 0, new()); }
    }

    /// <summary>下载曲谱到 songsDir; 成功返回落盘路径, 失败返回 null(err 输出错误)。</summary>
    public static async Task<string?> DownloadAsync(CloudSheet sheet, string songsDir, Action<string> onErr)
    {
        try
        {
            var url = sheet.DownloadUrl.StartsWith("http") ? sheet.DownloadUrl : Base + sheet.DownloadUrl;
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) { onErr($"下载失败 HTTP {(int)resp.StatusCode}"); return null; }
            var bytes = await resp.Content.ReadAsByteArrayAsync();

            Directory.CreateDirectory(songsDir);
            var safe = string.Concat(sheet.Title.Split(Path.GetInvalidFileNameChars())).Trim();
            if (safe.Length == 0) safe = "sheet_" + sheet.Id;
            var target = Path.Combine(songsDir, safe + ".txt");
            if (File.Exists(target)) target = Path.Combine(songsDir, $"{safe}_{sheet.Id}.txt");
            await File.WriteAllBytesAsync(target, bytes);
            return target;
        }
        catch (Exception ex) { onErr("下载错误: " + ex.Message); return null; }
    }

    /// <summary>上传曲谱; 成功返回 null, 失败返回错误信息。cover 为已压缩的 JPEG 字节(可选)。</summary>
    public static async Task<string?> UploadAsync(string filePath, string title, string artist, string trans, int difficulty, string tags, string desc, byte[]? cover = null)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(UserId.ToString()), "user_id");
            form.Add(new StringContent(Mid ?? ""), "mid");
            form.Add(new StringContent(title), "title");
            form.Add(new StringContent(artist), "artist");
            form.Add(new StringContent(trans), "transcribed_by");
            form.Add(new StringContent(difficulty.ToString()), "difficulty");
            form.Add(new StringContent(tags), "tags");
            form.Add(new StringContent(desc), "description");
            var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
            // 文件名去掉非 ASCII(中文名 multipart 编码后 PHP pathinfo 取不到扩展名 → 误判"非txt")；扩展名必须保留
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) ext = ".txt";
            form.Add(fileContent, "file", "sheet" + ext);
            if (cover is { Length: > 0 })
            {
                var cc = new ByteArrayContent(cover);
                cc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                form.Add(cc, "cover", "cover.jpg");
            }

            using var resp = await Http.PostAsync(Base + "/api/sheets/upload.php", form);
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() == "ok") return null;
            return root.TryGetProperty("msg", out var m) ? m.GetString() : "上传失败";
        }
        catch (Exception ex) { return "网络错误: " + ex.Message; }
    }

    /// <summary>上传客户端日志到缪斯树屋(存 game_logs 表); 成功返回 null, 失败返回错误信息。允许匿名。</summary>
    public static async Task<string?> UploadLogAsync(string log)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { mid = Mid ?? "", username = Username ?? "", version = UpdateChecker.AppVersion, log });
            using var resp = await Http.PostAsync(Base + "/api/game_log.php", new StringContent(body, Encoding.UTF8, "application/json"));
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("status", out var st) && st.GetString() == "ok") return null;
            return doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString() : "上传失败";
        }
        catch (Exception ex) { return "网络错误: " + ex.Message; }
    }

    static string GetStr(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int GetInt(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
