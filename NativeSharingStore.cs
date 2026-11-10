using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SMAP_WPF;

public sealed record NativeCloudLink(string Origin, Guid WorkId, Guid VersionId, string PayloadSha256, string LocalFile);
public sealed record PendingPublication(Guid CreateKey, Guid PublishKey, int ActorUserId, SharingWork? Work,
    string Title, string Artist, string TranscribedBy, string Description, string Visibility,
    List<SharingSource> Sources, string PayloadSha256);

// Cloud relations stay outside project.json and SVG/PNG recovery envelopes.
public sealed class NativeSharingStore(string directory)
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "NativeSharing");
    string PendingPath(int actor) => Path.Combine(directory, $"pending-{actor}.json");
    string PayloadPath(Guid id) => Path.Combine(directory, "pending-" + id.ToString("N") + ".smap");
    string LinkPath(string file) => Path.Combine(directory, "link-" + Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant()))) + ".json");
    public NativeCloudLink? Find(string file, Uri origin)
    {
        string path = LinkPath(file); if (!File.Exists(path) || new FileInfo(path).Length > 16384 || !File.Exists(file) || new FileInfo(file).Length > Core.SmapFile.MaxArchiveBytes) return null;
        var link = JsonSerializer.Deserialize<NativeCloudLink>(File.ReadAllBytes(path), NativeSharingClient.Json);
        if (link == null || link.Origin != origin.AbsoluteUri || !link.LocalFile.Equals(Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)
            || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))) != link.PayloadSha256) return null;
        return link;
    }
    static void Atomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public PendingPublication? Pending(int actor)
    {
        var path = PendingPath(actor); if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Invalid pending publication.");
        var draft = JsonSerializer.Deserialize<PendingPublication>(File.ReadAllBytes(path), NativeSharingClient.Json);
        if (draft == null || draft.ActorUserId != actor || draft.CreateKey == Guid.Empty || draft.PublishKey == Guid.Empty) throw new InvalidDataException("Invalid pending publication.");
        return draft;
    }
    public PendingPublication Stage(int actor, SharingWork? work, byte[] payload, string title, string artist, string transcriber,
        string description, string visibility, List<SharingSource> sources)
    {
        if (actor <= 0) throw new SharingException("AUTH_REQUIRED", 401);
        if (Pending(actor) != null) throw new SharingException("PENDING_PUBLICATION");
        var draft = new PendingPublication(Guid.NewGuid(), Guid.NewGuid(), actor, work, title, artist, transcriber, description,
            visibility, sources, Convert.ToHexStringLower(SHA256.HashData(payload)));
        Atomic(PayloadPath(draft.CreateKey), payload); Save(draft); return draft;
    }
    public void Save(PendingPublication draft) => Atomic(PendingPath(draft.ActorUserId), JsonSerializer.SerializeToUtf8Bytes(draft, NativeSharingClient.Json));
    public byte[] Payload(PendingPublication draft)
    {
        var file = new FileInfo(PayloadPath(draft.CreateKey)); if (file.Length > Core.SmapFile.MaxArchiveBytes) throw new InvalidDataException();
        var bytes = File.ReadAllBytes(file.FullName);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != draft.PayloadSha256) throw new SharingException("INTEGRITY_MISMATCH");
        return bytes;
    }
    public void Complete(PendingPublication draft)
    {
        // Successful server publication is confirmed before the retry state can be removed.
        try { File.Delete(PendingPath(draft.ActorUserId)); File.Delete(PayloadPath(draft.CreateKey)); }
        catch (IOException) { /* Repeating this retained request returns the same immutable version. */ }
        catch (UnauthorizedAccessException) { }
    }
    public async Task<SharingVersion> PublishAsync(NativeSharingClient client, PendingPublication draft, CancellationToken cancellation)
    {
        if (draft.Work == null)
        {
            var work = await client.CreateWorkAsync(draft.CreateKey, cancellation);
            draft = draft with { Work = work }; Save(draft);
        }
        byte[] payload = await Task.Run(() => Payload(draft), cancellation);
        var version = await client.PublishAsync(draft.Work, payload, draft.Title, draft.Artist, draft.TranscribedBy, draft.Description,
            draft.Visibility, draft.Sources, draft.PublishKey, cancellation);
        Complete(draft); return version;
    }
    public string Import(SharingDownload download, string songsDirectory)
    {
        Directory.CreateDirectory(songsDirectory); NativeSharingClient.ValidateVersion(download.Version);
        if (Convert.ToHexStringLower(SHA256.HashData(download.Payload)) != download.Version.PayloadSha256) throw new SharingException("INTEGRITY_MISMATCH");
        string name = string.Concat(download.Version.Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (name.Length > 48) name = string.Concat(name.EnumerateRunes().Take(40).Select(r => r.ToString())); if (name.Length == 0) name = "SMAP";
        string path = Path.Combine(songsDirectory, name + "-" + download.Version.VersionId.ToString("N") + ".smap");
        if (File.Exists(path) && (new FileInfo(path).Length > Core.SmapFile.MaxArchiveBytes || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) != download.Version.PayloadSha256))
            path = Path.Combine(songsDirectory, name + "-" + Guid.NewGuid().ToString("N") + ".smap");
        if (!File.Exists(path))
        {
            string temp = Path.Combine(songsDirectory, ".share-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(download.Payload); file.Flush(true); }
                // A concurrent editor/import may have created the target; never replace it.
                if (File.Exists(path)) path = Path.Combine(songsDirectory, name + "-" + Guid.NewGuid().ToString("N") + ".smap");
                File.Move(temp, path, false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        var link = new NativeCloudLink(download.Origin.AbsoluteUri, download.Version.WorkId, download.Version.VersionId, download.Version.PayloadSha256, Path.GetFullPath(path));
        Atomic(LinkPath(path), JsonSerializer.SerializeToUtf8Bytes(link, NativeSharingClient.Json)); return path;
    }
}
