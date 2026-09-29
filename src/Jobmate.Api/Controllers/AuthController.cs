using Asp.Versioning;
using FluentValidation;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Auth;
using Jobmate.Application.Features.Auth.Commands;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Authentication: registration with phone OTP, sign-in, token rotation and
/// password recovery. These are the only endpoints that do not require a bearer
/// token.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/auth")]
[ApiVersion("1.0")]
[Produces("application/json")]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// POST /api/v1/auth/register — creates an unverified account and sends the OTP.
    /// </summary>
    /// <remarks>
    /// The Flutter flow is register → /otp → role selection → profile setup, so this
    /// endpoint returns <c>requiresOtpVerification: true</c> and a token pair that
    /// the app stores while the code is entered.
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(ToCommand(request), cancellationToken);

        return Ok(new ApiResponse<AuthResultDto>(result, "Account created. Enter the verification code to continue."));
    }

    /// <summary>POST /api/v1/auth/verify-otp — confirms the phone and completes registration.</summary>
    [HttpPost("verify-otp")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> VerifyOtp(
        [FromBody] VerifyOtpRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new VerifyOtpCommand(request.Phone, request.Code, request.Role),
            cancellationToken);

        return Ok(new ApiResponse<AuthResultDto>(result, "Phone number verified."));
    }

    /// <summary>POST /api/v1/auth/resend-otp — issues a fresh code, superseding any outstanding one.</summary>
    [HttpPost("resend-otp")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<OtpDispatchDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<OtpDispatchDto>>> ResendOtp(
        [FromBody] ResendOtpRequest request,
        CancellationToken cancellationToken)
    {
        var issued = await mediator.Send(
            new ResendOtpCommand(request.Phone, request.Purpose),
            cancellationToken);

        var payload = new OtpDispatchDto
        {
            Phone = issued.Phone,
            Code = issued.Code,
            Message = issued.Message,
            ExpiresInSeconds = issued.ExpiresInSeconds
        };

        return Ok(new ApiResponse<OtpDispatchDto>(payload, "A new verification code has been sent."));
    }

    /// <summary>POST /api/v1/auth/login — signs in with an email address or a mobile number.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new LoginCommand(request.Identifier, request.Password), cancellationToken);

        return Ok(new ApiResponse<AuthResultDto>(result));
    }

    /// <summary>POST /api/v1/auth/refresh — rotates the refresh token and returns a new pair.</summary>
    /// <remarks>Presenting an already-rotated token revokes the whole token family as theft.</remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResultDto>>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);

        return Ok(new ApiResponse<AuthResultDto>(result));
    }

    /// <summary>POST /api/v1/auth/logout — revokes one session, or every session when no token is sent.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object?>>> Logout(
        [FromBody] LogoutRequest? request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();

        await mediator.Send(new LogoutCommand(userId, request?.RefreshToken), cancellationToken);

        return Ok(new ApiResponse<object?>(null, "Signed out."));
    }

    /// <summary>POST /api/v1/auth/forgot-password — always the same response, registered or not.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<GenericMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<GenericMessageDto>>> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ForgotPasswordCommand(request.Phone), cancellationToken);

        return Ok(new ApiResponse<GenericMessageDto>(result));
    }

    /// <summary>POST /api/v1/auth/reset-password — verifies the code and revokes all sessions.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(typeof(ApiResponse<GenericMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<GenericMessageDto>>> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetPasswordCommand(request.Phone, request.Code, request.NewPassword),
            cancellationToken);

        return Ok(new ApiResponse<GenericMessageDto>(result));
    }

    private Guid CurrentUserId() =>
        Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                   ?? throw new UnauthorizedException("You must be signed in to do that."));

    /// <summary>
    /// Maps the request onto the command, normalising the name into first and last
    /// so the users table stays split rather than storing one display string.
    /// </summary>
    private static RegisterCommand ToCommand(RegisterRequest request) => new(
        request.Name,
        request.Phone,
        request.Email,
        request.Password,
        request.Role,
        request.Area);
}
