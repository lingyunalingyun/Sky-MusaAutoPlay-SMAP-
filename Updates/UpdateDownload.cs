using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SMAP_WPF.Updates;

public sealed class GitHubUpdates : IDisposable
{
    public const string Repository = "LangHingA/Sky-MusaAutoPlay-SMAP-";
    readonly HttpClient _http;
    public GitHubUpdates(HttpMessageHandler? handler = null)
        => _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(3) };
    public void Dispose() => _http.Dispose();

    public static Uri Asset(string tag, string name)
    {
        if (string.IsNullOrEmpty(tag) || tag.Length > 100 || tag.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-'))
            || name.Length > 140 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-')))
            throw new InvalidDataException("Invalid release asset.");
        return new($"https://github.com/{Repository}/releases/download/{tag}/{name}");
    }
    async Task<HttpResponseMessage> Send(Uri source, CancellationToken cancellation, long? start = null, long? end = null)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, source);
            req.Headers.UserAgent.ParseAdd("SMAP-Updater/1"); req.Headers.AcceptEncoding.ParseAdd("identity");
            if (start.HasValue) req.Headers.Range = new RangeHeaderValue(start, end);
            var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is < 300 or >= 400) return response;
            var location = response.Headers.Location; response.Dispose();
            Uri next = location == null ? throw new InvalidDataException("Invalid update redirect.") : new(source, location);
            if (next.Scheme != "https" || !next.IsDefaultPort || next.UserInfo.Length != 0 || next.Fragment.Length != 0
                || !new[] { "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" }.Contains(next.Host, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Untrusted update redirect.");
            source = next;
        }
        throw new InvalidDataException("Too many update redirects.");
    }
    async Task<byte[]> Small(Uri url, int limit, CancellationToken cancellation)
    {
        using var res = await Send(url, cancellation); res.EnsureSuccessStatusCode();
        if (res.StatusCode != HttpStatusCode.OK || res.Content.Headers.ContentLength > limit) throw new InvalidDataException("Invalid update response.");
        await using var source = await res.Content.ReadAsStreamAsync(cancellation); using var result = new MemoryStream();
        var buffer = new byte[81920]; int n;
        while ((n = await source.ReadAsync(buffer, cancellation)) > 0)
        { if (result.Length + n > limit) throw new InvalidDataException("Update response too large."); result.Write(buffer, 0, n); }
        return result.ToArray();
    }
    public async Task<(UpdateManifest Manifest, byte[] Signed)?> Latest(string channel, string currentVersion, IReadOnlyList<UpdateKey> keys,
        Func<string, string, int> compare, CancellationToken cancellation)
    {
        if (channel is not ("alpha" or "stable")) throw new ArgumentException("Unknown update channel.");
        byte[] bytes = await Small(new($"https://api.github.com/repos/{Repository}/releases?per_page=50"), 2 * 1024 * 1024, cancellation);
        using var doc = JsonDocument.Parse(bytes); if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid releases list.");
        string? tag = null, version = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            bool pre = release.GetProperty("prerelease").GetBoolean();
            string candidate = release.GetProperty("tag_name").GetString() ?? "";
            if (!candidate.StartsWith('v')) continue;
            string value = candidate[1..];
            if (!UpdateFormat.IsVersion(value)) continue;
            if (channel == "stable" ? pre || value.Contains('-') : !pre || !value.Contains('-')) continue;
            if (compare(value, currentVersion) <= 0 || version != null && compare(value, version) <= 0) continue;
            if (!release.GetProperty("assets").EnumerateArray().Any(a => a.GetProperty("name").GetString() == $"SMAP-{channel}-manifest.json")) continue;
            tag = candidate; version = value;
        }
        if (tag == null) return null;
        byte[] signed = await Small(Asset(tag, $"SMAP-{channel}-manifest.json"), UpdateFormat.MaxManifestBytes, cancellation);
        return (UpdateFormat.Verify(signed, keys, channel, tag), signed);
    }

    public async Task Download(UpdatePlan plan, string cache, IProgress<UpdateProgress>? progress, CancellationToken cancellation)
    {
        UpdateFormat.RejectLinks(cache); Directory.CreateDirectory(cache);
        long completed = 0, network = 0; var missing = new List<UpdateChunk>();
        foreach (var c in plan.RequiredChunks)
        {
            string path = UpdatePlanning.CachePath(cache, c.Sha256);
            if (UpdatePlanning.Matches(path, c.Size, c.Sha256)) completed += c.Size;
            else { if (File.Exists(path)) File.Delete(path); missing.Add(c); }
        }
        progress?.Report(new("download", completed, plan.DownloadBytes, network));
        foreach (var group in missing.GroupBy(c => c.Pack))
        {
            var ordered = group.OrderBy(c => c.Offset).ToArray(); int first = 0;
            while (first < ordered.Length)
            {
                cancellation.ThrowIfCancellationRequested(); int last = first;
                while (last + 1 < ordered.Length && ordered[last + 1].Offset - ordered[last].Offset - ordered[last].Size <= 32768
                    && ordered[last + 1].Offset + ordered[last + 1].Size - ordered[first].Offset <= 4 * 1024 * 1024) last++;
                long start = ordered[first].Offset, end = ordered[last].Offset + ordered[last].Size - 1;
                using var res = await Send(Asset(plan.Manifest.Tag, group.Key), cancellation, start, end); res.EnsureSuccessStatusCode();
                if (res.Content.Headers.ContentEncoding.Any() || res.Content.Headers.ContentType?.MediaType is "text/html" or "application/json")
                    throw new InvalidDataException("Invalid update pack response.");
                long skip;
                var pack = plan.Manifest.Packs.Single(p => p.Name == group.Key);
                if (res.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = res.Content.Headers.ContentRange;
                    if (range?.From != start || range.To != end || range.Length != pack.Size || res.Content.Headers.ContentLength is { } length && length != end - start + 1)
                        throw new InvalidDataException("Invalid update byte range.");
                    skip = 0;
                }
                else if (res.StatusCode == HttpStatusCode.OK && (res.Content.Headers.ContentLength == null || res.Content.Headers.ContentLength == pack.Size)) skip = start;
                else throw new InvalidDataException("Unexpected update response.");
                await using var stream = await res.Content.ReadAsStreamAsync(cancellation); var scratch = new byte[81920];
                while (skip > 0)
                {
                    int n = await stream.ReadAsync(scratch.AsMemory(0, (int)Math.Min(skip, scratch.Length)), cancellation);
                    if (n == 0) throw new EndOfStreamException(); skip -= n; network += n;
                }
                var data = new byte[checked((int)(end - start + 1))]; await stream.ReadExactlyAsync(data, cancellation); network += data.Length;
                for (int i = first; i <= last; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var c = ordered[i]; byte[] bytes = data.AsSpan(checked((int)(c.Offset - start)), c.Size).ToArray();
                    if (UpdateFormat.Hash(bytes) != c.Sha256) throw new InvalidDataException("Corrupt update chunk.");
                    string path = UpdatePlanning.CachePath(cache, c.Sha256), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { using (var output = new FileStream(temporary, FileMode.CreateNew)) { output.Write(bytes); output.Flush(true); } File.Move(temporary, path, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    completed += c.Size; progress?.Report(new("download", completed, plan.DownloadBytes, network));
                }
                first = last + 1;
            }
        }
    }
}
