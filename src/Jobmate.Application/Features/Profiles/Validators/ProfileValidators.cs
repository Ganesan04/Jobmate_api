using FluentValidation;
using Jobmate.Application.Features.Profiles.Commands;
using Jobmate.Application.Features.Profiles.Media;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Features.Profiles.Validators;

/// <summary>
/// Shape checks on the profile forms. Anything that needs to know what is already
/// stored is left to <see cref="ProfileRules"/> and the handler, so a rule never has to
/// choose between "invalid" and "does not apply".
///
/// Each rule names the body field rather than the property path, so the app attaches each
/// message to the input the user was actually typing in.
/// </summary>
public sealed class UpdateWorkerProfileCommandValidator : AbstractValidator<UpdateWorkerProfileCommand>
{
    public UpdateWorkerProfileCommandValidator()
    {
        // Null means "leave as it is", so a field is only length-checked when supplied.
        When(x => x.Request.Bio is not null, () =>
            RuleFor(x => x.Request.Bio).MaximumLength(2000).WithName("bio"));

        When(x => x.Request.Gender is not null, () =>
            RuleFor(x => x.Request.Gender).MaximumLength(40).WithName("gender"));

        When(x => x.Request.Location is not null, () =>
            RuleFor(x => x.Request.Location).MaximumLength(200).WithName("location"));

        When(x => x.Request.Experience is not null, () =>
            RuleFor(x => x.Request.Experience).MaximumLength(1000).WithName("experience"));

        When(x => x.Request.Availability is not null, () =>
            RuleFor(x => x.Request.Availability).MaximumLength(200).WithName("availability"));

        RuleFor(x => x.Request.Latitude)
            .InclusiveBetween(-90, 90)
            .WithMessage("Latitude must be between -90 and 90.")
            .WithName("latitude")
            .When(x => x.Request.Latitude is not null);

        RuleFor(x => x.Request.Longitude)
            .InclusiveBetween(-180, 180)
            .WithMessage("Longitude must be between -180 and 180.")
            .WithName("longitude")
            .When(x => x.Request.Longitude is not null);

        RuleFor(x => x.Request.Skills)
            .Must(skills => skills is not null && skills.Count <= ProfileRules.MaxSkills)
            .WithMessage($"Keep your skills to {ProfileRules.MaxSkills} or fewer so the list stays scannable.")
            .WithName("skills")
            .When(x => x.Request.Skills is not null);

        RuleForEach(x => x.Request.Skills)
            .Must(skill => !string.IsNullOrWhiteSpace(skill))
            .WithMessage("A skill cannot be blank.")
            .When(x => x.Request.Skills is not null);

        // Whether the pay is plausible, and whether the date of birth can be real, is
        // decided by the rules in the handler.
    }
}

public sealed class UpdateGiverProfileCommandValidator : AbstractValidator<UpdateGiverProfileCommand>
{
    public UpdateGiverProfileCommandValidator()
    {
        // Only the ceiling is checked here. A blank name is refused by the handler with
        // BusinessNameRequired, and duplicating it here would replace that specific code
        // with a generic validation error the app cannot act on.
        When(x => x.Request.BusinessName is not null, () =>
            RuleFor(x => x.Request.BusinessName)
                .MaximumLength(150)
                .WithName("businessName"));

        When(x => x.Request.BusinessType is not null, () =>
            RuleFor(x => x.Request.BusinessType).MaximumLength(100).WithName("businessType"));

        When(x => x.Request.Description is not null, () =>
            RuleFor(x => x.Request.Description).MaximumLength(2000).WithName("description"));

        When(x => x.Request.Location is not null, () =>
            RuleFor(x => x.Request.Location).MaximumLength(200).WithName("location"));

        When(x => x.Request.ContactPerson is not null, () =>
            RuleFor(x => x.Request.ContactPerson).MaximumLength(120).WithName("contactPerson"));

        When(x => x.Request.ContactPhone is not null, () =>
            RuleFor(x => x.Request.ContactPhone).MaximumLength(20).WithName("contactPhone"));

        When(x => x.Request.GstNumber is not null, () =>
            RuleFor(x => x.Request.GstNumber)
                .MaximumLength(20)
                .Matches("^[A-Za-z0-9]+$")
                .WithMessage("A GST number is letters and digits only.")
                .WithName("gstNumber"));

        RuleFor(x => x.Request.Latitude)
            .InclusiveBetween(-90, 90)
            .WithMessage("Latitude must be between -90 and 90.")
            .WithName("latitude")
            .When(x => x.Request.Latitude is not null);

        RuleFor(x => x.Request.Longitude)
            .InclusiveBetween(-180, 180)
            .WithMessage("Longitude must be between -180 and 180.")
            .WithName("longitude")
            .When(x => x.Request.Longitude is not null);
    }
}

/// <summary>
/// The weekly grid. The count is bounded because the column exists to draw one week: a
/// body with more windows than that is a client bug, and rejecting it here is cheaper
/// than storing rows nobody will read. Whether each window makes sense is
/// <see cref="ProfileRules.ValidateSlot"/>, which is where the day and range rules live.
/// </summary>
public sealed class SetAvailabilityCommandValidator : AbstractValidator<SetAvailabilityCommand>
{
    private const int MaxSlots = 21;

    public SetAvailabilityCommandValidator()
    {
        RuleFor(x => x.Request.Slots)
            .NotNull()
            .Must(slots => slots.Count <= MaxSlots)
            .WithMessage($"A week holds at most {MaxSlots} availability windows.")
            .WithName("slots");
    }
}

/// <summary>The two saved-job actions address a job by id, so an empty one is a client bug.</summary>
public sealed class SaveJobCommandValidator : AbstractValidator<SaveJobCommand>
{
    public SaveJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty().WithMessage("A job is required.").WithName("jobId");
    }
}

public sealed class UnsaveJobCommandValidator : AbstractValidator<UnsaveJobCommand>
{
    public UnsaveJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty().WithMessage("A job is required.").WithName("jobId");
    }
}

/// <summary>
/// The heartbeat carries no body beyond the flag, so there is nothing to check. The
/// command is still validated so a malformed presence call is refused rather than
/// silently treated as "online".
/// </summary>
public sealed class SetPresenceCommandValidator : AbstractValidator<SetPresenceCommand>
{
    public SetPresenceCommandValidator()
    {
        RuleFor(x => x.Request).NotNull().WithMessage("Send isOnline as true or false.").WithName("isOnline");
    }
}

/// <summary>
/// The uploaded file has already been written by the time this runs, so these only guard
/// the two values that reach the database as-is.
/// </summary>
public sealed class AttachProfileImageCommandValidator : AbstractValidator<AttachProfileImageCommand>
{
    public AttachProfileImageCommandValidator()
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("The upload did not return a usable URL.")
            .WithName("file");

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(255)
            .WithName("file");
    }
}

public sealed class AddWorkerDocumentCommandValidator : AbstractValidator<AddWorkerDocumentCommand>
{
    public AddWorkerDocumentCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum().WithMessage("Unknown document type.").WithName("type");

        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255).WithName("file");

        RuleFor(x => x.Url)
            .NotEmpty()
            .MaximumLength(500)
            .WithMessage("The upload did not return a usable URL.")
            .WithName("file");
    }
}
