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
/// Registers a worker or work giver. The account is created immediately in an
/// unverified state and an OTP is dispatched, which mirrors the Flutter flow:
/// register -> /otp -> role selection -> profile setup.
/// </summary>
public sealed record RegisterCommand(
    string Name,
    string Phone,
    string Email,
    string Password,
    UserRole? Role,
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
        var role = request.Role ?? UserRole.Worker;

        if (role == UserRole.Admin)
        {
            throw new BusinessRuleException(
                "Admin accounts cannot be self-registered.",
                "AdminSelfRegistrationBlocked");
        }

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
            Role = role,
            Area = request.Area?.Trim() ?? string.Empty,
            IsActive = true,
            IsVerified = false,
            VerificationStatus = VerificationStatus.NotVerified,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Role-specific profile is created up front so the profile-setup step can save into it.
        if (role == UserRole.Worker)
        {
            user.WorkerProfile = new WorkerProfile
            {
                UserId = user.Id,
                VerificationStatus = VerificationStatus.NotVerified,
                CreatedAt = now,
                UpdatedAt = now
            };
        }
        else
        {
            user.GiverProfile = new GiverProfile
            {
                UserId = user.Id,
                Kind = GiverKind.Individual,
                BusinessName = string.IsNullOrWhiteSpace(request.Area) ? string.Empty : request.Name.Trim(),
                ContactPerson = string.IsNullOrWhiteSpace(request.Name) ? string.Empty : request.Name.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        db.Users.Add(user);

        var code = await otpHelper.IssueAsync(phone.E164, OtpPurpose.PhoneVerification, cancellationToken);
        await otpService.SendAsync(phone.E164, code.Code, OtpPurpose.PhoneVerification, cancellationToken);

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, user, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(user, tokens, role, requiresOtp: true);
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
public sealed record VerifyOtpCommand(string Phone, string Code, UserRole? Role) : IRequest<AuthResultDto>;

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

        // The role choice belongs to finishing registration. A verified account re-running
        // OTP must not be able to swap roles, because that drops the other role's profile.
        var canChooseRole = !user.IsVerified;

        user.IsVerified = true;

        // The trust badge the profile screen renders is driven by VerificationStatus,
        // so a confirmed phone number must move it too, not just the boolean.
        user.VerificationStatus = VerificationStatus.Verified;

        // Registration completes on OTP verification, so the role choice lands here.
        var appliedRole = user.Role;
        if (canChooseRole && request.Role is { } requested && requested != UserRole.Admin && user.Role != requested)
        {
            user.Role = requested;
            appliedRole = requested;
            ReconcileProfile(user, requested, now);
        }

        if (user.WorkerProfile is not null)
        {
            user.WorkerProfile.VerificationStatus = VerificationStatus.Verified;
        }

        user.LastLoginAt = now;

        var (tokens, _) = AuthResultFactory.IssueTokens(db, tokenService, user, now);

        await db.SaveChangesAsync(cancellationToken);

        return AuthResultFactory.Build(user, tokens, appliedRole, requiresOtp: false);
    }

    /// <summary>Keeps exactly one role profile in sync when the role changes at verification time.</summary>
    private static void ReconcileProfile(User user, UserRole role, DateTime now)
    {
        if (role == UserRole.Worker)
        {
            user.WorkerProfile ??= new WorkerProfile
            {
                UserId = user.Id,
                CreatedAt = now,
                UpdatedAt = now
            };

            user.GiverProfile = null;
            return;
        }

        user.GiverProfile ??= new GiverProfile
        {
            UserId = user.Id,
            Kind = GiverKind.Individual,
            BusinessName = user.FullName,
            ContactPerson = user.FullName,
            CreatedAt = now,
            UpdatedAt = now
        };

        user.WorkerProfile = null;
    }
}
