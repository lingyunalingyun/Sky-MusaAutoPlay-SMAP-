using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SMAP_WPF.Updates;
using Xunit;

namespace SMAP.Tests;

public sealed class DeltaUpdateTests
{
    sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult(reply(request)); }
    }
    sealed class InlineProgress(Action<UpdateProgress> action) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => action(value); }
    sealed class Fixture : IDisposable
    {
        public readonly TestProject Temp = new();
        public readonly ECDsa Signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public UpdateKey[] Keys => [new("synthetic", Convert.ToBase64String(Signer.ExportSubjectPublicKeyInfo()), "alpha")];
        public string Source => Temp.PathFor("published");
        public string Root => Temp.PathFor("installed");
        public string Packs => Temp.PathFor("packs");
        public Fixture()
        {
            Directory.CreateDirectory(Source); Directory.CreateDirectory(Root);
            var bytes = new byte[3 * 1024 * 1024]; new Random(42).NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(Source, "SMAP.exe"), bytes);
            File.WriteAllText(Path.Combine(Source, "SMAP.Updater.exe"), "synthetic updater; never executed");
            foreach (var f in Directory.GetFiles(Source)) File.Copy(f, Path.Combine(Root, Path.GetFileName(f)));
        }
        public UpdateManifest Build() => UpdateBuilder.Build(Source, Packs, "alpha", "3.0.0-alpha.update.2");
        public byte[] Signed(UpdateManifest m) => UpdateBuilder.Sign(m, "synthetic", Signer);
        public Reply Network(bool fullResponse = false, bool corrupt = false, bool wrongRange = false)
            => new(req =>
            {
                Assert.Equal("https", req.RequestUri!.Scheme); Assert.Equal("github.com", req.RequestUri.Host);
                Assert.Null(req.Headers.Authorization);
                var data = File.ReadAllBytes(Path.Combine(Packs, req.RequestUri.Segments.Last()));
                var range = req.Headers.Range!.Ranges.Single(); int start = (int)range.From!, end = (int)range.To!;
                var bytes = fullResponse ? data : data.AsSpan(start, end - start + 1).ToArray(); if (corrupt) bytes[0] ^= 1;
                var response = new HttpResponseMessage(fullResponse ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                if (!fullResponse) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start + (wrongRange ? 1 : 0), end, data.Length);
                return response;
            });
        public void Change()
        {
            string path = Path.Combine(Source, "SMAP.exe"); var data = File.ReadAllBytes(path);
            File.WriteAllBytes(path, data[..100000].Concat(Encoding.UTF8.GetBytes("inserted target change")).Concat(data[100000..]).ToArray());
        }
        public void Dispose() { Signer.Dispose(); Temp.Dispose(); }
    }

    [Fact]
    public void UnchangedReleaseNeedsNoDownload()
    {
        using var f = new Fixture(); var m = f.Build(); var plan = UpdatePlanning.Plan(m, f.Root);
        Assert.Empty(plan.ChangedFiles); Assert.Equal(0, plan.DownloadBytes); Assert.Equal(m.Files.Sum(x => x.Size), plan.ReusedBytes);
    }
    [Fact]
    public void RecoveryIdentityDoesNotDependOnDirectoryTrailingSeparator()
    {
        using var f = new Fixture();
        Assert.Equal(UpdateTransaction.RecoveryDirectory(f.Root), UpdateTransaction.RecoveryDirectory(f.Root + Path.DirectorySeparatorChar));
    }
    [Fact]
    public void ContentDefinedChunksReuseDataAfterInsertion()
    {
        using var f = new Fixture(); f.Change(); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        Assert.True(plan.ReusedBytes > 2 * 1024 * 1024); Assert.InRange(plan.DownloadBytes, 1, 1024 * 1024);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealByteRangeOrFullPackFallbackReconstructsExactFiles(bool full)
    {
        using var f = new Fixture(); f.Change(); var m = f.Build(); var plan = UpdatePlanning.Plan(m, f.Root);
        using var network = f.Network(full); using var remote = new GitHubUpdates(network);
        string cache = f.Temp.PathFor("cache"), staging = f.Temp.PathFor("staged");
        await remote.Download(plan, cache, null, default); int calls = network.Requests;
        await remote.Download(plan, cache, null, default); Assert.Equal(calls, network.Requests);
        await UpdatePlanning.Reconstruct(plan, cache, staging, null, default);
        foreach (var file in plan.ChangedFiles) Assert.Equal(File.ReadAllBytes(Path.Combine(f.Source, file.Path)), File.ReadAllBytes(Path.Combine(staging, file.Path)));
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CorruptContentAndIncorrectRangesAreRejected(bool corrupt, bool wrongRange)
    {
        using var f = new Fixture(); f.Change(); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        using var network = f.Network(corrupt: corrupt, wrongRange: wrongRange); using var remote = new GitHubUpdates(network);
        await Assert.ThrowsAsync<InvalidDataException>(() => remote.Download(plan, f.Temp.PathFor("cache"), null, default));
    }
    [Fact]
    public async Task CorruptCachedBlocksAreDownloadedAgain()
    {
        using var f = new Fixture(); f.Change(); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        using var network = f.Network(); using var remote = new GitHubUpdates(network); string cache = f.Temp.PathFor("cache");
        await remote.Download(plan, cache, null, default); int calls = network.Requests;
        File.WriteAllText(UpdatePlanning.CachePath(cache, plan.RequiredChunks[0].Sha256), "damaged cached block");
        await remote.Download(plan, cache, null, default); Assert.True(network.Requests > calls);
    }
    [Fact]
    public void SignatureTamperingWrongKeyAndWrongChannelFailClosed()
    {
        using var f = new Fixture(); var m = f.Build(); byte[] signed = f.Signed(m);
        Assert.Equal(m.ReleaseId, UpdateFormat.Verify(signed, f.Keys, "alpha", m.Tag).ReleaseId);
        var envelope = JsonSerializer.Deserialize<SignedUpdate>(signed, UpdateFormat.Json)!;
        byte[] tampered = JsonSerializer.SerializeToUtf8Bytes(envelope with { Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")) }, UpdateFormat.Json);
        Assert.Throws<InvalidDataException>(() => UpdateFormat.Verify(tampered, f.Keys, "alpha", m.Tag));
        Assert.Throws<InvalidDataException>(() => UpdateFormat.Verify(signed, [], "alpha", m.Tag));
        Assert.Throws<InvalidDataException>(() => UpdateFormat.Verify(signed, f.Keys, "stable", m.Tag));
        Assert.Throws<InvalidDataException>(() => UpdateFormat.Verify(signed, f.Keys, "alpha", "v9.0.0"));
    }
    [Theory]
    [InlineData("../SMAP.exe")]
    [InlineData("songs/user.smap")]
    [InlineData("settings.json")]
    [InlineData("Assets/../private.json")]
    [InlineData("SMAP.exe:evil")]
    [InlineData("Assets/CON.wav")]
    [InlineData("runtimes/foo.dll ")]
    [InlineData("C:/SMAP.exe")]
    public void UpdatesCannotTargetPrivateDataOrAmbiguousPaths(string path) => Assert.Throws<InvalidDataException>(() => UpdateFormat.ManagedPath(path));
    [Fact]
    public void PublishedDotNetDiagnosticsAreAllowedWithoutAcceptingArbitraryExecutables()
    {
        UpdateFormat.ManagedPath("createdump.exe");
        Assert.Throws<InvalidDataException>(() => UpdateFormat.ManagedPath("unrelated.exe"));
    }
    [Fact]
    public void DuplicateOrOverlappingManifestContentIsRejected()
    {
        using var f = new Fixture(); var m = f.Build();
        Assert.Throws<InvalidDataException>(() => UpdateFormat.Validate(m with { Files = m.Files.Append(m.Files[0]).ToArray() }));
        var file = m.Files[0]; var chunks = file.Chunks.ToArray(); chunks[0] = chunks[0] with { Offset = -1 };
        Assert.Throws<InvalidDataException>(() => UpdateFormat.Validate(m with { Files = [file with { Chunks = chunks }, m.Files[1]] }));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationUpdateAndFailureRollbackNeverReplaceSongsOrSettings(bool fail)
    {
        using var f = new Fixture(); f.Change(); var m = f.Build(); byte[] signed = f.Signed(m);
        Directory.CreateDirectory(Path.Combine(f.Root, "songs")); File.WriteAllText(Path.Combine(f.Root, "songs/user.smap"), "persistent original song");
        File.WriteAllText(Path.Combine(f.Root, "settings.json"), "persistent settings");
        byte[] original = File.ReadAllBytes(Path.Combine(f.Root, "SMAP.exe"));
        var plan = UpdatePlanning.Plan(m, f.Root); using var network = f.Network(); using var remote = new GitHubUpdates(network);
        string staging = f.Temp.PathFor("staged"), cache = f.Temp.PathFor("cache");
        await remote.Download(plan, cache, null, default); await UpdatePlanning.Reconstruct(plan, cache, staging, null, default);
        Action apply = () => UpdateTransaction.Apply(f.Root, staging, signed, f.Keys, "alpha", m.Tag, fail ? i => throw new IOException("Synthetic interruption") : null);
        if (fail) { Assert.Throws<IOException>(apply); Assert.Equal(original, File.ReadAllBytes(Path.Combine(f.Root, "SMAP.exe"))); }
        else { apply(); Assert.Equal(File.ReadAllBytes(Path.Combine(f.Source, "SMAP.exe")), File.ReadAllBytes(Path.Combine(f.Root, "SMAP.exe"))); }
        Assert.Equal("persistent original song", File.ReadAllText(Path.Combine(f.Root, "songs/user.smap")));
        Assert.Equal("persistent settings", File.ReadAllText(Path.Combine(f.Root, "settings.json")));
    }
    [Fact]
    public async Task CancellationRetainsVerifiedChunksForResume()
    {
        using var f = new Fixture(); f.Change(); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        using var network = f.Network(); using var remote = new GitHubUpdates(network); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remote.Download(plan, f.Temp.PathFor("cache"), null, stop.Token));
        Assert.Equal(0, network.Requests); await remote.Download(plan, f.Temp.PathFor("cache"), null, default);
        Assert.True(network.Requests > 0);
    }
    [Fact]
    public async Task MidDownloadPauseKeepsVerifiedBlocksAndResumesMissingDataOnly()
    {
        using var f = new Fixture(); var fresh = new byte[3 * 1024 * 1024]; new Random(88).NextBytes(fresh);
        File.WriteAllBytes(Path.Combine(f.Source, "SMAP.exe"), fresh); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        using var network = f.Network(); using var remote = new GitHubUpdates(network); using var stop = new CancellationTokenSource();
        string cache = f.Temp.PathFor("cache");
        var progress = new InlineProgress(p => { if (p.Completed > 0) stop.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => remote.Download(plan, cache, progress, stop.Token));
        var retained = Directory.GetFiles(cache, "*.chunk"); Assert.NotEmpty(retained); Assert.True(retained.Length < plan.RequiredChunks.Length);
        var before = retained.ToDictionary(p => p, p => File.GetLastWriteTimeUtc(p));
        await remote.Download(plan, cache, null, default);
        Assert.All(before, p => Assert.Equal(p.Value, File.GetLastWriteTimeUtc(p.Key)));
        await UpdatePlanning.Reconstruct(plan, cache, f.Temp.PathFor("stage"), null, default);
        Assert.Equal(fresh, File.ReadAllBytes(Path.Combine(f.Temp.PathFor("stage"), "SMAP.exe")));
    }
    [Fact]
    public async Task StableUpdateDiscoveryCannotSelectAlphaReleases()
    {
        using var f = new Fixture();
        using var response = new Reply(_ => new(HttpStatusCode.OK) { Content = new StringContent("[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v99.0.0-alpha.update.1\",\"assets\":[{\"name\":\"SMAP-stable-manifest.json\"}]}]") });
        using var remote = new GitHubUpdates(response);
        Assert.Null(await remote.Latest("stable", "3.0.0", f.Keys, (_, _) => throw new InvalidOperationException("Alpha must be filtered before version selection."), default)); Assert.Equal(1, response.Requests);
    }
    [Fact]
    public async Task PlaintextAndForeignRedirectsAreRejected()
    {
        using var f = new Fixture(); f.Change(); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        using var response = new Reply(_ => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("http://foreign.invalid/update.pack"); return r; });
        using var remote = new GitHubUpdates(response);
        await Assert.ThrowsAsync<InvalidDataException>(() => remote.Download(plan, f.Temp.PathFor("cache"), null, default)); Assert.Equal(1, response.Requests);
    }
    [Fact]
    public async Task ALocalSourceChangeDuringPreparationCannotReachInstallation()
    {
        using var f = new Fixture(); f.Change(); var plan = UpdatePlanning.Plan(f.Build(), f.Root);
        using var response = f.Network(); using var remote = new GitHubUpdates(response); string cache = f.Temp.PathFor("cache");
        await remote.Download(plan, cache, null, default);
        using (var altered = new FileStream(Path.Combine(f.Root, "SMAP.exe"), FileMode.Open, FileAccess.Write)) { altered.SetLength(1); }
        await Assert.ThrowsAnyAsync<IOException>(() => UpdatePlanning.Reconstruct(plan, cache, f.Temp.PathFor("stage"), null, default));
        Assert.Equal(1, new FileInfo(Path.Combine(f.Root, "SMAP.exe")).Length);
    }
}
