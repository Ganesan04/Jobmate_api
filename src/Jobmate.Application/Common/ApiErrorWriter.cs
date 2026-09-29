using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Jobmate.Application.Common;

/// <summary>
/// Writes the documented error envelope straight to the response.
///
/// It is shared rather than duplicated because the envelope is a contract with the
/// Flutter app: a failure raised by a handler, a validation failure and a rejected
/// token all have to arrive in the same shape, or the client ends up parsing two
/// different error formats.
/// </summary>
public static class ApiErrorWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Writes <c>{ success: false, message, errorCode }</c>.</summary>
    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string message,
        string errorCode)
    {
        if (context.Response.HasStarted)
        {
            // The body is already on the wire; overwriting it would corrupt the response.
            return Task.CompletedTask;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new ApiErrorResponse { Message = message, ErrorCode = errorCode };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, SerializerOptions),
            context.RequestAborted);
    }

    /// <summary>Writes <c>{ success: false, message, errorCode, errors: [...] }</c>.</summary>
    public static Task WriteValidationAsync(
        HttpContext context,
        IReadOnlyList<ApiError> errors,
        string message = "Please correct the highlighted fields and try again.",
        string errorCode = "ValidationFailure")
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new ApiErrorResponse
        {
            Message = message,
            ErrorCode = errorCode,
            Errors = errors
        };

        return context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, SerializerOptions),
            context.RequestAborted);
    }
}
