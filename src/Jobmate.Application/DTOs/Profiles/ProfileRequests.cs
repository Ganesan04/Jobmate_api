using Jobmate.Domain.Common;
using Jobmate.Domain.Enums;

namespace Jobmate.Application.DTOs.Profiles;

/// <summary>PUT /profile/worker — the edit-profile form for a worker.</summary>
public sealed record UpdateWorkerProfileRequest
{
    public string? Bio { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    public string? Gender { get; init; }

    public string? Location { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? Experience { get; init; }

    /// <summary>Free-text summary shown on the profile. The weekly grid is set separately.</summary>
    public string? Availability { get; init; }

    public decimal? ExpectedPay { get; init; }

    public PayType? ExpectedPayType { get; init; }

    /// <summary>The full set of skills. Omit to leave the current list untouched.</summary>
    public List<string>? Skills { get; init; }

    public bool? IsAvailable { get; init; }
}

/// <summary>PUT /profile/giver — the edit-profile form for an employer.</summary>
public sealed record UpdateGiverProfileRequest
{
    public GiverKind? Kind { get; init; }

    public string? BusinessName { get; init; }

    public string? BusinessType { get; init; }

    public string? Description { get; init; }

    public string? Location { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string? ContactPerson { get; init; }

    public string? ContactPhone { get; init; }

    public string? GstNumber { get; init; }
}

/// <summary>PUT /profile/availability — the whole weekly grid, replaced in one go.</summary>
public sealed record SetAvailabilityRequest
{
    public List<AvailabilitySlotInput> Slots { get; init; } = [];
}

/// <summary>One recurring window in <see cref="SetAvailabilityRequest"/>.</summary>
public sealed record AvailabilitySlotInput
{
    /// <summary>ISO day number: 1 = Monday ... 7 = Sunday, matching the app's WorkDay.weekday.</summary>
    public int DayOfWeek { get; init; }

    public TimeOnly StartTime { get; init; }

    public TimeOnly EndTime { get; init; }

    public bool IsAvailable { get; init; } = true;
}

/// <summary>PUT /profile/presence — the app's online / offline heartbeat.</summary>
public sealed record SetPresenceRequest
{
    public bool IsOnline { get; init; }
}

/// <summary>GET /worker/saved-jobs — the bookmarked jobs list.</summary>
public sealed record GetSavedJobsRequest : PageRequest
{
    /// <summary>Hide jobs that have since been cancelled or completed. Default is to hide them.</summary>
    public bool IncludeClosed { get; init; }
}
