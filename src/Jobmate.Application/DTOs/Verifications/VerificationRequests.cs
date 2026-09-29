using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Verifications;

/// <summary>POST /verifications — starts a verification request.</summary>
public sealed record SubmitVerificationRequest
{
    public VerificationType Type { get; init; }

    public string? DocumentName { get; init; }

    public string? DocumentUrl { get; init; }

    public string? Note { get; init; }
}

/// <summary>Admin decision on a verification request.</summary>
public sealed record ReviewVerificationRequest
{
    public string? Note { get; init; }

    public string? RejectionReason { get; init; }
}

/// <summary>
/// The caller's own verification history. GET /api/v1/verifications.
///
/// Also accepts <c>type</c> to filter to one kind of check, which is what the checklist
/// row's "retry" button links to.
/// </summary>
public sealed record GetMyVerificationsRequest : PageRequest
{
    public VerificationType? Type { get; init; }
}

/// <summary>
/// Withdraws a request the user no longer wants reviewed. Sent as its own action rather
/// than a delete because a rejected attempt is part of the history the user may appeal.
/// </summary>
public sealed record WithdrawVerificationRequest
{
    public string? Reason { get; init; }
}
