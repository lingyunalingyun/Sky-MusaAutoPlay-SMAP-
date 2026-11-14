using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SMAP_WPF;

public sealed record CloudSession(int UserId, string Username, string Mid, string? Avatar, string Token, long ExpiresAt);

public static class CloudSessionStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SMAP.desktop-session.v1");
    public static bool ValidToken(string? token) => token != null && Regex.IsMatch(token, @"\Asmap1\.[a-f0-9]{24}\.[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    public static bool IsUsable(CloudSession session) => session.UserId > 0 && ValidToken(session.Token)
        && session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static void Save(string path, CloudSession session)
    {
        if (!IsUsable(session)) throw new InvalidOperationException("Invalid session.");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static CloudSession? Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var session = JsonSerializer.Deserialize<CloudSession>(plain);
                return session != null && IsUsable(session) ? session : null;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
    }
}

public static class CloudSecrets
{
    public static string Redact(string text)
    {
        text = Regex.Replace(text, @"smap1\.[a-f0-9]{24}\.[a-f0-9]{64}|\b[a-f0-9]{18}(?::|%3a)[a-f0-9]{64}\b", "[REDACTED]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(Authorization|Cookie|Set-Cookie)\s*:[^\r\n]*", "$1: [REDACTED]", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"([""']?(?:password|pass|device_token|access_token|refresh_token|reset_token|token|validator|remember|cookie|set-cookie|authorization)[""']?\s*[:=]\s*)(""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[^\r\n]*)", "$1[REDACTED]", RegexOptions.IgnoreCase);
    }
}
