using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jobmate.Infrastructure.Persistence;

/// <summary>
/// Creates the one row the system cannot start without: the initial admin account,
/// which cannot be self-registered and has no other way in.
///
/// The password is read from configuration, never hardcoded, and the seeder is a
/// no-op once an admin exists. The rest of the data the app needs (categories, pay
/// types) is a closed set of enum values in code, so it is deliberately not seeded
/// into a table that could then drift out of sync with those enums.
/// </summary>
public sealed class AdminSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<AdminSeeder> logger)
{
    /// <summary>Configuration key holding the initial admin password.</summary>
    public const string PasswordKey = "Seed:AdminPassword";

    public const string DefaultAdminEmail = "admin@jobmate.app";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Users.AsNoTracking().AnyAsync(u => u.Role == UserRole.Admin, cancellationToken))
        {
            logger.LogInformation("Admin account already present; skipping seed");
            return;
        }

        // Read through IConfiguration so both user-secrets and environment variables
        // work, without this seeder having to know which source was used.
        var configuredPassword = configuration[PasswordKey];

        if (string.IsNullOrWhiteSpace(configuredPassword))
        {
            // Failing loudly beats shipping a known default credential.
            logger.LogError(
                "No admin account exists and the initial password is not configured. Set the " +
                "{Key} user-secret or environment variable, then restart.", PasswordKey);

            throw new InvalidOperationException(
                $"Cannot seed the admin account: {PasswordKey} is not set.");
        }

        var now = DateTime.UtcNow;

        var admin = new User
        {
            FirstName = "Jobmate",
            LastName = "Admin",
            Email = EmailAddress.Create(DefaultAdminEmail).Value,
            PhoneNumber = PhoneNumber.Create("9000000000").E164,
            PasswordHash = passwordHasher.Hash(configuredPassword),
            Role = UserRole.Admin,
            IsActive = true,
            IsVerified = true,
            VerificationStatus = VerificationStatus.Verified,
            Area = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded the initial admin account {Email} ({Id})", admin.Email, admin.Id);
    }
}
