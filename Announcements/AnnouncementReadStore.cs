using System.IO;
using System.Text.Json;

namespace SMAP_WPF.Announcements;

public sealed class AnnouncementReadStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "announcements-v1.json");
    sealed record State(int SchemaVersion, Dictionary<long, int> Read, Dictionary<long, int> Notified);
    readonly string _path;
    readonly object _gate = new();
    State _state;
    public AnnouncementReadStore(string path) { _path = Path.GetFullPath(path); _state = Load(); }
    State Load()
    {
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length <= 1024 * 1024)
            {
                var state = JsonSerializer.Deserialize<State>(File.ReadAllBytes(_path));
                if (state is { SchemaVersion: 1, Read: not null, Notified: not null } && state.Read.Count <= 10000 && state.Notified.Count <= 10000
                    && state.Read.Concat(state.Notified).All(x => x.Key is > 0 and <= uint.MaxValue && x.Value > 0)) return state;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        return new(1, [], []);
    }
    public bool IsRead(Announcement item) { lock (_gate) return _state.Read.GetValueOrDefault(item.Id) >= item.Revision; }
    public bool WasNotified(Announcement item) { lock (_gate) return _state.Notified.GetValueOrDefault(item.Id) >= item.Revision; }
    public void MarkRead(Announcement item) => Save(item, false);
    public void MarkNotified(Announcement item) => Save(item, true);
    void Save(Announcement item, bool notified)
    {
        if (item.Id is < 1 or > uint.MaxValue || item.Revision < 1) throw new ArgumentException("Invalid announcement identity.");
        lock (_gate)
        {
            var disk = Load(); var read = new Dictionary<long, int>(_state.Read); var notices = new Dictionary<long, int>(_state.Notified);
            foreach (var pair in disk.Read) read[pair.Key] = Math.Max(read.GetValueOrDefault(pair.Key), pair.Value);
            foreach (var pair in disk.Notified) notices[pair.Key] = Math.Max(notices.GetValueOrDefault(pair.Key), pair.Value);
            var values = notified ? notices : read; values[item.Id] = Math.Max(values.GetValueOrDefault(item.Id), item.Revision);
            // Bound local history; old revisions may become unread again after the limit is exceeded.
            Dictionary<long, int> Bound(Dictionary<long, int> source) => source.OrderByDescending(x => x.Key).Take(10000).ToDictionary(x => x.Key, x => x.Value);
            var next = new State(1, Bound(read), Bound(notices));
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!); string pending = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(JsonSerializer.SerializeToUtf8Bytes(next)); file.Flush(true); }
                File.Move(pending, _path, true); _state = next;
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
