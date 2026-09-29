using System.Security.Claims;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.Common.Interfaces;

/// <summary>Result of an OTP delivery attempt. The code is returned only in development.</summary>
public sealed record OtpDispatchResult(bool Success, string? Code, string Message);

/// <summary>
/// One-time password delivery. The development implementation writes the code to the
/// log and returns it in the API response so the Flutter app can be tested without
/// an SMS vendor. Swap in an SMS gateway behind this interface later.
/// </summary>
public interface IOtpService
{
    Task<OtpDispatchResult> SendAsync(string phoneNumber, string code, OtpPurpose purpose, CancellationToken cancellationToken = default);
}

/// <summary>A short-lived delivery result for a password reset link or code.</summary>
public sealed record PasswordResetDispatchResult(bool Success, string? Token, string Message);

/// <summary>
/// Password reset delivery. Mirrors <see cref="IOtpService"/> so no vendor SDK
/// reaches the Application layer.
/// </summary>
public interface IPasswordResetService
{
    Task<PasswordResetDispatchResult> SendAsync(string phoneNumber, string token, CancellationToken cancellationToken = default);
}

/// <summary>A verified JWT pair plus the identity claims the Flutter app needs.</summary>
public sealed record TokenPair(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    Guid UserId,
    UserRole Role);

/// <summary>Issues and validates access / refresh tokens.</summary>
public interface ITokenService
{
    TokenPair CreateTokens(Domain.Entities.User user, string? deviceId = null, string? ipAddress = null);

    /// <summary>Validates an access token and returns its principal, or null when invalid.</summary>
    ClaimsPrincipal? ValidateAccessToken(string token);
}

/// <summary>Hashes and verifies passwords. BCrypt in production.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

/// <summary>Request to charge a worker for a booking.</summary>
public sealed record PaymentChargeRequest(
    Guid PaymentId,
    int Amount,
    int OvertimeAmount,
    int PlatformFee,
    int TotalAmount,
    string Currency,
    PaymentMethod Method,
    string IdempotencyKey,
    string? Description);

/// <summary>Provider outcome for a charge attempt.</summary>
public sealed record PaymentChargeResult(
    bool Success,
    string? TransactionReference,
    string? FailureReason,
    string? RawResponse)
{
    public static PaymentChargeResult Succeeded(string reference, string? raw = null) =>
        new(true, reference, null, raw);

    public static PaymentChargeResult Failed(string reason, string? raw = null) =>
        new(false, null, reason, raw);
}

/// <summary>A refund request against a settled payment.</summary>
public sealed record PaymentRefundRequest(Guid PaymentId, int Amount, string Reason, string IdempotencyKey);

/// <summary>Provider outcome for a refund attempt.</summary>
public sealed record PaymentRefundResult(bool Success, string? Reference, string? FailureReason);

/// <summary>
/// Payment gateway abstraction. <c>MockPaymentService</c> is the only implementation
/// until a real PSP (Razorpay / Cashfree / Stripe) is chosen; because the Application
/// layer only sees this interface, wiring one in requires no handler changes.
/// </summary>
public interface IPaymentService
{
    string ProviderName { get; }

    Task<PaymentChargeResult> ChargeAsync(PaymentChargeRequest request, CancellationToken cancellationToken = default);

    Task<PaymentRefundResult> RefundAsync(PaymentRefundRequest request, CancellationToken cancellationToken = default);

    /// <summary>Verifies a webhook signature. Always false for the mock provider.</summary>
    Task<bool> VerifyWebhookSignatureAsync(string payload, string? signature, CancellationToken cancellationToken = default);
}

/// <summary>A push payload addressed to one user.</summary>
public sealed record PushNotification(
    Guid UserId,
    string Title,
    string Body,
    NotificationType Type,
    NotificationReferenceType ReferenceType,
    Guid? ReferenceId,
    string? Route);

/// <summary>Outcome of a push dispatch attempt.</summary>
public sealed record PushDispatchResult(bool Success, int DevicesTargeted, string Message);

/// <summary>
/// Push delivery abstraction. The development implementation logs the payload; an
/// FCM-backed implementation replaces it without touching any handler, because
/// notifications are always persisted to the database first.
/// </summary>
public interface IPushNotificationService
{
    Task<PushDispatchResult> SendAsync(PushNotification notification, CancellationToken cancellationToken = default);

    Task<PushDispatchResult> SendManyAsync(IReadOnlyCollection<PushNotification> notifications, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reserves and releases a job's worker slots.
///
/// Booking a worker is the one place in the product where two requests can honestly
/// disagree: two givers confirming the last slot at the same moment, or the same
/// application being selected twice by a double tap. Reading the job, checking the
/// count and then writing it back is a race, so the count is changed by a single
/// conditional <c>UPDATE</c> that the database itself evaluates while holding the row
/// lock. The caller's read is only ever used to explain the failure.
///
/// This lives behind an interface so the SQL stays in Infrastructure and the
/// Application layer keeps no provider dependency.
/// </summary>
public interface IWorkerSlotStore
{
    /// <summary>
    /// Increments <c>workers_filled</c> by one, but only while the job is still
    /// selectable and below its <c>workers_needed</c>. Returns false when the slot could
    /// not be taken, in which case nothing was written.
    /// </summary>
    Task<bool> TryReserveSlotAsync(Guid jobId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-app notification funnel. Handlers depend on this rather than writing
/// <c>Notification</c> rows directly, so persistence, push dispatch and unread
/// bookkeeping stay in one place.
/// </summary>
public interface INotificationService
{
    /// <summary>Persists one notification for a single user and queues a push.</summary>
    Task<Domain.Entities.Notification> SendAsync(
        Guid userId,
        Domain.Enums.NotificationType type,
        string title,
        string message,
        Domain.Enums.NotificationReferenceType referenceType = Domain.Enums.NotificationReferenceType.None,
        Guid? referenceId = null,
        string? route = null,
        CancellationToken cancellationToken = default);

    /// <summary>Persists one notification per distinct recipient, for fan-out events.</summary>
    Task SendManyAsync(
        IEnumerable<Guid> userIds,
        Domain.Enums.NotificationType type,
        string title,
        string message,
        Domain.Enums.NotificationReferenceType referenceType = Domain.Enums.NotificationReferenceType.None,
        Guid? referenceId = null,
        string? route = null,
        CancellationToken cancellationToken = default);
}
