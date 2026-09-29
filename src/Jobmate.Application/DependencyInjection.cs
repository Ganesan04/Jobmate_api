using System.Reflection;
using FluentValidation;
using Jobmate.Application.Common;
using Jobmate.Application.Common.Behaviors;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Services;
using Jobmate.Application.Features.Auth.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace Jobmate.Application;

/// <summary>
/// Registers the Application layer. It is pure composition: handlers, validators and
/// MediatR behaviours only. Everything vendor-specific (EF Core, JWT, BCrypt, SMS, PSP,
/// FCM) is registered by <c>Jobmate.Infrastructure</c> against the interfaces declared here,
/// which is what keeps the dependency direction pointing inward.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);

            // Validation runs before the handler, so a handler never sees an invalid request.
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));

            // Logging wraps the handler so failures are recorded with the request type.
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        // Application-owned implementations. The vendor-backed alternatives for
        // ITokenService / IPasswordHasher are registered by Infrastructure.
        services.AddSingleton<IClock, SystemClock>();

        // Scoped, not singleton: it reads and writes OtpChallenge rows through the
        // scoped IAppDbContext, so a singleton would capture a stale context.
        services.AddScoped<OtpServiceHelper>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }
}
