using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using SMAP_WPF;
using Xunit;

namespace SMAP.Tests;

public class CloudSecurityTests
{
    [Theory]
    [InlineData(200, "{\"status\":\"ok\"}", true)]
    [InlineData(200, "{\"status\":\"error\"}", false)]
    [InlineData(200, "{\"status\":\"ok\",\"success\":false}", false)]
    [InlineData(200, "{\"status\":\"ok\",\"error\":\"service_unavailable\"}", false)]
    [InlineData(200, "{\"status\":\"ok\",\"code\":\"service_unavailable\"}", false)]
    [InlineData(200, "{\"status\":\"ok\",\"success\":\"false\"}", false)]
    [InlineData(200, "{}", false)]
    [InlineData(200, "{\"status\":\"error\",\"status\":\"ok\"}", false)]
    [InlineData(200, "<html>error</html>", false)]
    [InlineData(401, "{\"code\":\"invalid_session\"}", true)]
    [InlineData(401, "{\"code\":\"invalid_session\",\"success\":true}", false)]
    [InlineData(401, "{\"code\":\"invalid_session\",\"status\":\"ok\"}", false)]
    [InlineData(401, "<html>proxy login</html>", false)]
    [InlineData(401, "{\"code\":\"authentication_required\"}", false)]
    [InlineData(503, "{\"status\":\"ok\"}", false)]
    [InlineData(204, "", false)]
    public async Task RevocationNeedsAConfirmedProtocolResponse(int status, string body, bool expected)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        Assert.Equal(expected, await CloudTransport.RevocationConfirmedAsync(response));
    }
    static readonly string Token = "smap1." + new string('a', 24) + "." + new string('b', 64);
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(reply(request)); }
    }

    [Theory]
    [InlineData("http://musetreehouse.com/")]
    [InlineData("http://127.0.0.1:8999/")]
    [InlineData("https://user:password@musetreehouse.com/")]
    public void NoHttpOrEmbeddedCredentialsInOrigin(string origin) => Assert.Throws<ArgumentException>(() => new CloudTransport(new Uri(origin)));

    [Theory]
    [InlineData("//other.invalid/api")]
    [InlineData("http://musetreehouse.com/api")]
    [InlineData("/api/game_login.php?token=synthetic")]
    [InlineData("/api/game_login.php#secret")]
    public async Task UntrustedSensitiveTargetsNeverSend(string target)
    {
        var handler = new Handler(_ => new(HttpStatusCode.OK));
        using var transport = new CloudTransport(CloudTransport.ApiOrigin, handler);
        await Assert.ThrowsAsync<ArgumentException>(() => transport.SendSensitiveAsync(target, new StringContent("synthetic"), Token));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SensitiveRequestStartsHttpsAndDoesNotFollowRedirect()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("musetreehouse.com", request.RequestUri.Host);
            Assert.Equal(Token, request.Headers.Authorization!.Parameter);
            Assert.Empty(request.RequestUri.Query);
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://other.invalid/api");
            return response;
        });
        using var transport = new CloudTransport(CloudTransport.ApiOrigin, handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendSensitiveAsync("/api/sheets/upload.php", new StringContent("synthetic"), Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http://musetreehouse.com/file")]
    [InlineData("https://other.invalid/file")]
    public async Task PublicRedirectsCannotDowngradeOrChangeHost(string target)
    {
        var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(target); return response;
        });
        using var transport = new CloudTransport(CloudTransport.ApiOrigin, handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.GetPublicBytesAsync("/public"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task LegacyPublicUrlIsUpgradedBeforeFirstSendWithoutCredentials()
    {
        var handler = new Handler(request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Null(request.Headers.Authorization);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) };
        });
        using var transport = new CloudTransport(CloudTransport.ApiOrigin, handler);
        Assert.Equal(new byte[] { 1, 2 }, await transport.GetPublicBytesAsync("http://musetreehouse.com/public"));
        Assert.Null(transport.PublicUri("https://other.invalid/cover"));
        Assert.Null(transport.PublicUri("file:///private"));
    }

    [Fact]
    public async Task NetworkFailureIsNotRetriedOrDowngraded()
    {
        var handler = new Handler(_ => throw new HttpRequestException("synthetic offline"));
        using var transport = new CloudTransport(CloudTransport.ApiOrigin, handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendSensitiveAsync("/api/game_login.php", new StringContent("synthetic")));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DefaultTransportRejectsUntrustedTlsBeforeSendingCredentials()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool receivedBody = false;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using var tls = new SslStream(client.GetStream());
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert }, timeout.Token);
                var bytes = new byte[1024]; receivedBody = await tls.ReadAsync(bytes, timeout.Token) > 0;
            }
            catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException) { }
        });
        try
        {
            using var transport = new CloudTransport(new Uri($"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"));
            await Assert.ThrowsAsync<HttpRequestException>(() => transport.SendSensitiveAsync("/api/game_login.php", new StringContent("synthetic-password")));
            await server; Assert.False(receivedBody);
        }
        finally { listener.Stop(); timeout.Cancel(); }
    }

    [Fact]
    public void SessionUsesWindowsUserEncryptionAndRejectsTampering()
    {
        string folder = Path.Combine(Path.GetTempPath(), "smap-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "session.dat");
        var session = new CloudSession(1, "synthetic", "public-mid", null, Token, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());
        try
        {
            CloudSessionStore.Save(path, session);
            Assert.DoesNotContain(Token, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(session, CloudSessionStore.Load(path));
            File.WriteAllText(path, "untrusted or legacy plaintext");
            Assert.Null(CloudSessionStore.Load(path));
            Assert.False(CloudSessionStore.IsUsable(session with { ExpiresAt = 0 }));
            Assert.False(CloudSessionStore.IsUsable(session with { Token = "public-mid" }));
            Assert.False(CloudSessionStore.ValidToken(null));
        }
        finally { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }

    [Fact]
    public void StorageFailureLeavesNoPlaintextOrTemporaryCredentialFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "smap-cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var session = new CloudSession(1, "synthetic", "public-mid", null, Token, DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds());
        try
        {
            var error = Record.Exception(() => CloudSessionStore.Save(folder, session));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(folder)!, Path.GetFileName(folder) + ".*.tmp"));
        }
        finally { Directory.Delete(folder); }
    }

    [Fact]
    public void LogsRedactPrivateCredentialsWhileKeepingPublicMid()
    {
        var input = $"MID=91000001\nAuthorization: Bearer {Token}\nCookie: synthetic-cookie\npassword=synthetic-password\n{{\"device_token\":\"{Token}\"}}";
        var safe = CloudSecrets.Redact(input);
        Assert.DoesNotContain(Token, safe); Assert.DoesNotContain("synthetic-password", safe); Assert.DoesNotContain("synthetic-cookie", safe);
        Assert.Contains("91000001", safe);
        var json = "{\"password\":\"synthetic \\\"private tail\",\"Cookie\":\"synthetic-cookie\"}";
        var redacted = CloudSecrets.Redact(json);
        Assert.DoesNotContain("private tail", redacted); Assert.DoesNotContain("synthetic-cookie", redacted);
    }

    [Fact]
    public void LogsRedactEncodedRememberCredentialsWithoutAHeader()
    {
        string remember = new string('c', 18) + "%3A" + new string('d', 64);
        var safe = CloudSecrets.Redact("restore=" + remember + "\n{\"remember\":\"private-cookie\"}\nMID=91000001");
        Assert.DoesNotContain(remember, safe);
        Assert.DoesNotContain("private-cookie", safe);
        Assert.Contains("91000001", safe);
    }
}
