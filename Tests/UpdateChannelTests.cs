using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SMAP_WPF;
using SMAP_WPF.Updates;
using Xunit;

namespace SMAP.Tests;

public sealed class UpdateChannelTests
{
    [Theory]
    [InlineData("3.0.0", "stable")]
    [InlineData("3.0.0-rc.2", "stable")]
    [InlineData("3.0.0-alpha.update.6", "alpha")]
    public void ExistingBuildDefaultsArePreserved(string version, string expected)
        => Assert.Equal(expected, UpdateChannelSettings.DefaultFor(version));

    [Theory]
    [InlineData("stable")]
    [InlineData("alpha")]
    public void PreferenceSurvivesRestartWithoutChangingOtherFiles(string channel)
    {
        using var temp = new TestProject(); string path = temp.PathFor("settings/update-channel.txt");
        string song = temp.PathFor("song.txt"); File.WriteAllText(song, "original song");
        UpdateChannelSettings.Save(path, channel);
        Assert.Equal(channel, UpdateChannelSettings.Load(path, channel == "stable" ? "alpha" : "stable"));
        Assert.Equal("original song", File.ReadAllText(song));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        using var service = new DeltaUpdateService(UpdateChannelSettings.Load(path, "stable"));
        Assert.Equal(channel, service.Channel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("preview")]
    [InlineData("ALPHA")]
    [InlineData("stable\nalpha")]
    public void InvalidStoredPreferenceUsesSafeFallback(string content)
    {
        using var temp = new TestProject(); string path = temp.PathFor("channel.txt"); File.WriteAllText(path, content);
        Assert.Equal("stable", UpdateChannelSettings.Load(path, "stable"));
        Assert.Throws<ArgumentException>(() => UpdateChannelSettings.Save(path, content));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void MissingOrOversizedPreferenceUsesFallback()
    {
        using var temp = new TestProject(); string path = temp.PathFor("channel.txt");
        Assert.Equal("stable", UpdateChannelSettings.Load(path, "stable"));
        File.WriteAllText(path, new string('a', 100));
        Assert.Equal("stable", UpdateChannelSettings.Load(path, "stable"));
    }

    [Fact]
    public void FailedPreferenceWriteDoesNotLeavePartialFiles()
    {
        using var temp = new TestProject(); string path = temp.PathFor("channel.txt"); Directory.CreateDirectory(path);
        var error = Record.Exception(() => UpdateChannelSettings.Save(path, "alpha"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Empty(Directory.GetFiles(temp.DirectoryPath));
    }

    [Fact]
    public async Task ChangingChannelDiscardsPendingInstallButRetainsDownloadCache()
    {
        using var temp = new TestProject(); string cache = temp.PathFor("verified.chunk"); File.WriteAllText(cache, "retained");
        using var service = new DeltaUpdateService("alpha");
        var manifest = new UpdateManifest(1, "SMAP", "alpha", "3.0.0-alpha.2", "v3.0.0-alpha.2", Guid.NewGuid(), ContentChunks.Algorithm, [], []);
        var pending = new UpdatePlan(manifest, temp.DirectoryPath, [], [], [], 10, 2);
        typeof(DeltaUpdateService).GetProperty(nameof(DeltaUpdateService.Plan))!.SetValue(service, pending);
        typeof(DeltaUpdateService).GetProperty(nameof(DeltaUpdateService.Progress))!.SetValue(service, new UpdateProgress("reconstruct", 1, 1));
        typeof(DeltaUpdateService).GetProperty(nameof(DeltaUpdateService.State))!.SetValue(service, DeltaUpdateState.Ready);
        service.ChangeChannel("stable");
        Assert.Equal("stable", service.Channel); Assert.Equal(DeltaUpdateState.Idle, service.State);
        Assert.Null(service.Plan); Assert.Null(service.Progress);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareRestart());
        Assert.Equal("retained", File.ReadAllText(cache));
    }

    [Theory]
    [InlineData("_running")]
    [InlineData("_handoffPreparing")]
    public void ChannelCannotChangeDuringAnOperation(string operation)
    {
        using var service = new DeltaUpdateService("alpha");
        typeof(DeltaUpdateService).GetField(operation, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, true);
        Assert.False(service.CanChangeChannel);
        Assert.Throws<InvalidOperationException>(() => service.ChangeChannel("stable"));
        Assert.Equal("alpha", service.Channel);
    }

    [Fact]
    public void InvalidChannelNeverReplacesSelectedChannel()
    {
        using var service = new DeltaUpdateService("stable");
        Assert.Throws<ArgumentException>(() => service.ChangeChannel("other")); Assert.Equal("stable", service.Channel);
    }

    sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; return Task.FromResult(response(request)); }
    }

    [Fact]
    public async Task InstallerFallbackUsesStableRegardlessOfRunningBuild()
    {
        object Release(string tag, bool pre = false, bool draft = false) => new
        {
            tag_name = "v" + tag, prerelease = pre, draft,
            assets = new[] { new { name = "SMAP-Setup.exe", browser_download_url = "https://github.com/langhinga/Sky-MusaAutoPlay-SMAP-/releases/download/v" + tag + "/SMAP-Setup.exe", size = 10 } }
        };
        var releases = new[] { Release("99.0.0-rc.1", true), Release("98.0.0-beta.1"), Release("100.0.0", draft: true), Release("4.0.0") };
        using var http = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(releases)) }));
        var release = await UpdateChecker.CheckAsync(http, reportErrors: true);
        Assert.Equal("4.0.0", release?.Tag);
    }

    [Fact]
    public async Task InstallerHttpFailureCannotBeReportedAsLatest()
    {
        using var http = new HttpClient(new Reply(_ => new(HttpStatusCode.ServiceUnavailable)));
        await Assert.ThrowsAsync<HttpRequestException>(() => UpdateChecker.CheckAsync(http, reportErrors: true));
    }

    [Fact]
    public async Task StableSelectionDoesNotDowngradeToOlderInstaller()
    {
        var releases = new[] { new { tag_name = "v2.2", prerelease = false, draft = false, assets = new[] { new { name = "SMAP-Setup.exe", browser_download_url = "https://github.com/langhinga/Sky-MusaAutoPlay-SMAP-/releases/download/v2.2/SMAP-Setup.exe", size = 10 } } } };
        using var http = new HttpClient(new Reply(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(releases)) }));
        Assert.Null(await UpdateChecker.CheckAsync(http, reportErrors: true));
    }

    [Theory]
    [InlineData("stable", "2.0.0", "3.0.0")]
    [InlineData("alpha", "2.0.0", "3.1.0-rc.1")]
    [InlineData("stable", "4.0.0", null)]
    [InlineData("alpha", "4.0.0-alpha.1", null)]
    public async Task ChosenChannelControlsReleaseAndSignatureSelection(string channel, string current, string? expected)
    {
        using var temp = new TestProject(); using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string source = temp.PathFor("source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "SMAP.exe"), "synthetic app");
        File.WriteAllText(Path.Combine(source, "SMAP.Updater.exe"), "synthetic updater");
        var keys = new[] { "alpha", "stable" }.Select(c => new UpdateKey("synthetic-" + c, Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()), c)).ToArray();
        var signatures = new Dictionary<string, byte[]>();
        var releases = new List<object>();
        foreach (var (version, releaseChannel) in new[] { ("3.0.0", "stable"), ("3.1.0-alpha.2", "alpha"), ("3.1.0-beta.1", "alpha"), ("3.1.0-rc.1", "alpha") })
        {
            var manifest = UpdateBuilder.Build(source, temp.PathFor(version), releaseChannel, version);
            signatures[version] = UpdateBuilder.Sign(manifest, "synthetic-" + releaseChannel, signer);
            releases.Add(new { draft = false, prerelease = releaseChannel == "alpha", tag_name = "v" + version, assets = new[] { new { name = "SMAP-" + releaseChannel + "-manifest.json" } } });
        }
        releases.Add(new { draft = true, prerelease = true, tag_name = "v9.0.0-alpha.1", assets = new[] { new { name = "SMAP-alpha-manifest.json" } } });
        var response = new Reply(request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            byte[] bytes;
            if (request.RequestUri.Host == "api.github.com") bytes = JsonSerializer.SerializeToUtf8Bytes(releases);
            else
            {
                Assert.NotNull(expected);
                Assert.Equal(GitHubUpdates.Asset("v" + expected, "SMAP-" + channel + "-manifest.json"), request.RequestUri);
                bytes = signatures[expected!];
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        });
        using var remote = new GitHubUpdates(response);
        var selected = await remote.Latest(channel, current, keys, UpdateChecker.Compare, default);
        Assert.Equal(expected, selected?.Manifest.Version);
        Assert.Equal(expected == null ? 1 : 2, response.Requests);
        if (selected != null) Assert.Throws<InvalidDataException>(() => UpdateFormat.Verify(selected.Value.Signed, keys, channel == "alpha" ? "stable" : "alpha", selected.Value.Manifest.Tag));
    }
}
