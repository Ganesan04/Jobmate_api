using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Domain.Enums;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jobmate.Api.Tests.Features.Auth;

/// <summary>
/// One account, two modes. Registration takes no role and creates both profiles; the mode
/// is switched with POST /auth/switch-role, which has to issue a new token because the
/// role claim inside it is what every <c>[Authorize(Roles = ...)]</c> gate reads.
/// </summary>
public sealed class RoleSwitchApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private const string Password = "Passw0rd!2345";

    private const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    private readonly JobmateApiFactory _factory;

    public RoleSwitchApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Registering_without_a_role_creates_both_profiles_and_starts_as_a_worker()
    {
        using var client = _factory.CreateAnonymousClient();

        var registered = (await TestApi.ReadJsonAsync(
            await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                name = "Dual Person",
                phone = "9811122233",
                email = "dual.person@jobmate.test",
                password = Password,
                area = "Delhi"
            }),
            HttpStatusCode.OK)).Data();

        Assert.Equal("worker", registered.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("worker", registered.GetProperty("selectedRole").GetString());
        Assert.Equal("Worker", RoleIn(registered.GetProperty("accessToken").GetString()!));

        var userId = registered.GetProperty("user").GetProperty("id").GetGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(UserRole.Worker, await db.Users.Where(u => u.Id == userId).Select(u => u.ActiveRole).SingleAsync());
        Assert.True(await db.WorkerProfiles.AnyAsync(p => p.UserId == userId));
        Assert.True(await db.GiverProfiles.AnyAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task Verifying_the_otp_keeps_the_account_in_its_mode_and_both_profiles()
    {
        using var client = _factory.CreateAnonymousClient();

        await TestApi.ReadJsonAsync(
            await client.PostAsJsonAsync("/api/v1/auth/register", new
            {
                name = "Otp Person",
                phone = "9811122244",
                email = "otp.person@jobmate.test",
                password = Password
            }),
            HttpStatusCode.OK);

        var verified = (await TestApi.ReadJsonAsync(
            await client.PostAsJsonAsync("/api/v1/auth/verify-otp", new { phone = "9811122244", code = "123456" }),
            HttpStatusCode.OK)).Data();

        Assert.Equal("worker", verified.GetProperty("user").GetProperty("role").GetString());

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = verified.GetProperty("user").GetProperty("id").GetGuid();

        Assert.True(await db.WorkerProfiles.AnyAsync(p => p.UserId == userId));
        Assert.True(await db.GiverProfiles.AnyAsync(p => p.UserId == userId));
    }

    [Fact]
    public async Task Switching_returns_a_new_token_pair_whose_role_claim_is_the_new_mode()
    {
        using var account = await TestApi.CreateWorkerAsync(_factory);

        var oldToken = account.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        Assert.Equal("Worker", RoleIn(oldToken));

        var switched = (await TestApi.ReadJsonAsync(
            await account.Client.PostAsJsonAsync("/api/v1/auth/switch-role", new { role = "workGiver" }),
            HttpStatusCode.OK)).Data();

        var newToken = switched.GetProperty("accessToken").GetString()!;

        Assert.NotEqual(oldToken, newToken);
        Assert.Equal("WorkGiver", RoleIn(newToken));
        Assert.False(string.IsNullOrEmpty(switched.GetProperty("refreshToken").GetString()));
        Assert.Equal("workGiver", switched.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("workGiver", switched.GetProperty("selectedRole").GetString());
    }

    [Fact]
    public async Task After_switching_worker_endpoints_refuse_and_giver_endpoints_answer()
    {
        using var account = await TestApi.CreateWorkerAsync(_factory);

        Assert.Equal(HttpStatusCode.OK, (await account.Client.GetAsync("/api/v1/worker/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await account.Client.GetAsync("/api/v1/giver/jobs")).StatusCode);

        await account.SwitchRoleAsync(UserRole.WorkGiver);

        Assert.Equal(HttpStatusCode.Forbidden, (await account.Client.GetAsync("/api/v1/worker/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await account.Client.GetAsync("/api/v1/worker/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await account.Client.GetAsync("/api/v1/giver/jobs")).StatusCode);

        // And back again: nothing about the account was lost on the way.
        await account.SwitchRoleAsync(UserRole.Worker);

        Assert.Equal(HttpStatusCode.OK, (await account.Client.GetAsync("/api/v1/worker/wallet")).StatusCode);
    }

    [Fact]
    public async Task Switching_to_the_current_mode_is_a_no_op_that_still_returns_fresh_tokens()
    {
        using var account = await TestApi.CreateWorkerAsync(_factory);

        var oldToken = account.Client.DefaultRequestHeaders.Authorization!.Parameter!;

        var same = (await TestApi.ReadJsonAsync(
            await account.Client.PostAsJsonAsync("/api/v1/auth/switch-role", new { role = "worker" }),
            HttpStatusCode.OK)).Data();

        Assert.NotEqual(oldToken, same.GetProperty("accessToken").GetString());
        Assert.Equal("Worker", RoleIn(same.GetProperty("accessToken").GetString()!));
        Assert.Equal("worker", same.GetProperty("user").GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_and_refresh_return_the_last_active_mode()
    {
        using var account = await TestApi.CreateWorkerAsync(_factory);
        await account.SwitchRoleAsync(UserRole.WorkGiver);

        using var client = _factory.CreateAnonymousClient();

        var login = (await TestApi.ReadJsonAsync(
            await client.PostAsJsonAsync("/api/v1/auth/login", new { identifier = account.Email, password = Password }),
            HttpStatusCode.OK)).Data();

        Assert.Equal("workGiver", login.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("workGiver", login.GetProperty("selectedRole").GetString());
        Assert.Equal("WorkGiver", RoleIn(login.GetProperty("accessToken").GetString()!));

        var refreshed = (await TestApi.ReadJsonAsync(
            await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = login.GetProperty("refreshToken").GetString() }),
            HttpStatusCode.OK)).Data();

        Assert.Equal("WorkGiver", RoleIn(refreshed.GetProperty("accessToken").GetString()!));
    }

    [Fact]
    public async Task A_missing_or_unknown_role_is_a_400()
    {
        using var account = await TestApi.CreateWorkerAsync(_factory);

        await TestApi.ReadJsonAsync(
            await account.Client.PostAsJsonAsync("/api/v1/auth/switch-role", new { }),
            HttpStatusCode.BadRequest);

        // There is no admin mode to switch into.
        await TestApi.ReadJsonAsync(
            await account.Client.PostAsJsonAsync("/api/v1/auth/switch-role", new { role = "admin" }),
            HttpStatusCode.BadRequest);

        await TestApi.ReadJsonAsync(
            await account.Client.PostAsJsonAsync("/api/v1/auth/switch-role", new { role = 3 }),
            HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Switching_needs_a_signed_in_user()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/auth/switch-role", new { role = "workGiver" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_admin_login_is_gone()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/admin/auth/login",
            new { email = "admin@jobmate.app", password = "anything" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Reads the role claim out of a JWT without validating it; the server already did.</summary>
    private static string? RoleIn(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');

        using var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));

        // The token handler may shorten the claim type to "role" on the way out.
        return claims.RootElement.TryGetProperty("role", out var role)
               || claims.RootElement.TryGetProperty(RoleClaim, out role)
            ? role.GetString()
            : null;
    }
}
