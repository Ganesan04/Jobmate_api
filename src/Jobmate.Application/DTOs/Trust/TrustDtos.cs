using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Trust;

/// <summary>Flutter <c>SupportReport</c> submitted from the report-a-problem screen.</summary>
public sealed record SupportReportDto
{
    public Guid Id { get; init; }

    public ReportReason Reason { get; init; }

    public string Details { get; init; } = string.Empty;

    public DateTime SubmittedAt { get; init; }

    public string RelatedJob { get; init; } = string.Empty;

    public bool IsResolved { get; init; }
}
