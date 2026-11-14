using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace SMAP_WPF.Announcements;

public sealed record Announcement(long Id, int Revision, string Title, string Summary, bool Important, long PublishedAt, string? Content = null);
public sealed record AnnouncementPage(Announcement[] Items, int Page, int PageSize, int Total);

public sealed class AnnouncementClient : IDisposable
{
    readonly CloudTransport _transport;
    public AnnouncementClient(Uri? origin = null, HttpMessageHandler? handler = null) => _transport = new(origin ?? CloudTransport.ApiOrigin, handler);
    async Task<JsonDocument> Get(string path, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _transport.SendSharingAsync(HttpMethod.Get, path, null, null, null, timeout.Token);
        response.EnsureSuccessStatusCode();
        const int limit = 256 * 1024;
        if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("Invalid announcement response.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token); using var output = new MemoryStream();
        var buffer = new byte[8192]; int n;
        while ((n = await input.ReadAsync(buffer, timeout.Token)) > 0)
        { if (output.Length + n > limit) throw new InvalidDataException("Announcement response too large."); output.Write(buffer, 0, n); }
        var doc = JsonDocument.Parse(output.ToArray(), new JsonDocumentOptions { MaxDepth = 12 });
        try { Unique(doc.RootElement); if (doc.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unknown announcement schema."); return doc; }
        catch { doc.Dispose(); throw; }
    }
    static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject()) { if (!names.Add(field.Name)) throw new InvalidDataException("Duplicate announcement field."); Unique(field.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    static string Text(JsonElement value, string name, int max, bool required, bool multiline = false)
    {
        string text = value.GetProperty(name).GetString() ?? throw new InvalidDataException("Missing announcement text.");
        if (text.EnumerateRunes().Count() > max || required && string.IsNullOrWhiteSpace(text)
            || text.Any(c => char.IsControl(c) && !(multiline && c is '\r' or '\n' or '\t')))
            throw new InvalidDataException("Invalid announcement text.");
        return text;
    }
    static Announcement Item(JsonElement value, bool detail)
    {
        long id = value.GetProperty("id").GetInt64(); int revision = value.GetProperty("revision").GetInt32();
        long published = value.GetProperty("publishedAt").GetInt64();
        if (id is < 1 or > uint.MaxValue || revision < 1 || published <= 0) throw new InvalidDataException("Invalid announcement identity.");
        _ = DateTimeOffset.FromUnixTimeSeconds(published);
        return new(id, revision, Text(value, "title", 160, true), Text(value, "summary", 240, false), value.GetProperty("important").GetBoolean(), published,
            detail ? Text(value, "content", 10000, true, true) : null);
    }
    public async Task<AnnouncementPage> List(int page, CancellationToken cancellation = default)
    {
        if (page is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(page));
        using var doc = await Get("/api/smap/v1/announcements.php?page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellation);
        var root = doc.RootElement; var items = root.GetProperty("items").EnumerateArray().Select(x => Item(x, false)).ToArray();
        int actualPage = root.GetProperty("page").GetInt32(), pageSize = root.GetProperty("pageSize").GetInt32(), total = root.GetProperty("total").GetInt32();
        if (actualPage != page || pageSize != 20 || total < 0 || items.Length > 20 || items.Select(x => x.Id).Distinct().Count() != items.Length)
            throw new InvalidDataException("Invalid announcement page.");
        return new(items, actualPage, pageSize, total);
    }
    public async Task<Announcement> Detail(long id, CancellationToken cancellation = default)
    {
        if (id is < 1 or > uint.MaxValue) throw new ArgumentOutOfRangeException(nameof(id));
        using var doc = await Get("/api/smap/v1/announcements.php?id=" + id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellation);
        var item = Item(doc.RootElement.GetProperty("announcement"), true);
        if (item.Id != id) throw new InvalidDataException("Announcement identity mismatch."); return item;
    }
    public void Dispose() => _transport.Dispose();
}
