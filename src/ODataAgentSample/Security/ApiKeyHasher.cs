using System.Security.Cryptography;
using System.Text;

namespace ODataAgentSample.Security;

/// <summary>
/// API-Keys sind zufaellige Tokens mit hoher Entropie, daher genuegt ein schneller Hash (SHA-256).
/// Fuer menschliche Passwoerter waere das falsch - dort braucht es ein KDF wie Argon2 oder PBKDF2.
/// </summary>
public static class ApiKeyHasher
{
    public static string Hash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool TryParseHash(string? hex, out byte[] hash)
    {
        hash = [];
        if (hex is not { Length: 64 })
            return false;
        try
        {
            hash = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
