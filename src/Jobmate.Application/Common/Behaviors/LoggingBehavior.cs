using MediatR;
using Microsoft.Extensions.Logging;

namespace Jobmate.Application.Common.Behaviors;

/// <summary>
/// Logs every request with its name, elapsed time and outcome. Sensitive payload
/// fields never appear here: only the request type name is recorded, never its data.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;

        logger.LogInformation("Handling {RequestName}", name);

        var startedAt = DateTime.UtcNow;

        try
        {
            var response = await next();
            logger.LogInformation("Handled {RequestName} in {ElapsedMs}ms", name, (DateTime.UtcNow - startedAt).TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Request {RequestName} failed after {ElapsedMs}ms", name, (DateTime.UtcNow - startedAt).TotalMilliseconds);
            throw;
        }
    }
}
