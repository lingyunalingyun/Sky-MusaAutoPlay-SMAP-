using System.IO;
using System.Text;
using System.Text.Json;

namespace SMAP_WPF.Updates;

public static class UpdateHealth
{
    public static void Confirm(string root, string version, string nonce, IReadOnlyList<UpdateKey> keys)
    {
        if (nonce.Length != 32 || nonce.Any(c => !char.IsAsciiHexDigitLower(c))) throw new InvalidDataException("Invalid update launch token.");
        string recovery = UpdateTransaction.RecoveryDirectory(root), path = Path.Combine(recovery, "journal.json"); UpdateFormat.RejectLinks(path);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Recovery journal too large.");
        var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(path), UpdateFormat.Json) ?? throw new InvalidDataException("Missing update journal.");
        var manifest = UpdateFormat.Verify(journal.SignedManifest, keys, journal.Channel, journal.Tag);
        if (manifest.Version != version || journal.State != "committed"
            || !Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Equals(journal.Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update launch version mismatch.");
        string ready = Path.Combine(recovery, "launch-" + nonce + ".ready"); UpdateFormat.RejectLinks(ready);
        using var output = new FileStream(ready, FileMode.CreateNew); output.Write(Encoding.UTF8.GetBytes(manifest.ReleaseId.ToString())); output.Flush(true);
    }
}
