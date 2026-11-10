using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SMAP_WPF;

public sealed class CloudTransport : IDisposable
{
    public static readonly Uri ApiOrigin = new("https://musetreehouse.com/");
    readonly Uri _origin;
    readonly HttpClient _http;
    public Uri Origin => _origin;

    public CloudTransport(Uri origin, HttpMessageHandler? handler = null)
    {
        if (origin.Scheme != Uri.UriSchemeHttps || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new ArgumentException("HTTPS origin required.");
        _origin = origin;
        // No cookies, automatic redirects, certificate overrides or HTTP fallbacks.
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 1024 * 1024 };
    }

    bool SameOrigin(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Host == _origin.Host && uri.Port == _origin.Port
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    public Uri? PublicUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("//")) return null;
        if (!Uri.TryCreate(_origin, value, out var uri)) return null;
        // Old public library responses may contain same-host HTTP URLs. Upgrade before sending.
        if (uri.Scheme == Uri.UriSchemeHttp && uri.Host == _origin.Host && uri.IsDefaultPort)
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = _origin.Port }.Uri;
        return SameOrigin(uri) ? uri : null;
    }

    public async Task<HttpResponseMessage> SendSensitiveAsync(string path, HttpContent content, string? token = null)
    {
        if (!path.StartsWith('/') || path.StartsWith("//")) throw new ArgumentException("API path required.");
        var uri = new Uri(_origin, path);
        if (!SameOrigin(uri) || uri.Query.Length != 0) throw new ArgumentException("API origin without query required.");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
        if (token != null)
        {
            if (!CloudSessionStore.ValidToken(token)) throw new InvalidOperationException("Invalid session.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        var response = await _http.SendAsync(request);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new HttpRequestException("Sensitive API redirects are refused.");
        }
        return response;
    }

    public async Task<HttpResponseMessage> SendSharingAsync(HttpMethod method, string path, HttpContent? content,
        string? token, Guid? idempotencyKey, CancellationToken cancellation)
    {
        if (method != HttpMethod.Get && method != HttpMethod.Post) throw new ArgumentException("Unsupported sharing method.");
        if (!path.StartsWith("/api/smap/v1/", StringComparison.Ordinal) || path.Contains('#')) throw new ArgumentException("Sharing API path required.");
        var uri = new Uri(_origin, path);
        if (!SameOrigin(uri) || !uri.AbsolutePath.StartsWith("/api/smap/v1/", StringComparison.Ordinal)) throw new ArgumentException("Sharing origin required.");
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        if (token != null)
        {
            if (!CloudSessionStore.ValidToken(token)) throw new InvalidOperationException("Invalid session.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (idempotencyKey != null) request.Headers.Add("Idempotency-Key", idempotencyKey.Value.ToString());
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        if ((int)response.StatusCode is >= 300 and < 400)
        { response.Dispose(); throw new HttpRequestException("Sharing redirects are refused."); }
        return response;
    }

    public static async Task<bool> RevocationConfirmedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is not System.Net.HttpStatusCode.OK and not System.Net.HttpStatusCode.Unauthorized) return false;
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name)) return false;
            bool ok = response.StatusCode == System.Net.HttpStatusCode.OK;
            if (root.TryGetProperty("success", out var success) && success.ValueKind != (ok ? JsonValueKind.True : JsonValueKind.False)) return false;
            if (ok)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null
                    && (error.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(error.GetString()))) return false;
                if (root.TryGetProperty("code", out var code) && (code.ValueKind != JsonValueKind.String || code.GetString() != "ok")) return false;
            }
            else if (root.TryGetProperty("status", out var status) && (status.ValueKind != JsonValueKind.String || status.GetString() != "error")) return false;
            string field = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "code" : "status";
            string expected = response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "invalid_session" : "ok";
            return root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
        }
        catch (JsonException) { return false; }
    }

    public async Task<byte[]> GetPublicBytesAsync(string value)
    {
        var uri = PublicUri(value) ?? throw new HttpRequestException("Untrusted public resource.");
        for (int redirects = 0; redirects <= 3; redirects++)
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location ?? throw new HttpRequestException("Missing redirect target.");
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                // Redirects must already be same-origin HTTPS, including legacy public URLs.
                if (!SameOrigin(next)) throw new HttpRequestException("Untrusted public redirect.");
                uri = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            const int limit = 6 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > limit) throw new HttpRequestException("Public resource too large.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) != 0)
            {
                if (output.Length + count > limit) throw new HttpRequestException("Public resource too large.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        throw new HttpRequestException("Too many public redirects.");
    }

    public void Dispose() => _http.Dispose();
}
