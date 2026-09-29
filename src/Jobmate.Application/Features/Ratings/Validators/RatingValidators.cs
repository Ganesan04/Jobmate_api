using FluentValidation;
using Jobmate.Application.Features.Ratings.Commands;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Features.Ratings.Validators;

/// <summary>
/// Field names are the request body's, so the app can attach each message to the stars
/// box or the feedback field the rater was actually typing in.
/// </summary>
public sealed class CreateRatingCommandValidator : AbstractValidator<CreateRatingCommand>
{
    public CreateRatingCommandValidator()
    {
        RuleFor(x => x.Request.JobId)
            .NotEmpty()
            .WithMessage("A job is required.")
            .WithName("jobId");

        RuleFor(x => x.Request.ToUserId)
            .NotEmpty()
            .WithMessage("Choose who you are rating.")
            .WithName("toUserId");

        RuleFor(x => x.Request.Stars)
            .InclusiveBetween(RatingRules.MinStars, RatingRules.MaxStars)
            .WithMessage($"Rating must be between {RatingRules.MinStars} and {RatingRules.MaxStars}.")
            .WithName("stars");

        RuleFor(x => x.Request.Feedback)
            .MaximumLength(RatingRules.MaxCommentLength)
            .WithMessage($"Feedback cannot exceed {RatingRules.MaxCommentLength} characters.")
            .When(x => x.Request.Feedback is not null);
    }
}

/// <summary>The check-in code is four digits; the domain rule is the authority on the value.</summary>
public sealed class CheckInAssignmentCommandValidator : AbstractValidator<CheckInAssignmentCommand>
{
    public CheckInAssignmentCommandValidator()
    {
        RuleFor(x => x.AssignmentId)
            .NotEmpty()
            .WithMessage("A booking is required.")
            .WithName("id");

        RuleFor(x => x.Request.Code)
            .NotEmpty()
            .WithMessage("Enter the check-in code.")
            .Matches("^[0-9]{4}$")
            .WithMessage("The check-in code is four digits.")
            .WithName("code");
    }
}

/// <summary>
/// Closing a booking may record overtime, which becomes part of the payout, so it is
/// held to the same ceiling the payment rules use.
/// </summary>
public sealed class CompleteAssignmentCommandValidator : AbstractValidator<CompleteAssignmentCommand>
{
    public CompleteAssignmentCommandValidator()
    {
        RuleFor(x => x.AssignmentId)
            .NotEmpty()
            .WithMessage("A booking is required.")
            .WithName("id");

        RuleFor(x => x.Request.OvertimeAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Overtime cannot be negative.")
            .When(x => x.Request.OvertimeAmount is not null);
    }
}
