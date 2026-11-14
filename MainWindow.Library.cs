using System.IO;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class MainWindow
{
    FileSystemWatcher? _libraryWatcher;
    DispatcherTimer? _libraryRefreshDelay;
    bool _libraryWatchClosed, _libraryScanRunning, _libraryScanAgain;
    int _libraryRefreshVersion;

    void StartLibraryWatcher()
    {
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        _libraryRefreshDelay ??= new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        { Interval = TimeSpan.FromMilliseconds(600) };
        _libraryRefreshDelay.Tick -= LibraryRefreshTick;
        _libraryRefreshDelay.Tick += LibraryRefreshTick;
        try
        {
            Directory.CreateDirectory(SongLibrary.SongsDir);
            var watcher = new FileSystemWatcher(SongLibrary.SongsDir)
            { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            watcher.Created += LibraryFileChanged;
            watcher.Changed += LibraryFileChanged;
            watcher.Deleted += LibraryFileChanged;
            watcher.Renamed += (_, e) =>
            {
                if (IsLibraryFile(e.OldFullPath) || IsLibraryFile(e.FullPath)) QueueLibraryRefresh();
            };
            watcher.Error += (_, _) => QueueLibraryRefresh();
            _libraryWatcher = watcher;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Logger.Exception("Local library watcher", e); }
    }

    static bool IsLibraryFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".json" or ".smap";
    void LibraryFileChanged(object sender, FileSystemEventArgs e)
    { if (IsLibraryFile(e.FullPath)) QueueLibraryRefresh(); }

    void QueueLibraryRefresh()
    {
        if (_libraryWatchClosed || Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_libraryWatchClosed || _libraryRefreshDelay == null) return;
            _libraryRefreshDelay.Stop();
            _libraryRefreshDelay.Start();
        }));
    }

    async void LibraryRefreshTick(object? sender, EventArgs e)
    {
        _libraryRefreshDelay?.Stop();
        await RefreshLibraryFromDisk();
    }

    async Task RefreshLibraryFromDisk()
    {
        if (_libraryWatchClosed) return;
        if (_libraryScanRunning) { _libraryScanAgain = true; return; }
        _libraryScanRunning = true;
        int version = ++_libraryRefreshVersion;
        try
        {
            var songs = await Task.Run(SongLibrary.Scan);
            if (!_libraryWatchClosed && version == _libraryRefreshVersion) ApplyLibrary(songs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { Logger.Exception("Local library refresh", e); }
        finally
        {
            _libraryScanRunning = false;
            if (_libraryScanAgain) { _libraryScanAgain = false; QueueLibraryRefresh(); }
        }
    }

    void StopLibraryWatcher()
    {
        _libraryWatchClosed = true;
        _libraryRefreshDelay?.Stop();
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
    }
}
