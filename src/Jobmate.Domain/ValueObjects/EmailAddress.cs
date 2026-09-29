using System.Diagnostics.CodeAnalysis;

namespace Jobmate.Domain.ValueObjects;

/// <summary>
/// Validated, normalised email address. Stored lower-cased so the unique index on
/// User.Email is case-insensitive in effect and login lookups cannot miss a row.
/// </summary>
public sealed record EmailAddress
{
    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public static EmailAddress Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new Exceptions.BusinessRuleException("Email address is required.", "EmailRequired");
        }

        var value = raw.Trim().ToLowerInvariant();

        if (value.Length > 254 || !IsValid(value))
        {
            throw new Exceptions.BusinessRuleException("Enter a valid email address.", "EmailInvalid");
        }

        return new EmailAddress(value);
    }

    /// <summary>
    /// Non-throwing parse. <c>true</c> guarantees <paramref name="email"/> is
    /// non-null, so callers can dereference it without a null check.
    /// </summary>
    public static bool TryCreate(string? raw, [NotNullWhen(true)] out EmailAddress? email)
    {
        try
        {
            email = Create(raw);
            return true;
        }
        catch (Exceptions.BusinessRuleException)
        {
            email = null;
            return false;
        }
    }

    private static bool IsValid(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        var domain = value[(at + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains(".."))
        {
            return false;
        }

        return !value.Any(char.IsWhiteSpace);
    }

    public override string ToString() => Value;
}
