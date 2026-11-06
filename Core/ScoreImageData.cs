using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace SMAP_WPF.Core;

public sealed record ScoreImageEnvelope
{
    public string Kind { get; init; } = "smap-image";
    public int EnvelopeVersion { get; init; } = 1;
    public string Encoding { get; init; } = "base64";
    public int ProjectFormatVersion { get; init; }
    public int KeyInterpretationVersion { get; init; }
    public int CanonicalVersion { get; init; }
    public int PayloadLength { get; init; }
    public string PayloadSha256 { get; init; } = "";
    public string PerformanceSha256 { get; init; } = "";
    public Guid[] TrackIds { get; init; } = Array.Empty<Guid>();
    public int PageIndex { get; init; }
    public int PageCount { get; init; }
    public string Payload { get; init; } = "";
}

public static class ScoreImageData
{
    public const string Namespace = "urn:smap:image:1";
    public const int MaxEnvelopeBytes = 48 * 1024 * 1024;
    public const int MaxImageBytes = 128 * 1024 * 1024;
    static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    static readonly byte[] Keyword = "SMAP.Project\0\0\0\0\0"u8.ToArray();
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static string Create(ScoreExportDocument document, int page)
    {
        if (page < 1 || page > document.Pages.Count) throw new ArgumentOutOfRangeException(nameof(page));
        return JsonSerializer.Serialize(new ScoreImageEnvelope
        {
            ProjectFormatVersion = document.Project.FormatVersion, KeyInterpretationVersion = document.Project.KeyInterpretationVersion,
            CanonicalVersion = document.CanonicalVersion, PayloadLength = document.Payload.Length,
            PayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(document.Payload)), PerformanceSha256 = document.PerformanceHash,
            TrackIds = document.Options.TrackIds.ToArray(), PageIndex = page, PageCount = document.Pages.Count,
            Payload = Convert.ToBase64String(document.Payload)
        }, Options);
    }

    public static SmapProject Recover(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxEnvelopeBytes) throw new InvalidDataException("ScoreImageTooLarge");
        try
        {
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            CheckDuplicates(parsed.RootElement);
            var e = parsed.Deserialize<ScoreImageEnvelope>(Options) ?? throw new InvalidDataException("ScoreBadImage");
            if (e.Kind != "smap-image" || e.EnvelopeVersion != 1 || e.Encoding != "base64"
                || e.PayloadLength is <= 0 or > SmapFile.MaxArchiveBytes || e.Payload == null
                || e.Payload.Length > 4L * ((SmapFile.MaxArchiveBytes + 2) / 3)
                || e.PageIndex < 1 || e.PageCount < e.PageIndex || e.PageCount > ScoreExport.MaxPages
                || e.TrackIds == null || e.TrackIds.Length == 0 || e.TrackIds.Distinct().Count() != e.TrackIds.Length
                || !Hash(e.PayloadSha256) || !Hash(e.PerformanceSha256)) throw new InvalidDataException("ScoreBadImage");
            byte[] payload = Convert.FromBase64String(e.Payload);
            if (payload.Length != e.PayloadLength || Convert.ToHexStringLower(SHA256.HashData(payload)) != e.PayloadSha256)
                throw new InvalidDataException("ScoreIntegrity");
            var p = SmapFile.FromBytes(payload);
            if (p.FormatVersion != e.ProjectFormatVersion || p.KeyInterpretationVersion != e.KeyInterpretationVersion
                || ScoreExport.Canonical(p) != e.CanonicalVersion || !p.Tracks.Select(t => t.Id).ToHashSet().SetEquals(e.TrackIds)
                || PerformanceIdentity.ForTracks(p, e.TrackIds) != e.PerformanceSha256) throw new InvalidDataException("ScoreIntegrity");
            return p;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        { throw new InvalidDataException("ScoreBadImage", ex); }
    }

    static bool Hash(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    static void CheckDuplicates(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in e.EnumerateObject()) { if (!keys.Add(p.Name)) throw new InvalidDataException("ScoreBadImage"); CheckDuplicates(p.Value); }
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var v in e.EnumerateArray()) CheckDuplicates(v);
    }

    public static SmapProject Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxImageBytes) throw new InvalidDataException("ScoreImageTooLarge");
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (file.Length > 64 * 1024 * 1024) throw new InvalidDataException("ScoreImageTooLarge");
            using var reader = XmlReader.Create(file, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024 * 1024 });
            var xml = XDocument.Load(reader);
            var nodes = xml.Descendants(XName.Get("envelope", Namespace)).ToArray();
            if (nodes.Length != 1 || nodes[0].Parent?.Name != XName.Get("metadata", "http://www.w3.org/2000/svg"))
                throw new InvalidDataException("ScoreNoData");
            return Recover(nodes[0].Value);
        }
        using var buffer = new MemoryStream(); file.CopyTo(buffer);
        return Recover(ReadPng(buffer.ToArray()));
    }

    public static byte[] EmbedPng(byte[] png, string envelope)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(envelope) > MaxEnvelopeBytes) throw new InvalidDataException("ScoreImageTooLarge");
        ParsePng(png, out var found);
        if (found != null) throw new InvalidDataException("ScoreBadImage");
        byte[] json = System.Text.Encoding.UTF8.GetBytes(envelope), data = new byte[Keyword.Length + json.Length];
        Keyword.CopyTo(data, 0); json.CopyTo(data, Keyword.Length);
        using var output = new MemoryStream(); output.Write(png.AsSpan(0, 33)); WriteChunk(output, "iTXt"u8, data); output.Write(png.AsSpan(33));
        if (output.Length > MaxImageBytes) throw new InvalidDataException("ScoreImageTooLarge");
        return output.ToArray();
    }

    public static string ReadPng(byte[] png)
    { ParsePng(png, out string? json); return json ?? throw new InvalidDataException("ScoreNoData"); }

    static void ParsePng(byte[] png, out string? envelope)
    {
        envelope = null;
        if (png.Length > MaxImageBytes || png.Length < 45 || !png.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("ScoreBadImage");
        int offset = 8; bool header = false, end = false, image = false;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12) throw new InvalidDataException("ScoreBadImage");
            uint size = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            if (size > MaxImageBytes || size > png.Length - offset - 12) throw new InvalidDataException("ScoreBadImage");
            int length = (int)size; var type = png.AsSpan(offset + 4, 4); var data = png.AsSpan(offset + 8, length);
            if (Crc(png.AsSpan(offset + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4))) throw new InvalidDataException("ScoreIntegrity");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || offset != 8 || length != 13) throw new InvalidDataException("ScoreBadImage");
                uint w = BinaryPrimitives.ReadUInt32BigEndian(data), h = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                if (w == 0 || h == 0 || (ulong)w * h > 64 * 1024 * 1024) throw new InvalidDataException("ScoreImageTooLarge");
                header = true;
            }
            else if (!header) throw new InvalidDataException("ScoreBadImage");
            if (type.SequenceEqual("IDAT"u8)) image = true;
            if (type.SequenceEqual("iTXt"u8) && data.StartsWith("SMAP.Project\0"u8))
            {
                if (envelope != null || length > MaxEnvelopeBytes || !data.StartsWith(Keyword)) throw new InvalidDataException("ScoreBadImage");
                envelope = new UTF8Encoding(false, true).GetString(data[Keyword.Length..]);
            }
            offset += length + 12;
            if (type.SequenceEqual("IEND"u8)) { if (length != 0) throw new InvalidDataException("ScoreBadImage"); end = true; break; }
        }
        if (!header || !image || !end || offset != png.Length) throw new InvalidDataException("ScoreBadImage");
    }

    static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length); output.Write(number);
        output.Write(type); output.Write(data);
        uint crc = 0xffffffff; foreach (byte b in type) crc = Update(crc, b); foreach (byte b in data) crc = Update(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
    }
    static uint Crc(ReadOnlySpan<byte> data) { uint crc = 0xffffffff; foreach (byte b in data) crc = Update(crc, b); return ~crc; }
    static uint Update(uint crc, byte b)
    { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1; return crc; }
}
