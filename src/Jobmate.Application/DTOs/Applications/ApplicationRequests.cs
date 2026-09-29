using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Applications;

/// <summary>POST /jobs/{jobId}/applications — the Flutter apply screen payload.</summary>
public sealed record ApplyToJobRequest
{
    public string? Message { get; init; }

    public int? ExpectedPay { get; init; }

    /// <summary>Flutter <c>JobApplication.availableFrom</c>, default "Immediately available".</summary>
    public string? AvailableFrom { get; init; }
}

/// <summary>GET /jobs/{jobId}/applications filters.</summary>
public sealed record GetApplicationsRequest : Domain.Common.PageRequest
{
    public List<ApplicationStatus>? Statuses { get; init; }

    public string? Search { get; init; }

    /// <summary>Minimum worker rating, used by the giver's applicant filter.</summary>
    public decimal? MinRating { get; init; }

    public string? SortBy { get; init; }
}

/// <summary>GET /worker/applications filters.</summary>
public sealed record GetWorkerApplicationsRequest : Domain.Common.PageRequest
{
    public List<ApplicationStatus>? Statuses { get; init; }

    public JobStatus? JobStatus { get; init; }
}

/// <summary>POST /applications/{id}/shortlist|reject|select.</summary>
public sealed record ReviewApplicationRequest
{
    public string? Reason { get; init; }

    /// <summary>Pay the giver proposes, surfaced as <c>JobApplicant.proposedPay</c>.</summary>
    public int? ProposedPay { get; init; }
}

/// <summary>POST /applications/{id}/select — the agreed pay for the booking.</summary>
public sealed record SelectWorkerRequest
{
    /// <summary>Defaults to the posted pay when omitted.</summary>
    public int? AgreedPay { get; init; }

    /// <summary>Prefer this pay as the default for the selected worker.</summary>
    public int? ProposedPay { get; init; }
}

/// <summary>
/// Bulk selection, matching the confirm-selection step on the Flutter
/// Select Worker screen which confirms every chosen applicant in one action.
/// </summary>
public sealed record SelectWorkersRequest
{
    /// <summary>Application id to agreed pay. One entry per worker to book.</summary>
    public Dictionary<Guid, int> Selections { get; init; } = [];

    /// <summary>Release shortlisted workers that were not selected back to applied.</summary>
    public bool ReleaseUnselectedShortlists { get; init; } = true;
}

/// <summary>POST /assignments/{id}/check-in.</summary>
public sealed record CheckInRequest
{
    public string Code { get; init; } = string.Empty;
}

/// <summary>POST /assignments/{id}/complete.</summary>
public sealed record CompleteAssignmentRequest
{
    public int? OvertimeAmount { get; init; }
}

/// <summary>GET /worker/work-history filters.</summary>
public sealed record WorkHistoryRequest : Domain.Common.PageRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public PaymentStatus? PaymentStatus { get; init; }

    public Guid? JobId { get; init; }
}
