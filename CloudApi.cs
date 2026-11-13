using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SMAP_WPF;

// 字段必须是属性(property): WPF DisplayMemberBinding 只能绑定属性, 绑不到字段。
public class CloudSheet : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    BitmapImage? _cover;
    bool _coverRequested;
    public BitmapImage? Cover
    {
        get
        {
            if (!_coverRequested && HasCover) { _coverRequested = true; _ = LoadCoverAsync(); }
            return _cover;
        }
    }
    async Task LoadCoverAsync()
    {
        _cover = await CloudApi.DownloadCoverAsync(CoverUrl);
        PropertyChanged?.Invoke(this, new(nameof(Cover)));
    }
    public int Id { get; set; }
    public Guid? WorkId { get; set; }
    public Guid? VersionId { get; set; }
    public bool IsNative => WorkId.HasValue && VersionId.HasValue;
    public string CatalogKey => IsNative ? "work:" + WorkId : "legacy:" + Id;
    public SongMetadataTarget MetadataTarget => IsNative ? new("work", WorkId!.Value.ToString()) : new("legacy", Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public int Bpm { get; set; }
    public int Difficulty { get; set; }
    public int NoteCount { get; set; }
    int _downloads, _likes;
    public int Downloads { get => _downloads; set { _downloads = value; PropertyChanged?.Invoke(this, new(nameof(Downloads))); PropertyChanged?.Invoke(this, new(nameof(DownloadsText))); } }
    public int Likes { get => _likes; set { _likes = value; PropertyChanged?.Invoke(this, new(nameof(Likes))); PropertyChanged?.Invoke(this, new(nameof(DownloadsText))); } }
    public bool StatisticsAvailable { get; set; } = true;
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
    public string DownloadsText => StatisticsAvailable ? $"↓{Downloads}  ♥{Likes}" : "↓—  ♥—";
}

