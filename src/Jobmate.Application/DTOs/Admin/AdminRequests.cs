using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Admin;

/// <summary>
/// GET /admin/verifications — the review queue.
///
/// Defaults to pending and oldest first, because the queue an admin works through is
/// the one that is waiting, not the one they added an hour ago. The other statuses are
/// reachable by asking for them explicitly.
/// </summary>
public sealed record GetAdminVerificationsRequest : PageRequest
{
    /// <summary>Pending, the queue default. Pass rejected or approved to see decided work.</summary>
    public VerificationRequestStatus Status { get; init; } = VerificationRequestStatus.Pending;

    public VerificationType? Type { get; init; }

    /// <summary>Free text over the applicant's name, phone number and document name.</summary>
    public string? Search { get; init; }
}

/// <summary>GET /admin/users — the users table.</summary>
public sealed record GetAdminUsersRequest : PageRequest
{
    public UserRole? Role { get; init; }

    /// <summary>Filter to active or suspended accounts. Null returns both.</summary>
    public bool? IsActive { get; init; }

    public VerificationStatus? Verification { get; init; }

    /// <summary>Matches name, email or phone number.</summary>
    public string? Search { get; init; }
}

/// <summary>GET /admin/jobs — the jobs table.</summary>
public sealed record GetAdminJobsRequest : PageRequest
{
    public JobStatus? Status { get; init; }

    public JobCategory? Category { get; init; }

    /// <summary>Matches the job title or the hiring company.</summary>
    public string? Search { get; init; }
}

/// <summary>
/// POST /admin/users/{id}/status — suspends or reinstates an account.
///
/// One field rather than two endpoints, because "make this account active" and "make it
/// inactive" are the same decision and an admin should not have to remember which verb
/// means which.
/// </summary>
public sealed record SetUserStatusRequest
{
    public bool IsActive { get; init; }

    /// <summary>Recorded on the notification the suspended user receives.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// GET /admin/support-reports — the support and safety report queue.
///
/// Reports are not filtered to open by default, unlike the dispute queue: a support report
/// has no counterpart waiting on an answer, so an admin sweeping the list wants to see
/// what has already been dealt with as well as what has not.
/// </summary>
public sealed record GetAdminSupportReportsRequest : PageRequest
{
    public bool? IsResolved { get; init; }
}
