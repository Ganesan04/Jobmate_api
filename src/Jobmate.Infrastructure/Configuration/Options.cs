namespace Jobmate.Infrastructure.Configuration;

/// <summary>
/// JWT options bound from the <c>Jwt</c> configuration section. Every value is
/// supplied by user-secrets or environment variables; nothing has a usable default
/// so a misconfigured deployment fails fast at startup instead of signing tokens
/// with a well-known key.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Symmetric signing key. Must be at least 32 bytes for HS256.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime. Kept short so a stolen token expires quickly.</summary>
    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>Refresh token lifetime. Rotated on every use.</summary>
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>External provider settings. All optional: the app runs without them.</summary>
public sealed class ExternalServicesOptions
{
    public const string SectionName = "ExternalServices";

    public string OtpProvider { get; set; } = "mock";

    public string PushProvider { get; set; } = "mock";

    public string PaymentProvider { get; set; } = "mock";
}

/// <summary>Upload and document limits, enforced before anything is written to storage.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public long MaxDocumentBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedDocumentContentTypes { get; set; } =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    ];
}
