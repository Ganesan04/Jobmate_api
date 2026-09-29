using FluentValidation;
using Jobmate.Application.Features.Messaging.Commands;
using Jobmate.Domain.Rules;

namespace Jobmate.Application.Features.Messaging.Validators;

/// <summary>Field names are the request body's, so the app can attach each message to the box being typed in.</summary>
public sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty()
            .WithMessage("A conversation is required.")
            .WithName("id");

        // A system message is written by the API, never by a user, so the body is optional
        // for that kind and required for everything else.
        RuleFor(x => x.Request.Text)
            .NotEmpty()
            .WithMessage("Type a message before sending.")
            .MaximumLength(MessagingRules.MaxMessageLength)
            .WithMessage($"A message cannot be longer than {MessagingRules.MaxMessageLength} characters.")
            .When(x => x.Request.MessageType == Domain.Enums.MessageType.Text);

        RuleFor(x => x.Request.MessageType)
            .IsInEnum()
            .WithMessage("Unknown message type.")
            .WithName("messageType");
    }
}

public sealed class StartConversationCommandValidator : AbstractValidator<StartConversationCommand>
{
    public StartConversationCommandValidator()
    {
        RuleFor(x => x.Request.ParticipantId)
            .NotEmpty()
            .WithMessage("Choose who you want to message.")
            .WithName("participantId");

        // Optional, but a GUID that is not all zeroes is far more likely a mistyped id than
        // a deliberate "no job", and it would otherwise fail later as a missing job.
        RuleFor(x => x.Request.JobId)
            .NotEqual(Guid.Empty)
            .WithMessage("That job reference is not valid.")
            .WithName("jobId")
            .When(x => x.Request.JobId is not null);
    }
}

public sealed class ArchiveConversationCommandValidator : AbstractValidator<ArchiveConversationCommand>
{
    public ArchiveConversationCommandValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty()
            .WithMessage("A conversation is required.")
            .WithName("id");
    }
}
