using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Rules;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jobmate.Api.Tests.Features.Common;

/// <summary>
/// The development demo data, run against the real database.
///
/// The seeder is invoked directly with a configuration that claims Development, because the
/// test host is deliberately <em>not</em> Development: that is the first thing asserted
/// here. The rest checks that the scenario it writes is the one the rest of the app can
/// read — jobs in several states, a booking with a settled payment, verification history
/// and a support report — and that running it twice does not double the rows.
/// </summary>
public sealed class DemoDataSeederApiTests : IAsyncLifetime
{
    private const string DemoPassword = "DemoPassw0rd!2026";

    private readonly JobmateApiFactory _factory = new();

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public void The_test_host_never_seeds_demo_data_on_its_own()
    {
        // The safety property that has to hold before anything else in this file: the test
        // environment is Testing, so a test run can never write fictional employers into a
        // database and then assert against its own leftovers.
        var configuration = _factory.Services.GetRequiredService<IConfiguration>();

        Assert.False(DemoDataSeeder.IsEnabled(configuration));
    }

    [Fact]
    public async Task It_seeds_a_coherent_marketplace()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Employers, workers, and the jobs in between them. Five jobs: four one-off and one
        // recurring, because the weekday rows are the one part of a job that is normalised
        // out and would otherwise never appear in a development database.
        Assert.Equal(3, await db.Users.CountAsync(u => u.ActiveRole == UserRole.WorkGiver));
        Assert.Equal(4, await db.Users.CountAsync(u => u.ActiveRole == UserRole.Worker));

        // Every account can work and hire, so every one has both profiles, the same pair
        // registration creates.
        Assert.Equal(7, await db.WorkerProfiles.CountAsync());
        Assert.Equal(7, await db.GiverProfiles.CountAsync());

