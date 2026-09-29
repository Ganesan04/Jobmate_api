using Jobmate.Application.Common;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Messaging;

/// <summary>
/// The authorisation check shared by every thread-scoped operation.
///
/// A thread has four entry points — read it, post to it, clear its badge, archive it — and
/// all four must refuse the same way. Without a single guard they drift: one answers 404
/// and another 403 for the same request, and a user cannot tell whether the thread is gone
/// or simply not theirs.
///
/// The two cases are deliberately different answers. "No such thread" is a 404, because the
/// id is wrong. "Not yours" is a 403, because a real thread exists and a user who used to
/// be in it deserves to be told that rather than told it never existed.
///
/// The check reads the participants of the requested thread and looks for the caller among
/// them, so it is a property of the thread rather than of anything the caller supplied.
/// </summary>
internal static class ConversationAccess
{
    public static async Task RequireMemberAsync(
        IAppDbContext db,
        Guid conversationId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        // One read answers both questions: a thread with no participants at all is one
        // that does not exist, and a thread whose participants exclude the caller is one
        // they may not touch.
        var participantIds = await db.ConversationParticipants
            .AsNoTracking()
            .Where(p => p.ConversationId == conversationId)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken);

        if (participantIds.Count == 0)
        {
            throw new NotFoundException("Conversation not found.");
        }

        if (!participantIds.Contains(callerId))
        {
            throw new ForbiddenException("You are not part of this conversation.");
        }
    }

    /// <summary>
    /// The caller's own membership row, for a handler that is about to write to it.
    ///
    /// Separated from the guard above so the authorisation decision and the row being
    /// written are two distinct reads: the first decides whether the write is allowed at
    /// all, and the second cannot be talked out of it.
    /// </summary>
    public static async Task<ConversationParticipant> RequireRowAsync(
        IAppDbContext db,
        Guid conversationId,
        Guid callerId,
        CancellationToken cancellationToken) =>
        await db.ConversationParticipants
            .FirstOrDefaultAsync(
                p => p.ConversationId == conversationId && p.UserId == callerId,
                cancellationToken)
            ?? throw new NotFoundException("Conversation not found.");
}
