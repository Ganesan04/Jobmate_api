using System.Collections.Concurrent;
using System.Security.Claims;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Jobmate.Application.Common;

/// <summary>
/// The authenticated caller, resolved from the JWT. The only way Application
/// handlers learn who is calling, which keeps them free of <c>HttpContext</c>.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);

    /// <summary>Role carried by the token, or null when the caller is anonymous.</summary>
    UserRole? Role { get; }

    /// <summary>Free-text area from the token, used to default search and profile views.</summary>
    string? Area { get; }

    /// <summary>Caller id, or a thrown 401 when the request was not authenticated.</summary>
    Guid RequireUserId();

    /// <summary>Caller id plus an assertion that they hold <paramref name="role"/>.</summary>
    Guid RequireRole(string role);
}

/// <summary>Default implementation reading the <see cref="ClaimsPrincipal"/>.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var raw = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? Principal?.FindFirst("sub")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public UserRole? Role
    {
        get
        {
            var value = Principal?.FindFirst(ClaimTypes.Role)?.Value;

            return Enum.TryParse<UserRole>(value, ignoreCase: true, out var role) ? role : null;
        }
    }

    public string? Area => Principal?.FindFirst("area")?.Value;

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException("You must be signed in to do that.");

    public Guid RequireRole(string role)
    {
        if (!IsAuthenticated)
        {
            throw new UnauthorizedException("You must be signed in to do that.");
        }

        if (!IsInRole(role))
        {
            throw new ForbiddenException($"This action requires the {role} role.");
        }

        return UserId!.Value;
    }
}

/// <summary>
/// Time source. Injected everywhere instead of calling <c>DateTime.UtcNow</c>
/// directly so token expiry, OTP windows and expiry sweeps are testable.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    DateOnly Today { get; }
}

/// <summary>System clock, the production implementation.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>In-memory cache abstraction, used to rate limit and dedupe short-lived concerns.</summary>
public interface IAppCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Process-local cache. Correct for a single-instance deployment; swap for Redis
/// behind the same interface before scaling the API horizontally.
/// </summary>
public sealed class InMemoryAppCache : IAppCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (!_entries.TryGetValue(key, out var entry))
        {
            return Task.FromResult<T?>(default);
        }

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<T?>(default);
        }

        // The cache is untyped, so a wrong type must miss rather than throw.
        return Task.FromResult(entry.Value is T typed ? typed : default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        _entries[key] = new CacheEntry(value, DateTime.UtcNow.Add(expiry));
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.TryRemove(key, out _));

    private sealed record CacheEntry(object? Value, DateTime ExpiresAt);
}
