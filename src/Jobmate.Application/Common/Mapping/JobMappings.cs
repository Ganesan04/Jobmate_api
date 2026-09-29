using System.Globalization;
using Jobmate.Application.DTOs.Applications;
using Jobmate.Application.DTOs.Jobs;
using Jobmate.Application.DTOs.Ratings;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.ValueObjects;

namespace Jobmate.Application.Common.Mapping;

/// <summary>Entity to DTO projections for jobs, applications, assignments and ratings.</summary>
public static class JobMappings
{
    /// <summary>
    /// Projects a job. <paramref name="applicantCount"/> and
    /// <paramref name="viewerLat"/>/<paramref name="viewerLng"/> come from the query
    /// so the row count and distance are computed in SQL rather than per row in memory.
    /// </summary>
    public static JobDto ToDto(
        this Job job,
        int applicantCount = 0,
        double? viewerLat = null,
        double? viewerLng = null,
        IReadOnlyCollection<string>? skills = null)
    {
        var distance = Distance(job, viewerLat, viewerLng);

        return new JobDto
        {
            Id = job.Id,
            Title = job.Title,
            Company = string.IsNullOrWhiteSpace(job.Company) ? (job.Giver?.FullName ?? string.Empty) : job.Company,
            GiverId = job.GiverId,
            Category = job.Category,
            Description = job.Description,
            Date = job.JobDate,
            StartTime = FormatTime(job.StartTime),
            EndTime = FormatTime(job.EndTime),
            ReportTime = FormatTime(job.ReportTime),
            Pay = job.Pay,
            PayType = job.PayType,
            WorkersNeeded = job.WorkersNeeded,
            WorkersFilled = job.WorkersFilled,
            RemainingWorkers = job.RemainingWorkers,
            Area = job.Area,
            Address = job.Address,
            Applicants = applicantCount,
            Skills = [.. skills ?? job.Skills.Select(s => s.Name)],
            Experience = job.Experience ?? string.Empty,
            DistanceKm = Math.Round(distance, 2),
            Status = job.Status,
            WorkplaceType = job.WorkplaceType,
            JobType = job.JobType,
            BreakMinutes = job.BreakMinutes,
            OvertimeRate = job.OvertimeRate,
            TravelAllowance = job.TravelAllowance,
            FoodAllowance = job.FoodAllowance,
            PaymentDate = job.PaymentDate,
            PaymentMethod = job.PaymentMethod,
            IsRecurring = job.IsRecurring,
            Weekdays = job.Weekdays.Count > 0 ? [.. job.Weekdays.Order()] : [.. job.WeekdayRows.Select(w => w.DayOfWeek).Order()],
            DressCode = job.DressCode ?? string.Empty,
            PhysicalRequirement = job.PhysicalRequirements ?? string.Empty,
            VehicleRequired = job.VehicleRequired,
            SafetyInfo = job.SafetyInformation ?? string.Empty,
            EmergencyContact = job.EmergencyContact ?? string.Empty,
            ContactPerson = job.ContactPerson ?? string.Empty,
            LocationVisible = job.LocationVisible,
            IsUrgent = job.IsUrgent,
            CreatedAt = job.CreatedAt,
            PublishedAt = job.PublishedAt,
            CancelledAt = job.CancelledAt,
            CompletedAt = job.CompletedAt,
            ApplicationDeadline = job.ApplicationDeadline,
            Latitude = job.Latitude,
            Longitude = job.Longitude,
            TotalPayout = job.TotalPayout,
            TimeRange = $"{FormatTime(job.StartTime)} – {FormatTime(job.EndTime)}",
            ExperienceLabel = string.IsNullOrWhiteSpace(job.Experience) ? "Any experience" : job.Experience,
            CategoryLabel = job.Category.ToString()
        };
    }

    public static JobSummaryDto ToSummaryDto(this Job job) => new()
    {
        Id = job.Id,
        Title = job.Title,
        Company = string.IsNullOrWhiteSpace(job.Company) ? (job.Giver?.FullName ?? string.Empty) : job.Company,
        Category = job.Category,
        Status = job.Status,
        Pay = job.Pay,
        PayType = job.PayType,
        Area = job.Area,
        Date = job.JobDate,
        StartTime = FormatTime(job.StartTime),
        EndTime = FormatTime(job.EndTime)
    };

    public static JobApplicationDto ToDto(this JobApplication application, Job? job = null) => new()
    {
        Id = application.Id,
        JobId = application.JobId,
        WorkerId = application.WorkerId,
        Status = application.Status,
        Message = application.Message,
        ExpectedPay = application.ExpectedPay,
        AvailableFrom = application.AvailableFrom,
        AppliedAt = application.AppliedAt,
        ViewedAt = application.ViewedAt,
        ShortlistedAt = application.ShortlistedAt,
        RejectedAt = application.RejectedAt,
        WithdrawnAt = application.WithdrawnAt,
        AcceptedAt = application.AcceptedAt,
        RejectionReason = application.RejectionReason,
        ProposedPay = application.ProposedPay,
        Job = job?.ToSummaryDto()
    };

