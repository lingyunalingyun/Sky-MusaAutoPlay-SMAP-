using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMAP_WPF;
using SMAP_WPF.Core;

static partial class Program
{
    static async Task MetadataNetwork()
    {
        Directory.CreateDirectory(Output);
        using var state = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Output, "private/state.json")));
        if (!state.RootElement.GetProperty("database").GetString()!.StartsWith("smap_rc_")) throw new InvalidOperationException("Synthetic database required.");
        var origin = new Uri("https://localhost:" + state.RootElement.GetProperty("tlsPort").GetInt32());
        using var transport = new CloudTransport(origin, Handler());
        async Task<string> Token(string name)
        {
            using var reply = await transport.SendSensitiveAsync("/api/game_login.php", new StringContent(JsonSerializer.Serialize(new { username = name, password = state.RootElement.GetProperty("loginPassword").GetString() }), Encoding.UTF8, "application/json"));
            using var document = JsonDocument.Parse(await reply.Content.ReadAsByteArrayAsync());
            if (!reply.IsSuccessStatusCode) throw new InvalidOperationException("Synthetic login rejected.");
            return document.RootElement.GetProperty("user").GetProperty("device_token").GetString()!;
        }
        string? tokenA = null; var a = new NativeSharingClient(transport, () => tokenA);
        // Prepare the WPF image on the STA entry thread before any asynchronous continuation.
        byte[] pixels = Enumerable.Repeat((byte)110, 64 * 64 * 3).ToArray();
        var picture = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Rgb24, null, pixels, 64 * 3);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(picture)); using var image = new MemoryStream(); encoder.Save(image);
        string cover = Convert.ToBase64String(SharingCover.Encode(image.ToArray()));
        tokenA = await Token("s0_alpha");
        string tokenB = await Token("s0_beta"); var b = new NativeSharingClient(transport, () => tokenB);
        await a.RequirePresentationAsync(); Check("LOCAL-TLS-PRESENTATION-CAPABILITY", true);
        var p = Sample(); var track = p.Tracks[0];
        track.Notes.Add(new() { TrackId = track.Id, Beat = 8, LengthBeats = 2, Action = NoteAction.Hold, Key = 3 });
        p = p with { Metadata = p.Metadata with { Cover = cover }, Tempos = [new(0, 500_000), new(4, 1_000_000)], Meters = [new(0, 4, 4), new(6, 6, 8)] };
        string identity = PerformanceIdentity.ForProject(p); byte[] payload = SmapFile.ToBytes(p);
        var presentation = new SharingPresentation { Difficulty = 3, Tags = ["流行", "教学"], CoverSha256 = SharingPresentation.CoverHash(cover) };
        var work = await a.CreateWorkAsync(Guid.NewGuid()); var request = Guid.NewGuid();
        var first = await a.PublishAsync(work, payload, "Metadata first", "Synthetic composer", "Synthetic arranger", "Synthetic introduction", "public", [], request, presentation: presentation);
        Check("LOCAL-PUBLISH-DISPLAY-METADATA", first.Presentation?.Difficulty == 3 && first.Presentation.Tags.SequenceEqual(presentation.Tags));
        Check("LOCAL-SERVER-DERIVED-TEMPO-METER", first.PerformanceInfo is { MinBpm: 60, MaxBpm: 120, TempoCount: 2, DurationSeconds: 8 } info && info.Meters.SequenceEqual(["4/4", "6/8"]));
        var repeated = await a.PublishAsync(work, payload, "Metadata first", "Synthetic composer", "Synthetic arranger", "Synthetic introduction", "public", [], request, presentation: presentation);
        Check("LOCAL-METADATA-IDEMPOTENCY", repeated.VersionId == first.VersionId);
        var resolved = await b.ResolveAsync(first.FormattedCode); var downloaded = await b.DownloadAsync(resolved);
        Check("LOCAL-CODE-DOWNLOAD-COVER-AND-IDENTITY", downloaded.Project.Metadata.Cover == cover && downloaded.Payload.SequenceEqual(payload) && PerformanceIdentity.ForProject(downloaded.Project) == identity);
        var store = new NativeSharingStore(Path.Combine(Output, "private/metadata-import"));
        string local = store.Import(downloaded, Path.Combine(Output, "private/metadata-songs"));
        var edit = new EditorSession(SmapFile.Load(local)); edit.Create(2, 14); SmapFile.Save(edit.Project, local);
        Check("LOCAL-EDIT-PRESERVES-DISPLAY-NOT-TRUSTED-LINK", store.Find(local, origin) == null && store.PresentationFor(local, origin)?.Tags.SequenceEqual(presentation.Tags) == true && SmapFile.Load(local).Tracks.Sum(t => t.Notes.Count) == p.Tracks.Sum(t => t.Notes.Count) + 1);
        var current = await a.GetWorkAsync(work.WorkId);
        var changed = presentation with { Difficulty = 4, Tags = ["改编"] };
        var second = await a.PublishAsync(current, payload, "Metadata second", "Synthetic composer", "Synthetic arranger", "Revised description", "public", [], Guid.NewGuid(), presentation: changed);
        Check("LOCAL-METADATA-NEW-VERSION-SAME-PERFORMANCE", second.VersionId != first.VersionId && second.PerformanceHash == first.PerformanceHash && second.VersionNumber == first.VersionNumber + 1);
        var old = await b.ResolveAsync(first.ShareCode);
        Check("LOCAL-OLD-CODE-OLD-METADATA", old.Title == "Metadata first" && old.Presentation?.Difficulty == 3 && old.Presentation.Tags.SequenceEqual(presentation.Tags));
        Check("LOCAL-LATEST-POINTER", (await a.GetWorkAsync(work.WorkId)).LatestVersionId == second.VersionId);
        bool denied = false;
        try { await b.PublishAsync(await a.GetWorkAsync(work.WorkId), payload, "Attempt", "", "", "", "public", [], Guid.NewGuid(), presentation: presentation); }
        catch (SharingException error) { denied = error.Status == 403; }
        Check("LOCAL-METADATA-NONOWNER-REFUSED", denied);
        bool mismatch = false;
        try { await a.PublishAsync(await a.GetWorkAsync(work.WorkId), payload, "Mismatch", "", "", "", "public", [], Guid.NewGuid(), presentation: presentation with { CoverSha256 = new string('0', 64) }); }
        catch (SharingException error) { mismatch = error.Code == "INTEGRITY_MISMATCH"; }
        Check("LOCAL-MISMATCHED-COVER-REFUSED", mismatch);
        var plain = Sample(); byte[] oldPayload = SmapFile.ToBytes(plain); var plainWork = await a.CreateWorkAsync(Guid.NewGuid());
        var plainVersion = await a.PublishAsync(plainWork, oldPayload, "Old client contract", "", "", "", "public", [], Guid.NewGuid());
        Check("LOCAL-OLD-PUBLISH-CONTRACT", plainVersion.Presentation == null && (await a.DownloadAsync(plainVersion)).Payload.SequenceEqual(oldPayload));
    }
}
