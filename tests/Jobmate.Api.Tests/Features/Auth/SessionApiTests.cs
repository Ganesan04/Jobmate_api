using System.Net;
using System.Net.Http.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jobmate.Api.Tests.Features.Auth;

/// <summary>
/// Sessions: every sign-in path must hand back a refresh token the server will accept,
/// and a rotated token presented again must end every session for that account.
/// </summary>
public sealed class SessionApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private const string Password = "Passw0rd!2345";

    private readonly JobmateApiFactory _factory;

    public SessionApiTests(JobmateApiFactory factory)
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
    public async Task A_refresh_token_from_login_can_be_exchanged()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var client = _factory.CreateAnonymousClient();

        var issued = await LoginAsync(client, worker.Email);

        var refreshed = await RefreshAsync(client, issued, HttpStatusCode.OK);

        Assert.NotEqual(issued, refreshed.Data().GetProperty("refreshToken").GetString());
    }

    [Fact]
    public async Task Login_records_the_sign_in_time()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var client = _factory.CreateAnonymousClient();

        var before = DateTime.UtcNow.AddSeconds(-5);

        await LoginAsync(client, worker.Email);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var lastLogin = await db.Users
            .Where(u => u.Id == worker.UserId)
            .Select(u => u.LastLoginAt)
            .SingleAsync();

        Assert.NotNull(lastLogin);
        Assert.True(lastLogin >= before);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_family()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var client = _factory.CreateAnonymousClient();

        var first = await LoginAsync(client, worker.Email);

        var second = (await RefreshAsync(client, first, HttpStatusCode.OK))
            .Data().GetProperty("refreshToken").GetString()!;

        // The already-rotated token comes back: treated as theft.
        await RefreshAsync(client, first, HttpStatusCode.Unauthorized);

        // So the legitimate holder's current token is dead too.
        await RefreshAsync(client, second, HttpStatusCode.Unauthorized);
    }

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { identifier = email, password = Password });

        var body = await TestApi.ReadJsonAsync(response);

        return body.Data().GetProperty("refreshToken").GetString()!;
    }

    private static async Task<System.Text.Json.JsonElement> RefreshAsync(
        HttpClient client,
        string refreshToken,
        HttpStatusCode expected)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken });

        return await TestApi.ReadJsonAsync(response, expected);
    }
}
