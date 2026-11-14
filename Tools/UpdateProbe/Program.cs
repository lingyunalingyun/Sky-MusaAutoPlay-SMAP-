using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SMAP_WPF.Updates;

// Synthetic process test only. This tool is excluded from the product build and never reads real accounts.
string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
string root = AppContext.BaseDirectory;
if (args.Length == 0)
{
    File.WriteAllText(Path.Combine(root, "restart-marker.txt"), version); return 0;
}
if (args.Length == 2 && args[0] == "--smap-update-health")
{
    UpdateHealth.Confirm(root, version.Split('+')[0], args[1], UpdateTrust.Keys);
    File.WriteAllText(Path.Combine(root, "restart-marker.txt"), version); return 0;
}
if (args.Length != 1) throw new ArgumentException("Expected synthetic fixture configuration.");
using var doc = JsonDocument.Parse(File.ReadAllBytes(args[0])); var c = doc.RootElement;
if (c.GetProperty("syntheticOnly").GetBoolean() != true) throw new InvalidDataException("Synthetic fixture required.");
string target = c.GetProperty("root").GetString()!, staging = c.GetProperty("staging").GetString()!, manifestPath = c.GetProperty("manifest").GetString()!;
byte[] signed = File.ReadAllBytes(manifestPath); string tag = c.GetProperty("tag").GetString()!;
var manifest = UpdateFormat.Verify(signed, UpdateTrust.Keys, "alpha", tag);
if (c.GetProperty("mode").GetString() == "crash")
{
    UpdateTransaction.Apply(target, staging, signed, UpdateTrust.Keys, "alpha", tag, i => { if (i == 0) Environment.Exit(23); });
    return 3;
}
if (!Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Synthetic process is not running from the target installation.");
string worker = c.GetProperty("worker").GetString()!;
var helper = manifest.Files.Single(f => f.Path == "SMAP.Updater.exe");
if (!UpdatePlanning.Matches(worker, helper.Size, helper.Sha256)) throw new InvalidDataException("Worker hash mismatch.");
string nonce = Guid.NewGuid().ToString("N");
var start = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true };
foreach (var value in new[] { root, staging, "alpha", tag, manifestPath, Environment.ProcessId.ToString(), nonce }) start.ArgumentList.Add(value);
using var process = Process.Start(start)!;
string ready = Path.Combine(staging, "handoff-" + nonce + ".ready");
for (int i = 0; i < 300 && !process.HasExited; i++)
{
    if (File.Exists(ready) && File.ReadAllText(ready) == manifest.ReleaseId.ToString())
    { File.WriteAllText(Path.Combine(root, "parent-exit-marker.txt"), version); return 0; }
    Thread.Sleep(100);
}
return 4;
