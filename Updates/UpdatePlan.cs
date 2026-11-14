using System.IO;
using System.Security.Cryptography;

namespace SMAP_WPF.Updates;

public sealed record LocalUpdateChunk(string File, long Offset, int Size);
public sealed record UpdatePlan(UpdateManifest Manifest, string Root, UpdateFile[] ChangedFiles,
    Dictionary<string, LocalUpdateChunk> LocalChunks, UpdateChunk[] RequiredChunks, long ReusedBytes, long DownloadBytes);
public sealed record UpdateProgress(string Stage, long Completed, long Total, long NetworkBytes = 0);

public static class UpdatePlanning
{
    public static bool Matches(string path, long size, string hash)
    {
        UpdateFormat.RejectLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)) == hash;
    }
    public static UpdatePlan Plan(UpdateManifest manifest, string root, CancellationToken cancellation = default)
    {
        UpdateFormat.Validate(manifest);
        var changed = new List<UpdateFile>(); var unchanged = new List<UpdateFile>(); var required = new Dictionary<string, UpdateChunk>(StringComparer.Ordinal); long reused = 0;
        foreach (var file in manifest.Files)
        {
            cancellation.ThrowIfCancellationRequested(); string path = UpdateFormat.Under(root, file.Path);
            if (Matches(path, file.Size, file.Sha256)) { reused += file.Size; unchanged.Add(file); continue; }
            changed.Add(file); foreach (var chunk in file.Chunks) required[chunk.Sha256] = chunk;
        }
        var local = new Dictionary<string, LocalUpdateChunk>(StringComparer.Ordinal);
        // A verified whole-file hash also verifies its target chunk offsets; no second scan is needed.
        foreach (var file in unchanged)
        {
            long offset = 0; string path = UpdateFormat.Under(root, file.Path);
            foreach (var chunk in file.Chunks)
            { if (required.ContainsKey(chunk.Sha256)) local.TryAdd(chunk.Sha256, new(path, offset, chunk.Size)); offset += chunk.Size; }
        }
        // Only inspect explicitly managed target paths. Never scan songs, credentials or arbitrary directories.
        foreach (var file in changed)
        {
            if (local.Count == required.Count) break;
            cancellation.ThrowIfCancellationRequested(); string path = UpdateFormat.Under(root, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length > 2L * 1024 * 1024 * 1024) continue;
            using var stream = File.OpenRead(path);
            foreach (var chunk in ContentChunks.Read(stream))
            {
                cancellation.ThrowIfCancellationRequested(); string hash = UpdateFormat.Hash(chunk.Bytes);
                if (required.ContainsKey(hash)) local.TryAdd(hash, new(path, chunk.Offset, chunk.Bytes.Length));
            }
        }
        var missing = required.Values.Where(c => !local.ContainsKey(c.Sha256)).ToArray();
        long localBytes = changed.SelectMany(f => f.Chunks).Where(c => local.ContainsKey(c.Sha256)).Sum(c => (long)c.Size);
        return new(manifest, Path.GetFullPath(root), changed.ToArray(), local, missing, reused + localBytes, missing.Sum(c => (long)c.Size));
    }
    public static string CachePath(string cache, string hash)
    {
        if (!UpdateFormat.IsHash(hash)) throw new InvalidDataException("Invalid update content key.");
        string path = Path.Combine(Path.GetFullPath(cache), hash + ".chunk"); UpdateFormat.RejectLinks(path); return path;
    }
    public static async Task Reconstruct(UpdatePlan plan, string cache, string staging, IProgress<UpdateProgress>? progress, CancellationToken cancellation)
    {
        long completed = 0, total = plan.ChangedFiles.Sum(f => f.Size); UpdateFormat.RejectLinks(staging); Directory.CreateDirectory(staging);
        foreach (var file in plan.ChangedFiles)
        {
            string target = UpdateFormat.Under(staging, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    foreach (var chunk in file.Chunks)
                    {
                        cancellation.ThrowIfCancellationRequested(); var bytes = new byte[chunk.Size];
                        if (plan.LocalChunks.TryGetValue(chunk.Sha256, out var local))
                        {
                            UpdateFormat.RejectLinks(local.File); using var source = File.OpenRead(local.File); source.Position = local.Offset;
                            await source.ReadExactlyAsync(bytes, cancellation);
                        }
                        else
                        {
                            using var source = File.OpenRead(CachePath(cache, chunk.Sha256));
                            if (source.Length != chunk.Size) throw new InvalidDataException("Invalid cached block size.");
                            await source.ReadExactlyAsync(bytes, cancellation);
                        }
                        if (bytes.Length != chunk.Size || UpdateFormat.Hash(bytes) != chunk.Sha256) throw new InvalidDataException("Update source changed; check and retry.");
                        await output.WriteAsync(bytes, cancellation); completed += bytes.Length; progress?.Report(new("reconstruct", completed, total));
                    }
                    output.Flush(true);
                }
                if (!Matches(temporary, file.Size, file.Sha256)) throw new InvalidDataException("Reconstructed file failed verification.");
                UpdateFormat.RejectLinks(target); File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
