using Asp.Versioning;
using Jobmate.Application.Common;
using Jobmate.Application.DTOs.Messaging;
using Jobmate.Application.Features.Messaging.Commands;
using Jobmate.Application.Features.Messaging.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Jobmate.Api.Controllers;

/// <summary>
/// Chat between the two people on a job.
///
/// Delivery is by polling: the app opens a thread and re-reads it on an interval, which
/// keeps chat on the same REST surface as everything else and leaves the unread counter on
/// the database as the single source of truth. Nothing here is written specifically for
/// polling — a message is persisted and summarised in one operation — so a push-based
/// transport can be added later without the handlers changing.
///
/// Every route is scoped to the signed-in caller by the handlers, and no route accepts
/// anybody else's id, so there is no way to read a conversation you are not in.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/conversations")]
[ApiVersion("1.0")]
[Produces("application/json")]
[Authorize]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
public sealed class MessagingController(IMediator mediator) : ControllerBase
{
    /// <summary>GET /api/v1/conversations — the caller's threads, most recently active first.</summary>
    /// <remarks>
    /// Each row carries the other person's name, the job the thread is about, the unread
    /// count and the last message. A thread with no messages yet still appears, sorted
    /// last, so a conversation opened from a job card is visible immediately. Pass
    /// <c>includeArchived=true</c> to see threads that have been cleared from the list.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ConversationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ConversationDto>>>> List(
        [FromQuery] GetConversationsRequest request,
        CancellationToken cancellationToken)
    {
        var conversations = await mediator.Send(new GetConversationsQuery(request), cancellationToken);

        return Ok(new ApiResponse<IReadOnlyList<ConversationDto>>(conversations));
    }

    /// <summary>POST /api/v1/conversations — opens, or re-opens, the thread with one person.</summary>
    /// <remarks>
    /// Send the other person's <c>participantId</c> and, when the question is about a
    /// particular job, its <c>jobId</c>. Both people must have been involved in that job.
    /// Asking for the same pair about the same job twice returns the thread that already
    /// exists rather than a second copy, so the job card's Message button and the chat
    /// list cannot end up with two threads between the same two people.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Messaging)]
    [ProducesResponseType(typeof(ApiResponse<ConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ConversationDto>>> Start(
        [FromBody] StartConversationRequest? request,
        CancellationToken cancellationToken)
    {
        var conversation = await mediator.Send(
            new StartConversationCommand(request ?? new StartConversationRequest()),
            cancellationToken);

        return Ok(new ApiResponse<ConversationDto>(conversation, "Conversation ready."));
    }

    /// <summary>GET /api/v1/conversations/{id} — the header of one thread.</summary>
    /// <remarks>The other person's name, the job context and the unread count, for the chat screen's title bar.</remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ConversationDto>>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var conversation = await mediator.Send(new GetConversationQuery(id), cancellationToken);

        return Ok(new ApiResponse<ConversationDto>(conversation));
    }

    /// <summary>GET /api/v1/conversations/{id}/messages — one page of a thread, oldest first.</summary>
    /// <remarks>
    /// <c>newestFirst=true</c> (the default the app uses) returns the most recent page in
    /// reading order, which is what a chat screen wants: page 1 is the bottom of the
    /// conversation. Pass <c>newestFirst=false</c> to walk backwards through history.
    /// Reading does not clear the badge — POST <c>/read</c> does, so the badge drops when
    /// the screen opens rather than when a page of messages happens to arrive.
    /// </remarks>
    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(typeof(PagedResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<MessageDto>>> Messages(
        Guid id,
        [FromQuery] GetMessagesRequest request,
        CancellationToken cancellationToken)
    {
        var page = await mediator.Send(new GetMessagesQuery(id, request), cancellationToken);

        return Ok(new PagedResponse<MessageDto>(page));
    }

    /// <summary>POST /api/v1/conversations/{id}/messages — posts one message.</summary>
    /// <remarks>
    /// The other participant's unread count goes up and they get a notification, so the
    /// message reaches them whether or not they are looking at the app. The sender's own
    /// badge stays at zero and the thread returns to the top of their list.
    /// </remarks>
    [HttpPost("{id:guid}/messages")]
    [EnableRateLimiting(RateLimitPolicies.Messaging)]
    [ProducesResponseType(typeof(ApiResponse<MessageDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MessageDto>>> Send(
        Guid id,
        [FromBody] SendMessageRequest? request,
        CancellationToken cancellationToken)
    {
        var message = await mediator.Send(
            new SendMessageCommand(id, request ?? new SendMessageRequest()),
            cancellationToken);

        return Created(
            $"/api/v1/conversations/{id}/messages",
            new ApiResponse<MessageDto>(message, "Message sent."));
    }

    /// <summary>POST /api/v1/conversations/{id}/read — clears this thread's unread badge.</summary>
    /// <remarks>
    /// Answers with how many messages were cleared, which is what the app subtracts from
    /// its local badge without a second round trip. Calling it on an already-read thread
    /// is accepted and returns 0, so a retried screen open is harmless.
    /// </remarks>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<int>>> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var cleared = await mediator.Send(new MarkConversationReadCommand(id), cancellationToken);

        return Ok(new ApiResponse<int>(cleared, "Conversation marked as read."));
    }

    /// <summary>POST /api/v1/conversations/{id}/archive — clears a thread from the list.</summary>
    /// <remarks>
    /// Hides the thread for the caller only and drops its unread count, so the badge stops
    /// counting a conversation that is no longer on screen. Nothing is deleted: the other
    /// person keeps their copy, and sending a new message in that thread brings it back.
    /// </remarks>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> Archive(
        Guid id,
        [FromBody] ArchiveConversationRequest? request,
        CancellationToken cancellationToken)
    {
        var archived = await mediator.Send(
            new ArchiveConversationCommand(id, request?.Archived ?? true),
            cancellationToken);

        return Ok(new ApiResponse<bool>(archived, archived ? "Conversation archived." : "Conversation restored."));
    }
}