    /// <summary>
    /// Giver-side applicant view. <c>ApplicationStatus.Accepted</c> is presented as the
    /// Dart <c>ApplicantStatus.selected</c> value the Select Worker screen switches on.
    /// </summary>
    public static JobApplicantDto ToApplicantDto(
        this JobApplication application,
        User worker,
        double distanceKm = 0) => new()
    {
        Id = application.Id,
        JobId = application.JobId,
        Worker = worker.ToDto(),
        Status = ToApplicantStatus(application.Status),
        ProposedPay = application.ProposedPay,
        Message = application.Message,
        AppliedAt = application.AppliedAt,
        ExpectedPay = application.ExpectedPay,
        DistanceKm = Math.Round(distanceKm, 2),
        AvailableFrom = application.AvailableFrom,
        ApplicationStatus = application.Status
    };

    public static ApplicantStatus ToApplicantStatus(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Applied or ApplicationStatus.Viewed => ApplicantStatus.Applied,
        ApplicationStatus.Shortlisted => ApplicantStatus.Shortlisted,
        ApplicationStatus.Accepted => ApplicantStatus.Selected,
        ApplicationStatus.Rejected => ApplicantStatus.Rejected,
        ApplicationStatus.Withdrawn => ApplicantStatus.Applied,
        ApplicationStatus.Expired => ApplicantStatus.Rejected,
        _ => ApplicantStatus.Applied
    };

    /// <summary>
    /// Projects a booking.
    ///
    /// <paramref name="checkInCode"/> is passed in rather than read from the entity on
    /// purpose: the code proves a worker is on site, so it must only ever be handed to
    /// the employer who has to read it out. Leaving it out of the projection by default
    /// means a new caller cannot leak it by forgetting, and the employer fetches it from
    /// the dedicated check-in-code endpoint.
    /// </summary>
    public static WorkerAssignmentDto ToDto(
        this WorkerAssignment assignment,
        Job? job = null,
        string? workerName = null,
        string? checkInCode = null) => new()
    {
        Id = assignment.Id,
        JobId = assignment.JobId,
        WorkerId = assignment.WorkerId,
        GiverId = assignment.GiverId,
        AgreedPay = assignment.AgreedPay,
        ConfirmedAt = assignment.ConfirmedAt,
        PaymentStatus = assignment.PaymentStatus,
        CheckedInAt = assignment.CheckedInAt,
        CheckInCode = checkInCode,
        CompletedAt = assignment.CompletedAt,
        PaymentId = assignment.Payment?.Id.ToString(),
        PaidAt = assignment.PaidAt,
        Status = assignment.Status,
        OvertimeAmount = assignment.OvertimeAmount ?? 0,
        TotalPay = assignment.TotalPay,
        IsCheckedIn = assignment.CheckedInAt is not null,
        Job = job?.ToSummaryDto(),
        WorkerName = workerName
    };

    public static WorkerRatingDto ToDto(this Rating rating, string jobTitle, string fromName) => new()
    {
        Id = rating.Id,
        JobId = rating.JobId,
        WorkerId = rating.RatingDirection == RatingDirection.GiverToWorker ? rating.ToUserId : rating.FromUserId,
        GiverId = rating.RatingDirection == RatingDirection.GiverToWorker ? rating.FromUserId : rating.ToUserId,
        Stars = rating.Stars,
        Feedback = rating.Comment,
        CreatedAt = rating.CreatedAt,
        JobTitle = jobTitle,
        FromUserName = fromName
    };

    /// <summary>12-hour clock with am/pm, matching the strings stored in the Dart model.</summary>
    public static string FormatTime(TimeOnly time) =>
        DateTime.Today.Add(time.ToTimeSpan()).ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Payment reference in the <c>PAY-000123</c> shape the app displays.</summary>
    public static string FormatReference(Guid id) => PaymentMappings.ReceiptNumber(id);

    /// <summary>
    /// Distance fallback for a single row. List endpoints already receive the
    /// distance computed in SQL; this keeps single-job reads correct when the caller
    /// sent coordinates.
    /// </summary>
    public static double Distance(Job job, double? viewerLat, double? viewerLng)
    {
        if (viewerLat is null || viewerLng is null || job.Latitude is null || job.Longitude is null)
        {
            return 0;
        }

        return GeoDistance.Between(viewerLat.Value, viewerLng.Value, job.Latitude.Value, job.Longitude.Value);
    }

    private static readonly string[] MonthNames =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    /// <summary>Month bucket key, e.g. <c>2026-09</c>, used to group the wallet screen.</summary>
    public static string MonthKey(DateTime date) => $"{date.Year}-{date.Month:D2}";

    /// <summary>Display label, e.g. <c>September 2026</c>, matching the app's month grouping.</summary>
    public static string MonthLabel(string key)
    {
        var parts = key.Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var month) || month is < 1 or > 12)
        {
            return key;
        }

        return $"{MonthNames[month - 1]} {parts[0]}";
    }
}
