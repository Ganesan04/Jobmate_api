using FluentValidation;
using Jobmate.Application.Common;
using Jobmate.Application.Features.Jobs.Commands;
using Jobmate.Application.Features.Jobs.Queries;
using Jobmate.Domain.Common;
using Jobmate.Domain.ValueObjects;

namespace Jobmate.Application.Features.Jobs.Validators;

/// <summary>
/// Field rules for the six-step Create Job wizard. The cross-field checks (report time
/// before the shift, a deadline before the job date) live here rather than in the
/// handler so they are enforced before any database work happens.
/// </summary>
public sealed class CreateJobCommandValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobCommandValidator()
    {
        RuleFor(x => x.Request.Title)
            .NotEmpty().WithMessage("A job title is required.")
            .Length(3, 150).WithMessage("The title must be between 3 and 150 characters.");

        RuleFor(x => x.Request.Description)
            .NotEmpty().WithMessage("A job description is required.")
            .MaximumLength(4000).WithMessage("The description cannot exceed 4000 characters.");

        RuleFor(x => x.Request.WorkersNeeded)
            .InclusiveBetween(1, 100).WithMessage("You must hire between 1 and 100 workers.");

        RuleFor(x => x.Request.Pay)
            .InclusiveBetween(1, 1_000_000).WithMessage("Pay must be between 1 and 1000000.");

        RuleFor(x => x.Request.OvertimeRate)
            .GreaterThanOrEqualTo(0).When(x => x.Request.OvertimeRate.HasValue)
            .WithMessage("The overtime rate cannot be negative.");

        RuleFor(x => x.Request.TravelAllowance)
            .GreaterThanOrEqualTo(0).WithMessage("The travel allowance cannot be negative.");

        RuleFor(x => x.Request.FoodAllowance)
            .GreaterThanOrEqualTo(0).WithMessage("The food allowance cannot be negative.");

        RuleFor(x => x.Request.BreakMinutes)
            .InclusiveBetween(0, 240).WithMessage("A break cannot exceed 240 minutes.");

        RuleFor(x => x.Request.Area)
            .NotEmpty().WithMessage("An area is required so workers can find the job.")
            .MaximumLength(120);

        RuleFor(x => x.Request.Address)
            .MaximumLength(400);

        RuleFor(x => x.Request.Experience)
            .MaximumLength(200);

        RuleFor(x => x.Request.ContactPerson)
            .MaximumLength(120);

        RuleFor(x => x.Request.SafetyInfo)
            .MaximumLength(2000);

        RuleFor(x => x.Request.PhysicalRequirement)
            .MaximumLength(1000);

        RuleFor(x => x.Request.DressCode)
            .MaximumLength(200);

        RuleFor(x => x.Request.EmergencyContact)
            .MaximumLength(200);

        // Skills are trimmed and de-duplicated by the handler, so the rules only have
        // to bound what arrived.
        RuleForEach(x => x.Request.Skills)
            .NotEmpty()
            .MaximumLength(60).WithMessage("A skill cannot exceed 60 characters.")
            .When(x => x.Request.Skills is not null);

        RuleFor(x => x.Request.Skills)
            .Must(s => s is null || s.Count <= 20).WithMessage("A job can list at most 20 skills.");

        RuleForEach(x => x.Request.Weekdays)
            .InclusiveBetween(1, 7).WithMessage("Weekdays are ISO day numbers from 1 (Monday) to 7 (Sunday).");

        RuleFor(x => x.Request)
            .Must(r => !r.IsRecurring || r.Weekdays.Count > 0)
            .WithMessage("Select at least one weekday for a recurring job.")
            .WithName("weekdays");

        // The report time is when a worker should turn up, so it is at or before the
        // shift start.
        RuleFor(x => x.Request)
            .Must(r => r.ReportTime <= r.StartTime)
            .WithMessage("The report time must be at or before the start time.")
            .WithName("reportTime");

        RuleFor(x => x.Request)
            .Must(r => r.EndTime > r.StartTime)
            .WithMessage("The end time must be after the start time.")
            .WithName("endTime");

        // A job is scheduled work, so today is allowed and yesterday is not. The
        // comparison is on the UTC date, which is the same day or the day before for
        // every timezone the app serves, so no slack day is needed here.
        RuleFor(x => x.Request.Date)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Choose today or a future date.");

        RuleFor(x => x.Request.ApplicationDeadline)
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.Request.ApplicationDeadline.HasValue)
            .WithMessage("The application deadline cannot be in the past.");

        RuleFor(x => x.Request)
            .Must(r => r.ApplicationDeadline is null || r.ApplicationDeadline <= r.Date)
            .WithMessage("The application deadline must be on or before the job date.")
            .WithName("applicationDeadline");

        When(x => x.Request.Latitude.HasValue != x.Request.Longitude.HasValue, () =>
        {
            RuleFor(x => x.Request.Latitude)
                .NotNull().WithMessage("Provide both a latitude and a longitude.");

            RuleFor(x => x.Request.Longitude)
                .NotNull().WithMessage("Provide both a latitude and a longitude.");
        });

        RuleFor(x => x.Request.Latitude)
            .Must(v => v is null || GeoDistance.IsValidLatitude(v.Value))
            .WithMessage("Latitude must be between -90 and 90.");

        RuleFor(x => x.Request.Longitude)
            .Must(v => v is null || GeoDistance.IsValidLongitude(v.Value))
            .WithMessage("Longitude must be between -180 and 180.");
    }
}

