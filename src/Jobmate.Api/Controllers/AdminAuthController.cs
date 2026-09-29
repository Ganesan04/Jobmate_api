using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Application.Features.Auth.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// The admin console's sign-in. Separate from <see cref="AuthController"/> because
/// the admin surface is email-only and a non-admin account must never be able to
/// authenticate here even with a correct password.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/admin/auth")]
[ApiVersion("1.0")]
[Produces("application/json")]
public sealed class AdminAuthController(IMediator mediator) : ControllerBase
{
    /// <summary>POST /api/v1/admin/auth/login — email and password, admin role only.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Login(
        [FromBody] AdminLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new AdminLoginCommand(request.Email, request.Password), cancellationToken);

        return Ok(new ApiResponse<AuthResultDto>(result, "Signed in to the admin console."));
    }
}
