using System.Net;
using System.Net.Http;
using System.Text;
using SMAP_WPF;
using Xunit;
using SMAP_WPF.Core;
using System.Text.Json;

namespace SMAP.Tests;

public sealed class NativeSharingTests
{
    [Theory]
    [InlineData("8j1h-jzrr-gcp5", "8J1HJZRRGCP5")]
    [InlineData(" 000o-111l-iii1\r\n", "000011111111")]
    public void CodeNormalization(string input, string expected) => Assert.Equal(expected, NativeSharingClient.NormalizeCode(input));

    [Theory]
    [InlineData("")]
    [InlineData("0000-0000-000U")]
    [InlineData("0000-0000-000ſ")]
    [InlineData("００００００００００００")]
    [InlineData("https://foreign.invalid/8J1HJZRRGCP5")]
    public void AmbiguousOrForeignInputsRejected(string value) => Assert.Throws<SharingException>(() => NativeSharingClient.NormalizeCode(value));

    sealed class Reply(HttpStatusCode status, string body, string type = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, type) });
    }
    [Theory]
    [InlineData(HttpStatusCode.OK, "<html>gateway</html>", "text/html")]
    [InlineData(HttpStatusCode.OK, "{broken", "application/json")]
    [InlineData(HttpStatusCode.OK, "{\"versionId\":null,\"versionId\":null}", "application/json")]
    [InlineData(HttpStatusCode.BadGateway, "<html>gateway</html>", "text/html")]
    public async Task HtmlBrokenJsonAndGatewayFailSafely(HttpStatusCode status, string body, string type)
    {
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(status, body, type));
        var client = new NativeSharingClient(transport, () => null);
        await Assert.ThrowsAsync<SharingException>(() => client.ResolveAsync("8J1HJZRRGCP5"));
    }
    [Fact]
    public async Task RedirectsNeverCarryCredentialsToAnotherHost()
    {
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.TemporaryRedirect, ""));
        var client = new NativeSharingClient(transport, () => "smap1." + new string('a', 24) + "." + new string('b', 64));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateWorkAsync(Guid.NewGuid()));
    }
    [Fact]
    public async Task InvalidSessionNotifiesTheCredentialOwner()
    {
        bool invalidated = false;
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.Unauthorized, "{\"code\":\"invalid_session\"}"));
        var client = new NativeSharingClient(transport, () => "smap1." + new string('a', 24) + "." + new string('b', 64), () => invalidated = true);
        var error = await Assert.ThrowsAsync<SharingException>(() => client.CreateWorkAsync(Guid.NewGuid()));
        Assert.Equal(401, error.Status); Assert.True(invalidated);
    }
    [Theory]
    [InlineData("{\"success\":false,\"code\":\"not_found\"}", "application/json")]
    [InlineData("<html>404 Not Found</html>", "text/html")]
    public async Task MissingApiRouteDoesNotBlameAccountPermissions(string body, string type)
    {
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.NotFound, body, type));
        var client = new NativeSharingClient(transport, () => "smap1." + new string('a', 24) + "." + new string('b', 64));
        var error = await Assert.ThrowsAsync<SharingException>(() => client.CreateWorkAsync(Guid.NewGuid()));
        Assert.Equal("SHARING_API_UNAVAILABLE", error.Code);
        Assert.Equal(Lang.Sharing("apiUnavailable"), NativeSharingWindow.ErrorText(error));
    }
    [Theory]
    [InlineData("RESOURCE_NOT_FOUND")]
    [InlineData("SHARE_NOT_FOUND")]
    public async Task MissingPublishedResourceKeepsItsPrivacySafeMessage(string code)
    {
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.NotFound, "{\"status\":\"error\",\"code\":\"" + code + "\"}"));
        var client = new NativeSharingClient(transport, () => null);
        var error = await Assert.ThrowsAsync<SharingException>(() => client.ResolveAsync("8J1HJZRRGCP5"));
        Assert.Equal(code, error.Code);
        Assert.Equal(Lang.Sharing("notFound"), NativeSharingWindow.ErrorText(error));
    }
    [Fact]
    public async Task ActualAuthorizationFailureRemainsForbidden()
    {
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.Forbidden, "{\"status\":\"error\",\"code\":\"FORBIDDEN\"}"));
        var client = new NativeSharingClient(transport, () => "smap1." + new string('a', 24) + "." + new string('b', 64));
        var error = await Assert.ThrowsAsync<SharingException>(() => client.CreateWorkAsync(Guid.NewGuid()));
        Assert.Equal(403, error.Status);
        Assert.Equal(Lang.Sharing("forbidden"), NativeSharingWindow.ErrorText(error));
    }
    [Fact]
    public async Task EmptyContentLookupRemainsAnExplicitResult()
    {
        var project = new SmapProject { Tracks = [new ProjectTrack()] };
        string body = JsonSerializer.Serialize(new { items = Array.Empty<object>(), performanceHash = PerformanceIdentity.ForProject(project), canonicalVersion = 1, more = false, legacyCatalogReady = false });
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.OK, body));
        var result = await new NativeSharingClient(transport, () => null).FindMatchesAsync(project);
        Assert.Empty(result.Items);Assert.False(result.LegacyCatalogReady);
    }
    [Theory]
    [InlineData("{\"items\":[],\"performanceHash\":\"wrong\",\"canonicalVersion\":1}")]
    [InlineData("{\"items\":null,\"performanceHash\":\"wrong\",\"canonicalVersion\":1}")]
    [InlineData("{\"items\":[],\"items\":[],\"performanceHash\":\"wrong\",\"canonicalVersion\":1}")]
    public async Task UnboundOrMalformedContentLookupRejected(string body)
    {
        var project = new SmapProject { Tracks = [new ProjectTrack()] };
        if (body.Contains("\"items\":null")) body = body.Replace("wrong", PerformanceIdentity.ForProject(project));
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Reply(HttpStatusCode.OK, body));
        await Assert.ThrowsAsync<SharingException>(() => new NativeSharingClient(transport, () => null).FindMatchesAsync(project));
    }
    sealed class Waiting : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new HttpResponseMessage(); }
    }
    [Fact]
    public async Task CancellationTerminatesRequests()
    {
        using var transport = new CloudTransport(new Uri("https://share.example.invalid/"), new Waiting());
        var client = new NativeSharingClient(transport, () => null);using var cancel = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ResolveAsync("8J1HJZRRGCP5", cancel.Token));
    }
}
