using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Trust;
using Jobmate.Application.Features.Trust.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Reporting a problem. A report needs no booking: a fake job listing somebody only ever
/// saw has no other party to argue with, and refusing to let anybody report those would
/// lose exactly the abuse cases that matter most.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/support-reports")]
[ApiVersion("1.0")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class SupportReportController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/support-reports — files a report for the team to look at.</summary>
    /// <remarks>
    /// Describe the problem in a sentence or two. <c>relatedJobId</c> is optional; include
    /// it when the report is about one particular booking.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    [ProducesResponseType(typeof(ApiResponse<SupportReportDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SupportReportDto>>> Create(
        [FromBody] CreateSupportReportRequest? request,
        CancellationToken cancellationToken)
    {
        var report = await mediator.Send(
            new CreateSupportReportCommand(request ?? new CreateSupportReportRequest()),
            cancellationToken);

        return Created($"/api/v1/support-reports/{report.Id}", new ApiResponse<SupportReportDto>(report));
    }
}
