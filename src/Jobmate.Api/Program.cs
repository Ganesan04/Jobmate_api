using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Jobmate.Api.Media;
using Jobmate.Api.Middleware;
using Jobmate.Application;
using Jobmate.Application.Common;
using Jobmate.Infrastructure;
using Jobmate.Infrastructure.Configuration;
using Jobmate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

const string CorsPolicyName = "jobmate-app";

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- logging
// Serilog is configured first so that failures during the rest of startup are also
// captured, rather than only those after the host is built.
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console();

    // In a container the log goes to stdout; on a host a file sink is more useful.
    if (!context.HostingEnvironment.IsDevelopment())
    {
        loggerConfiguration.WriteTo.File(
            "logs/jobmate-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true);
    }
});

// ---------------------------------------------------------------- services
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInfrastructureValidation();
builder.Services.AddApplication();

// The Application layer reads the caller through ICurrentUser, which is backed by
// the authenticated ClaimsPrincipal rather than by HttpContext directly.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Local-disk upload handling. Registered here rather than in Application because it is
// the one piece that touches the file system and IFormFile; handlers only ever see the
// URL it produces. Singleton because it holds no per-request state.
builder.Services.AddSingleton<LocalMediaStore>();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Matches the Dart models: camelCase names and string enum values, so the
        // payload needs no client-side renaming.
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// ProblemDetails for framework-level failures (404 route not found, model binding).
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------- abuse guards

// The named policies live in Application so the numbers can be asserted on without a web
// host, and so an endpoint opts in with one word rather than a limiter configured inline.
var rateLimits = builder.Configuration
    .GetSection(RateLimitOptions.SectionName)
    .Get<RateLimitOptions>() ?? RateLimitOptions.Default;

rateLimits.Validate();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Jobmate.Api.RateLimiting");

        // The partition key is deliberately not logged. It is an account id or an address,
        // and neither belongs in a log line that gets shipped anywhere.
        //
        // The policy name comes from the endpoint's own metadata: the rejection callback is
        // not handed the policy it refused under, and guessing from the path would make the
        // log wrong the moment a second policy is added to one route.
        var policyName = context.HttpContext.GetEndpoint()?
            .Metadata
            .GetMetadata<EnableRateLimitingAttribute>()?
            .PolicyName ?? "unknown";

        logger.LogWarning(
            "Rate limit {Policy} refused {Method} {Path} for a caller over the allowance",
            policyName,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);

        // A client that has to guess when to come back is a client that will not come
        // back, so the wait is stated in the header and in the message.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? $" Try again in {Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds))} seconds."
            : string.Empty;

        context.HttpContext.Response.ContentType = "application/json";

        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                success = false,
                message = $"Too many requests. Please slow down.{wait}",
                errorCode = "RateLimitExceeded"
            },
            cancellationToken);
    };

    foreach (var policy in rateLimits.Policies)
    {
        options.AddPolicy(policy.Name, context => RateLimitPartition.GetFixedWindowLimiter(
            AbuseGuardPartitioner.PartitionKey(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                context.Connection.RemoteIpAddress?.ToString()),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = policy.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }
});

// ---------------------------------------------------------------- versioning
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("x-api-version"),
            new QueryStringApiVersionReader("api-version"));
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// ---------------------------------------------------------------- swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Jobmate API",
        Version = "v1",
        Description =
            "Backend for the Jobmate Flutter app. All responses use the envelope " +
            "{ success, data, message } or { success, data, pagination }."
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by /api/v1/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });

    // XML comments from the handlers and DTOs become the request/response docs.
    var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        // Only used by Swagger and local tooling; the Flutter app is not a browser,
        // so the list is explicit rather than "*".
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        if (origins.Length > 0)
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Model-binding and validation failures are reshaped into the same
    // { success, data, message, errors } envelope the handlers produce, so the
    // Flutter client has exactly one error format to parse.
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiError(
                ToCamelCase(entry.Key),
                string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The value supplied is not valid." : error.ErrorMessage)))
            .ToArray();

        return new BadRequestObjectResult(new ApiErrorResponse
        {
            Message = "One or more validation errors occurred.",
            ErrorCode = "ValidationFailure",
            Errors = errors
        });
    };
});

// Model-state keys are PascalCase ("PhoneNumber") while the DTOs and the Dart
// client are camelCase, so the field names in the error list are normalised here.
static string ToCamelCase(string pascal)
{    if (string.IsNullOrEmpty(pascal) || !char.IsUpper(pascal[0]))
    {
        return pascal;
    }

    var characters = pascal.ToCharArray();

    for (var i = 0; i < characters.Length; i++)
    {
        if (i > 0 && i + 1 < characters.Length && !char.IsUpper(characters[i + 1]))
        {
            break;
        }

        characters[i] = char.ToLowerInvariant(characters[i]);
    }

    return new string(characters);
}

var app = builder.Build();

// ---------------------------------------------------------------- pipeline
// The exception handler runs first so it can wrap everything downstream,
// including routing and authorization failures.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate =
        "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

    // Health and swagger polling would otherwise dominate the log.
    options.GetLevel = (httpContext, _, exception) =>
        exception is not null || httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : LogEventLevel.Debug;
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Jobmate API v1");
        options.DocumentTitle = "Jobmate API";
    });
}

// Development serves plain HTTP on the LAN so a phone can reach it; redirecting would
// send the app to an https://localhost address it cannot open.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors(CorsPolicyName);

app.UseAuthentication();

// After authentication, because that is what lets a signed-in call be counted against
// the account rather than against the address, and so one abusive user on a shared
// connection cannot spend everybody else's allowance. Before authorization, so a flood
// from a caller who would be refused anyway is stopped rather than run through the rest
// of the pipeline.
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

// A small demonstration marketplace, so a development build can be clicked through
// without posting a wizard six times first. Gated on both the Development environment
// and Seed:DemoData, and a no-op in every other case.
using (var scope = app.Services.CreateScope())
{
    var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();

    await demoSeeder.SeedAsync();
}

app.Run();

/// <summary>Exposed so the integration tests can boot the real pipeline with WebApplicationFactory.</summary>
public partial class Program;
