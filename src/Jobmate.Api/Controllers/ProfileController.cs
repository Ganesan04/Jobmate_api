using Asp.Versioning;
using Jobmate.Api.Media;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Profiles;
using Jobmate.Application.Features.Profiles.Commands;
using Jobmate.Application.Features.Profiles.Media;
using Jobmate.Application.Features.Profiles.Queries;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// The caller's own profile, and the reduced public view of anyone else's.
///
/// Two shapes rather than one on purpose: the owner screen needs phone, email and
/// documents, and none of that may reach a stranger. The public endpoint returns a
/// different type so those fields cannot be added back by accident.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/profile")]
[ApiVersion("1.0")]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class ProfileController(IMediator mediator, LocalMediaStore mediaStore) : ControllerBase
{
    /// <summary>GET /api/v1/profile/me — everything the profile screen shows.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<MyProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MyProfileDto>>> Me(CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(new GetMyProfileQuery(), cancellationToken);

        return Ok(new ApiResponse<MyProfileDto>(profile));
    }

    /// <summary>GET /api/v1/profile/{userId} — another user's public profile.</summary>
    /// <remarks>
    /// No phone number, email or coordinates. The app shows this from a chat thread and
    /// from an application, and contact details are exchanged in the conversation itself.
    /// </remarks>
    [HttpGet("{userId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<PublicProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PublicProfileDto>>> Public(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(new GetPublicProfileQuery(userId), cancellationToken);

        return Ok(new ApiResponse<PublicProfileDto>(profile));
    }

    /// <summary>PUT /api/v1/profile/worker — saves the worker edit-profile form.</summary>
    /// <remarks>
    /// Partial: send only what changed, and <c>skills</c> replaces the whole list. Omit
    /// <c>skills</c> to leave it alone; send <c>[]</c> to clear it.
    /// </remarks>
    [HttpPut("worker")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(ApiResponse<WorkerProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<WorkerProfileDto>>> UpdateWorker(
        [FromBody] UpdateWorkerProfileRequest? request,
        CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(
            new UpdateWorkerProfileCommand(request ?? new UpdateWorkerProfileRequest()),
            cancellationToken);

        return Ok(new ApiResponse<WorkerProfileDto>(profile, "Profile saved."));
    }

    /// <summary>PUT /api/v1/profile/giver — saves the employer edit-profile form.</summary>
    [HttpPut("giver")]
    [Authorize(Roles = "WorkGiver")]
    [ProducesResponseType(typeof(ApiResponse<GiverProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<GiverProfileDto>>> UpdateGiver(
        [FromBody] UpdateGiverProfileRequest? request,
        CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(
            new UpdateGiverProfileCommand(request ?? new UpdateGiverProfileRequest()),
            cancellationToken);

        return Ok(new ApiResponse<GiverProfileDto>(profile, "Profile saved."));
    }

    /// <summary>PUT /api/v1/profile/availability — replaces the weekly availability grid.</summary>
    /// <remarks>
    /// The whole week in one call, because the app edits it as one screen. An empty
    /// <c>slots</c> array clears the week.
    /// </remarks>
    [HttpPut("availability")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(ApiResponse<WorkerProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<WorkerProfileDto>>> SetAvailability(
        [FromBody] SetAvailabilityRequest? request,
        CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(
            new SetAvailabilityCommand(request ?? new SetAvailabilityRequest()),
            cancellationToken);

        return Ok(new ApiResponse<WorkerProfileDto>(profile, "Availability saved."));
    }

    /// <summary>PUT /api/v1/profile/presence — the online / offline heartbeat.</summary>
    /// <remarks>
    /// Call with <c>isOnline: true</c> when the app comes to the foreground and false when
    /// it is backgrounded. This is the flag behind the green dot in the chat list.
    /// </remarks>
    [HttpPut("presence")]
    [Authorize(Roles = "Worker")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> SetPresence(
        [FromBody] SetPresenceRequest? request,
        CancellationToken cancellationToken)
    {
        var profile = await mediator.Send(
            new SetPresenceCommand(request ?? new SetPresenceRequest { IsOnline = true }),
            cancellationToken);

        return Ok(new ApiResponse<UserProfileDto>(profile));
    }

    /// <summary>POST /api/v1/profile/image — uploads a profile photo and attaches it.</summary>
    /// <remarks>
    /// Multipart with a single <c>file</c> part. JPEG, PNG, WebP or PDF up to the
    /// configured limit. The previous photo is left on disk and simply stops being linked,
    /// so a failed re-upload cannot leave the profile with no image.
    /// </remarks>
    [HttpPost("image")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Media)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<UserProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> UploadImage(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new BusinessRuleException("Attach a file as 'file'.", "MissingFile");
        }

        var stored = await mediaStore.SaveAsync(file, MediaKind.ProfileImage, cancellationToken);

        var profile = await mediator.Send(
            new AttachProfileImageCommand(stored.Url, stored.FileName),
            cancellationToken);

        return Ok(new ApiResponse<UserProfileDto>(profile, "Profile photo updated."));
    }

    /// <summary>POST /api/v1/profile/documents — uploads an identity or skill document.</summary>
    /// <remarks>
    /// Multipart: a <c>file</c> part and a <c>type</c> field of identity, phone, email,
    /// skills or address. The document is stored and listed as pending; send its
    /// <c>url</c> to <c>POST /verifications</c> to have it reviewed. Documents are private
    /// to the worker who uploaded them and to an admin.
    /// </remarks>
    [HttpPost("documents")]
    [Authorize(Roles = "Worker")]
    [EnableRateLimiting(RateLimitPolicies.Media)]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<WorkerDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<WorkerDocumentDto>>> UploadDocument(
        IFormFile? file,
        [FromForm] VerificationType? type,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new BusinessRuleException("Attach a file as 'file'.", "MissingFile");
        }

        var stored = await mediaStore.SaveAsync(file, MediaKind.Document, cancellationToken);

        var document = await mediator.Send(
            new AddWorkerDocumentCommand(type ?? VerificationType.Identity, stored.FileName, stored.Url),
            cancellationToken);

        return Created(
            stored.Url,
            new ApiResponse<WorkerDocumentDto>(document, "Document uploaded."));
    }
}

/// <summary>
/// Serves uploaded files back.
///
/// Split into two routes because the two families have different audiences: a profile
/// photo is shown to anyone signed in, while an identity document is readable only by the
/// worker who uploaded it and by an admin. One route for both would mean either
/// publishing everyone's passports or hiding every avatar.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/media")]
[ApiVersion("1.0")]
[Authorize]
public sealed class MediaController(IMediator mediator, LocalMediaStore mediaStore) : ControllerBase
{
    /// <summary>GET /api/v1/media/profile-images/{fileName} — an avatar.</summary>
    [HttpGet("profile-images/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public IActionResult ProfileImage(string fileName) => Send(MediaKind.ProfileImage, fileName);

    /// <summary>GET /api/v1/media/documents/{fileName} — an identity or skill document.</summary>
    [HttpGet("documents/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Document(string fileName, CancellationToken cancellationToken)
    {
        // Authorisation is answered by the Application layer against the document row; a
        // name that is not a document this caller may read is a 404 here, so the response
        // does not distinguish "no such file" from "not yours".
        await mediator.Send(new GetDocumentAccessQuery(fileName), cancellationToken);

        return Send(MediaKind.Document, fileName);
    }

    /// <summary>
    /// Streams one stored file.
    ///
    /// The path is resolved through the store, which rejects anything that is not a name
    /// it could itself have written, so a traversal attempt in the URL is a 404 rather
    /// than a read of something outside the media folder.
    /// </summary>
    private IActionResult Send(MediaKind kind, string fileName)
    {
        var path = mediaStore.Resolve(kind, fileName);

        if (path is null || !System.IO.File.Exists(path))
        {
            throw new NotFoundException("That file does not exist.");
        }

        // The stored type came from the uploader's Content-Type header, so the browser is
        // told not to second-guess it and render an "image" as HTML.
        Response.Headers.XContentTypeOptions = "nosniff";

        return PhysicalFile(path, LocalMediaStore.ContentTypeFor(path), enableRangeProcessing: true);
    }
}
