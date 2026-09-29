using FluentValidation;
using MediatR;
using ValidationException = FluentValidation.ValidationException;

namespace Jobmate.Application.Common.Behaviors;

/// <summary>
/// Runs every FluentValidation validator registered for a request before the handler
/// sees it. Failures are collected into one response so the Flutter app can show
/// field-level messages.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = results
                .SelectMany(r => r.Errors)
                .Where(f => f is not null)
                .ToList();

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
        }

        return await next();
    }
}
