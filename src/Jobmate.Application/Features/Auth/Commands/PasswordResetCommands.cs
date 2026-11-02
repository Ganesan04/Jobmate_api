using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Domain.Entities;
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
