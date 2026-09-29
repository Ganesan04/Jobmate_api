using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Jobmate.Infrastructure.Security;

/// <summary>
/// Issues and validates the app's JWTs. The access token is a short-lived HS256
/// signed token carrying only what the client needs to render itself; the refresh
/// token is an opaque random string, so revoking a session is a database write
/// rather than a token blacklist.
/// </summary>
public sealed class JwtService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public TokenPair CreateTokens(User user, string? deviceId = null, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via user-secrets or an environment variable.");
        }

        var now = DateTime.UtcNow;
        var accessExpiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiresAt = now.AddDays(_options.RefreshTokenDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.MobilePhone, user.PhoneNumber),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AuthClaimKeys.Area, user.Area ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: accessExpiresAt,
            signingCredentials: new SigningCredentials(CreateKey(), SecurityAlgorithms.HmacSha256));

        return new TokenPair(
            new JwtSecurityTokenHandler().WriteToken(token),
            Hashing.RandomToken(),
            accessExpiresAt,
            refreshExpiresAt,
            user.Id,
            user.Role);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            return null;
        }

        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = CreateKey(),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            };

            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, parameters, out var validatedToken);

            // A token that parses but carries no subject is not usable as an identity.
            if (validatedToken is not JwtSecurityToken jwt ||
                string.IsNullOrWhiteSpace(jwt.Subject))
            {
                return null;
            }

            return principal;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            // An invalid or expired token is an expected outcome, not a fault: the
            // caller turns this into a 401 rather than a 500.
            return null;
        }
    }

    private SymmetricSecurityKey CreateKey() =>
        new(Encoding.UTF8.GetBytes(_options.SigningKey));
}

/// <summary>Custom claim names, kept in one place so the API and token service agree.</summary>
public static class AuthClaimKeys
{
    public const string Area = "area";

    public const string DeviceId = "device_id";
}
