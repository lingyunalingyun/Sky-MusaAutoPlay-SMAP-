using System.Diagnostics;
using System.IO;
using SMAP_WPF.Updates;

namespace SMAP_WPF;

public enum DeltaUpdateState { Idle, Checking, Planning, Downloading, Paused, Reconstructing, Ready, Current, Error }

public sealed class DeltaUpdateService : IDisposable
{
    public event Action? Changed;
    public event Action? Found;
    public event Action? Ready;
    public DeltaUpdateState State { get; private set; }
    public UpdatePlan? Plan { get; private set; }
    public UpdateProgress? Progress { get; private set; }
    public string? Version => Plan?.Manifest.Version;
    public double BytesPerSecond { get; private set; }
    public string Channel => UpdateChecker.AppVersion.Contains("-alpha.", StringComparison.Ordinal) ? "alpha" : "stable";
    public bool Supported => UpdateTrust.Keys.Any(k => k.Channel == Channel);
    readonly GitHubUpdates _remote = new();
    readonly string _root = AppContext.BaseDirectory;
    readonly string _working = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SMAP", "updates");
    CancellationTokenSource? _operation;
    bool _running, _disposed;
    byte[]? _signed;
    string? _staging, _releaseDirectory;
    readonly Stopwatch _clock = new();
    long _lastWire;
    double _lastTick;
    void Set(DeltaUpdateState state) { State = state; Changed?.Invoke(); }
    public void Pause() => _operation?.Cancel();

    public async Task Start(bool manual = false)
    {
        if (_disposed || !Supported || _running || State == DeltaUpdateState.Ready) return;
        _running = true; _operation?.Dispose(); _operation = new(); var cancellation = _operation.Token;
        try
        {
            Set(DeltaUpdateState.Checking);
            var release = await _remote.Latest(Channel, UpdateChecker.AppVersion, UpdateTrust.Keys, UpdateChecker.Compare, cancellation);
            if (release == null) { Set(DeltaUpdateState.Current); return; }
            string recovery = Path.Combine(UpdateTransaction.RecoveryDirectory(_root), "journal.json");
            if (!manual && File.Exists(recovery) && new FileInfo(recovery).Length < 64 * 1024 * 1024)
            {
                var failed = System.Text.Json.JsonSerializer.Deserialize<UpdateJournal>(await File.ReadAllBytesAsync(recovery, cancellation), UpdateFormat.Json);
                if (failed is { State: "rolledBack" } && failed.Tag == release.Value.Manifest.Tag) { Set(DeltaUpdateState.Error); return; }
            }
            _signed = release.Value.Signed; Set(DeltaUpdateState.Planning);
            Plan = await Task.Run(() => UpdatePlanning.Plan(release.Value.Manifest, _root, cancellation), cancellation);
            UpdateFormat.RejectLinks(_working); Directory.CreateDirectory(_working);
            _releaseDirectory = Path.Combine(_working, "releases", Plan.Manifest.ReleaseId.ToString("N"));
            _staging = Path.Combine(_releaseDirectory, "staging"); UpdateFormat.RejectLinks(_staging); Directory.CreateDirectory(_staging);
            string cache = Path.Combine(_working, "chunks");
            long changed = Plan.ChangedFiles.Sum(f => f.Size), helper = Plan.Manifest.Files.Single(f => f.Path == "SMAP.Updater.exe").Size;
            if (new DriveInfo(Path.GetPathRoot(_working)!).AvailableFreeSpace < changed + Plan.DownloadBytes + helper + 32 * 1024 * 1024
                || new DriveInfo(Path.GetPathRoot(_root)!).AvailableFreeSpace < changed + 16 * 1024 * 1024)
                throw new IOException("Insufficient space to prepare an update safely.");
            await File.WriteAllBytesAsync(Path.Combine(_releaseDirectory, "manifest.json"), _signed, cancellation);
            Found?.Invoke(); _clock.Restart(); _lastTick = 0; _lastWire = 0; BytesPerSecond = 0;
            var progress = new Progress<UpdateProgress>(p =>
            {
                if (_disposed) return;
                Progress = p; double now = _clock.Elapsed.TotalSeconds;
                if (p.Stage == "download" && now - _lastTick >= .15)
                { BytesPerSecond = Math.Max(0, p.NetworkBytes - _lastWire) / (now - _lastTick); _lastWire = p.NetworkBytes; _lastTick = now; Changed?.Invoke(); }
                else if (p.Stage != "download" || p.Completed == p.Total) Changed?.Invoke();
            });
            Set(DeltaUpdateState.Downloading);
            await Task.Run(() => _remote.Download(Plan, cache, progress, cancellation), cancellation);
            Set(DeltaUpdateState.Reconstructing);
            await Task.Run(() => UpdatePlanning.Reconstruct(Plan, cache, _staging, progress, cancellation), cancellation);
            cancellation.ThrowIfCancellationRequested(); Set(DeltaUpdateState.Ready); Ready?.Invoke();
        }
        catch (OperationCanceledException) { if (!_disposed) Set(DeltaUpdateState.Paused); }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or FormatException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
        { Logger.Info("Background update failed: " + error.GetType().Name); if (!_disposed) Set(DeltaUpdateState.Error); }
        finally { _running = false; }
    }

    public async Task<Process> PrepareRestart()
    {
        if (State != DeltaUpdateState.Ready || Plan == null || _signed == null || _staging == null || _releaseDirectory == null)
            throw new InvalidOperationException("Update is not ready.");
        foreach (var other in Process.GetProcessesByName("SMAP"))
        {
            using (other)
                if (other.Id != Environment.ProcessId && !other.HasExited && string.Equals(other.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Another instance of this installation is still running.");
        }
        string nonce = Guid.NewGuid().ToString("N"), directory = Path.Combine(_releaseDirectory, "handoff", nonce);
        string worker = await Task.Run(() =>
        {
            var helper = Plan.Manifest.Files.Single(f => f.Path == "SMAP.Updater.exe");
            string source = UpdateFormat.Under(_staging, helper.Path);
            if (!UpdatePlanning.Matches(source, helper.Size, helper.Sha256)) source = UpdateFormat.Under(_root, helper.Path);
            if (!UpdatePlanning.Matches(source, helper.Size, helper.Sha256)) throw new InvalidDataException("Updater verification failed.");
            UpdateFormat.RejectLinks(directory); Directory.CreateDirectory(directory); string path = Path.Combine(directory, "SMAP.Updater.exe"); File.Copy(source, path, false);
            if (!UpdatePlanning.Matches(path, helper.Size, helper.Sha256)) throw new InvalidDataException("Updater copy verification failed."); return path;
        });
        var info = new ProcessStartInfo(worker) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        foreach (var arg in new[] { _root, _staging, Channel, Plan.Manifest.Tag, Path.Combine(_releaseDirectory, "manifest.json"), Environment.ProcessId.ToString(), nonce }) info.ArgumentList.Add(arg);
        var process = Process.Start(info) ?? throw new IOException("Updater could not start.");
        string ready = Path.Combine(_staging, "handoff-" + nonce + ".ready");
        for (int i = 0; i < 300 && !process.HasExited; i++)
        {
            if (File.Exists(ready) && await File.ReadAllTextAsync(ready) == Plan.Manifest.ReleaseId.ToString()) return process;
            await Task.Delay(100);
        }
        if (!process.HasExited) process.Kill(); process.Dispose(); throw new IOException("Updater handoff did not complete.");
    }
    public void Dispose() { _disposed = true; _operation?.Cancel(); _remote.Dispose(); }
}
