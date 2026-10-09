using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace ODataAgentSample.Demo;

/// <summary>
/// Zustandsloses, signiertes Sitzungs-Cookie: "sitzungs-id.ablauf.hmac". Der Schluessel kommt aus der Konfiguration,
/// deshalb bleiben Anmeldungen ueber einen Neustart gueltig. Es werden keine Personendaten gespeichert.
/// </summary>
public static class DemoSession
{
    public const string CookieName = "demo_session";

    public static string Create(string cookieKey, DateTimeOffset expires, out string sessionId)
    {
        sessionId = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var payload = $"{sessionId}.{expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";
        return $"{payload}.{Sign(cookieKey, payload)}";
    }

    /// <summary>Liefert die Sitzungs-ID, wenn das Cookie echt und nicht abgelaufen ist, sonst null.</summary>
    public static string? Validate(string cookieKey, string? cookie, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(cookie) || cookie.Length > 200)
            return null;

        var parts = cookie.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0)
            return null;
        if (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiry))
            return null;

        var expected = Sign(cookieKey, $"{parts[0]}.{parts[1]}");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2])))
            return null;

        return expiry > now.ToUnixTimeSeconds() ? parts[0] : null;
    }

    private static string Sign(string cookieKey, string payload) =>
        WebEncoders.Base64UrlEncode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(cookieKey), Encoding.UTF8.GetBytes(payload)));
}