/// <summary>Edit rules. Every field is optional, so a partial update is valid by design.</summary>
public sealed class UpdateJobCommandValidator : AbstractValidator<UpdateJobCommand>
{
    public UpdateJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty().WithMessage("A job id is required.");

        RuleFor(x => x.Request.Title)
            .Length(3, 150).When(x => x.Request.Title is not null)
            .WithMessage("The title must be between 3 and 150 characters.");

        RuleFor(x => x.Request.Description)
            .MaximumLength(4000).When(x => x.Request.Description is not null);

        RuleFor(x => x.Request.WorkersNeeded)
            .InclusiveBetween(1, 100).When(x => x.Request.WorkersNeeded.HasValue)
            .WithMessage("You must hire between 1 and 100 workers.");

        RuleFor(x => x.Request.Pay)
            .InclusiveBetween(1, 1_000_000).When(x => x.Request.Pay.HasValue)
            .WithMessage("Pay must be between 1 and 1000000.");

        RuleFor(x => x.Request.OvertimeRate)
            .GreaterThanOrEqualTo(0).When(x => x.Request.OvertimeRate.HasValue);

        RuleFor(x => x.Request.TravelAllowance)
            .GreaterThanOrEqualTo(0).When(x => x.Request.TravelAllowance.HasValue);

        RuleFor(x => x.Request.FoodAllowance)
            .GreaterThanOrEqualTo(0).When(x => x.Request.FoodAllowance.HasValue);

        RuleFor(x => x.Request.BreakMinutes)
            .InclusiveBetween(0, 240).When(x => x.Request.BreakMinutes.HasValue);

        RuleFor(x => x.Request.Area)
            .MaximumLength(120).When(x => x.Request.Area is not null);

        RuleFor(x => x.Request.Address)
            .MaximumLength(400).When(x => x.Request.Address is not null);

        RuleForEach(x => x.Request.Skills!)
            .NotEmpty()
            .MaximumLength(60)
            .When(x => x.Request.Skills is not null);

        RuleFor(x => x.Request.Skills)
            .Must(s => s is null || s.Count <= 20).WithMessage("A job can list at most 20 skills.");

        RuleForEach(x => x.Request.Weekdays!)
            .InclusiveBetween(1, 7)
            .When(x => x.Request.Weekdays is not null);

        RuleFor(x => x.Request.Latitude)
            .Must(v => v is null || GeoDistance.IsValidLatitude(v.Value))
            .WithMessage("Latitude must be between -90 and 90.");

        RuleFor(x => x.Request.Longitude)
            .Must(v => v is null || GeoDistance.IsValidLongitude(v.Value))
            .WithMessage("Longitude must be between -180 and 180.");
    }
}

