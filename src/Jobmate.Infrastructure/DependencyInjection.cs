using System.Text;
using Jobmate.Application;
using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Services;
using Jobmate.Domain.Enums;
using Jobmate.Infrastructure.Configuration;
using Jobmate.Infrastructure.Persistence;
using Jobmate.Infrastructure.Security;
using Jobmate.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Jobmate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<ExternalServicesOptions>(configuration.GetSection(ExternalServicesOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        AddPersistence(services, configuration);
        AddAuthentication(services, configuration);
        AddExternalServices(services);

        // Process-local cache. Registered here rather than in Application because the
        // production implementation (Redis) lives in Infrastructure.
        services.AddSingleton<IAppCache, InMemoryAppCache>();

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        // One DbContext serves reads and writes, so it uses WriteConnection.
        // ReadConnection is configured alongside it for when reads move to a replica.
        var connectionString = configuration.GetConnectionString("WriteConnection")
                               ?? throw new InvalidOperationException(
                                   "ConnectionStrings:WriteConnection is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            PersistenceConfiguration.Configure(options, connectionString);

            // Query logging is opt-in: it is too noisy for production but invaluable
            // when diagnosing a slow endpoint.
            if (configuration.GetValue("Database:EnableSensitiveDataLogging", false))
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // Handlers and queries depend on IAppDbContext, never on AppDbContext, so the
        // Application layer stays free of EF's concrete surface.
        services.AddScoped<IAppDbContext>(provider =>
            provider.GetRequiredService<AppDbContext>());

        services.AddScoped<AdminSeeder>();

        // Resolved unconditionally at startup and gated inside itself, the same shape as
        // the admin seeder: whether demo data is allowed to run is a decision the seeder
        // makes from its own configuration rather than one the composition root guesses.
        services.AddScoped<DemoDataSeeder>();
    }

    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it with `dotnet user-secrets set \"Jwt:SigningKey\" ...` " +
                "or the equivalent environment variable. The application refuses to start without it.");
        }

        if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes to sign with HS256.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = configuration.GetValue("Jwt:RequireHttpsMetadata", true);
                options.SaveToken = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = System.Security.Claims.ClaimTypes.Name,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                // A rejected request is the app's most common failure, and the Flutter
                // client parses one error shape. Left to itself the framework answers
                // with an empty body, which would force the app to special-case 401/403.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        if (context.Response.HasStarted)
                        {
                            return;
                        }

                        // Suppresses the default empty 401 and lets us write the envelope.
                        context.HandleResponse();

                        await ApiErrorWriter.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Please sign in to continue.",
                            "Unauthenticated");
                    },
                    OnForbidden = context => ApiErrorWriter.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "Your account is not allowed to do that.",
                        "Forbidden")
                };
            });

        services.AddAuthorization();

        services.AddSingleton<ITokenService, JwtService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
    }

    private static void AddExternalServices(IServiceCollection services)
    {
        // Only mock implementations exist today. Each is registered behind the
        // interface it satisfies, so choosing a real vendor later is a one-line change
        // here and requires no change in any handler.
        services.AddSingleton<IOtpService, MockOtpService>();
        services.AddSingleton<IPasswordResetService, MockPasswordResetService>();
        services.AddSingleton<IPaymentService, MockPaymentService>();

        // Scoped, not a singleton like the other mocks: the provider counts the user's
        // active devices, so it needs the request's DbContext to do it.
        services.AddScoped<IPushNotificationService, MockPushNotificationService>();

        // The capacity check that decides who gets the last worker slot. Registered
        // here so handlers depend on the interface and the SQL stays in Infrastructure.
        services.AddScoped<IWorkerSlotStore, WorkerSlotStore>();
    }

    /// <summary>
    /// Validates options as soon as the host is built, so a bad JWT key or a missing
    /// connection string fails at startup with a clear message rather than on the
    /// first request.
    /// </summary>
    public static IServiceCollection AddInfrastructureValidation(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer is required.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience is required.")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.SigningKey), "Jwt:SigningKey is required.")
            .Validate(
                o => o.AccessTokenMinutes > 0, "Jwt:AccessTokenMinutes must be positive.")
            .Validate(
                o => o.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be positive.")
            .ValidateOnStart();

        return services;
    }
}
