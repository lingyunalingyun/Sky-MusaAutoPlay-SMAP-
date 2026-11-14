using System.Diagnostics;
using System.Text.Json;
using SMAP_WPF.Updates;

// This executable is copied out of the installation before launch. It never terminates the application.
string? root = null;
bool validatedHandoff = false, launchDidNotStart = false;
try
{
    if (args.Length == 2 && args[0] == "--recover")
    {
        root = Path.GetFullPath(args[1]); string recovery = UpdateTransaction.RecoveryDirectory(root);
        UpdateFormat.Under(root, "SMAP.exe"); UpdateFormat.RejectLinks(recovery);
        using var gate = new FileStream(Path.Combine(root, ".smap-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var running = new FileStream(Path.Combine(root, ".smap-running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string path = Path.Combine(recovery, "journal.json");
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Recovery journal too large.");
        var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(path), UpdateFormat.Json) ?? throw new InvalidDataException("Recovery journal missing.");
        UpdateTransaction.Recover(root, recovery, journal, UpdateTrust.Keys);
        running.Dispose(); gate.Dispose();
        Process.Start(new ProcessStartInfo(UpdateFormat.Under(root, "SMAP.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root });
        return 0;
    }
    if (args.Length != 7 || !int.TryParse(args[5], out int pid) || pid <= 0 || args[6].Length != 32 || args[6].Any(c => !char.IsAsciiHexDigitLower(c))) throw new ArgumentException("Invalid update handoff.");
    root = Path.GetFullPath(args[0]); string staging = Path.GetFullPath(args[1]);
    string exe = UpdateFormat.Under(root, "SMAP.exe"); UpdateFormat.RejectLinks(staging); UpdateFormat.RejectLinks(args[4]);
    if (new FileInfo(args[4]).Length > UpdateFormat.MaxManifestBytes) throw new InvalidDataException("Manifest too large.");
    byte[] signed = File.ReadAllBytes(args[4]); var manifest = UpdateFormat.Verify(signed, UpdateTrust.Keys, args[2], args[3]);
    using (var process = Process.GetProcessById(pid))
    {
        if (!string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Application process mismatch.");
        validatedHandoff = true;
        using (var ready = new FileStream(Path.Combine(staging, "handoff-" + args[6] + ".ready"), FileMode.CreateNew))
        { var bytes = System.Text.Encoding.UTF8.GetBytes(manifest.ReleaseId.ToString()); ready.Write(bytes); ready.Flush(true); }
        if (!process.WaitForExit(120000)) return 2;
    }
    UpdateTransaction.Apply(root, staging, signed, UpdateTrust.Keys, manifest.Channel, manifest.Tag);
    string launchNonce = Guid.NewGuid().ToString("N"), recoveryPath = UpdateTransaction.RecoveryDirectory(root);
    var launch = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
    launch.ArgumentList.Add("--smap-update-health"); launch.ArgumentList.Add(launchNonce);
    launchDidNotStart = true;
    using var started = Process.Start(launch) ?? throw new IOException("Updated application did not start.");
    launchDidNotStart = false;
    string confirmation = Path.Combine(recoveryPath, "launch-" + launchNonce + ".ready");
    for (int i = 0; i < 1200; i++)
    {
        if (File.Exists(confirmation) && File.ReadAllText(confirmation) == manifest.ReleaseId.ToString()) return 0;
        if (started.HasExited)
        {
            // The just-launched application exited before readiness. No running application is terminated.
            using var gate = new FileStream(Path.Combine(root, ".smap-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var running = new FileStream(Path.Combine(root, ".smap-running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(Path.Combine(recoveryPath, "journal.json")), UpdateFormat.Json)!;
            UpdateTransaction.Recover(root, recoveryPath, journal, UpdateTrust.Keys);
            throw new IOException("Updated application failed startup; previous version restored.");
        }
        Thread.Sleep(100);
    }
    // A slow but still-running application may already contain user work: retain it and require review.
    File.WriteAllText(Path.Combine(recoveryPath, "launch-unconfirmed.json"), JsonSerializer.Serialize(new { status = "NEEDS_REVIEW", manifest.Version }));
    return 2;
}
catch (Exception error)
{
    // Keep the transaction and old program intact. Do not restart unknown or partially recovered files.
    string folder = root == null ? Path.Combine(Path.GetTempPath(), "SMAP", "update-errors") : UpdateTransaction.RecoveryDirectory(root);
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "last-install-error.json"), JsonSerializer.Serialize(new { root, error = error.GetType().Name, message = error.Message }));
    if (root != null)
    {
        string journalPath = Path.Combine(UpdateTransaction.RecoveryDirectory(root), "journal.json");
        if (File.Exists(journalPath) && new FileInfo(journalPath).Length < 64 * 1024 * 1024)
        {
            var journal = JsonSerializer.Deserialize<UpdateJournal>(File.ReadAllBytes(journalPath), UpdateFormat.Json);
            if (validatedHandoff && launchDidNotStart && journal?.State == "committed")
            {
                using var gate = new FileStream(Path.Combine(root, ".smap-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                using var running = new FileStream(Path.Combine(root, ".smap-running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                UpdateTransaction.Recover(root, UpdateTransaction.RecoveryDirectory(root), journal, UpdateTrust.Keys);
                journal = journal with { State = "rolledBack" };
            }
            if (validatedHandoff && journal?.State == "rolledBack") Process.Start(new ProcessStartInfo(UpdateFormat.Under(root, "SMAP.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root });
        }
    }
    return 1;
}
