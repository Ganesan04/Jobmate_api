using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Profiles.Media;

/// <summary>
/// Records a document the worker has just uploaded. POST /api/v1/profile/documents.
///
/// The file is written by the API before this command runs; the row here is what makes it
/// findable again. It starts <see cref="VerificationStatus.Pending"/> and is not evidence
/// of anything on its own; the verification request that points at it is what counts.
/// </summary>
public sealed record AddWorkerDocumentCommand(
    VerificationType Type,
    string FileName,
    string Url) : IRequest<WorkerDocumentDto>;

public sealed class AddWorkerDocumentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<AddWorkerDocumentCommand, WorkerDocumentDto>
{
    public async Task<WorkerDocumentDto> Handle(
        AddWorkerDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var profile = await db.WorkerProfiles
            .FirstOrDefaultAsync(p => p.UserId == callerId, cancellationToken)
            ?? throw new ForbiddenException("This account does not have a worker profile.");

        var document = new WorkerDocument
        {
            WorkerProfileId = profile.Id,
            Type = request.Type,
            FileName = request.FileName,
            Url = request.Url,
            Status = VerificationStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.WorkerDocuments.Add(document);

        await db.SaveChangesAsync(cancellationToken);

        return new WorkerDocumentDto
        {
            Id = document.Id,
            Type = document.Type,
            FileName = document.FileName,
            Url = document.Url,
            Status = document.Status,
            ReviewedAt = document.ReviewedAt
        };
    }
}

/// <summary>Who may read a stored document. Returned by the media access check.</summary>
/// <param name="OwnerUserId">The worker who uploaded it.</param>
public sealed record DocumentAccessDto(Guid OwnerUserId);

/// <summary>
/// Decides whether the caller may read one stored document. Used by
/// GET /api/v1/media/documents/{fileName} before the bytes are streamed.
///
/// A document backs a KYC check, so it is not public: only the person it belongs to may
/// read it. The file name alone is not enough to identify the
/// row, so the lookup is by the stored file name — which is generated, so it cannot be
/// guessed from a sequence.
///
/// The file bytes are streamed by the API. This query only answers the authorisation
/// question, so the Application layer never touches the file system.
/// </summary>
public sealed record GetDocumentAccessQuery(string FileName) : IRequest<DocumentAccessDto>;

public sealed class GetDocumentAccessQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetDocumentAccessQuery, DocumentAccessDto>
{
    public async Task<DocumentAccessDto> Handle(
        GetDocumentAccessQuery request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var ownerId = await db.WorkerDocuments
            .AsNoTracking()
            .Where(d => d.FileName == request.FileName)
            .Select(d => d.WorkerProfile!.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        // An unknown file name and someone else's document get the same answer on purpose.
        // A 403 here would confirm that the file exists, which turns this endpoint into a
        // way for any signed-in user to probe which documents have been uploaded; the
        // generated name is unguessable, but "not found" costs nothing and closes that off.
        if (ownerId == Guid.Empty || ownerId != callerId)
        {
            throw new NotFoundException("That file does not exist.");
        }

        return new DocumentAccessDto(ownerId);
    }
}
