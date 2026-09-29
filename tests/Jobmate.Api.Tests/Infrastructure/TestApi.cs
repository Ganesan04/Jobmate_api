using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Domain.Enums;

namespace Jobmate.Api.Tests.Infrastructure;

/// <summary>
/// Helpers that register throwaway accounts through the real auth endpoints and hand
/// back an authenticated client, so a test exercises the same token path the Flutter
/// app uses instead of fabricating claims.
/// </summary>
public static class TestApi
{
    private static int _sequence;

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a work giver and returns a client carrying their access token.</summary>
    public static Task<AuthenticatedClient> CreateGiverAsync(
        JobmateApiFactory factory,
        string? area = "Delhi",
        CancellationToken cancellationToken = default) =>
        CreateUserAsync(factory, UserRole.WorkGiver, area, cancellationToken);

    /// <summary>Registers a worker and returns a client carrying their access token.</summary>
    public static Task<AuthenticatedClient> CreateWorkerAsync(
        JobmateApiFactory factory,
        string? area = "Delhi",
        CancellationToken cancellationToken = default) =>
        CreateUserAsync(factory, UserRole.Worker, area, cancellationToken);

    private static async Task<AuthenticatedClient> CreateUserAsync(
        JobmateApiFactory factory,
        UserRole role,
        string? area,
        CancellationToken cancellationToken)
    {
        var anonymous = factory.CreateAnonymousClient();

        // A unique identity per call, so tests never collide on the unique indexes.
        var ordinal = Interlocked.Increment(ref _sequence);
        var suffix = $"{Guid.NewGuid():N}"[..10];

        // Ten digits starting with 9, which is what PhoneNumber accepts.
        var phone = $"9{700000000 + (ordinal % 100000000)}";
        var email = $"{role.ToString().ToLowerInvariant()}.{suffix}@jobmate.test";

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                name = $"Test {role}",
                phone,
                email,
                password = "Passw0rd!2345",
                role = (int)role,
                area
            },
            cancellationToken);

        var payload = await ReadJsonAsync(response, cancellationToken: cancellationToken);

        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            payload.GetProperty("data").GetProperty("accessToken").GetString());

        var userId = payload.GetProperty("data").GetProperty("user").GetProperty("id").GetGuid();

        return new AuthenticatedClient(client, phone, email, role, userId);
    }

    /// <summary>Reads the response envelope, failing with the server's message when it is not a success.</summary>
    public static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        var body = await ReadBodyAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<JsonElement>(body, Json);
    }

    /// <summary>
    /// Reads the response body, asserting the status code first. An error response is a
    /// valid result here, not a test failure: the envelope has the same shape either
    /// way, so the body is returned for the caller to assert on.
    /// </summary>
    public static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        HttpStatusCode expected,
        CancellationToken cancellationToken = default)
    {
        var body = await ReadBodyAsync(response, cancellationToken);

        if (response.StatusCode != expected)
        {
            throw new InvalidOperationException(
                $"Expected {(int)expected} but got {(int)response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<JsonElement>(body, Json);
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.Content.ReadAsStringAsync(cancellationToken);

    /// <summary>The <c>data</c> object of a success envelope.</summary>
    public static JsonElement Data(this JsonElement envelope) => envelope.GetProperty("data");
}

/// <summary>
/// An HTTP client plus the account details behind its bearer token. <see cref="UserId"/>
/// is carried because several endpoints address a person by id (paying a worker,
/// starting a conversation) and a test should not have to go back to the database to
/// find it.
/// </summary>
public sealed record AuthenticatedClient(
    HttpClient Client,
    string Phone,
    string Email,
    UserRole Role,
    Guid UserId) : IDisposable
{
    public void Dispose() => Client.Dispose();

    public override string ToString() => $"{Role} <{Email}>";
}
