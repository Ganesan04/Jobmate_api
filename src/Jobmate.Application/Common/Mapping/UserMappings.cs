using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.ValueObjects;

namespace Jobmate.Application.Common.Mapping;

/// <summary>
/// Hand-written projections from entities to Flutter-facing DTOs. AutoMapper is not
/// used: most of these shapes are computed (initials, timeRange, paymentStatus,
/// applicantStatus) or renamed to match the Dart field names, which configuration
/// would obscure rather than clarify.
/// </summary>
public static class UserMappings
{
    public static UserProfileDto ToDto(this User user, IReadOnlyCollection<string>? skills = null) => new()
    {
        Id = user.Id,
        Name = user.FullName,
        Phone = user.PhoneNumber,
        Email = user.Email ?? string.Empty,
        Role = user.ActiveRole,
        Area = user.Area,
        Skills = [.. skills ?? user.WorkerProfile?.Skills.Select(s => s.Name) ?? []],
        Rating = user.WorkerProfile?.AverageRating ?? user.GiverProfile?.AverageRating ?? 0m,
        JobsCompleted = user.WorkerProfile?.TotalCompletedJobs ?? 0,
        IsOnline = user.WorkerProfile?.IsOnline ?? false,
        ProfileImageUrl = user.ProfileImageUrl,
        IsActive = user.IsActive,
        IsVerified = user.IsVerified || user.VerificationStatus == VerificationStatus.Verified,
        Verification = user.VerificationStatus,
        JoinedOn = user.CreatedAt,
        Initials = Initials(user.FullName)
    };

    /// <summary>Mirrors the Dart <c>UserProfile.displayInitials</c> computation.</summary>
    public static string? Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return null;
        }

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => null,
            1 => parts[0][..1].ToUpperInvariant(),
            _ => string.Concat(parts[0][..1], parts[1][..1]).ToUpperInvariant()
        };
    }
}

public static class ProfileMappings
{
    /// <summary>
    /// Projects a worker profile.
    ///
    /// The skill list is sorted alphabetically rather than returned in whatever order the
    /// database hands back. There is no rank column, and a skill list whose order changes
    /// between two loads of the same profile is visibly wrong: the worker edits it, comes
    /// back, and their tags have moved.
    /// </summary>
    public static WorkerProfileDto ToDto(this WorkerProfile profile) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        User = profile.User?.ToDto(OrderedSkills(profile)) ?? new UserProfileDto { Id = profile.UserId },
        Bio = profile.Bio,
        DateOfBirth = profile.DateOfBirth,
        Gender = profile.Gender,
        Location = profile.Location,
        Latitude = profile.Latitude,
        Longitude = profile.Longitude,
        Skills = OrderedSkills(profile),
        Experience = profile.Experience,
        Availability = profile.Availability,
        AvailabilitySlots =
        [
            .. profile.AvailabilitySlots
                .OrderBy(s => s.DayOfWeek)
                .Select(s => new AvailabilitySlotDto
                {
                    Id = s.Id,
                    DayOfWeek = s.DayOfWeek,
                    ShortLabel = DayLabel(s.DayOfWeek),
                    StartTime = TimeOnly.FromTimeSpan(s.StartTime),
                    EndTime = TimeOnly.FromTimeSpan(s.EndTime),
                    IsAvailable = s.IsAvailable
                })
        ],
        ExpectedPay = profile.ExpectedPay.HasValue ? (int)profile.ExpectedPay.Value : null,
        ExpectedPayType = profile.ExpectedPayType,
        VerificationStatus = profile.User?.VerificationStatus ?? VerificationStatus.NotVerified,
        AverageRating = profile.AverageRating,
        RatingCount = profile.RatingCount,
        TotalCompletedJobs = profile.TotalCompletedJobs,
        TotalApplications = profile.TotalApplications,
        IsAvailable = profile.IsAvailable,
        IsOnline = profile.IsOnline
    };

    /// <summary>
    /// Projects a work giver profile. Takes the owner explicitly because the verified badge
    /// is the person's, not the profile's, and must match what worker mode shows.
    /// </summary>
    public static GiverProfileDto ToDto(this GiverProfile profile, User owner) => new()
    {
        Id = profile.Id,
        UserId = profile.UserId,
        Name = owner.FullName,
        Organisation = profile.BusinessName,
        ContactPerson = profile.ContactPerson ?? owner.FullName,
        TotalHired = profile.TotalHired,
        Rating = profile.AverageRating,
        Kind = profile.Kind,
        BusinessType = profile.BusinessType,
        Description = profile.Description,
        Location = profile.Location,
        Latitude = profile.Latitude,
        Longitude = profile.Longitude,
        ContactPhone = profile.ContactPhone,
        VerificationStatus = owner.VerificationStatus,
        TotalJobsPosted = profile.TotalJobsPosted
    };

    /// <summary>Short weekday label matching the Flutter <c>WorkDay.shortLabel</c> values.</summary>
    public static string DayLabel(int isoDayOfWeek) => isoDayOfWeek switch
    {
        1 => "Mon",
        2 => "Tue",
        3 => "Wed",
        4 => "Thu",
        5 => "Fri",
        6 => "Sat",
        7 => "Sun",
        _ => string.Empty
    };

    /// <summary>Skill names in a stable order, for every projection that lists them.</summary>
    private static List<string> OrderedSkills(WorkerProfile profile) =>
        [.. profile.Skills.Select(s => s.Name).OrderBy(name => name, StringComparer.Ordinal)];
}
