using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Domain.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobmate.Application.Features.Auth.Commands;

/// <summary>
/// Signs in with either an email address or a phone number, matching the Flutter
/// login screen's single "identifier" field. The same failure message is returned for
/// an unknown identifier and a wrong password so the endpoint cannot enumerate accounts.
/// </summary>
public sealed record LoginCommand(string Identifier, string Password) : IRequest<AuthResultDto>;

public sealed class LoginCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    ILogger<LoginCommandHandler> logger) : IRequestHandler<LoginCommand, AuthResultDto>
{
    private const string InvalidCredentials = "Incorrect email/phone or password.";

    public async Task<AuthResultDto> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var key = request.Identifier?.Trim() ?? string.Empty;

        if (key.Length == 0)
        {
            throw new UnauthorizedException(InvalidCredentials);
        }

        var user = await FindByIdentifierAsync(key, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("Failed sign-in attempt for an unknown identifier");
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Failed sign-in attempt: password mismatch");
            throw new UnauthorizedException(InvalidCredentials);
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("This account has been deactivated. Contact support.");
        }

        user.LastLoginAt = now;

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, user, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(user, tokens, user.Role, requiresOtp: !user.IsVerified);
    }

    /// <summary>
    /// Resolves an email or a phone number (with or without the country code) to one user.
    /// Tracked, because a successful sign-in writes <c>LastLoginAt</c> back.
    /// </summary>
    private async Task<User?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken)
    {
        if (EmailAddress.TryCreate(identifier, out var email))
        {
            return await db.Users
                .FirstOrDefaultAsync(u => u.Email == email.Value, cancellationToken);
        }

        if (PhoneNumber.TryCreate(identifier, out var phone))
        {
            return await db.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == phone.E164, cancellationToken);
        }

        return null;
    }
}

/// <summary>
/// Exchanges a refresh token for a new pair, rotating the token. Reusing a token that
/// was already rotated revokes the entire family, which is the standard
/// token-theft containment strategy.
/// </summary>
public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResultDto>;

public sealed class RefreshTokenCommandHandler(
    IAppDbContext db,
    ITokenService tokenService,
    IClock clock,
    ILogger<RefreshTokenCommandHandler> logger)
    : IRequestHandler<RefreshTokenCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hash = Hashing.Sha256(request.RefreshToken ?? string.Empty);

        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            throw new UnauthorizedException("Invalid session. Please sign in again.");
        }

        if (stored.ReplacedByTokenId is not null)
        {
            // The token was already exchanged: assume theft and kill the whole family.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking the token family", stored.UserId);

            var family = await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in family)
            {
                token.RevokedAt = now;
                token.RevokedReason = "RevokedTokenReuse";
            }

            await db.SaveChangesAsync(cancellationToken);

            throw new UnauthorizedException("Your session was revoked for security reasons. Please sign in again.");
        }

        if (!stored.IsActive(now))
        {
            throw new UnauthorizedException("Your session has expired. Please sign in again.");
        }

        if (stored.User is null || !stored.User.IsActive)
        {
            throw new ForbiddenException("This account has been deactivated.");
        }

        var (tokens, replacement) = AuthResultFactory.IssueTokens(db, tokenService, stored.User, now);

        // Linking the old token to its replacement is what lets a second use of it be
        // recognised as reuse above, rather than as an ordinary expired session.
        stored.RevokedAt = now;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenId = replacement.Id;

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(stored.User, tokens, stored.User.Role, requiresOtp: !stored.User.IsVerified);
    }
}

/// <summary>
/// Signs out. Revoking a single token ends that session; omitting the token revokes
/// every session for the user (the "sign out everywhere" case).
/// </summary>
public sealed record LogoutCommand(Guid UserId, string? RefreshToken) : IRequest<Unit>;

public sealed class LogoutCommandHandler(IAppDbContext db, IClock clock) : IRequestHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        List<RefreshToken> tokens;

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            tokens = await db.RefreshTokens
                .Where(t => t.UserId == request.UserId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var hash = Hashing.Sha256(request.RefreshToken);

            var single = await db.RefreshTokens
                .Where(t => t.UserId == request.UserId && t.TokenHash == hash)
                .ToListAsync(cancellationToken);

            tokens = single;
        }

        foreach (var token in tokens)
        {
            token.RevokedAt = now;
            token.RevokedReason = "UserLogout";
        }

        if (tokens.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
