namespace Jobmate.Domain.Enums;

/// <summary>
/// In-app notification kind. Serialised as camelCase and matches the Flutter
/// <c>NotificationType</c> enum exactly, including <c>jobAccepted</c>,
/// <c>jobShortlisted</c>, <c>jobReminder</c> and <c>newApplicant</c>.
/// </summary>
public enum NotificationType
{
    ApplicationUpdate = 1,
    JobAccepted = 2,
    JobShortlisted = 3,
    Payment = 4,
    JobReminder = 5,
    NewApplicant = 6,
    System = 7
}

/// <summary>Deep-link target stored on a notification, mirroring Flutter <c>targetRoute</c>.</summary>
public enum NotificationReferenceType
{
    Job = 1,
    Application = 2,
    Assignment = 3,
    Payment = 4,
    Conversation = 5,
    Dispute = 6,
    Verification = 7,
    None = 8
}

/// <summary>Booking lifecycle. Serialised as camelCase for the worker and giver views.</summary>
public enum AssignmentStatus
{
    Pending = 1,
    Active = 2,
    CheckedIn = 3,
    Completed = 4,
    Cancelled = 5
}

/// <summary>Free-text message vs system event inside a conversation.</summary>
public enum MessageType
{
    Text = 1,
    System = 2
}

/// <summary>Platform a push token belongs to.</summary>
public enum DevicePlatform
{
    Android = 1,
    Ios = 2,
    Web = 3
}
