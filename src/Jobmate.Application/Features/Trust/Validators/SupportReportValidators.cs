using FluentValidation;
using Jobmate.Application.Features.Trust.Commands;

namespace Jobmate.Application.Features.Trust.Validators;

/// <summary>
/// Reason is required here because the report itself has no other content: without a
/// reason a bare "it was bad" lands in the queue with nothing to triage on. Details is
/// left to the handler's minimum, which can say the 10-character rule by name.
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
