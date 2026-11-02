using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>Verification request rules and the projection of a request onto the badge.</summary>
public static class VerificationRules
{
    /// <summary>
    /// Raises a verification request, approved on the spot. There is no reviewer: the
    /// request is the person's own declaration, and the badge it feeds is the person's,
    /// shown the same way in worker and work giver mode.
    /// </summary>
    public static VerificationRequest Create(User user, VerificationType type, string? documentName, string? note, DateTime now)
    {
        if (type == VerificationType.Phone && !user.PhoneNumber.StartsWith("+91", StringComparison.Ordinal))
        {
            throw new BusinessRuleException("Add a valid mobile number before verifying it.", "PhoneMissing");
        }

        if (type == VerificationType.Email && string.IsNullOrWhiteSpace(user.Email))
        {
            throw new BusinessRuleException("Add an email address before verifying it.", "EmailMissing");
        }

        return new VerificationRequest
        {
            UserId = user.Id,
            Type = type,
            Status = VerificationRequestStatus.Approved,
            DocumentName = documentName?.Trim(),
            Note = note?.Trim(),
            ReviewedAt = now,
            ReviewedByUserId = null,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Folds a decision into the per-type state a reviewer sees.
    ///
    /// The overall badge is deliberately not computed here: it needs every type at once,
    /// which is what <see cref="VerificationSummary"/> is for. Folding one decision into a
    /// running badge cannot know whether the other three are done, and a second
    /// implementation of the fold is a second set of answers to the same question.
    /// </summary>
    public static VerificationStatus StatusFor(VerificationRequestStatus decision) =>
        decision switch
        {
            VerificationRequestStatus.Approved => VerificationStatus.Verified,
            VerificationRequestStatus.Rejected => VerificationStatus.Rejected,

            // Pending and withdrawn are the same to a reviewer — nobody is looking at this
            // — but they must not collapse into one value. A withdrawn request is not under
            // review, so it reads as not verified; without this case it fell through to
            // pending and the badge stayed amber after the applicant took the request back.
            VerificationRequestStatus.Pending => VerificationStatus.Pending,
            VerificationRequestStatus.Withdrawn => VerificationStatus.NotVerified,

            // A status added later should fail to compile here rather than inherit
            // whichever value happened to be the fallback.
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision),
                decision,
                "Verification request status is not mapped to a badge state.")
        };
}

/// <summary>
/// The per-type verification state of one user, as the trust badge screen reads it.
/// </summary>
/// <param name="Identity">ID document check.</param>
/// <param name="Phone">Mobile number check.</param>
/// <param name="Email">Email address check.</param>
/// <param name="Skills">Trade or certificate check.</param>
/// <param name="Address">Address check. Not part of the overall badge.</param>
/// <param name="Overall">The single badge the profile screen shows.</param>
/// <param name="VerifiedCount">How many of identity, phone, email and skills are approved.</param>
public sealed record VerificationRollup(
    VerificationStatus Identity,
    VerificationStatus Phone,
    VerificationStatus Email,
    VerificationStatus Skills,
    VerificationStatus Address,
    VerificationStatus Overall,
    int VerifiedCount)
{
    /// <summary>
    /// The badge state for one type, so a caller projecting a history row can label it with
    /// the same value the badge uses instead of re-deriving it.
    /// </summary>
    public VerificationStatus StatusOf(VerificationType type) => type switch
    {
        VerificationType.Identity => Identity,
        VerificationType.Phone => Phone,
        VerificationType.Email => Email,
        VerificationType.Skills => Skills,
        VerificationType.Address => Address,
        _ => VerificationStatus.NotVerified
    };
}

/// <summary>
/// Folds a user's verification requests into the badge the profile screen shows.
///
/// Reads the <em>latest</em> request per type, not the set of all of them. A user who was
/// rejected for a bad ID photo and then re-uploaded a good one is verified for identity:
/// judging them on their first attempt would leave them permanently badged as rejected
/// with no way out, which is exactly the state this is meant to escape.
///
/// A type with no request at all counts as not verified rather than as a failure, so an
/// employer who has verified nothing yet sees an honest empty badge instead of a red one.
/// </summary>
public static class VerificationSummary
{
    /// <summary>
    /// The types that make up the overall badge. Address is excluded: confirming where
    /// someone lives is a separate concern from who they are, and gating the badge on it
    /// would fail every user who has simply not moved house yet.
    /// </summary>
    private static readonly VerificationType[] BadgeTypes =
    [
        VerificationType.Identity,
        VerificationType.Phone,
        VerificationType.Email,
        VerificationType.Skills
    ];

    public static VerificationRollup Build(IEnumerable<VerificationRequest> requests)
    {
        var latest = requests
            .GroupBy(r => r.Type)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).First());

        var statuses = BadgeTypes.Concat([VerificationType.Address])
            .ToDictionary(type => type, type => StatusFor(latest, type));

        var verifiedCount = BadgeTypes.Count(type => statuses[type] == VerificationStatus.Verified);

        return new VerificationRollup(
            statuses[VerificationType.Identity],
            statuses[VerificationType.Phone],
            statuses[VerificationType.Email],
            statuses[VerificationType.Skills],
            statuses[VerificationType.Address],
            Overall(statuses, verifiedCount),
            verifiedCount);
    }

    private static VerificationStatus StatusFor(
        IReadOnlyDictionary<VerificationType, VerificationRequest> latest,
        VerificationType type)
    {
        if (!latest.TryGetValue(type, out var request))
        {
            return VerificationStatus.NotVerified;
        }

        // One mapping from a decision to a badge state, so a reviewer reading a queue row
        // and a profile reading the badge cannot be told two different things.
        return VerificationRules.StatusFor(request.Status);
    }

    /// <summary>
    /// One badge from five states. Rejection outranks everything because a user who has
    /// been told no needs to see that, not a green tick for the four types that passed.
    /// </summary>
    private static VerificationStatus Overall(
        IReadOnlyDictionary<VerificationType, VerificationStatus> statuses,
        int verifiedCount)
    {
        var all = Enum.GetValues<VerificationType>()
            .Where(type => type != VerificationType.Address)
            .Select(type => statuses[type])
            .ToList();

        if (all.Any(status => status == VerificationStatus.Rejected))
        {
            return VerificationStatus.Rejected;
        }

        if (all.Any(status => status == VerificationStatus.Pending))
        {
            return VerificationStatus.Pending;
        }

        return verifiedCount == BadgeTypes.Length ? VerificationStatus.Verified : VerificationStatus.NotVerified;
    }
}
