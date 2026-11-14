using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SMAP_WPF.Announcements;

public sealed class AnnouncementService : IDisposable
{
    readonly AnnouncementClient _client;
    readonly AnnouncementReadStore _store;
    readonly CancellationTokenSource _lifetime = new();
    readonly HashSet<(long, int)> _shown = [];
    readonly List<Announcement> _items = [];
    int _page;
    bool _disposed;
    public event Action? Changed;
    public IReadOnlyList<Announcement> Items => _items;
    public int Total { get; private set; }
    public bool Busy { get; private set; }
    public bool Failed { get; private set; }
    public bool HasMore => _page < 1000 && _page * 20 < Total;
    public bool HasUnread => _items.Any(x => !_store.IsRead(x));
    public bool IsRead(Announcement item) => _store.IsRead(item);
    public AnnouncementService(AnnouncementClient client, AnnouncementReadStore store) { _client = client; _store = store; }
    public Task Refresh() => Load(true);
    public Task More() => HasMore ? Load(false) : Task.CompletedTask;
    async Task Load(bool reset)
    {
        if (_disposed || Busy) return;
        Busy = true; Failed = false; Changed?.Invoke();
        try
        {
            var page = await _client.List(reset ? 1 : _page + 1, _lifetime.Token);
            if (_disposed) return;
            if (reset) _items.Clear();
            foreach (var item in page.Items) { int at = _items.FindIndex(x => x.Id == item.Id); if (at < 0) _items.Add(item); else _items[at] = item; }
            _page = page.Page; Total = page.Total;
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception error) when (IsReadError(error)) { Failed = true; }
        finally { Busy = false; if (!_disposed) Changed?.Invoke(); }
    }
    public static bool IsReadError(Exception error) => error is HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException or OperationCanceledException;
    public async Task<Announcement> Detail(long id, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token);
        return await _client.Detail(id, linked.Token);
    }
    public async Task MarkRead(Announcement item)
    {
        await Task.Run(() => _store.MarkRead(item));
        if (_disposed) return;
        int at = _items.FindIndex(x => x.Id == item.Id); if (at >= 0 && _items[at].Revision <= item.Revision) _items[at] = item with { Content = null };
        Changed?.Invoke();
    }
    public Announcement? Important() => _items.FirstOrDefault(x => x.Important && !_store.IsRead(x) && !_store.WasNotified(x) && !_shown.Contains((x.Id, x.Revision)));
    public async Task NoticeShown(Announcement item)
    {
        _shown.Add((item.Id, item.Revision));
        try { await Task.Run(() => _store.MarkNotified(item)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Logger.Info("Announcement notification state could not be saved."); }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); _client.Dispose(); _lifetime.Dispose(); }
}
