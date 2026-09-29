using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Auth;

/// <summary>
/// Issued token pair plus the identity the app stores. Access token is short lived;
/// the refresh token is rotated on every use.
/// </summary>
public sealed record AuthResultDto
{
    public string AccessToken { get; init; } = string.Empty;

    public string TokenType { get; init; } = "Bearer";

    public int ExpiresInSeconds { get; init; }

    public DateTime AccessTokenExpiresAt { get; init; }

    public string RefreshToken { get; init; } = string.Empty;

    public DateTime RefreshTokenExpiresAt { get; init; }

    public UserProfileDto User { get; init; } = new();

    /// <summary>Echoed when registration required a role choice the backend then applied.</summary>
    public UserRole? SelectedRole { get; init; }

    /// <summary>True when the phone is still unverified and the app should route to /otp.</summary>
    public bool RequiresOtpVerification { get; init; }
}

/// <summary>Response for endpoints that must not reveal whether an account exists.</summary>
public sealed record GenericMessageDto
{
    public string Message { get; init; } = "Success";
}

/// <summary>
/// Development-only echo of the dispatched OTP. Present in non-production builds so
/// the Flutter app can be tested without an SMS vendor; never populated in production.
/// </summary>
public sealed record OtpDispatchDto
{
    public string Phone { get; init; } = string.Empty;

    public string? Code { get; init; }

    public string Message { get; init; } = string.Empty;

    public int ExpiresInSeconds { get; init; }
}
