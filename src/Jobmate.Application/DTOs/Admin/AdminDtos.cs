using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Admin;

/// <summary>
/// A support report as the admin queue shows it. Carries the reporter's name and id,
/// which the user's own copy deliberately does not need.
/// </summary>
public sealed record SupportReportAdminDto
{
    public Guid Id { get; init; }

    public ReportReason Reason { get; init; }

    public string Details { get; init; } = string.Empty;

    public DateTime SubmittedAt { get; init; }

    public string ReportedBy { get; init; } = string.Empty;

    public Guid ReportedByUserId { get; init; }

    /// <summary>Kept as the stored text form, so the admin can tell a bad id from no id.</summary>
    public string? RelatedJobId { get; init; }

    public bool IsResolved { get; init; }

    public DateTime? ResolvedAt { get; init; }
}

/// <summary>Flutter <c>AdminUser</c> — a row in the admin users table.</summary>
public sealed record AdminUserDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public UserRole Role { get; init; }

    public VerificationStatus Verification { get; init; }

    public bool IsActive { get; init; }

    public DateTime JoinedOn { get; init; }

    /// <summary>Jobs posted for givers, applications made for workers.</summary>
    public int JobsCount { get; init; }

    public string Area { get; init; } = string.Empty;

    public decimal Rating { get; init; }

    public int? PhoneLast4 { get; init; }

    public DateTime? LastLoginAt { get; init; }
}

/// <summary>
/// Flutter <c>AdminStats</c> — the dashboard rollup. Computed by the server in one
/// aggregated query so the app never has to count hundreds of rows itself.
/// </summary>
public sealed record AdminStatsDto
{
    public int TotalUsers { get; init; }

    public int Workers { get; init; }

    public int WorkGivers { get; init; }

    public int ActiveJobs { get; init; }

    public int Applications { get; init; }

    public int CompletedJobs { get; init; }

    /// <summary>Total settled payout volume, matching the app's "Payments" tile.</summary>
    public long Payments { get; init; }

    public int VerificationRequests { get; init; }

    public int OpenDisputes { get; init; }

    public int TotalJobs { get; init; }

    public int OpenDisputesUnderReview { get; init; }

    public int NewUsersThisWeek { get; init; }

    public decimal TotalPayoutThisMonth { get; init; }
}

/// <summary>
/// Flutter <c>VerificationRequest</c> (admin view): id, userId, userName, userRole,
/// type, submittedAt, status, document, note.
/// </summary>
public sealed record AdminVerificationRequestDto
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string UserName { get; init; } = string.Empty;

    public UserRole UserRole { get; init; }

    public VerificationType Type { get; init; }

    public DateTime SubmittedAt { get; init; }

    /// <summary>Maps 1:1 onto the Flutter <c>VerificationStatus</c> values the app filters on.</summary>
    public VerificationStatus Status { get; init; }

    public string Document { get; init; } = string.Empty;

    public string? Note { get; init; }

    public string? RejectionReason { get; init; }

    public DateTime? ReviewedAt { get; init; }

    public string? ReviewedBy { get; init; }

    public string UserPhone { get; init; } = string.Empty;
}

/// <summary>A job row in the admin jobs table.</summary>
public sealed record AdminJobDto
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Company { get; init; } = string.Empty;

    public Guid GiverId { get; init; }

    public string GiverName { get; init; } = string.Empty;

    public JobCategory Category { get; init; }

    public JobStatus Status { get; init; }

    public int Pay { get; init; }

    public PayType PayType { get; init; }

    public int WorkersNeeded { get; init; }

    public int WorkersFilled { get; init; }

    public int Applicants { get; init; }

    public string Area { get; init; } = string.Empty;

    public DateOnly JobDate { get; init; }

    public DateTime CreatedAt { get; init; }
}

/// <summary>Admin job detail, including the applicant and booking breakdown.</summary>
public sealed record AdminJobDetailDto
{
    public AdminJobDto Job { get; init; } = new();

    public int TotalApplications { get; init; }

    public int ShortlistedCount { get; init; }

    public int SelectedCount { get; init; }

    public int PaidCount { get; init; }

    public decimal TotalPaid { get; init; }

    public int OpenDisputes { get; init; }
}
