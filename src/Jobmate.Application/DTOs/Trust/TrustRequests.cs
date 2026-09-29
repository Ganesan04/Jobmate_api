using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Trust;

/// <summary>POST /disputes.</summary>
public sealed record CreateDisputeRequest
{
    public Guid JobId { get; init; }

    public ReportReason Reason { get; init; }

    public string Description { get; init; } = string.Empty;

    public int Amount { get; init; }
}

/// <summary>POST /admin/disputes/{id}/resolve.</summary>
public sealed record ResolveDisputeRequest
{
    public DisputeStatus Status { get; init; } = DisputeStatus.Resolved;

    public string Resolution { get; init; } = string.Empty;
}

/// <summary>POST /support-reports — the report-a-problem screen.</summary>
public sealed record CreateSupportReportRequest
{
    public ReportReason Reason { get; init; }

    public string Details { get; init; } = string.Empty;

    public string? RelatedJobId { get; init; }
}
