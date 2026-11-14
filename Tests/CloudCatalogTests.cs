using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SMAP_WPF;
using Xunit;

namespace SMAP.Tests;

public sealed class CloudCatalogTests
{
    static readonly Guid Work = Guid.NewGuid(), Version = Guid.NewGuid();
    static object Native(bool stats = true) => new { kind = "work", id = (int?)null, work_id = Work, version_id = Version, title = "Native", downloads = 3, likes = 2, statistics_available = stats, download_url = "/api/smap/v1/project.php?id=" + Version };
    static string Catalog(params object[] items) => JsonSerializer.Serialize(new { status = "ok", catalog_version = 1, total = items.Length, pages = 1, items });
    static CloudTransport Transport() => new(new Uri("https://fixture.invalid/"));

    [Fact]
    public void MixedCatalogPreservesIndependentIdentitiesAndEditTargets()
    {
        using var transport = Transport();
        var result = CloudApi.ParseCatalog(Catalog(new { id = 15, kind = "legacy", title = "Old", downloads = 9, likes = 1 }, Native()), transport);
        Assert.True(result.IncludesNative); Assert.Equal(2, result.Items.Count);
        Assert.Equal(new SongMetadataTarget("legacy", "15"), result.Items[0].MetadataTarget);
        Assert.Equal(new SongMetadataTarget("work", Work.ToString()), result.Items[1].MetadataTarget);
        Assert.NotEqual(result.Items[0].CatalogKey, result.Items[1].CatalogKey);
        Assert.True(result.Items[1].IsNative); Assert.Equal(Version, result.Items[1].VersionId);
        Assert.Equal("↓3  ♥2", result.Items[1].DownloadsText);
    }

    [Fact]
    public void StatisticsUnavailableIsNotShownAsZero()
    {
        using var transport = Transport();
        var result = CloudApi.ParseCatalog(Catalog(Native(false)), transport);
        Assert.Equal("↓—  ♥—", result.Items[0].DownloadsText);
    }

    [Fact]
    public void LegacyOnlyServerIsExplicitlyDistinguishable()
    {
        using var transport = Transport();
        var result = CloudApi.ParseCatalog("{\"status\":\"ok\",\"total\":1,\"pages\":1,\"items\":[{\"id\":15,\"title\":\"Old\"}]}", transport);
        Assert.True(result.Ok); Assert.False(result.IncludesNative); Assert.False(result.Items[0].IsNative);
    }

    [Theory]
    [InlineData("{\"kind\":\"work\",\"work_id\":\"invalid\",\"version_id\":\"invalid\"}")]
    [InlineData("{\"kind\":\"legacy\",\"id\":0}")]
    [InlineData("{\"kind\":\"unknown\",\"id\":15}")]
    public void InvalidIdentityCannotSilentlyUseLegacyRoute(string item)
    {
        using var transport = Transport();
        Assert.Throws<InvalidDataException>(() => CloudApi.ParseCatalog("{\"status\":\"ok\",\"catalog_version\":1,\"items\":[" + item + "]}", transport));
    }

    [Fact]
    public void DownloadAndLikeChangesNotifyBoundCounterText()
    {
        var sheet = new CloudSheet(); var names = new List<string?>();
        sheet.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        sheet.Downloads = 1; sheet.Likes = 2;
        Assert.Equal(2, names.Count(n => n == nameof(sheet.DownloadsText)));
        Assert.Equal("↓1  ♥2", sheet.DownloadsText);
    }

    [Fact]
    public async Task ClientRequestsOptInAndKeepsSearchAndPaging()
    {
        using var handler = new ResponseHandler(Catalog(Native())); using var transport = new CloudTransport(new Uri("https://fixture.invalid/"), handler);
        var result = await CloudApi.ListAsync("合奏 & test", "downloads", 4, 2, 20, transport);
        Assert.True(result.Ok); Assert.True(result.IncludesNative);
        Assert.Contains("include_native=1", handler.Uri!.Query); Assert.Contains("page=2", handler.Uri.Query);
        Assert.Contains("difficulty=4", handler.Uri.Query); Assert.Contains("sort=downloads", handler.Uri.Query);
        Assert.Contains(Uri.EscapeDataString("合奏 & test"), handler.Uri.Query);
    }

    [Theory]
    [InlineData("<html>gateway failure</html>")]
    [InlineData("{broken")]
    public async Task InvalidCatalogFailsWithoutPublishingOrDownloading(string body)
    {
        using var handler = new ResponseHandler(body); using var transport = new CloudTransport(new Uri("https://fixture.invalid/"), handler);
        Assert.False((await CloudApi.ListAsync("", "newest", 0, 1, 20, transport)).Ok);
    }

    [Fact]
    public async Task LegacyRepeatedDownloadsNeverOverwriteLocalEdits()
    {
        const string body = "[{\"name\":\"Old\",\"songNotes\":[{\"time\":0,\"key\":\"1Key0\"}]}]";
        using var handler = new ResponseHandler(body); using var transport = new CloudTransport(new Uri("https://fixture.invalid/"), handler);
        string directory = Path.Combine(Path.GetTempPath(), "smap-catalog-" + Guid.NewGuid().ToString("N"));
        var sheet = new CloudSheet { Id = 15, Title = "Old", DownloadUrl = "/legacy.txt" }; var store = new NativeSharingStore(Path.Combine(directory, "links"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Old.txt"), "Local edit A");
            File.WriteAllText(Path.Combine(directory, "Old_15_1.txt"), "Local edit B");
            string? error = null;
            string? first = await CloudApi.DownloadAsync(sheet, directory, e => error = e, transport, store);
            string? repeated = await CloudApi.DownloadAsync(sheet, directory, e => error = e, transport, store);
            Assert.Null(error); Assert.NotNull(first); Assert.Equal(first, repeated);
            Assert.Equal("Local edit A", File.ReadAllText(Path.Combine(directory, "Old.txt")));
            Assert.Equal("Local edit B", File.ReadAllText(Path.Combine(directory, "Old_15_1.txt")));
            Assert.Equal(body, File.ReadAllText(first));
        }
        finally { Directory.Delete(directory, true); }
    }

    sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Uri = request.RequestUri; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }); }
    }
}
