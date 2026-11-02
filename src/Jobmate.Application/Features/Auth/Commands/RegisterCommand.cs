using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using Jobmate.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Auth.Commands;

/// <summary>
/// Registers an account. The account is created immediately in an unverified state and
/// an OTP is dispatched. There is no role choice: every account gets both a worker and a
/// work giver profile, starts in worker mode, and switches with POST /auth/switch-role.
/// </summary>
public sealed record RegisterCommand(
    string Name,
    string Phone,
    string Email,
    string Password,
    string? Area) : IRequest<AuthResultDto>;

public sealed class RegisterCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    IOtpService otpService,
    IClock clock,
    ITokenService tokenService,
    OtpServiceHelper otpHelper) : IRequestHandler<RegisterCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var phone = PhoneNumber.Create(request.Phone);
        var email = EmailAddress.Create(request.Email);

        var phoneExists = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.PhoneNumber == phone.E164, cancellationToken);

        if (phoneExists)
        {
            throw new ConflictException("An account with this mobile number already exists.", "PhoneAlreadyRegistered");
        }

        if (await db.Users.AsNoTracking().AnyAsync(u => u.Email == email.Value, cancellationToken))
        {
            throw new ConflictException("An account with this email address already exists.", "EmailAlreadyRegistered");
        }

        var (firstName, lastName) = SplitName(request.Name);

        var user = new User
        {
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phone.E164,
            Email = email.Value,
            PasswordHash = passwordHasher.Hash(request.Password),
            ActiveRole = UserRole.Worker,
            Area = request.Area?.Trim() ?? string.Empty,
            IsActive = true,
            IsVerified = false,
            VerificationStatus = VerificationStatus.NotVerified,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Both profiles are created up front, so switching mode never has to create one
        // and the profile-setup step for either mode can save straight into it.
        user.WorkerProfile = new WorkerProfile
        {
            UserId = user.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        user.GiverProfile = new GiverProfile
        {
            UserId = user.Id,
            Kind = GiverKind.Individual,
            BusinessName = string.IsNullOrWhiteSpace(request.Area) ? string.Empty : request.Name.Trim(),
            ContactPerson = string.IsNullOrWhiteSpace(request.Name) ? string.Empty : request.Name.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Users.Add(user);

        var code = await otpHelper.IssueAsync(phone.E164, OtpPurpose.PhoneVerification, cancellationToken);
        await otpService.SendAsync(phone.E164, code.Code, OtpPurpose.PhoneVerification, cancellationToken);

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, user, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(user, tokens, user.ActiveRole, requiresOtp: true);
    }

    internal static (string First, string Last) SplitName(string? name)
    {
        var parts = (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => (string.Empty, string.Empty),
            1 => (parts[0], string.Empty),
            _ => (parts[0], string.Join(' ', parts[1..]))
        };
    }
}

/// <summary>Issues a fresh OTP, revoking any outstanding one for the same phone and purpose.</summary>
public sealed record ResendOtpCommand(string Phone, OtpPurpose Purpose) : IRequest<OtpIssued>;

public sealed record OtpIssued(string Phone, string? Code, string Message, int ExpiresInSeconds);

public sealed class ResendOtpCommandHandler(IOtpService otpService, OtpServiceHelper otpHelper)
    : IRequestHandler<ResendOtpCommand, OtpIssued>
{
    public async Task<OtpIssued> Handle(ResendOtpCommand request, CancellationToken cancellationToken)
    {
        var phone = PhoneNumber.Create(request.Phone);

        var issued = await otpHelper.IssueAsync(phone.E164, request.Purpose, cancellationToken);
        await otpService.SendAsync(phone.E164, issued.Code, request.Purpose, cancellationToken);

        return new OtpIssued(phone.E164, issued.Code, "A verification code has been sent.", OtpServiceHelper.TtlSeconds);
    }
}

/// <summary>Consumes an OTP, marks the phone verified and returns a token pair.</summary>
public sealed record VerifyOtpCommand(string Phone, string Code) : IRequest<AuthResultDto>;

public sealed class VerifyOtpCommandHandler(
    IAppDbContext db,
    ITokenService tokenService,
    OtpServiceHelper otpHelper,
    IClock clock) : IRequestHandler<VerifyOtpCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var phone = PhoneNumber.Create(request.Phone);

        var challenge = await otpHelper.ValidateAsync(phone.E164, request.Code, OtpPurpose.PhoneVerification, cancellationToken);

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone.E164, cancellationToken)
            ?? throw new NotFoundException("No account exists for this mobile number.");

        challenge.ConsumedAt = now;

        user.IsVerified = true;

        // The trust badge the profile screen renders is driven by VerificationStatus,
        // so a confirmed phone number must move it too, not just the boolean.
        user.VerificationStatus = VerificationStatus.Verified;

        user.LastLoginAt = now;

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, user, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(user, tokens, user.ActiveRole, requiresOtp: false);
    }
}
