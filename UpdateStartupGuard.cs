using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SMAP_WPF.Updates;

namespace SMAP_WPF;

internal static class UpdateStartupGuard
{
    // Keep this shared lock while the app runs. The installer takes both gates exclusively.
    public static FileStream? Enter(string root)
    {
        UpdateFormat.Under(root, "SMAP.exe");
        UpdateFormat.RejectLinks(Path.Combine(root, ".smap-update.lock"));
        UpdateFormat.RejectLinks(Path.Combine(root, ".smap-running.lock"));
        using var gate = new FileStream(Path.Combine(root, ".smap-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string journalPath = Path.Combine(UpdateTransaction.RecoveryDirectory(root), "journal.json");
        UpdateFormat.RejectLinks(journalPath);
        if (File.Exists(journalPath))
        {
            if (new FileInfo(journalPath).Length > 64 * 1024 * 1024) throw new InvalidDataException("Update recovery data must be reviewed.");
            var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(journalPath), UpdateFormat.Json);
            if (journal is { State: "applying" })
            {
                var manifest = UpdateFormat.Verify(journal.SignedManifest, UpdateTrust.Keys, journal.Channel, journal.Tag);
                var helper = manifest.Files.Single(f => f.Path == "SMAP.Updater.exe");
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMAP", "updates", "releases", manifest.ReleaseId.ToString("N"), "handoff");
                UpdateFormat.RejectLinks(directory);
                string? worker = Directory.Exists(directory) ? Directory.EnumerateDirectories(directory).Take(100)
                    .Select(p => Path.Combine(p, "SMAP.Updater.exe")).FirstOrDefault(p => UpdatePlanning.Matches(p, helper.Size, helper.Sha256)) : null;
                if (worker == null) throw new IOException("An interrupted update requires its retained recovery helper.");
                gate.Dispose();
                var info = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("--recover"); info.ArgumentList.Add(root); Process.Start(info);
                return null;
            }
        }
        return new FileStream(Path.Combine(root, ".smap-running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
    }
}
