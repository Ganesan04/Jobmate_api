using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Jobmate.Domain.ValueObjects;

/// <summary>
/// Validated phone number. The Flutter app signs in with a 10-digit Indian mobile
/// entered without a country code, so the value object normalises to E.164 and
/// stores the last 10 digits for display, matching the "98•••210" mask the app shows.
/// </summary>
public sealed partial record PhoneNumber
{
    private PhoneNumber(string e164, string national)
    {
        E164 = e164;
        National = national;
    }

    public string E164 { get; }

    /// <summary>Ten-digit national number, without the country code.</summary>
    public string National { get; }

    /// <summary>Masked form, e.g. <c>98•••210</c>.</summary>
    public string Masked => National.Length < 6
        ? new string('•', National.Length)
        : $"{National[..2]}•••{National[^3..]}";

    /// <summary>
    /// Normalises user input. Accepts an optional leading +, country code or spaces.
    /// Throws <see cref="Exceptions.BusinessRuleException"/> when invalid so the
    /// FluentValidation layer can surface a field-level message.
    /// </summary>
    public static PhoneNumber Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new Exceptions.BusinessRuleException("Phone number is required.", "PhoneRequired");
        }

        var digits = NormalizeDigits(raw);

        // Strip a leading 91 country code when the number is longer than 10 digits.
        if (digits.Length > 10 && digits.StartsWith('9') && digits.Length == 12)
        {
            digits = digits[2..];
        }

        if (digits.Length != 10)
        {
            throw new Exceptions.BusinessRuleException("Enter a valid 10-digit mobile number.", "PhoneInvalid");
        }

        if (!IndianMobile().IsMatch(digits))
        {
            throw new Exceptions.BusinessRuleException("Enter a valid Indian mobile number.", "PhoneInvalid");
        }

        return new PhoneNumber($"+91{digits}", digits);
    }

    /// <summary>
    /// Non-throwing parse. <c>true</c> guarantees <paramref name="phone"/> is
    /// non-null, so callers can dereference it without a null check.
    /// </summary>
    public static bool TryCreate(string? raw, [NotNullWhen(true)] out PhoneNumber? phone)
    {
        try
        {
            phone = Create(raw);
            return true;
        }
        catch (Exceptions.BusinessRuleException)
        {
            phone = null;
            return false;
        }
    }

    private static string NormalizeDigits(string raw)
    {
        var trimmed = raw.Trim();
        var plus = trimmed.StartsWith('+');
        var digits = new string(trimmed.SkipWhile(c => c is '+' or ' ' or '-' or '(' or ')').Where(char.IsDigit).ToArray());
        return plus ? digits : digits;
    }

    public override string ToString() => E164;

    [GeneratedRegex(@"^[6-9]\d{9}$")]
    private static partial Regex IndianMobile();
}
