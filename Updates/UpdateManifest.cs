using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SMAP_WPF.Updates;

public sealed record UpdateChunk(string Sha256, int Size, string Pack, long Offset);
public sealed record UpdateFile(string Path, long Size, string Sha256, UpdateChunk[] Chunks);
public sealed record UpdatePack(string Name, long Size, string Sha256);
public sealed record UpdateManifest(int SchemaVersion, string Product, string Channel, string Version, string Tag,
    Guid ReleaseId, string Chunking, UpdateFile[] Files, UpdatePack[] Packs);
public sealed record SignedUpdate(string KeyId, string Payload, string Signature);
public sealed record UpdateKey(string Id, string PublicKey, string Channel);

public static class UpdateFormat
{
    public const int MaxManifestBytes = 16 * 1024 * 1024;
    public const int MaxPackBytes = 32 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 20 };
    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
    public static bool IsHash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    public static bool IsVersion(string value) => value is { Length: <= 80 } && Regex.IsMatch(value, @"\A[0-9]{1,6}\.[0-9]{1,6}\.[0-9]{1,6}(?:-[A-Za-z0-9.-]+)?\z");

    public static UpdateManifest Verify(byte[] envelope, IReadOnlyList<UpdateKey> keys, string channel, string tag)
    {
        if (envelope.Length > MaxManifestBytes) throw new InvalidDataException("Update manifest too large.");
        using var doc = JsonDocument.Parse(envelope); Unique(doc.RootElement);
        var signed = doc.Deserialize<SignedUpdate>(Json) ?? throw new InvalidDataException("Missing update signature.");
        var key = keys.SingleOrDefault(k => k.Id == signed.KeyId && k.Channel == channel)
            ?? throw new InvalidDataException("Untrusted update signing key.");
        byte[] payload = Convert.FromBase64String(signed.Payload), signature = Convert.FromBase64String(signed.Signature);
        using var verifier = ECDsa.Create(); verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key.PublicKey), out int read);
        if (signature.Length != 64 || !verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException("Invalid update signature.");
        using var content = JsonDocument.Parse(payload); Unique(content.RootElement);
        var manifest = content.Deserialize<UpdateManifest>(Json) ?? throw new InvalidDataException("Invalid update manifest.");
        Validate(manifest);
        if (manifest.Channel != channel || manifest.Tag != tag) throw new InvalidDataException("Update channel or release mismatch.");
        return manifest;
    }

    public static void Validate(UpdateManifest m)
    {
        if (m.SchemaVersion != 1 || m.Product != "SMAP" || m.Channel is not ("alpha" or "stable")
            || !IsVersion(m.Version)
            || m.Tag != "v" + m.Version || m.ReleaseId == Guid.Empty || m.Chunking != ContentChunks.Algorithm
            || m.Files == null || m.Packs == null || m.Files.Length is < 1 or > 10000 || m.Packs.Length is < 1 or > 900
            || m.Files.Any(f => f == null) || m.Packs.Any(p => p == null))
            throw new InvalidDataException("Unsupported update manifest.");
        var packs = new Dictionary<string, UpdatePack>(StringComparer.Ordinal);
        foreach (var p in m.Packs)
            if (p.Name == null || !Regex.IsMatch(p.Name, @"\ASMAP-" + m.Channel + @"-[a-f0-9]{64}\.pack\z") || p.Size is < 1 or > MaxPackBytes
                || !IsHash(p.Sha256) || p.Name != $"SMAP-{m.Channel}-{p.Sha256}.pack" || !packs.TryAdd(p.Name, p))
                throw new InvalidDataException("Invalid update pack.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0; int count = 0;
        var chunks = new Dictionary<string, UpdateChunk>(StringComparer.Ordinal);
        foreach (var file in m.Files)
        {
            ManagedPath(file.Path);
            if (!names.Add(file.Path) || !IsHash(file.Sha256) || file.Size is < 0 or > 2L * 1024 * 1024 * 1024 || file.Chunks == null)
                throw new InvalidDataException("Invalid update file.");
            long size = 0;
            foreach (var c in file.Chunks)
            {
                if (c == null || ++count > 100000 || !IsHash(c.Sha256) || c.Size is < 1 or > ContentChunks.MaxSize || c.Offset < 0
                    || !packs.TryGetValue(c.Pack, out var p) || c.Offset > p.Size - c.Size
                    || chunks.TryGetValue(c.Sha256, out var existing) && existing != c)
                    throw new InvalidDataException("Invalid update chunk.");
                chunks[c.Sha256] = c; size += c.Size;
            }
            if (size != file.Size || (total += file.Size) > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Invalid update size.");
        }
        if (!names.Contains("SMAP.exe") || !names.Contains("SMAP.Updater.exe")) throw new InvalidDataException("Update entry points missing.");
        foreach (var pack in packs.Keys)
        {
            long end = 0;
            foreach (var c in chunks.Values.Where(c => c.Pack == pack).OrderBy(c => c.Offset))
            { if (c.Offset != end) throw new InvalidDataException("Overlapping or unaccounted update data."); end += c.Size; }
            if (end != packs[pack].Size) throw new InvalidDataException("Unaccounted update pack.");
        }
    }

    public static void ManagedPath(string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 230 || relative.Contains('\\')) throw new InvalidDataException("Invalid update path.");
        var parts = relative.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.Any(c => c < 32 || "<>:\"|?*".Contains(c))
            || Regex.IsMatch(p, @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe update path.");
        bool root = parts.Length == 1 && (relative is "SMAP.exe" or "SMAP.Updater.exe" or "createdump.exe" or "SMAP.deps.json" or "SMAP.runtimeconfig.json" or "LICENSE.txt" or "ThirdPartyNotices.txt"
            || relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        bool asset = parts.Length >= 2 && parts[0] is "Assets" or "runtimes" && Path.GetExtension(relative).ToLowerInvariant() is ".wav" or ".dll" or ".json" or ".png";
        bool satellite = parts.Length == 2 && parts[0] is "cs" or "de" or "es" or "fr" or "it" or "ja" or "ko" or "pl" or "pt-BR" or "ru" or "tr" or "zh-Hans" or "zh-Hant"
            && parts[1].EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
        if (!root && !asset && !satellite) throw new InvalidDataException("An update cannot modify private data or songs.");
    }

    public static string Under(string root, string relative)
    {
        ManagedPath(relative); string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (fullRoot == Path.GetPathRoot(fullRoot)?.TrimEnd(Path.DirectorySeparatorChar)) throw new InvalidDataException("Independent application directory required.");
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update path escaped root.");
        RejectLinks(path); return path;
    }
    public static void RejectLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse links cannot be updated.");
    }
    static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate update field."); Unique(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var v in value.EnumerateArray()) Unique(v);
    }
}
