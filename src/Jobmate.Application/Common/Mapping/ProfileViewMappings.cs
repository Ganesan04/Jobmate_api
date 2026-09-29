using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Entities;

namespace Jobmate.Application.Common.Mapping;

/// <summary>
/// Projections for the profile screens.
///
/// The split between <see cref="ToPublicDto"/> and the owner-facing
/// <c>UserProfileDto</c> is the important part of this file: the public shape is built
/// field by field from what is safe to show a stranger, so adding a private column to
/// <see cref="User"/> cannot accidentally leak it to every profile screen.
/// </summary>
public static class ProfileViewMappings
{
    /// <summary>
    /// Someone else's profile.
    ///
    /// Every field is assigned from a literal rather than spread from the entity. The
    /// omission that matters is the contact block: a signed-in user can see any other
    /// user's name and work history, but not their phone number, email or coordinates,
    /// because those are how someone gets scammed and the app has a reporting flow for
    /// exactly that.
    /// </summary>
    public static PublicProfileDto ToPublicDto(this User user)
    {
        var worker = user.WorkerProfile;
        var giver = user.GiverProfile;

        return new PublicProfileDto
        {
            Id = user.Id,
            Name = user.FullName,
            Role = user.Role,
            Area = user.Area,
            Skills = worker is null
                ? []
                :
                [
                    .. worker.Skills
                        .Select(s => s.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                ],
            Rating = worker?.AverageRating ?? giver?.AverageRating ?? 0m,
            RatingCount = worker?.RatingCount ?? giver?.RatingCount ?? 0,
            IsOnline = worker?.IsOnline ?? false,
            ProfileImageUrl = user.ProfileImageUrl,
            Verification = user.VerificationStatus,
            Headline = Headline(worker, giver),
            Bio = worker?.Bio ?? giver?.Description,
            Experience = worker?.Experience,
            AvailabilitySlots = worker is null
                ? []
                :
                [
                    .. worker.AvailabilitySlots
                        .OrderBy(s => s.DayOfWeek)
                        .ThenBy(s => s.StartTime)
                        .Select(s => new AvailabilitySlotDto
                        {
                            Id = s.Id,
                            DayOfWeek = s.DayOfWeek,
                            ShortLabel = ProfileMappings.DayLabel(s.DayOfWeek),
                            StartTime = TimeOnly.FromTimeSpan(s.StartTime),
                            EndTime = TimeOnly.FromTimeSpan(s.EndTime),
                            IsAvailable = s.IsAvailable
                        })
                ],
            Organisation = giver?.BusinessName,
            GiverKind = giver?.Kind,
            IsAvailable = worker?.IsAvailable,
            CompletedJobs = worker?.TotalCompletedJobs ?? giver?.TotalHired ?? 0,
            JoinedOn = user.CreatedAt,
            Initials = UserMappings.Initials(user.FullName)
        };
    }

    /// <summary>
    /// The one line under the name. A worker's headline is their top few skills, because
    /// "Painter, Plumber, Electrician" tells an employer more than their bio does; a
    /// giver's is the business they hire for.
    /// </summary>
    private static string? Headline(WorkerProfile? worker, GiverProfile? giver)
    {
        if (giver is not null)
        {
            return string.IsNullOrWhiteSpace(giver.BusinessName) ? null : giver.BusinessName;
        }

        if (worker is null)
        {
            return null;
        }

        var top = worker.Skills
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        return top.Count == 0 ? null : string.Join(", ", top);
    }
}
