using FluentValidation;
using Jobmate.Application.Features.Verifications.Commands;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.Features.Verifications.Validators;

/// <summary>
/// Shape checks on a verification submission. Whether the check is even answerable — a
/// phone check on an account with no number — is decided by <c>VerificationRules</c> in the
/// handler, because that depends on the stored account rather than on the request.
/// </summary>
public sealed class SubmitVerificationCommandValidator : AbstractValidator<SubmitVerificationCommand>
{
    public SubmitVerificationCommandValidator()
    {
        RuleFor(x => x.Request)
            .NotNull()
            .WithMessage("Send the check you want reviewed.")
            .WithName("type");

        RuleFor(x => x.Request.Type)
            .IsInEnum()
            .WithMessage("Unknown verification type.")
            .WithName("type");

        When(x => x.Request.DocumentName is not null, () =>
            RuleFor(x => x.Request.DocumentName).MaximumLength(255).WithName("documentName"));

        When(x => x.Request.DocumentUrl is not null, () =>
            RuleFor(x => x.Request.DocumentUrl).MaximumLength(500).WithName("documentUrl"));

        When(x => x.Request.Note is not null, () =>
            RuleFor(x => x.Request.Note)
                .MaximumLength(500)
                .WithMessage("A note cannot be longer than 500 characters.")
                .WithName("note"));

        // Phone and email are confirmed against the account itself, so there is nothing to
        // attach; demanding a document for them would only produce uploads nobody reads.
        RuleFor(x => x.Request.DocumentName)
            .NotEmpty()
            .WithMessage("Attach the document you want checked.")
            .WithName("documentName")
            .When(x => x.Request.Type is VerificationType.Identity
                or VerificationType.Skills
                or VerificationType.Address);
    }
}

/// <summary>
/// A withdrawal note belongs to the applicant's own record, so it is optional and short.
/// </summary>
public sealed class WithdrawVerificationCommandValidator : AbstractValidator<WithdrawVerificationCommand>
{
    public WithdrawVerificationCommandValidator()
    {
        RuleFor(x => x.RequestId)
            .NotEmpty()
            .WithMessage("A verification request is required.")
            .WithName("id");

        When(x => x.Request?.Reason is not null, () =>
            RuleFor(x => x.Request!.Reason)
                .MaximumLength(500)
                .WithMessage("A reason cannot be longer than 500 characters.")
                .WithName("reason"));
    }
}
