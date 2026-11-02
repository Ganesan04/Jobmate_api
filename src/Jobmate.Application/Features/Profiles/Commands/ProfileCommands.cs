using Jobmate.Application.Common;
using Jobmate.Application.Common.Interfaces;
using Jobmate.Application.Common.Mapping;
using Jobmate.Application.DTOs.Profiles;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Exceptions;
using Jobmate.Domain.Rules;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Jobmate.Application.Features.Profiles.Commands;

/// <summary>
/// Loads the caller's user row and role profile, or refuses.
///
/// Editing a profile you do not have is a 403 rather than a 404: the user exists and is
/// signed in, so pretending otherwise would only produce a confusing "not found" on a
/// screen the user is looking at. The message names what is missing because the usual
/// cause is a role the account has not finished switching over.
///
/// The worker's skills and availability grid come along in the same read. They are
/// compared against the incoming list to work out what to add and remove, and a query
/// that returned the profile without them would make every edit look like a full
/// replacement of collections that had not been loaded.
/// </summary>
internal static class ProfileLoader
{
    public static async Task<User> RequireUserAsync(
        IAppDbContext db,
        Guid userId,
        CancellationToken cancellationToken) =>
        await db.Users
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.Skills)
            .Include(u => u.WorkerProfile)!
                .ThenInclude(p => p!.AvailabilitySlots)
            .Include(u => u.GiverProfile)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
        ?? throw new NotFoundException("Profile not found.");

    public static async Task<User> RequireWorkerAsync(
        IAppDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(db, userId, cancellationToken);

        if (user.WorkerProfile is null)
        {
            throw new ForbiddenException("This account does not have a worker profile.");
        }

        return user;
    }

    public static async Task<User> RequireGiverAsync(
        IAppDbContext db,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(db, userId, cancellationToken);

        if (user.GiverProfile is null)
        {
            throw new ForbiddenException("This account does not have a work giver profile.");
        }

        return user;
    }
}

/// <summary>
/// Saves the worker edit-profile form. PUT /api/v1/profile/worker.
///
/// A partial update: every field is optional and a null one is left as it was. The app
/// sends the whole form, but a form that fails validation on one field should not be
/// able to wipe the fields the user did get right.
/// </summary>
public sealed record UpdateWorkerProfileCommand(UpdateWorkerProfileRequest Request) : IRequest<WorkerProfileDto>;

public sealed class UpdateWorkerProfileCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<UpdateWorkerProfileCommand, WorkerProfileDto>
{
    public async Task<WorkerProfileDto> Handle(
        UpdateWorkerProfileCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        var user = await ProfileLoader.RequireWorkerAsync(db, callerId, cancellationToken);
        var profile = user.WorkerProfile!;

        if (input.DateOfBirth is not null)
        {
            ProfileRules.ValidateDateOfBirth(input.DateOfBirth, clock.Today);
        }

        if (input.ExpectedPay is not null)
        {
            ProfileRules.ValidateExpectedPay(input.ExpectedPay);
        }

        profile.Bio = input.Bio?.Trim() ?? profile.Bio;
        profile.DateOfBirth = input.DateOfBirth ?? profile.DateOfBirth;
        profile.Gender = input.Gender?.Trim() ?? profile.Gender;
        profile.Location = input.Location?.Trim() ?? profile.Location;
        profile.Latitude = input.Latitude ?? profile.Latitude;
        profile.Longitude = input.Longitude ?? profile.Longitude;
        profile.Experience = input.Experience?.Trim() ?? profile.Experience;
        profile.Availability = input.Availability?.Trim() ?? profile.Availability;
        profile.ExpectedPay = input.ExpectedPay ?? profile.ExpectedPay;
        profile.ExpectedPayType = input.ExpectedPayType ?? profile.ExpectedPayType;
        profile.IsAvailable = input.IsAvailable ?? profile.IsAvailable;
        profile.UpdatedAt = now;

        // The user's own area is the default search location for both roles, so editing
        // the profile's location text keeps the two in step without a second form field.
        if (input.Location is not null)
        {
            user.Area = input.Location.Trim();
            user.UpdatedAt = now;
        }

        if (input.Skills is not null)
        {
            ProfileRules.ReplaceSkills(profile, input.Skills);
        }

        await db.SaveChangesAsync(cancellationToken);

        return profile.ToDto();
    }
}

/// <summary>
/// Saves the employer edit-profile form. PUT /api/v1/profile/giver.
///
/// The business name is the only required field, and a business cannot be an employer
/// under a blank name, so it is rejected rather than stored empty.
/// </summary>
public sealed record UpdateGiverProfileCommand(UpdateGiverProfileRequest Request) : IRequest<GiverProfileDto>;

