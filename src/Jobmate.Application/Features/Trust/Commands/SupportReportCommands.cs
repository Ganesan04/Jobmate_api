using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.DTOs.Trust;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Trust.Commands;

/// <summary>
/// Raises a support or safety report. POST /api/v1/support-reports.
///
/// A report does not need a booking: somebody can report a fake job listing they only
/// ever saw, and that has no counterpart to argue with.
/// </summary>
public sealed record CreateSupportReportCommand(CreateSupportReportRequest Request)
    : IRequest<SupportReportDto>;

public sealed class CreateSupportReportCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<CreateSupportReportCommand, SupportReportDto>
{
    public async Task<SupportReportDto> Handle(
        CreateSupportReportCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        if (string.IsNullOrWhiteSpace(input.Details) || input.Details.Trim().Length < 10)
        {
            throw new BusinessRuleException(
                "Describe the problem in at least 10 characters.",
                "DetailsTooShort");
        }

        var report = new SupportReport
        {
            UserId = callerId,
            Reason = input.Reason,
            Details = input.Details.Trim(),
            RelatedJobId = string.IsNullOrWhiteSpace(input.RelatedJobId) ? null : input.RelatedJobId.Trim(),
            IsResolved = false,
            CreatedAt = now
        };

        db.SupportReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        // The column holds the job id as text, so it is parsed here rather than compared in
        // SQL: Guid.ToString() is not translatable, and an unparseable value simply means
        // the report is not tied to a job we can still name.
        var relatedId = Guid.TryParse(report.RelatedJobId, out var parsed) ? parsed : (Guid?)null;

        var related = relatedId is null
            ? null
            : await db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == relatedId.Value)
                .Select(j => j.Title)
                .FirstOrDefaultAsync(cancellationToken);

        return new SupportReportDto
        {
            Id = report.Id,
            Reason = report.Reason,
            Details = report.Details,
            SubmittedAt = report.CreatedAt,
            RelatedJob = related ?? string.Empty,
            IsResolved = report.IsResolved
        };
    }
}
