using FluentValidation;
using Jobmate.Application.Features.Applications.Commands;
using Jobmate.Application.Features.Applications.Queries;
using Jobmate.Application.Features.Applications;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Features.Applications.Validators;

/// <summary>
/// Field rules for applying and for the giver's review actions. Whether the job is even
/// open is a domain question, answered by <c>ApplicationRules</c> once the job has been
/// loaded, so nothing here duplicates it.
/// </summary>
public sealed class ApplyToJobCommandValidator : AbstractValidator<ApplyToJobCommand>
{
    public ApplyToJobCommandValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty().WithMessage("A job is required.");

        RuleFor(x => x.Request.Message)
            .MaximumLength(2000).WithMessage("Your message cannot exceed 2000 characters.");

        RuleFor(x => x.Request.ExpectedPay)
            .GreaterThanOrEqualTo(0).When(x => x.Request.ExpectedPay.HasValue)
            .WithMessage("The pay you expect cannot be negative.");

        RuleFor(x => x.Request.AvailableFrom)
            .MaximumLength(120).WithMessage("Availability cannot exceed 120 characters.");
    }
}

public sealed class ShortlistApplicationCommandValidator : AbstractValidator<ShortlistApplicationCommand>
{
    public ShortlistApplicationCommandValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty().WithMessage("An application is required.");

        // A proposal is a starting point for a conversation, not a binding offer, so
        // there is no upper bound here beyond the int range.
        RuleFor(x => x.Request.ProposedPay)
            .GreaterThanOrEqualTo(0).When(x => x.Request.ProposedPay.HasValue)
            .WithMessage("The proposed pay cannot be negative.");
    }
}

public sealed class RejectApplicationCommandValidator : AbstractValidator<RejectApplicationCommand>
{
    public RejectApplicationCommandValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty().WithMessage("An application is required.");

        RuleFor(x => x.Request.Reason)
            .MaximumLength(1000).WithMessage("The reason cannot exceed 1000 characters.");
    }
}

/// <summary>
/// The applicant list's sort key is checked here rather than in the query, so a
/// mistyped key is a 400 with the field name instead of a silently re-sorted page.
/// </summary>
public sealed class GetJobApplicationsQueryValidator : AbstractValidator<GetJobApplicationsQuery>
{
    public GetJobApplicationsQueryValidator()
    {
        RuleFor(x => x.Request.SortBy)
            .Must(ApplicationQueryExtensions.IsKnownApplicantSortKey)
            .WithMessage("Sort by one of: newest, oldest, ratingHigh, ratingLow, expectedPayHigh, expectedPayLow, name.")
            .WithName("sortBy");

        RuleFor(x => x.Request.MinRating)
            .InclusiveBetween(0m, 5m).When(x => x.Request.MinRating.HasValue)
            .WithMessage("The minimum rating must be between 0 and 5.");
    }
}

public sealed class GetWorkerApplicationsQueryValidator : AbstractValidator<GetWorkerApplicationsQuery>
{
    public GetWorkerApplicationsQueryValidator()
    {
        RuleFor(x => x.Request.Statuses)
            .Must(statuses => statuses is null || statuses.Distinct().Count() == statuses.Count)
            .WithMessage("Do not repeat a status filter.")
            .WithName("statuses");
    }
}

/// <summary>
/// Pay bounds for a confirmed booking. The domain checks the same limits once the job
/// and applicant are loaded; repeating the cheap ones here means a mistyped zero comes
/// back as a field error instead of a rule failure further in.
/// </summary>
public sealed class SelectWorkerCommandValidator : AbstractValidator<SelectWorkerCommand>
{
    public SelectWorkerCommandValidator()
    {
        RuleFor(x => x.ApplicationId)
            .NotEmpty().WithMessage("An application is required.");

        RuleFor(x => x.Request.AgreedPay)
            .InclusiveBetween(1, SelectionRules.MaxReasonablePay)
            .When(x => x.Request.AgreedPay.HasValue)
            .WithMessage($"The agreed pay must be between 1 and {SelectionRules.MaxReasonablePay:N0}.")
            .WithName("agreedPay");

        RuleFor(x => x.Request.ProposedPay)
            .InclusiveBetween(1, SelectionRules.MaxReasonablePay)
            .When(x => x.Request.ProposedPay.HasValue)
            .WithMessage($"The proposed pay must be between 1 and {SelectionRules.MaxReasonablePay:N0}.")
            .WithName("proposedPay");
    }
}

public sealed class SelectWorkersCommandValidator : AbstractValidator<SelectWorkersCommand>
{
    public SelectWorkersCommandValidator()
    {
        RuleFor(x => x.JobId)
            .NotEmpty().WithMessage("A job is required.");

        RuleFor(x => x.Request.Selections)
            .NotEmpty().WithMessage("Select at least one worker.")
            .WithName("selections");

        RuleFor(x => x.Request.Selections)
            .Must(selections => selections.Values.All(pay => pay is >= 1 and <= SelectionRules.MaxReasonablePay))
            .WithMessage(
                $"The agreed pay for every worker must be between 1 and {SelectionRules.MaxReasonablePay:N0}.")
            .WithName("selections");
    }
}
