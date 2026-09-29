using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Auth;

/// <summary>POST /auth/register and POST /auth/otp/verify request.</summary>
public sealed record RegisterRequest
{
    public string Name { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// The role chosen on the Flutter role-selection step. Omitted means Worker,
    /// which is what the app defaults to.
    /// </summary>
    public UserRole? Role { get; init; }

    public string? Area { get; init; }
}

/// <summary>
/// POST /auth/login. <c>identifier</c> accepts either the email or the phone
/// number, matching the Flutter login screen's single "identifier" field.
/// </summary>
public sealed record LoginRequest
{
    public string Identifier { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

/// <summary>POST /auth/verify-otp.</summary>
public sealed record VerifyOtpRequest
{
    public string Phone { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    /// <summary>Set when the OTP was issued during registration, so the role step can be completed.</summary>
    public UserRole? Role { get; init; }
}

/// <summary>POST /auth/resend-otp.</summary>
public sealed record ResendOtpRequest
{
    public string Phone { get; init; } = string.Empty;

    public OtpPurpose Purpose { get; init; } = OtpPurpose.PhoneVerification;
}

/// <summary>POST /auth/refresh.</summary>
public sealed record RefreshTokenRequest
{
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>POST /auth/logout. Revoking without a token revokes every session.</summary>
public sealed record LogoutRequest
{
    public string? RefreshToken { get; init; }
}

/// <summary>POST /auth/forgot-password.</summary>
public sealed record ForgotPasswordRequest
{
    public string Phone { get; init; } = string.Empty;
}

/// <summary>POST /auth/reset-password.</summary>
public sealed record ResetPasswordRequest
{
    public string Phone { get; init; } = string.Empty;

    /// <summary>The code from the OTP that the forgot-password flow sent.</summary>
    public string Code { get; init; } = string.Empty;

    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>POST /admin/auth/login. Email-only: the admin console has its own flow.</summary>
public sealed record AdminLoginRequest
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}
