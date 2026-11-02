using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Trust;

/// <summary>POST /support-reports — the report-a-problem screen.</summary>
public sealed record CreateSupportReportRequest
{
    public ReportReason Reason { get; init; }

    public string Details { get; init; } = string.Empty;

    public string? RelatedJobId { get; init; }
}