/// <summary>缪斯树屋在线曲库 API。私密会话经当前 Windows 用户保护后保存。</summary>
public static class CloudApi
{
    static readonly CloudTransport Transport = new(CloudTransport.ApiOrigin);
    static CloudSession? Session;
    public static int UserId;
    public static string? Username;
    public static string? Mid;   // Public account identifier; never an authorization credential.
    public static string? Avatar;
    public static bool LoggedIn => Session != null && CloudSessionStore.IsUsable(Session);
    internal static string? SharingToken => LoggedIn ? Session!.Token : null;
    internal static void SharingAuthenticationFailed() => ForgetSession();
    static readonly string AuthFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "session.dat");

    static void Apply(CloudSession? session)
    {
        Session = session; UserId = session?.UserId ?? 0; Username = session?.Username;
        Mid = session?.Mid; Avatar = session?.Avatar;
    }
    public static void LoadAuth() => Apply(CloudSessionStore.Load(AuthFile));
    public static async Task<BitmapImage?> DownloadCoverAsync(string url)
    {
        try
        {
            var bytes = await Transport.GetPublicBytesAsync(url);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160; image.StreamSource = stream;
            image.EndInit(); image.Freeze();
            return image;
        }
        catch { return null; }
    }
    static void SaveAuth()
    {
        if (Session == null) return;
        var updated = Session with { Avatar = Avatar };
        CloudSessionStore.Save(AuthFile, updated); Apply(updated);
    }
    static bool ForgetSession()
    {
        Apply(null);
        try { File.Delete(AuthFile); return true; } catch { return false; }
    }
    static async Task<bool> RevokeAsync(CloudSession session)
    {
        try
        {
            using var response = await Transport.SendSensitiveAsync("/api/auth/logout.php", new StringContent("{}", Encoding.UTF8, "application/json"), session.Token);
            return await CloudTransport.RevocationConfirmedAsync(response);
        }
        catch { return false; }
    }
    public static async Task<string?> LogoutAsync()
    {
        var session = Session;
        bool local = ForgetSession();
        bool remote = session == null || await RevokeAsync(session);
        return !local ? Lang.S("cloud.storageFailed") : !remote ? Lang.S("cloud.revokeUnconfirmed") : null;
    }
    static string RequestFailure(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) { ForgetSession(); return Lang.S("cloud.loginRequired"); }
        return Lang.S("cloud.requestFailed") + " HTTP " + (int)response.StatusCode;
    }

    public static async Task<string?> LoginAsync(string user, string pass)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { username = user, password = pass });
            using var response = await Transport.SendSensitiveAsync("/api/game_login.php", new StringContent(body, Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode) return RequestFailure(response);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var success) || !success.GetBoolean()) return Lang.S("cloud.loginFailed");
            var u = root.GetProperty("user");
            var session = new CloudSession(u.GetProperty("id").GetInt32(), GetStr(u, "username"), GetStr(u, "mid"), GetStr(u, "avatar"), GetStr(u, "device_token"), u.GetProperty("expires_at").GetInt64());
            if (!CloudSessionStore.IsUsable(session)) return Lang.S("cloud.loginRequired");
            try { CloudSessionStore.Save(AuthFile, session); }
            catch { await RevokeAsync(session); return Lang.S("cloud.storageFailed"); }
            Apply(session);
            return null;
        }
        catch { return Lang.S("cloud.connectionFailed"); }
    }

    public static async Task<byte[]?> DownloadAvatarAsync()
    {
        if (!LoggedIn) return null;
        try
        {
            // Avatar recovery uses public resources without sending session credentials.
            if (string.IsNullOrWhiteSpace(Avatar) && UserId > 0)
            {
                var html = Encoding.UTF8.GetString(await Transport.GetPublicBytesAsync($"/pages/profile.php?id={UserId}"));
                var match = Regex.Match(html, @"uploads/avatars/([^""'<>?]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    Avatar = Uri.UnescapeDataString(match.Groups[1].Value);
                    SaveAuth();
                }
            }
            var file = Path.GetFileName(string.IsNullOrWhiteSpace(Avatar) ? "default.png" : Avatar);
            return await Transport.GetPublicBytesAsync($"/api/avatar.php?f={Uri.EscapeDataString(file)}");
        }
        catch { return null; }
    }

    public record ListResult(bool Ok, string? Err, int Total, int Pages, List<CloudSheet> Items, bool IncludesNative = false);

    public static async Task<ListResult> ListAsync(string q, string sort, int difficulty, int page, int perPage)
        => await ListAsync(q, sort, difficulty, page, perPage, Transport);

    internal static async Task<ListResult> ListAsync(string q, string sort, int difficulty, int page, int perPage, CloudTransport transport)
    {
        try
        {
            var url = $"/api/sheets/list.php?include_native=1&per_page={perPage}&page={page}&sort={Uri.EscapeDataString(sort)}";
            if (!string.IsNullOrWhiteSpace(q)) url += "&q=" + Uri.EscapeDataString(q.Trim());
            if (difficulty is >= 1 and <= 5) url += "&difficulty=" + difficulty;

            var text = Encoding.UTF8.GetString(await transport.GetPublicBytesAsync(url));
            return ParseCatalog(text, transport);
        }
        catch { return new(false, Lang.S("cloud.connectionFailed"), 0, 0, new()); }
    }

    internal static ListResult ParseCatalog(string text, CloudTransport transport)
    {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "ok")
                return new(false, root.TryGetProperty("msg", out var m) ? m.GetString() : "服务端错误", 0, 0, new());

            bool includesNative = GetInt(root, "catalog_version") == 1;
            var items = new List<CloudSheet>();
            if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                {
                    bool native = GetStr(it, "kind") == "work";
                    Guid? work = null, version = null;
                    if (native)
                    {
                        if (!includesNative || !Guid.TryParse(GetStr(it, "work_id"), out var wid) || wid == Guid.Empty || !Guid.TryParse(GetStr(it, "version_id"), out var vid) || vid == Guid.Empty) throw new InvalidDataException("Invalid native catalog identity.");
                        work = wid; version = vid;
                    }
                    else if (GetInt(it, "id") <= 0 || GetStr(it, "kind") is not ("" or "legacy")) throw new InvalidDataException("Invalid legacy catalog identity.");
                    items.Add(new CloudSheet
                    {
                        Id = GetInt(it, "id"),
                        WorkId = work, VersionId = version,
                        StatisticsAvailable = !native || it.TryGetProperty("statistics_available", out var stats) && stats.ValueKind == JsonValueKind.True,
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
                        CoverUrl = transport.PublicUri(GetStr(it, "cover_url"))?.AbsoluteUri ?? "",
                        UploadTime = GetStr(it, "created_at")
                    });
                }
            return new(true, null, GetInt(root, "total"), Math.Max(1, GetInt(root, "pages")), items, includesNative);
    }

    /// <summary>下载曲谱到 songsDir; 成功返回落盘路径, 失败返回 null(err 输出错误)。</summary>
    public static Task<string?> DownloadAsync(CloudSheet sheet, string songsDir, Action<string> onErr)
        => DownloadAsync(sheet, songsDir, onErr, Transport, new NativeSharingStore(NativeSharingStore.DefaultDirectory));

    internal static async Task<string?> DownloadAsync(CloudSheet sheet, string songsDir, Action<string> onErr, CloudTransport transport, NativeSharingStore store)
    {
        try
        {
            if (sheet.IsNative)
            {
                var client = new NativeSharingClient(transport, () => null);
                var version = await client.VersionAsync(sheet.VersionId!.Value);
                if (version.WorkId != sheet.WorkId) throw new InvalidDataException("Catalog work/version mismatch.");
                var download = await client.DownloadAsync(version);
                return store.Import(download, songsDir);
            }
            var bytes = await transport.GetPublicBytesAsync(sheet.DownloadUrl);
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            using (var doc = JsonDocument.Parse(await reader.ReadToEndAsync()))
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0) root = root[0];
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("songNotes", out var notes)
                    || notes.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid cloud sheet.");
            }

            Directory.CreateDirectory(songsDir);
            var safe = string.Concat(sheet.Title.Split(Path.GetInvalidFileNameChars())).Trim();
            if (safe.Length == 0) safe = "sheet_" + sheet.Id;
            var target = Path.Combine(songsDir, safe + ".txt");
            for (int suffix = 1; File.Exists(target); suffix++)
            {
                if ((await File.ReadAllBytesAsync(target)).SequenceEqual(bytes)) return target;
                target = Path.Combine(songsDir, $"{safe}_{sheet.Id}_{suffix}.txt");
            }
            var temporary = Path.Combine(songsDir, ".download-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { await output.WriteAsync(bytes); output.Flush(true); }
                File.Move(temporary, target, false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return target;
        }
        catch { onErr(Lang.S("cloud.connectionFailed")); return null; }
    }

    /// <summary>上传曲谱; 成功返回 null, 失败返回错误信息。cover 为已压缩的 JPEG 字节(可选)。</summary>
    public static async Task<string?> UploadAsync(string filePath, string title, string artist, string trans, int difficulty, string tags, string desc, byte[]? cover = null)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var session = Session;
            if (session == null || !CloudSessionStore.IsUsable(session)) return Lang.S("cloud.loginRequired");
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

            using var resp = await Transport.SendSensitiveAsync("/api/sheets/upload.php", form, session.Token);
            if (!resp.IsSuccessStatusCode) return RequestFailure(resp);
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("status", out var st) && st.GetString() == "ok") return null;
            return root.TryGetProperty("msg", out var m) ? SafeMessage(m.GetString()) : Lang.S("cloud.requestFailed");
        }
        catch { return Lang.S("cloud.connectionFailed"); }
    }

    /// <summary>上传客户端日志到缪斯树屋(存 game_logs 表); 成功返回 null, 失败返回错误信息。允许匿名。</summary>
    public static async Task<string?> UploadLogAsync(string log)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { version = UpdateChecker.AppVersion, log = CloudSecrets.Redact(log) });
            using var resp = await Transport.SendSensitiveAsync("/api/game_log.php", new StringContent(body, Encoding.UTF8, "application/json"), LoggedIn ? Session!.Token : null);
            if (!resp.IsSuccessStatusCode) return RequestFailure(resp);
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("status", out var st) && st.GetString() == "ok") return null;
            return doc.RootElement.TryGetProperty("msg", out var m) ? SafeMessage(m.GetString()) : Lang.S("cloud.requestFailed");
        }
        catch { return Lang.S("cloud.connectionFailed"); }
    }

    static string SafeMessage(string? value)
    {
        var text = CloudSecrets.Redact(value ?? "");
        return text[..Math.Min(text.Length, 200)];
    }
    static string GetStr(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static int GetInt(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
