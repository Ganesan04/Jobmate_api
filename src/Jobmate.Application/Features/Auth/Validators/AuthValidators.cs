using FluentValidation;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Application.Features.Auth.Commands;

namespace Jobmate.Application.Features.Auth.Validators;

/// <summary>Shared password rule: 8-100 chars with a letter and a digit.</summary>
public sealed class PasswordRule : AbstractValidator<string>
{
    public PasswordRule()
    {
        RuleFor(p => p)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100).WithMessage("Password cannot exceed 100 characters.")
            .Matches(@"[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches(@"\d").WithMessage("Password must contain a number.")
            .Matches(@"^[^\x00-\x20]+$").WithMessage("Password cannot contain spaces.");
    }
}

/// <summary>Shared phone rule, matching the 10-digit Indian mobile the app collects.</summary>
public sealed class PhoneRule : AbstractValidator<string>
{
    public PhoneRule()
    {
        RuleFor(p => p)
            .NotEmpty().WithMessage("Mobile number is required.")
            .Matches(@"^(\+?91[- ]?)?[6-9]\d{9}$")
            .WithMessage("Enter a valid 10-digit mobile number.");
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
            .MaximumLength(120).WithMessage("Name cannot exceed 120 characters.");

        RuleFor(x => x.Phone).SetValidator(new PhoneRule());
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email address is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(254).WithMessage("Email cannot exceed 254 characters.");

        RuleFor(x => x.Password).SetValidator(new PasswordRule());

        RuleFor(x => x.Area).MaximumLength(120).WithMessage("Area cannot exceed 120 characters.");
    }
}

/// <summary>On the command, not the request, so the MediatR validation pipeline runs it.</summary>
public sealed class SwitchRoleCommandValidator : AbstractValidator<SwitchRoleCommand>
{
    public SwitchRoleCommandValidator()
    {
        RuleFor(x => x.Role)
            .NotNull().WithMessage("Choose either the worker or work giver role.")
            .IsInEnum().WithMessage("Choose either the worker or work giver role.")
            .WithName("role");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Enter your email address or mobile number.")
            .MaximumLength(254);

        RuleFor(x => x.Password).NotEmpty().WithMessage("Password is required.");
    }
}

public sealed class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpRequestValidator()
    {
        RuleFor(x => x.Phone).SetValidator(new PhoneRule());

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Enter the verification code.")
            .Length(6).WithMessage("The verification code is 6 digits.")
            .Matches(@"^\d{6}$").WithMessage("The verification code is 6 digits.");
    }
}

public sealed class ResendOtpRequestValidator : AbstractValidator<ResendOtpRequest>
{
    public ResendOtpRequestValidator() => RuleFor(x => x.Phone).SetValidator(new PhoneRule());
}

public sealed class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
        => RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.");
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
        => RuleFor(x => x.RefreshToken).MaximumLength(512).WithMessage("Refresh token is too long.");
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Phone).SetValidator(new PhoneRule());
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Phone).SetValidator(new PhoneRule());

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Enter the reset code.")
            .Length(6).WithMessage("The reset code is 6 digits.")
            .Matches(@"^\d{6}$").WithMessage("The reset code is 6 digits.");

        RuleFor(x => x.NewPassword).SetValidator(new PasswordRule());
    }
}