/// <summary>Cancel needs a reason long enough to be useful to an applicant.</summary>
public sealed class CancelJobCommandValidator : AbstractValidator<CancelJobCommand>
{
    public CancelJobCommandValidator()
    {
        RuleFor(x => x.JobId).NotEmpty();

        RuleFor(x => x.Request.Reason)
            .MaximumLength(500).When(x => x.Request is not null)
            .WithMessage("The cancellation reason cannot exceed 500 characters.");
    }
}

/// <summary>Search query-string rules, including the documented sort keys.</summary>
public sealed class SearchJobsQueryValidator : AbstractValidator<SearchJobsQuery>
{
    public SearchJobsQueryValidator()
    {
        RuleFor(x => x.Request.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);

        RuleFor(x => x.Request.Search)
            .MaximumLength(200).When(x => x.Request.Search is not null);

        RuleFor(x => x.Request.Area)
            .MaximumLength(120).When(x => x.Request.Area is not null);

        RuleFor(x => x.Request.MinPay)
            .GreaterThanOrEqualTo(0).When(x => x.Request.MinPay.HasValue);

        RuleFor(x => x.Request.MaxPay)
            .GreaterThanOrEqualTo(0).When(x => x.Request.MaxPay.HasValue);

        RuleFor(x => x.Request)
            .Must(r => r.MinPay is null || r.MaxPay is null || r.MinPay <= r.MaxPay)
            .WithMessage("The minimum pay cannot be greater than the maximum pay.")
            .WithName("minPay");

        RuleFor(x => x.Request)
            .Must(r => r.StartDate is null || r.EndDate is null || r.StartDate <= r.EndDate)
            .WithMessage("The start date must not be after the end date.")
            .WithName("startDate");

        RuleFor(x => x.Request.RadiusKm)
            .InclusiveBetween(0.1, 500).When(x => x.Request.RadiusKm.HasValue)
            .WithMessage("The radius must be between 0.1 and 500 km.");

        RuleFor(x => x.Request.Latitude)
            .Must(v => v is null || GeoDistance.IsValidLatitude(v.Value));

        RuleFor(x => x.Request.Longitude)
            .Must(v => v is null || GeoDistance.IsValidLongitude(v.Value));

        RuleFor(x => x.Request.SortBy)
            .Must(s => JobQueryExtensions.IsKnownSortKey(s))
            .WithMessage($"Sort must be one of: {string.Join(", ", JobQueryExtensions.AllSortKeys)}.")
            .WithName("sortBy");
    }
}

public sealed class GetGiverJobsQueryValidator : AbstractValidator<GetGiverJobsQuery>
{
    public GetGiverJobsQueryValidator()
    {
        RuleFor(x => x.Request.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.Request.Search).MaximumLength(200).When(x => x.Request.Search is not null);
    }
}

public sealed class GetWorkerJobsQueryValidator : AbstractValidator<GetWorkerJobsQuery>
{
    public GetWorkerJobsQueryValidator()
    {
        RuleFor(x => x.Request.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.Request.Search).MaximumLength(200).When(x => x.Request.Search is not null);
        RuleFor(x => x.Request.Area).MaximumLength(120).When(x => x.Request.Area is not null);
        RuleFor(x => x.Request.MinPay).GreaterThanOrEqualTo(0).When(x => x.Request.MinPay.HasValue);
        RuleFor(x => x.Request.MaxPay).GreaterThanOrEqualTo(0).When(x => x.Request.MaxPay.HasValue);
        RuleFor(x => x.Request.RadiusKm)
            .InclusiveBetween(0.1, 500).When(x => x.Request.RadiusKm.HasValue)
            .WithMessage("The radius must be between 0.1 and 500 km.");
        RuleFor(x => x.Request.Latitude).Must(v => v is null || GeoDistance.IsValidLatitude(v.Value));
        RuleFor(x => x.Request.Longitude).Must(v => v is null || GeoDistance.IsValidLongitude(v.Value));
        RuleFor(x => x.Request.SortBy)
            .Must(s => JobQueryExtensions.IsKnownSortKey(s))
            .WithName("sortBy");
    }
}