        Assert.Equal(5, await db.Jobs.CountAsync());
        Assert.Equal(1, await db.Jobs.CountAsync(j => j.IsRecurring));
        Assert.Equal(5, await db.JobWeekdays.CountAsync());
    }

    [Fact]
    public async Task Every_demo_account_is_fictional_and_shareable()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var users = await db.Users.Where(u => u.Email!.EndsWith("@demo.jobmate.test")).ToListAsync();

        Assert.Equal(7, users.Count);
        Assert.All(users, u => Assert.EndsWith("@demo.jobmate.test", u.Email!));

        // One hash for all of them, so the reviewer can type it once. All of it inactive
        // product, not a real credential, and it lives in a file only Development loads.
        Assert.Single(users.Select(u => u.PasswordHash).Distinct());
        Assert.All(users, u => Assert.StartsWith("$2", u.PasswordHash));
        Assert.All(users, u => Assert.True(u.IsActive));
    }

    [Fact]
    public async Task The_password_in_the_configuration_is_the_one_that_was_hashed()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = await db.Users.FirstAsync(u => u.Email == "anita@demo.jobmate.test");

        Assert.True(hasher.Verify(DemoPassword, user.PasswordHash));
    }

    [Fact]
    public async Task The_verification_badge_varies_so_the_states_can_be_seen()
    {
        // Every account has a confirmed number; the badge is a separate question, and the
        // demo has to show all four answers rather than a wall of green ticks.
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var statuses = await db.Users
            .Where(u => u.Email!.EndsWith("@demo.jobmate.test"))
            .Select(u => u.VerificationStatus)
            .Distinct()
            .ToListAsync();

        Assert.Equal(4, statuses.Count);
        Assert.All(await db.Users.Where(u => u.Email!.EndsWith("@demo.jobmate.test")).ToListAsync(),
            u => Assert.True(u.IsVerified));
    }

    [Fact]
    public async Task Worker_profiles_carry_skills_and_the_history_a_browse_would_show()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The four accounts that start in worker mode; the employers' worker profiles are
        // the empty ones registration would create.
        var profiles = await db.WorkerProfiles
            .Include(p => p.Skills)
            .Where(p => p.User!.ActiveRole == UserRole.Worker)
            .OrderBy(p => p.TotalCompletedJobs)
            .ToListAsync();

        Assert.Equal(4, profiles.Count);

        // Newest and most experienced first, and one with nothing behind them, so the empty
        // state is on screen too.
        Assert.Equal(0, profiles[0].TotalCompletedJobs);
        Assert.Equal(14, profiles[^1].TotalCompletedJobs);
        Assert.Equal(4.6m, profiles[^1].AverageRating);
        Assert.All(profiles, p => Assert.NotEmpty(p.Skills));
    }

    [Fact]
    public async Task There_is_a_completed_booking_with_a_settled_payment_and_a_rating()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var completed = await db.Jobs.SingleAsync(j => j.Status == JobStatus.Completed);
        var assignment = await db.WorkerAssignments.SingleAsync();
        var payment = await db.Payments.SingleAsync();
        var rating = await db.Ratings.SingleAsync();

        Assert.Equal(AssignmentStatus.Completed, assignment.Status);
        Assert.Equal(completed.Id, assignment.JobId);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.NotNull(payment.PaidAt);
        Assert.NotNull(payment.TransactionReference);

        // The 5% fee the Flutter payment screen shows, so the money on screen and the money
        // in the ledger agree.
        Assert.Equal(40, payment.PlatformFee);
        Assert.Equal(payment.Amount + payment.PlatformFee, payment.TotalAmount);
        Assert.Equal(assignment.Id, rating.WorkerAssignmentId);
        Assert.Equal(5, rating.Stars);
    }

    [Fact]
    public async Task The_money_in_the_ledger_adds_up_the_way_the_rules_do()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payment = await db.Payments.SingleAsync();

        var breakdown = PaymentRules.Calculate(payment.Amount, payment.OvertimeAmount);

        Assert.Equal(breakdown.PlatformFee, payment.PlatformFee);
        Assert.Equal(breakdown.GiverTotal, payment.TotalAmount);
    }

    [Fact]
    public async Task There_is_a_conversation_with_read_markers_and_messages()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var conversation = await db.Conversations
            .Include(c => c.Participants)
            .SingleAsync();

        Assert.Equal(2, conversation.Participants.Count);
        Assert.NotNull(conversation.LastMessageAt);
        Assert.Equal(3, await db.Messages.CountAsync(m => m.ConversationId == conversation.Id));

        // Each side has read up to a different point, so the unread badge is not empty on
        // both halves of the screen.
        var readTimes = conversation.Participants.Select(p => p.LastReadAt).ToList();
        Assert.Contains(readTimes, t => t is not null);
        Assert.Equal(2, readTimes.Distinct().Count());
    }

    [Fact]
    public async Task There_is_verification_history_and_a_support_report()
    {
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Approved, because that is the only state a submission produces now.
        Assert.Equal(4, await db.VerificationRequests.CountAsync(
            v => v.Status == VerificationRequestStatus.Approved));

        var report = await db.SupportReports.SingleAsync();
        Assert.False(report.IsResolved);
        Assert.NotNull(report.RelatedJobId);
    }

    [Fact]
    public async Task Running_it_twice_does_not_duplicate_anything()
    {
        await SeedAsync();
        await SeedAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(7, await db.Users.CountAsync());
        Assert.Equal(5, await db.Jobs.CountAsync());
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(3, await db.Messages.CountAsync());
        Assert.Equal(4, await db.VerificationRequests.CountAsync());
    }

    [Fact]
    public async Task Enabled_without_a_password_writes_nothing_and_does_not_stop_the_app()
    {
        // The one case that would otherwise take the API down: the switch is on, the
        // password is missing. A development convenience is not worth failing startup over,
        // and the alternative — inventing a password — is how a known credential ends up
        // written into a database.
        await SeedAsync(enabled: true, password: null);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Users.CountAsync());
    }

    [Fact]
    public async Task Disabled_writes_nothing_even_with_a_password()
    {
        await SeedAsync(enabled: false, password: DemoPassword);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Users.CountAsync());
    }

    /// <summary>
    /// Runs the seeder by hand with a configuration that says Development, which the test
    /// host deliberately does not.
    /// </summary>
    private async Task SeedAsync(bool enabled = true, string? password = DemoPassword)
    {
        var values = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            [DemoDataSeeder.EnabledKey] = enabled.ToString(),
            [DemoDataSeeder.DemoPasswordKey] = password
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        await using var scope = _factory.Services.CreateAsyncScope();

        var seeder = new DemoDataSeeder(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
            configuration,
            NullLogger<DemoDataSeeder>.Instance);

        await seeder.SeedAsync();
    }
}
