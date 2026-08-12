using System.Security.Cryptography;
using System.Text;

namespace Msgifly.Web.Services.ApiKeys;

/// <summary>
/// API key generation + hashing. The DB stores only the SHA-256 hash (see ApiKey entity) — the
/// plaintext is shown to the creator exactly once and never persisted. SHA-256 (not a slow KDF
/// like bcrypt/argon2) is deliberate: these are full-entropy random strings, not user-chosen
/// passwords, so there's no dictionary/rainbow-table attack a slow hash would defend against —
/// it would only slow down the per-request auth lookup.
/// </summary>
public static class ApiKeyGenerator
{
    /// <summary>Self-identifying prefix — a leaked string is instantly recognizable as a Msgifly key by any secret-scanner.</summary>
    public const string KeyPrefix = "msgifly_live_";

    private const int DisplayBodyChars = 8;

    public record GeneratedKey(string Plaintext, string Hash, string DisplayPrefix);

    public static GeneratedKey Generate()
    {
        var bodyBytes = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(bodyBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var plaintext = KeyPrefix + body;
        return new GeneratedKey(plaintext, Hash(plaintext), KeyPrefix + body[..DisplayBodyChars]);
    }

    public static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool LooksLikeApiKey(string value) =>
        value.StartsWith(KeyPrefix, StringComparison.Ordinal) && value.Length > KeyPrefix.Length;
}
