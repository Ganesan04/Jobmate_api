using FluentValidation;
using Jobmate.Application.Features.Admin.Commands;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.Features.Admin.Validators;

/// <summary>
/// Admin-side shape checks. Whether a decision is legal — a request that has already been
/// reviewed, a rejection with no reason — belongs to <c>VerificationRules</c> and the
/// command handler, because that is a question about the stored row rather than about
/// the body.
/// </summary>
public sealed class ApproveVerificationCommandValidator : AbstractValidator<ApproveVerificationCommand>
{
    public ApproveVerificationCommandValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("A verification request is required.")
            .WithName("id");

        When(x => x.Request.Note is not null, () =>
            RuleFor(x => x.Request.Note)
                .MaximumLength(500)
                .WithMessage("A note cannot be longer than 500 characters.")
                .WithName("note"));
    }
}

public sealed class RejectVerificationCommandValidator : AbstractValidator<RejectVerificationCommand>
{
    public RejectVerificationCommandValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("A verification request is required.")
            .WithName("id");

        // Only the ceiling is checked here. A missing or blank reason is refused by the
        // handler with RejectionReasonRequired, and duplicating it here would replace that
        // specific code with a generic validation error the admin screen cannot act on.
        When(x => x.Request.RejectionReason is not null, () =>
            RuleFor(x => x.Request.RejectionReason)
                .MaximumLength(500)
                .WithMessage("A rejection reason cannot be longer than 500 characters.")
                .WithName("rejectionReason"));
    }
}

public sealed class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("An account is required.")
            .WithName("id");

        When(x => x.Request.Reason is not null, () =>
            RuleFor(x => x.Request.Reason)
                .MaximumLength(500)
                .WithMessage("A reason cannot be longer than 500 characters.")
                .WithName("reason"));
    }
}
