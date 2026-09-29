using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Trust;

/// <summary>
/// Flutter <c>Dispute</c>: id, reason, raisedBy, againstUser, jobTitle, company,
/// raisedAt, status, amount, details, notes.
/// </summary>
public sealed record DisputeDto
{
    public Guid Id { get; init; }

    public ReportReason Reason { get; init; }

    /// <summary>Name of the user who raised it (Flutter shows a name, not an id).</summary>
    public string RaisedBy { get; init; } = string.Empty;

    public Guid RaisedByUserId { get; init; }

    public string AgainstUser { get; init; } = string.Empty;

    public Guid AgainstUserId { get; init; }

    public string JobTitle { get; init; } = string.Empty;

    public string Company { get; init; } = string.Empty;

    public Guid JobId { get; init; }

    public DateTime RaisedAt { get; init; }

    public DisputeStatus Status { get; init; }

    public int Amount { get; init; }

    public string Details { get; init; } = string.Empty;

    /// <summary>Admin resolution notes (Flutter <c>Dispute.notes</c>).</summary>
    public string Notes { get; init; } = string.Empty;

    public string? ResolvedBy { get; init; }

    public DateTime? ResolvedAt { get; init; }
}

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
