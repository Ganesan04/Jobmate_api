using Jobmate.Application.Common.Interfaces;

namespace Jobmate.Infrastructure.Security;

/// <summary>
/// BCrypt password hashing. The work factor is 12: high enough to make an offline
/// crack expensive, low enough that a sign-in stays well under the ~100 ms budget
/// the app expects. Raise it as hardware improves; existing hashes stay valid
/// because the cost is embedded in the hash string.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    /// <summary>
    /// BCrypt silently truncates at 72 bytes, so a longer password must not be
    /// accepted silently. Callers validate the length; this guards the boundary.
    /// </summary>
    public const int MaxPasswordLength = 72;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (password.Length > MaxPasswordLength)
        {
            throw new ArgumentException(
                $"Password cannot exceed {MaxPasswordLength} characters.",
                nameof(password));
        }

        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException)
        {
            // A corrupt or truncated hash in the database is a failed verification, not a
            // crash: the caller responds with a normal invalid-credentials error.
            //
            // Both shapes have to be caught, because BCrypt reports them differently. A
            // malformed salt throws SaltParseException, while a hash of the wrong length
            // throws ArgumentOutOfRangeException out of its own decoder before it gets that
            // far. Catching only the first left a truncated column able to turn one user's
            // sign-in into a 500.
            return false;
        }
    }
}