public sealed class UpdateGiverProfileCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<UpdateGiverProfileCommand, GiverProfileDto>
{
    public async Task<GiverProfileDto> Handle(
        UpdateGiverProfileCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;
        var input = request.Request;

        var user = await ProfileLoader.RequireGiverAsync(db, callerId, cancellationToken);
        var profile = user.GiverProfile!;

        if (input.BusinessName is not null)
        {
            if (string.IsNullOrWhiteSpace(input.BusinessName))
            {
                throw new BusinessRuleException("Enter the business or your name.", "BusinessNameRequired");
            }

            profile.BusinessName = input.BusinessName.Trim();
        }

        profile.Kind = input.Kind ?? profile.Kind;
        profile.BusinessType = input.BusinessType?.Trim() ?? profile.BusinessType;
        profile.Description = input.Description?.Trim() ?? profile.Description;
        profile.Location = input.Location?.Trim() ?? profile.Location;
        profile.Latitude = input.Latitude ?? profile.Latitude;
        profile.Longitude = input.Longitude ?? profile.Longitude;
        profile.ContactPerson = input.ContactPerson?.Trim() ?? profile.ContactPerson;
        profile.ContactPhone = input.ContactPhone?.Trim() ?? profile.ContactPhone;
        profile.GstNumber = input.GstNumber?.Trim() ?? profile.GstNumber;
        profile.UpdatedAt = now;

        if (input.Location is not null)
        {
            user.Area = input.Location.Trim();
        }

        user.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return profile.ToDto(user.FullName);
    }
}

/// <summary>
/// Replaces the worker's weekly availability grid. PUT /api/v1/profile/availability.
///
/// Whole-grid replacement, because the app edits the week as one screen and a partial
/// update would make it impossible to clear a day that is no longer worked.
/// </summary>
public sealed record SetAvailabilityCommand(SetAvailabilityRequest Request) : IRequest<WorkerProfileDto>;

public sealed class SetAvailabilityCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<SetAvailabilityCommand, WorkerProfileDto>
{
    public async Task<WorkerProfileDto> Handle(
        SetAvailabilityCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var user = await ProfileLoader.RequireWorkerAsync(db, callerId, cancellationToken);
        var profile = user.WorkerProfile!;

        var slots = request.Request.Slots.Select(slot => new WorkerAvailabilitySlot
        {
            DayOfWeek = slot.DayOfWeek,
            StartTime = slot.StartTime.ToTimeSpan(),
            EndTime = slot.EndTime.ToTimeSpan(),
            IsAvailable = slot.IsAvailable
        });

        ProfileRules.ReplaceAvailability(profile, slots, now);

        await db.SaveChangesAsync(cancellationToken);

        return profile.ToDto();
    }
}

/// <summary>
/// The app's online / offline heartbeat. PUT /api/v1/profile/presence.
///
/// This is what the green dot in the chat list and the worker's "currently online" row
/// read, so the app calls it when it comes to the foreground and when it goes to the
/// background. It is a worker-only field: a work giver has no dot to show anywhere.
/// </summary>
public sealed record SetPresenceCommand(SetPresenceRequest Request) : IRequest<UserProfileDto>;

public sealed class SetPresenceCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<SetPresenceCommand, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(
        SetPresenceCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var user = await ProfileLoader.RequireWorkerAsync(db, callerId, cancellationToken);

        ProfileRules.SetPresence(user.WorkerProfile!, request.Request.IsOnline, now);

        await db.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }
}

/// <summary>
/// Points the caller's profile at a newly uploaded image. PUT /api/v1/profile/image.
///
/// The file itself is written by the API before this command runs; all this does is
/// record the URL, which keeps the database out of the file-writing business and means
/// swapping local disk for object storage later changes the upload action only.
/// </summary>
public sealed record AttachProfileImageCommand(string Url, string FileName) : IRequest<UserProfileDto>;

public sealed class AttachProfileImageCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<AttachProfileImageCommand, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(
        AttachProfileImageCommand request,
        CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == callerId, cancellationToken)
            ?? throw new NotFoundException("Profile not found.");

        user.ProfileImageUrl = request.Url;
        user.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }
}

/// <summary>
/// Bookmarks a job for the worker. POST /api/v1/worker/saved-jobs/{jobId}.
///
/// Idempotent: the unique index on (user, job) means a second tap cannot create a second
/// row, so this returns the row that already exists rather than failing. Saving twice is
/// what a double tap looks like, and it should not be an error.
/// </summary>
public sealed record SaveJobCommand(Guid JobId) : IRequest<SavedJobDto>;

public sealed class SaveJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<SaveJobCommand, SavedJobDto>
{
    public async Task<SavedJobDto> Handle(SaveJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();
        var now = clock.UtcNow;

        var job = await db.Jobs
            .AsNoTracking()
            .Include(j => j.Giver)
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new NotFoundException("Job not found.");

        var existing = await db.SavedJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == callerId && s.JobId == job.Id, cancellationToken);

        if (existing is not null)
        {
            return new SavedJobDto { Job = job.ToSummaryDto(), SavedAt = existing.CreatedAt };
        }

        var saved = new SavedJob
        {
            UserId = callerId,
            JobId = job.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.SavedJobs.Add(saved);

        await db.SaveChangesAsync(cancellationToken);

        return new SavedJobDto { Job = job.ToSummaryDto(), SavedAt = now };
    }
}

/// <summary>
/// Removes a bookmark. DELETE /api/v1/worker/saved-jobs/{jobId}.
///
/// Also idempotent, for the same reason: unsaving something already unsaved is the
/// state the caller asked for.
/// </summary>
public sealed record UnsaveJobCommand(Guid JobId) : IRequest<bool>;

public sealed class UnsaveJobCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock) : IRequestHandler<UnsaveJobCommand, bool>
{
    public async Task<bool> Handle(UnsaveJobCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var saved = await db.SavedJobs
            .FirstOrDefaultAsync(s => s.UserId == callerId && s.JobId == request.JobId, cancellationToken);

        if (saved is null)
        {
            return false;
        }

        saved.DeletedAt = clock.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
