using System.Net;

namespace Jobmate.Domain.Exceptions;

/// <summary>
/// Base for every deliberate business-rule failure. Carries the HTTP status the
/// API should surface so controllers never translate errors themselves.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    protected DomainException(string message, Exception? innerException, HttpStatusCode statusCode = HttpStatusCode.BadRequest)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Optional machine-readable code the Flutter client can branch on.</summary>
    public virtual string ErrorCode { get; protected init; } = "DomainError";
}

/// <summary>A domain invariant was violated. Surfaces as HTTP 400.</summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message, string errorCode = "BusinessRuleViolation") : base(message) => ErrorCode = errorCode;
}

/// <summary>Requested resource does not exist. Surfaces as HTTP 404.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message, HttpStatusCode.NotFound) => ErrorCode = "NotFound";
}

/// <summary>Request conflicts with current state, e.g. a duplicate application.</summary>
public class ConflictException : DomainException
{
    public ConflictException(string message, string errorCode = "Conflict") : base(message, HttpStatusCode.Conflict) => ErrorCode = errorCode;

    /// <summary>
    /// Used when a conflict is only known because the database refused the write, such
    /// as a unique index turning two identical requests into one row. The original
    /// failure is kept so it still reaches the logs.
    /// </summary>
    public ConflictException(string message, Exception innerException, string errorCode = "Conflict")
        : base(message, innerException, HttpStatusCode.Conflict) => ErrorCode = errorCode;
}

/// <summary>Caller is authenticated but not permitted. Surfaces as HTTP 403.</summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base(message, HttpStatusCode.Forbidden) => ErrorCode = "Forbidden";
}

/// <summary>Caller is not authenticated, or credentials are invalid. Surfaces as HTTP 401.</summary>
public sealed class UnauthorizedException : DomainException
{
    public UnauthorizedException(string message) : base(message, HttpStatusCode.Unauthorized) { }
}

/// <summary>
/// A job's worker capacity was exhausted by a concurrent request. Surfaces as
/// HTTP 409 so the Flutter client can refresh and retry.
/// </summary>
public sealed class WorkerLimitReachedException : ConflictException
{
    public WorkerLimitReachedException(int workersNeeded, int alreadySelected)
        : base($"This job needs {workersNeeded} worker(s) and {alreadySelected} are already selected.", "WorkerLimitReached")
    {
        WorkersNeeded = workersNeeded;
        AlreadySelected = alreadySelected;
    }

    public int WorkersNeeded { get; }

    public int AlreadySelected { get; }
}
