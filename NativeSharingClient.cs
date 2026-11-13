using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class SharingException(string code, int status = 0) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
public sealed record SharingWork(Guid WorkId, int Revision, Guid? LatestVersionId, string? Title = null, int VersionCount = 0, string State = "active");
public sealed record SharingSource(Guid? VersionId = null, string Relation = "derived_from", string Attribution = "", string Url = "");
public sealed record SharingSourceInfo(Guid? VersionId, Guid? WorkId, string Relation, string? Attribution, string? Url, string Provenance, bool Available);
public sealed record SharingVersion
{
    public Guid VersionId { get; init; }
    public Guid WorkId { get; init; }
    public Guid ProjectId { get; init; }
    public int VersionNumber { get; init; }
    public int WorkRevision { get; init; }
    public int AccessRevision { get; init; }
    public int OwnerUserId { get; init; }
    public string? PublisherName { get; init; }
    public Guid? LatestVersionId { get; init; }
    public string ShareCode { get; init; } = "";
    public string PayloadSha256 { get; init; } = "";
    public int PayloadSize { get; init; }
    public string PerformanceHash { get; init; } = "";
    public int CanonicalVersion { get; init; }
    public string ManifestBytes { get; init; } = "";
    public string ManifestSha256 { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string TranscribedBy { get; init; } = "";
    public string Description { get; init; } = "";
    public string Visibility { get; init; } = "";
    public string Lifecycle { get; init; } = "";
    public string WorkState { get; init; } = "active";
    public long CreatedAt { get; init; }
    public string FormattedCode => string.Join('-', Enumerable.Range(0, 3).Select(i => ShareCode.Length == 12 ? ShareCode.Substring(i * 4, 4) : ""));
}
public sealed record SharingHistory(List<SharingVersion> Items, int Revision, Guid? LatestVersionId, int? NextBefore);
public sealed record LegacyCatalogCoverage(string Status, int ConverterVersion, string Scope, int TotalRecords, int CoveredRecords, int UnresolvedRecords);
public sealed record LegacyCloudMatch(int SheetId, int SongIndex, string SourceSha256, string Title, int OwnerUserId, bool CanAssociate, string Relation, Guid? VersionId);
public sealed record SharingMatches(List<SharingVersion> Items, string PerformanceHash, int CanonicalVersion, bool More, bool LegacyCatalogReady)
{
    public LegacyCatalogCoverage? LegacyCatalog { get; init; }
    public List<LegacyCloudMatch> LegacyItems { get; init; } = new();
    public bool CanPublishLegacy => LegacyItems.Count > 0 || LegacyCatalog is { Status: "complete", ConverterVersion: LegacyConversion.Version, Scope: "published-song-sheets", UnresolvedRecords: 0 } c && c.TotalRecords == c.CoveredRecords;
}
public sealed record SharingDownload(SharingVersion Version, byte[] Payload, SmapProject Project)
{
    public Uri Origin { get; init; } = CloudTransport.ApiOrigin;
}

public sealed class NativeSharingClient(CloudTransport transport, Func<string?> credential, Action? authenticationFailed = null)
{
    public Uri Origin => transport.Origin;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 32 };
    const string Prefix = "/api/smap/v1/";
    public static SmapProject SharingSelection(SmapProject project, ScoreExportOptions options)
    {
        var snapshot = ScoreExport.ProjectSelection(project, options);
        var tracks = snapshot.Tracks.Select(t => t with { LegacyFields = SharingProvenance(project.Tracks.Single(p => p.Id == t.Id)) }).ToList();
        return SmapFile.FromBytes(SmapFile.ToBytes(snapshot with { Tracks = tracks }));
    }
    static System.Text.Json.Nodes.JsonObject? SharingProvenance(ProjectTrack track)
    {
        if (track.LegacyFields == null) return null;
        var source = track.LegacyFields["smapLegacySource"] as System.Text.Json.Nodes.JsonObject;
        var identity = new System.Text.Json.Nodes.JsonObject { ["converterVersion"] = LegacyConversion.Version };
        // Opaque old metadata can contain notes removed by editing or belonging to an unselected track.
        // It stays in the original/local project; public snapshots carry only the source identity.
        foreach (var key in new[] { "fileSha256", "songIndex" })
            if (source?[key] is System.Text.Json.Nodes.JsonValue value) identity[key] = value.DeepClone();
        return new() { ["smapLegacySource"] = identity };
    }
    public static string NormalizeCode(string value)
    {
        if (value == null || value.Length > 64 || value.Any(c => c > 127)) throw new SharingException("INVALID_SHARE_CODE");
        string code = string.Concat(value.Where(c => c is not ('-' or ' ' or '\t' or '\r' or '\n'))).ToUpperInvariant()
            .Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (code.Length != 12 || code.Any(c => !"0123456789ABCDEFGHJKMNPQRSTVWXYZ".Contains(c))) throw new SharingException("INVALID_SHARE_CODE");
        return code;
    }
    static bool Hash(string? value) => value != null && value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) { if (!names.Add(p.Name)) throw new SharingException("INVALID_RESPONSE"); Unique(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    public static void ValidateVersion(SharingVersion v)
    {
        try { ValidateVersionCore(v); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new SharingException("INVALID_RESPONSE"); }
    }
    static void ValidateVersionCore(SharingVersion v)
    {
        if (v.WorkId == Guid.Empty || v.VersionId == Guid.Empty || v.ProjectId == Guid.Empty || v.VersionNumber <= 0 || v.OwnerUserId <= 0
            || !Hash(v.PayloadSha256) || !Hash(v.PerformanceHash) || !Hash(v.ManifestSha256) || v.PayloadSize is <= 0 or > SmapFile.MaxArchiveBytes
            || v.CanonicalVersion is < 1 or > 3 || NormalizeCode(v.ShareCode) != v.ShareCode || v.ManifestBytes == null || v.ManifestBytes.Length > 128 * 1024
            || v.Title == null || v.Title.EnumerateRunes().Count() > 150 || v.Artist == null || v.TranscribedBy == null || v.Description == null)
            throw new SharingException("INVALID_RESPONSE");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(v.ManifestBytes); } catch (FormatException) { throw new SharingException("INVALID_RESPONSE"); }
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != v.ManifestSha256) throw new SharingException("INTEGRITY_MISMATCH");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 }); Unique(doc.RootElement); var m = doc.RootElement;
        if (m.GetProperty("schemaVersion").GetInt32() != 1 || m.GetProperty("versionId").GetGuid() != v.VersionId || m.GetProperty("workId").GetGuid() != v.WorkId
            || m.GetProperty("projectId").GetGuid() != v.ProjectId || m.GetProperty("payloadSha256").GetString() != v.PayloadSha256 || m.GetProperty("payloadSize").GetInt32() != v.PayloadSize
            || m.GetProperty("performanceIdentity").GetProperty("sha256").GetString() != v.PerformanceHash
            || m.GetProperty("performanceIdentity").GetProperty("canonicalVersion").GetInt32() != v.CanonicalVersion
            || m.GetProperty("performanceIdentity").GetProperty("scope").GetString() != "audible-project"
            || m.GetProperty("versionNumber").GetInt32() != v.VersionNumber || m.GetProperty("title").GetString() != v.Title
            || m.GetProperty("artist").GetString() != v.Artist || m.GetProperty("transcribedBy").GetString() != v.TranscribedBy
            || m.GetProperty("description").GetString() != v.Description) throw new SharingException("INTEGRITY_MISMATCH");
    }
    async Task<byte[]> Send(string endpoint, object? body, Guid? key, bool auth, int limit, CancellationToken cancellation, HttpContent? supplied = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(90));
        string? token = credential(); if (auth && token == null) throw new SharingException("AUTH_REQUIRED", 401);
        HttpContent? content = supplied ?? (body == null ? null : new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"));
        using var response = await transport.SendSharingAsync(content == null ? HttpMethod.Get : HttpMethod.Post, Prefix + endpoint, content, token, key, timeout.Token);
        int bound = response.IsSuccessStatusCode ? limit : 128 * 1024;
        if (response.Content.Headers.ContentLength > bound) throw new SharingException("RESPONSE_TOO_LARGE");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream(); byte[] chunk = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) != 0)
        { if (buffer.Length + count > bound) throw new SharingException("RESPONSE_TOO_LARGE"); buffer.Write(chunk, 0, count); }
        byte[] bytes = buffer.ToArray();
        if (!response.IsSuccessStatusCode)
        {
            string code = "SERVICE_UNAVAILABLE";
            try { using var error = JsonDocument.Parse(bytes); Unique(error.RootElement); if (error.RootElement.TryGetProperty("code", out var field) && field.ValueKind == JsonValueKind.String) code = field.GetString()!; }
            catch (JsonException) { }
            if ((int)response.StatusCode == 404 && code is not ("RESOURCE_NOT_FOUND" or "SHARE_NOT_FOUND")) code = "SHARING_API_UNAVAILABLE";
            if ((int)response.StatusCode == 401) authenticationFailed?.Invoke();
            throw new SharingException(code, (int)response.StatusCode);
        }
        if (limit != SmapFile.MaxArchiveBytes && response.Content.Headers.ContentType?.MediaType != "application/json") throw new SharingException("INVALID_RESPONSE");
        return bytes;
    }
    static T Decode<T>(byte[] bytes)
    {
        try { using var doc = JsonDocument.Parse(bytes); Unique(doc.RootElement); return doc.Deserialize<T>(Json) ?? throw new SharingException("INVALID_RESPONSE"); }
        catch (JsonException) { throw new SharingException("INVALID_RESPONSE"); }
    }
    public async Task<SharingWork> CreateWorkAsync(Guid key, CancellationToken ct = default)
        => Decode<SharingWork>(await Send("works.php", new { action = "create" }, key, true, 65536, ct));
    public async Task<SharingWork> GetWorkAsync(Guid id, CancellationToken ct = default)
        => Decode<SharingWork>(await Send("works.php?id=" + id, null, null, false, 65536, ct));
    public async Task<List<SharingWork>> MyWorksAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Send("works.php?mine=1", null, null, true, 256 * 1024, ct));
        Unique(doc.RootElement); return doc.RootElement.GetProperty("items").Deserialize<List<SharingWork>>(Json) ?? [];
    }
    public async Task<SharingVersion> ResolveAsync(string code, CancellationToken ct = default)
    {
        var v = Decode<SharingVersion>(await Send("shares.php?code=" + NormalizeCode(code), null, null, false, 256 * 1024, ct)); ValidateVersion(v); return v;
    }
    public async Task<SharingVersion> VersionAsync(Guid id, CancellationToken ct = default)
    {
        var v = Decode<SharingVersion>(await Send("versions.php?id=" + id, null, null, false, 256 * 1024, ct)); ValidateVersion(v); return v;
    }
    public async Task<SharingMatches> FindMatchesAsync(SmapProject project, CancellationToken ct = default)
    {
        var fingerprint = await Task.Run(() =>
        {
            var audible = project.AudibleTracks().ToArray();
            int canonical = audible.Any(t => t.Capability != null || t.KeyDomain != NoteKeyDomain.LegacyOrdinal) ? 3 : audible.Any(t => t.Notes.Any(n => n.RetriggerOnOverlap)) ? 2 : 1;
            return (Hash: PerformanceIdentity.ForProject(project), Canonical: canonical);
        }, ct);
        var result = Decode<SharingMatches>(await Send($"matches.php?canonicalVersion={fingerprint.Canonical}&performanceHash={fingerprint.Hash}", null, null, false, 2 * 1024 * 1024, ct));
        if (result.PerformanceHash != fingerprint.Hash || result.CanonicalVersion != fingerprint.Canonical || result.Items == null || result.Items.Count > 20) throw new SharingException("INVALID_RESPONSE");
        if (result.LegacyItems == null || result.LegacyItems.Count > 21 || result.LegacyItems.Any(l => l.SheetId <= 0 || l.SongIndex < 0 || !Hash(l.SourceSha256) || l.OwnerUserId <= 0 || l.Relation is not ("owned_legacy" or "public_legacy"))) throw new SharingException("INVALID_RESPONSE");
        if (result.LegacyCatalog is { } coverage && (coverage.ConverterVersion != LegacyConversion.Version || coverage.Scope != "published-song-sheets" || coverage.TotalRecords < 0 || coverage.CoveredRecords < 0 || coverage.UnresolvedRecords < 0 || coverage.CoveredRecords + coverage.UnresolvedRecords != coverage.TotalRecords || coverage.Status is not ("complete" or "incomplete" or "unavailable"))) throw new SharingException("INVALID_RESPONSE");
        foreach (var version in result.Items)
        {
            ValidateVersion(version);
            if (version.PerformanceHash != fingerprint.Hash || version.CanonicalVersion != fingerprint.Canonical || version.Lifecycle != "published" || version.WorkState != "active") throw new SharingException("INTEGRITY_MISMATCH");
        }
        return result;
    }
    public async Task<SharingHistory> HistoryAsync(Guid workId, int? before = null, CancellationToken ct = default)
        => Decode<SharingHistory>(await Send($"versions.php?workId={workId}&limit=20" + (before == null ? "" : "&before=" + before), null, null, false, 2 * 1024 * 1024, ct));
    public async Task<List<SharingSourceInfo>> SourcesAsync(Guid id, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Send("sources.php?id=" + id, null, null, false, 128 * 1024, ct));Unique(doc.RootElement);
        return doc.RootElement.GetProperty("items").Deserialize<List<SharingSourceInfo>>(Json) ?? [];
    }
    public async Task<SharingVersion> PublishAsync(SharingWork work, byte[] payload, string title, string artist, string transcriber, string description,
        string visibility, IReadOnlyList<SharingSource> sources, Guid key, CancellationToken ct = default)
    {
        var prepared = await Task.Run(() =>
        {
            var p = SmapFile.FromBytes(payload); var audible = p.AudibleTracks().ToArray();
            int version = audible.Any(t => t.Capability != null || t.KeyDomain != NoteKeyDomain.LegacyOrdinal) ? 3 : audible.Any(t => t.Notes.Any(n => n.RetriggerOnOverlap)) ? 2 : 1;
            return (Project: p, CanonicalVersion: version, PerformanceHash: PerformanceIdentity.ForProject(p), PayloadHash: Convert.ToHexStringLower(SHA256.HashData(payload)));
        }, ct);
        var project = prepared.Project;
        var manifest = new { schemaVersion = 1, workId = work.WorkId, revision = work.Revision, baseVersionId = work.LatestVersionId,
            title, artist, transcribedBy = transcriber, description, visibility, projectId = project.ProjectId, payloadSize = payload.Length,
            payloadSha256 = prepared.PayloadHash, performanceHash = prepared.PerformanceHash, canonicalVersion = prepared.CanonicalVersion, sources };
        using var multipart = new MultipartFormDataContent(); multipart.Add(new StringContent(JsonSerializer.Serialize(manifest, Json), Encoding.UTF8), "manifest");
        multipart.Add(new ByteArrayContent(payload), "project", "project.smap");
        var v = Decode<SharingVersion>(await Send("publish.php", null, key, true, 256 * 1024, ct, multipart)); ValidateVersion(v); return v;
    }
    public async Task<SharingDownload> DownloadAsync(SharingVersion version, CancellationToken ct = default)
    {
        ValidateVersion(version);
        byte[] payload = await Send("project.php?id=" + version.VersionId, null, null, false, SmapFile.MaxArchiveBytes, ct);
        if (payload.Length != version.PayloadSize || Convert.ToHexStringLower(SHA256.HashData(payload)) != version.PayloadSha256) throw new SharingException("INTEGRITY_MISMATCH");
        var project = await Task.Run(() =>
        {
            var p = SmapFile.FromBytes(payload);
            if (p.ProjectId != version.ProjectId || PerformanceIdentity.ForProject(p) != version.PerformanceHash) throw new SharingException("INTEGRITY_MISMATCH");
            return p;
        }, ct);
        return new(version, payload, project) { Origin = transport.Origin };
    }
    public async Task<SharingVersion> SetAccessAsync(SharingVersion version, string visibility, string lifecycle, Guid key, CancellationToken ct = default)
    {
        var v = Decode<SharingVersion>(await Send("versions.php", new { versionId = version.VersionId, accessRevision = version.AccessRevision, visibility, lifecycle }, key, true, 256 * 1024, ct)); ValidateVersion(v); return v;
    }
    public async Task<SharingWork> SetLatestAsync(SharingWork work, Guid versionId, Guid key, CancellationToken ct = default)
        => Decode<SharingWork>(await Send("works.php", new { action = "latest", workId = work.WorkId, revision = work.Revision, versionId }, key, true, 65536, ct));
    public async Task<SharingWork> SetWorkStateAsync(SharingWork work, string state, Guid key, CancellationToken ct = default)
        => Decode<SharingWork>(await Send("works.php", new { action = "state", workId = work.WorkId, revision = work.Revision, state }, key, true, 65536, ct));
}
