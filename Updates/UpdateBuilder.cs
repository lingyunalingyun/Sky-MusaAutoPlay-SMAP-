using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SMAP_WPF.Updates;

public static class UpdateBuilder
{
    // Build a complete target description. Older installations reuse matching content independently of their version.
    public static UpdateManifest Build(string source, string output, string channel, string version)
    {
        string root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar), destination = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar);
        if (root.Equals(destination, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update output must be outside the published application.");
        foreach (string entry in new[] { "SMAP.exe", "SMAP.Updater.exe" })
            if (!File.Exists(UpdateFormat.Under(root, entry))) throw new InvalidDataException("Published application entry point missing.");
        Directory.CreateDirectory(destination); UpdateFormat.RejectLinks(destination);
        var files = new List<UpdateFile>(); var packs = new List<UpdatePack>();
        var chunks = new Dictionary<string, UpdateChunk>(StringComparer.Ordinal);
        var pending = new List<(string Hash, int Size, long Offset)>();
        using var pack = new MemoryStream();
        void Flush()
        {
            if (pack.Length == 0) return;
            byte[] bytes = pack.ToArray(); string hash = UpdateFormat.Hash(bytes), name = $"SMAP-{channel}-{hash}.pack";
            string path = Path.Combine(destination, name); UpdateFormat.RejectLinks(path);
            if (File.Exists(path)) { if (UpdateFormat.Hash(File.ReadAllBytes(path)) != hash) throw new InvalidDataException("Existing pack is corrupt."); }
            else { using var file = new FileStream(path, FileMode.CreateNew); file.Write(bytes); file.Flush(true); }
            packs.Add(new(name, bytes.Length, hash));
            foreach (var c in pending) chunks[c.Hash] = new(c.Hash, c.Size, name, c.Offset);
            pending.Clear(); pack.SetLength(0); pack.Position = 0;
        }
        var descriptions = new List<(string Path, long Size, string Hash, string[] Chunks)>();
        IEnumerable<string> Walk(string directory)
        {
            UpdateFormat.RejectLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (relative.Split('/')[0].Equals("songs", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(".smap-", StringComparison.Ordinal)) continue;
                UpdateFormat.RejectLinks(path);
                if (Directory.Exists(path)) { foreach (var child in Walk(path)) yield return child; }
                else yield return path;
            }
        }
        foreach (string file in Walk(root).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            // Published folders may contain user songs. They never enter an update, even on first adoption.
            if (relative.Split('/')[0].Equals("songs", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(".smap-", StringComparison.Ordinal)) continue;
            if (relative.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            if (descriptions.Count >= 10000) throw new InvalidDataException("Too many update files.");
            UpdateFormat.ManagedPath(relative); UpdateFormat.RejectLinks(file);
            var hashes = new List<string>(); long size = 0;
            using var input = File.OpenRead(file); using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var c in ContentChunks.Read(input))
            {
                digest.AppendData(c.Bytes); size += c.Bytes.Length; string hash = UpdateFormat.Hash(c.Bytes); hashes.Add(hash);
                if (chunks.ContainsKey(hash) || pending.Any(p => p.Hash == hash)) continue;
                if (pack.Length + c.Bytes.Length > UpdateFormat.MaxPackBytes) Flush();
                pending.Add((hash, c.Bytes.Length, pack.Length)); pack.Write(c.Bytes);
            }
            descriptions.Add((relative, size, Convert.ToHexStringLower(digest.GetHashAndReset()), hashes.ToArray()));
        }
        Flush();
        foreach (var f in descriptions) files.Add(new(f.Path, f.Size, f.Hash, f.Chunks.Select(h => chunks[h]).ToArray()));
        var manifest = new UpdateManifest(1, "SMAP", channel, version, "v" + version, Guid.NewGuid(), ContentChunks.Algorithm, files.ToArray(), packs.ToArray());
        UpdateFormat.Validate(manifest); return manifest;
    }
    public static byte[] Sign(UpdateManifest manifest, string keyId, ECDsa signer)
    {
        UpdateFormat.Validate(manifest); byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateFormat.Json);
        byte[] signature = signer.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return JsonSerializer.SerializeToUtf8Bytes(new SignedUpdate(keyId, Convert.ToBase64String(payload), Convert.ToBase64String(signature)), UpdateFormat.Json);
    }
}
