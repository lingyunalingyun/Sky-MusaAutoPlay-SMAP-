using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace SMAP_WPF.Updates;

public sealed record UpdateOperation(string Path, string? OldHash, string? NewHash);
public sealed record UpdateJournal(string Root, string Channel, string Tag, byte[] SignedManifest, UpdateOperation[] Operations, string State);

public static class UpdateTransaction
{
    public const string InstalledManifest = ".smap-installed.json";
    public static string RecoveryDirectory(string root) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar))!,
        ".smap-recovery-" + UpdateFormat.Hash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))[..16]);
    static string Digest(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    static void Save(string path, byte[] data)
    {
        UpdateFormat.RejectLinks(path); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var file = new FileStream(temp, FileMode.CreateNew)) { file.Write(data); file.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static string Target(string root, string name)
    {
        if (name != InstalledManifest) return UpdateFormat.Under(root, name);
        string path = Path.Combine(root, name); UpdateFormat.RejectLinks(path); return path;
    }

    public static void Apply(string root, string staging, byte[] signed, IReadOnlyList<UpdateKey> keys, string channel, string tag,
        Action<int>? appliedForTesting = null)
    {
        var manifest = UpdateFormat.Verify(signed, keys, channel, tag); root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(UpdateFormat.Under(root, "SMAP.exe"))) throw new InvalidDataException("Existing SMAP installation required.");
        UpdateFormat.RejectLinks(staging); string recovery = RecoveryDirectory(root); UpdateFormat.RejectLinks(recovery);
        Directory.CreateDirectory(recovery);
        UpdateFormat.RejectLinks(Path.Combine(root, ".smap-update.lock"));
        UpdateFormat.RejectLinks(Path.Combine(root, ".smap-running.lock"));
        using var applicationLock = new FileStream(Path.Combine(root, ".smap-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var runningLock = new FileStream(Path.Combine(root, ".smap-running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string journalPath = Path.Combine(recovery, "journal.json");
        if (File.Exists(journalPath))
        {
            var previous = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(journalPath), UpdateFormat.Json)!;
            if (previous.State is not ("committed" or "rolledBack")) Recover(root, recovery, previous, keys);
            // Retain the prior transaction as evidence; never replace its backup files.
            string archive = recovery + "-" + Guid.NewGuid().ToString("N"); Directory.Move(recovery, archive); Directory.CreateDirectory(recovery);
        }
        else if (Directory.EnumerateFileSystemEntries(recovery).Any()) throw new InvalidDataException("Unknown recovery data must be reviewed.");
        var operations = new List<UpdateOperation>();
        var names = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files)
        {
            string target = UpdateFormat.Under(root, f.Path);
            if (UpdatePlanning.Matches(target, f.Size, f.Sha256)) continue;
            string staged = UpdateFormat.Under(staging, f.Path);
            if (!UpdatePlanning.Matches(staged, f.Size, f.Sha256)) throw new InvalidDataException("Prepared update changed.");
            string copy = UpdateFormat.Under(Path.Combine(recovery, "new"), f.Path); Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(staged, copy, false);
            if (!UpdatePlanning.Matches(copy, f.Size, f.Sha256)) throw new InvalidDataException("Update copy failed verification.");
            operations.Add(new(f.Path, File.Exists(target) ? Digest(target) : null, f.Sha256));
        }
        string installed = Target(root, InstalledManifest);
        if (File.Exists(installed))
        {
            if (new FileInfo(installed).Length > UpdateFormat.MaxManifestBytes) throw new InvalidDataException("Installed manifest too large.");
            byte[] old = File.ReadAllBytes(installed);
            using var envelope = JsonDocument.Parse(old); var e = envelope.Deserialize<SignedUpdate>(UpdateFormat.Json)!;
            using var payload = JsonDocument.Parse(Convert.FromBase64String(e.Payload));
            string oldTag = payload.RootElement.GetProperty("tag").GetString()!;
            var prior = UpdateFormat.Verify(old, keys, channel, oldTag);
            foreach (var f in prior.Files.Where(f => !names.Contains(f.Path)))
            {
                string target = UpdateFormat.Under(root, f.Path); if (!File.Exists(target)) continue;
                if (!UpdatePlanning.Matches(target, f.Size, f.Sha256)) throw new InvalidDataException("An obsolete managed file was modified; preserve and review it.");
                operations.Add(new(f.Path, f.Sha256, null));
            }
        }
        Save(Path.Combine(recovery, "new-manifest.json"), signed);
        operations.Add(new(InstalledManifest, File.Exists(installed) ? Digest(installed) : null, UpdateFormat.Hash(signed)));
        var journal = new UpdateJournal(root, channel, tag, signed, operations.ToArray(), "applying");
        Save(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal, UpdateFormat.Json));
        try
        {
            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i]; string target = Target(root, op.Path), backup = Path.Combine(recovery, $"old-{i}");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); UpdateFormat.RejectLinks(target);
                if (op.OldHash == null ? File.Exists(target) : !File.Exists(target) || Digest(target) != op.OldHash)
                    throw new IOException("An installed file changed during update.");
                string source = op.Path == InstalledManifest ? Path.Combine(recovery, "new-manifest.json") : UpdateFormat.Under(Path.Combine(recovery, "new"), op.Path);
                if (op.NewHash == null) File.Move(target, backup, false);
                else if (op.OldHash != null) File.Replace(source, target, backup, true);
                else File.Move(source, target, false);
                appliedForTesting?.Invoke(i);
            }
            foreach (var file in manifest.Files)
                if (!UpdatePlanning.Matches(UpdateFormat.Under(root, file.Path), file.Size, file.Sha256)) throw new IOException("Post-install verification failed.");
            Save(journalPath, JsonSerializer.SerializeToUtf8Bytes(journal with { State = "committed" }, UpdateFormat.Json));
        }
        catch
        {
            Recover(root, recovery, journal, keys); throw;
        }
    }

    public static void Recover(string root, string recovery, UpdateJournal journal, IReadOnlyList<UpdateKey> keys)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar); UpdateFormat.RejectLinks(root); UpdateFormat.RejectLinks(recovery);
        if (!root.Equals(journal.Root, StringComparison.OrdinalIgnoreCase) || recovery != RecoveryDirectory(root)
            || journal.Operations.Length > 20001 || journal.Operations.Select(o => o.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Operations.Length)
            throw new InvalidDataException("Recovery target mismatch.");
        UpdateFormat.Verify(journal.SignedManifest, keys, journal.Channel, journal.Tag);
        for (int i = journal.Operations.Length - 1; i >= 0; i--)
        {
            var op = journal.Operations[i]; string target = Target(root, op.Path), backup = Path.Combine(recovery, $"old-{i}"); UpdateFormat.RejectLinks(backup);
            if (op.OldHash != null && File.Exists(backup))
            {
                if (Digest(backup) != op.OldHash || File.Exists(target) && Digest(target) != op.NewHash && Digest(target) != op.OldHash)
                    throw new IOException("Recovery found unexpected data; backups retained.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(backup, target, true);
            }
            else if (op.OldHash == null && File.Exists(target))
            {
                if (Digest(target) != op.NewHash) throw new IOException("Recovery cannot discard an unexpected file.");
                File.Delete(target);
            }
            else if (op.OldHash != null && (!File.Exists(target) || Digest(target) != op.OldHash)) throw new IOException("Missing recovery original; do not continue automatically.");
        }
        Save(Path.Combine(recovery, "journal.json"), JsonSerializer.SerializeToUtf8Bytes(journal with { State = "rolledBack" }, UpdateFormat.Json));
    }
}
