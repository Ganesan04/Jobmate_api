using FluentValidation;
using Jobmate.Application.Features.Trust.Commands;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.Features.Trust.Validators;

/// <summary>
/// Length and shape only. Whether the caller is allowed to raise the dispute, and whether
/// the description is actually long enough to be useful, are decisions the domain rules
/// make — duplicating them here would replace their specific error codes with generic ones.
/// </summary>
public sealed class CreateDisputeCommandValidator : AbstractValidator<CreateDisputeCommand>
{
    public CreateDisputeCommandValidator()
    {
        RuleFor(x => x.Request.JobId)
            .NotEmpty()
            .WithMessage("Choose the booking the dispute is about.")
            .WithName("jobId");

        RuleFor(x => x.Request.Reason)
            .IsInEnum()
            .WithMessage("Choose what the dispute is about.")
            .WithName("reason");

        RuleFor(x => x.Request.Description)
            .MaximumLength(2000)
            .WithMessage("A description cannot be longer than 2000 characters.")
            .WithName("description");

        // A negative amount is left to the handler, which refuses it as AmountInvalid.
        // Checking it here as well would replace that specific code with a generic
        // validation error, and the same complaint has to be refused with no user id at
        // all so an amount cannot be probed before the dispute exists.
    }
}

/// <summary>
/// Only the ceiling. A missing resolution is refused by the handler with
/// ResolutionRequired, and saying so here would hide that behind a generic validation
/// error the admin screen cannot act on.
/// </summary>
public sealed class ResolveDisputeCommandValidator : AbstractValidator<ResolveDisputeCommand>
{
    public ResolveDisputeCommandValidator()
    {
        RuleFor(x => x.Request.Status)
            .IsInEnum()
            .WithMessage("A dispute closes as resolved or rejected.")
            .WithName("status");

        When(x => x.Request.Resolution is not null, () =>
            RuleFor(x => x.Request.Resolution)
                .MaximumLength(4000)
                .WithMessage("Resolution notes cannot be longer than 4000 characters.")
                .WithName("resolution"));
    }
}

/// <summary>
/// Reason is required here because the report itself has no other content: without a
/// reason a bare "it was bad" lands in the queue with nothing to triage on. Details is
/// left to the domain's minimum, which can say the 10-character rule by name.
/// </summary>
public sealed class CreateSupportReportCommandValidator : AbstractValidator<CreateSupportReportCommand>
{
    public CreateSupportReportCommandValidator()
    {
        RuleFor(x => x.Request.Reason)
            .IsInEnum()
            .WithMessage("Choose what the report is about.")
            .WithName("reason");

        RuleFor(x => x.Request.Details)
            .MaximumLength(2000)
            .WithMessage("A report cannot be longer than 2000 characters.")
            .WithName("details");

        RuleFor(x => x.Request.RelatedJobId)
            .MaximumLength(36)
            .WithMessage("That is not a job reference this app can open.")
            .WithName("relatedJobId");
    }
}
