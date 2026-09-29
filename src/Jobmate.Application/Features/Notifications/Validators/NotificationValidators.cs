using FluentValidation;
using Jobmate.Application.Features.Notifications.Commands;

namespace Jobmate.Application.Features.Notifications.Validators;

/// <summary>
/// A push token is an opaque provider string, so it is only length-checked; the provider
/// is the authority on whether it is well formed. The ceilings match the column widths
/// so an over-long token is refused with a readable message rather than a database error.
/// </summary>
public sealed class RegisterDeviceCommandValidator : AbstractValidator<RegisterDeviceCommand>
{
    private const int MaxTokenLength = 512;
    private const int MaxDeviceNameLength = 120;
    private const int MaxAppVersionLength = 30;

    public RegisterDeviceCommandValidator()
    {
        RuleFor(x => x.Request.Token)
            .NotEmpty()
            .WithMessage("A device token is required.")
            .MaximumLength(MaxTokenLength)
            .WithMessage($"A device token cannot exceed {MaxTokenLength} characters.")
            .WithName("token");

        RuleFor(x => x.Request.DeviceName)
            .MaximumLength(MaxDeviceNameLength)
            .WithMessage($"The device name cannot exceed {MaxDeviceNameLength} characters.")
            .When(x => x.Request.DeviceName is not null)
            .WithName("deviceName");

        RuleFor(x => x.Request.AppVersion)
            .MaximumLength(MaxAppVersionLength)
            .WithMessage($"The app version cannot exceed {MaxAppVersionLength} characters.")
            .When(x => x.Request.AppVersion is not null)
            .WithName("appVersion");

        RuleFor(x => x.Request.Platform)
            .IsInEnum()
            .WithMessage("Choose a valid device platform.")
            .WithName("platform");
    }
}

/// <summary>The row being marked read has to exist; the id comes from the route.</summary>
public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator()
    {
        RuleFor(x => x.NotificationId)
            .NotEmpty()
            .WithMessage("A notification is required.")
            .WithName("id");
    }
}
