using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SMAP_WPF;
using Xunit;

namespace SMAP.Tests;

public sealed class SongMetadataTests
{
    static readonly string Token = "smap1." + new string('a', 24) + "." + new string('b', 64);
    sealed class Reply(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request); }
    static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, NativeSharingClient.Json), Encoding.UTF8, "application/json") };
    static SongMetadataProfile Profile() => new() { SchemaVersion = 1, Kind = "legacy", Id = "12", Revision = 3, CanEdit = true, Authority = "publisher", OwnerUserId = 1, DescriptionLimit = 500, Metadata = new() { Title = "Original title" } };
    [Fact]
    public async Task SaveWhitelistsOnlyDisplayFieldsAndUsesAuthenticatedBody()
    {
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.DoesNotContain(Token, request.RequestUri!.AbsoluteUri);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(new[] { "coverAction", "id", "kind", "metadata", "revision" }, body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.Equal(new[] { "artist", "description", "difficulty", "tags", "title", "transcribedBy" }, body.RootElement.GetProperty("metadata").EnumerateObject().Select(p => p.Name).Order().ToArray());
            Assert.False(body.RootElement.TryGetProperty("ownerUserId", out _));
            return Json(Profile());
        }));
        var client = new NativeSharingClient(transport, () => Token);
        await client.SaveMetadataAsync(Profile(), Profile().Metadata, "keep", null, Guid.NewGuid());
    }
    [Theory]
    [InlineData("<html>gateway error</html>", "text/html")]
    [InlineData("{", "application/json")]
    [InlineData("null", "application/json")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}", "application/json")]
    public async Task InvalidResponsesFailWithoutBecomingEditable(string response, string type)
    {
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, type) })));
        var client = new NativeSharingClient(transport, () => Token);
        await Assert.ThrowsAsync<SharingException>(() => client.MetadataAsync(new("legacy", "12")));
    }
    [Fact]
    public async Task UntrustedCoverUrlNeverDeterminesRequestHost()
    {
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(request =>
        {
            Assert.Equal("muse.example.invalid", request.RequestUri!.Host);
            Assert.Equal("/api/smap/v1/metadata_cover.php", request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([255, 216, 255, 217]) { Headers = { ContentType = new("image/jpeg") } } });
        }));
        var client = new NativeSharingClient(transport, () => Token);
        await client.MetadataCoverAsync(Profile() with { CoverUrl = "https://untrusted.example.invalid/cookie-collector" });
    }
    [Fact]
    public async Task DisabledCapabilityRemainsCompatibleWithOldServer()
    {
        int requests = 0;
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(_ => { requests++; return Task.FromResult(Json(new { schemaVersion = 1 })); }));
        var client = new NativeSharingClient(transport, () => null);
        Assert.False(await client.MetadataSupportedAsync()); Assert.False(await client.MetadataSupportedAsync()); Assert.Equal(1, requests);
    }
    [Fact]
    public async Task TemporaryCapabilityFailureCanBeRetried()
    {
        int requests = 0;
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(_ =>
            Task.FromResult(++requests == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Unavailable") } : Json(new { metadataEditingVersion = 1 }))));
        var client = new NativeSharingClient(transport, () => null);
        await Assert.ThrowsAsync<SharingException>(() => client.MetadataSupportedAsync());
        Assert.True(await client.MetadataSupportedAsync()); Assert.Equal(2, requests);
    }
    [Fact]
    public async Task OutOfRangeAuditTimestampIsRejectedBeforeGuiFormatting()
    {
        var value = Profile().Metadata;
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(_ => Task.FromResult(Json(new { items = new[] { new SongMetadataRevision(1, 1, "Synthetic", "publisher", long.MaxValue, value, value) }, nextBefore = (int?)null }))));
        var client = new NativeSharingClient(transport, () => Token);
        await Assert.ThrowsAsync<SharingException>(() => client.MetadataHistoryAsync(new("legacy", "12")));
    }
    [Fact]
    public async Task OverlongLegacyDescriptionNeverGetsSilentlyTruncated()
    {
        using var transport = new CloudTransport(new Uri("https://muse.example.invalid/"), new Reply(_ => throw new InvalidOperationException("Invalid input must not be sent.")));
        var client = new NativeSharingClient(transport, () => Token);
        await Assert.ThrowsAsync<SharingException>(() => client.SaveMetadataAsync(Profile(), Profile().Metadata with { Description = new string('a', 501) }, "keep", null, Guid.NewGuid()));
    }
}
