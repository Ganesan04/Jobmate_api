using FluentValidation;
using Jobmate.Application.Common;
using Jobmate.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using ValidationException = FluentValidation.ValidationException;

namespace Jobmate.Api.Middleware;

/// <summary>
/// Turns every unhandled exception into the documented error envelope:
/// <c>{ success: false, message, errorCode }</c>. Without this, a bug would leak a
/// stack trace and an inconsistent body shape to the Flutter client.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException domainException)
        {
            // Expected, deliberate failures: a 4xx with a message the app can show.
            logger.LogInformation(
                "{Method} {Path} rejected with {ErrorCode}: {Message}",
                context.Request.Method,
                context.Request.Path,
                domainException.ErrorCode,
                domainException.Message);

            await WriteAsync(
                context,
                (int)domainException.StatusCode,
                domainException.Message,
                domainException.ErrorCode);
        }
        catch (ValidationException validationException)
        {
            // The request never reached a handler. These are the app's own validators
            // reporting expected input problems, so they belong in the documented
            // field-level envelope with a 400 rather than in the catch-all below.
            logger.LogInformation(
                "{Method} {Path} failed validation with {Count} problem(s)",
                context.Request.Method,
                context.Request.Path,
                validationException.Errors.Count());

            await WriteValidationAsync(context, validationException);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Two requests tried to change the same row; the client should re-read.
            logger.LogWarning(ex, "Concurrency conflict on {Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                "This record was changed by someone else. Please refresh and try again.",
                "ConcurrencyConflict");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // A unique index caught a race the handler's own check missed.
            logger.LogWarning(ex, "Unique constraint violation on {Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                "That action has already been completed.",
                "DuplicateRequest");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client hung up. Nothing to report to it.
            logger.LogInformation("Request {Method} {Path} was cancelled by the client", context.Request.Method, context.Request.Path);
        }
        catch (Exception ex)
        {
            // Genuinely unexpected. The real message is logged but never returned.
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                environment.IsDevelopment()
                    ? ex.Message
                    : "Something went wrong. Please try again.",
                "InternalError");
        }
    }

    /// <summary>
    /// True when PostgreSQL rejected the write because of a unique index. The
    /// handlers check for duplicates themselves, so this only fires on a genuine
    /// race between two concurrent requests.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>
    /// Writes the field-level envelope. FluentValidation names properties the way they
    /// are written in C# (<c>Request.Pay</c>), so the path is trimmed to the property
    /// and camelCased to match the JSON the app sends and receives.
    /// </summary>
    private static async Task WriteValidationAsync(HttpContext context, ValidationException exception)
    {
        var errors = exception.Errors
            .Select(failure => new ApiError(ToFieldName(failure.PropertyName), failure.ErrorMessage))
            .ToList();

        await ApiErrorWriter.WriteValidationAsync(context, errors);
    }

    /// <summary>
    /// <c>Request.Pay</c> becomes <c>pay</c>; a request-level failure has no property
    /// and is reported against the request itself.
    /// </summary>
    private static string ToFieldName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        // Keeps an indexer path intact, e.g. weekdays[0] -> weekdays[0].
        var segments = propertyName.Split('.');

        var last = segments[^1];

        return char.ToLowerInvariant(last[0]) + last[1..];
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string message, string errorCode) =>
        ApiErrorWriter.WriteAsync(context, statusCode, message, errorCode);
}
