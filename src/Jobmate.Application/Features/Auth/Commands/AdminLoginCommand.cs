using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobmate.Application.Features.Auth.Commands;

/// <summary>
/// Starts the forgot-password flow. The response is deliberately identical whether or
/// not the phone is registered, so the endpoint cannot be used to enumerate accounts.
/// </summary>
public sealed record ForgotPasswordCommand(string Phone) : IRequest<GenericMessageDto>;

public sealed class ForgotPasswordCommandHandler(
    IAppDbContext db,
    IPasswordResetService resetService,
    OtpServiceHelper otpHelper,
    ILogger<ForgotPasswordCommandHandler> logger)
    : IRequestHandler<ForgotPasswordCommand, GenericMessageDto>
{
    private const string NeutralResponse = "If that mobile number is registered, a reset code is on its way.";

    public async Task<GenericMessageDto> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        if (!PhoneNumber.TryCreate(request.Phone, out var phone) || phone is null)
        {
            return new GenericMessageDto { Message = NeutralResponse };
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone.E164, cancellationToken);

        if (user is null)
        {
            logger.LogInformation("Password reset requested for an unregistered phone number");
            return new GenericMessageDto { Message = NeutralResponse };
        }

        var issued = await otpHelper.IssueAsync(phone.E164, OtpPurpose.PasswordReset, cancellationToken);
        await resetService.SendAsync(phone.E164, issued.Code, cancellationToken);

        return new GenericMessageDto { Message = NeutralResponse };
    }
}

/// <summary>
/// Completes the reset: verifies the OTP, sets the new hash and revokes every
/// outstanding session so a stolen token cannot outlive the password change.
/// </summary>
public sealed record ResetPasswordCommand(string Phone, string Code, string NewPassword) : IRequest<GenericMessageDto>;

public sealed class ResetPasswordCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    OtpServiceHelper otpHelper,
    IClock clock,
    ILogger<ResetPasswordCommandHandler> logger)
    : IRequestHandler<ResetPasswordCommand, GenericMessageDto>
{
    public async Task<GenericMessageDto> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var phone = PhoneNumber.Create(request.Phone);

        var challenge = await otpHelper.ValidateAsync(phone.E164, request.Code, OtpPurpose.PasswordReset, cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone.E164, cancellationToken)
                   ?? throw new NotFoundException("No account exists for this mobile number.");

        challenge.ConsumedAt = now;
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = now;

        var sessions = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in sessions)
        {
            token.RevokedAt = now;
            token.RevokedReason = "PasswordReset";
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password reset completed for user {UserId}", user.Id);

        return new GenericMessageDto { Message = "Your password has been reset. Please sign in." };
    }
}

/// <summary>
/// Signs in the internal admin console. Admin accounts are rejected here even with
/// correct credentials, so the admin surface is reachable only through its own endpoint.
/// </summary>
public sealed record AdminLoginCommand(string Email, string Password) : IRequest<AuthResultDto>;

public sealed class AdminLoginCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    ILogger<AdminLoginCommandHandler> logger) : IRequestHandler<AdminLoginCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(AdminLoginCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        if (!EmailAddress.TryCreate(request.Email, out var email) || email is null)
        {
            throw new UnauthorizedException("Invalid admin credentials.");
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email.Value, cancellationToken);

        if (user is null || user.Role != UserRole.Admin)
        {
            logger.LogWarning("Failed admin sign-in attempt");
            throw new UnauthorizedException("Invalid admin credentials.");
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Failed admin sign-in attempt: password mismatch");
            throw new UnauthorizedException("Invalid admin credentials.");
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("This admin account has been deactivated.");
        }

        var tracked = await db.Users.FirstAsync(u => u.Id == user.Id, cancellationToken);
        tracked.LastLoginAt = now;

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, tracked, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(tracked, tokens, UserRole.Admin, requiresOtp: false);
    }
}
