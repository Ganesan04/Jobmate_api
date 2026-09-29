using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Jobmate.Api;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context for design-time commands
/// (migrations add, migrations script, database update) without booting the API.
///
/// It lives in the API project rather than Infrastructure on purpose: the connection
/// string and JWT key live in this project's user-secrets, and putting the factory in
/// Infrastructure would have to reference this project to read them.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(ResolveContentRoot())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("WriteConnection")
                               ?? throw new InvalidOperationException(
                                   "ConnectionStrings:WriteConnection is not configured. Set it with " +
                                   "`dotnet user-secrets set \"ConnectionStrings:WriteConnection\" ...` " +
                                   "from the src/Jobmate.Api directory.");

        var builder = new DbContextOptionsBuilder<AppDbContext>();

        PersistenceConfiguration.Configure(builder, connectionString);

        return new AppDbContext(builder.Options);
    }

    /// <summary>
    /// The content root holding appsettings.json. EF Core does not always set the
    /// working directory to the startup project, so the known locations are probed
    /// rather than assumed.
    /// </summary>
    private static string ResolveContentRoot()
    {
        var candidates = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "Jobmate.Api.csproj")))
            {
                return candidate;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
