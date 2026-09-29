using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.Features.Auth.Commands;

/// <summary>Builds the auth response consistently for register, login, refresh and OTP verify.</summary>
internal static class AuthResultFactory
{
    /// <summary>
    /// Issues a token pair and stages its refresh token for saving. Every sign-in path goes
    /// through here, because a refresh token that is never stored can never be exchanged.
    /// The caller saves the unit of work.
    /// </summary>
    public static (TokenPair Tokens, RefreshToken Stored) IssueTokens(
        IAppDbContext db,
        ITokenService tokenService,
        User user,
        DateTime now)
    {
        var tokens = tokenService.CreateTokens(user);

        var stored = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hashing.Sha256(tokens.RefreshToken),
            ExpiresAt = tokens.RefreshTokenExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.RefreshTokens.Add(stored);

        return (tokens, stored);
    }

    public static AuthResultDto Build(
        User user,
        TokenPair tokens,
        UserRole? selectedRole,
        bool requiresOtp)
    {
        var skills = user.WorkerProfile?.Skills.Select(s => s.Name).ToArray() ?? [];

        return new AuthResultDto
        {
            AccessToken = tokens.AccessToken,
            TokenType = "Bearer",
            ExpiresInSeconds = (int)Math.Max((tokens.AccessTokenExpiresAt - DateTime.UtcNow).TotalSeconds, 0),
            AccessTokenExpiresAt = tokens.AccessTokenExpiresAt,
            RefreshToken = tokens.RefreshToken,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt,
            User = user.ToDto(skills),
            SelectedRole = selectedRole,
            RequiresOtpVerification = requiresOtp
        };
    }
}
