using System.Security.Cryptography;
using System.Text;

namespace Jobmate.Application.Common;

/// <summary>
/// Hashing helpers for values that must be looked up by hash rather than read:
/// refresh tokens, OTP codes and password reset tokens. The database only ever holds
/// the digest, so a leaked table cannot be replayed against the auth endpoints.
/// </summary>
public static class Hashing
{
    /// <summary>Lower-case hex SHA-256, used as the stored token fingerprint.</summary>
    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Constant-time comparison of two hex digests.</summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);

        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>
    /// A cryptographically random URL-safe token, used as the opaque refresh token
    /// and password reset token handed to the client.
    /// </summary>
    public static string RandomToken(int bytes = 32) =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
