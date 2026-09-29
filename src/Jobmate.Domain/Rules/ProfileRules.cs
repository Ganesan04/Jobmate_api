using Jobmate.Domain.Entities;
using Jobmate.Domain.Exceptions;

namespace Jobmate.Domain.Rules;

/// <summary>
/// Profile editing rules. Kept out of the handlers so the same limits hold whether a
/// profile is edited over HTTP today or imported from somewhere else tomorrow.
/// </summary>
public static class ProfileRules
{
    /// <summary>Longest skill name the database column will hold.</summary>
    public const int MaxSkillLength = 60;

    /// <summary>Enough for a real trade's worth of tags, few enough to stay scannable.</summary>
    public const int MaxSkills = 30;

    /// <summary>
    /// Normalises a skill list from the client: trimmed, de-duplicated ignoring case,
    /// blank entries dropped.
    ///
    /// The database has a unique index on (profile, name) so duplicates cannot be stored,
    /// but rejecting the request for them would be user-hostile — a worker picking "Painter"
    /// twice means they missed it the first time, not that they did something wrong.
    /// Over-long and over-numerous entries are a different case and are refused: silently
    /// dropping a tag leaves the worker wondering why their skill is not filtering anything.
    /// </summary>
    public static List<string> NormaliseSkills(IEnumerable<string>? skills)
    {
        if (skills is null)
        {
            return [];
        }

        var cleaned = skills
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Select(skill => skill.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count > MaxSkills)
        {
            throw new BusinessRuleException(
                $"Keep your skills to {MaxSkills} or fewer so the list stays scannable.",
                "TooManySkills");
        }

        var tooLong = cleaned.FirstOrDefault(skill => skill.Length > MaxSkillLength);

        if (tooLong is not null)
        {
            throw new BusinessRuleException(
                $"\"{tooLong}\" is longer than {MaxSkillLength} characters.",
                "SkillTooLong");
        }

        return cleaned;
    }

    /// <summary>
    /// Rewrites a worker's skills to exactly the supplied set.
    ///
    /// The list is a set rather than an append, so removing the last tag is possible —
    /// which is the only way a worker can stop matching a job they have stopped doing.
    /// Matching is by name so a tag the worker did not change keeps its row id.
    /// </summary>
    public static void ReplaceSkills(WorkerProfile profile, IEnumerable<string> skills)
    {
        var wanted = NormaliseSkills(skills);

        var existing = profile.Skills.ToList();

        foreach (var stale in existing.Where(s => !wanted.Contains(s.Name, StringComparer.OrdinalIgnoreCase)))
        {
            profile.Skills.Remove(stale);
        }

        var kept = profile.Skills
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in wanted.Where(name => !kept.Contains(name)))
        {
            profile.Skills.Add(new WorkerSkill
            {
                WorkerProfileId = profile.Id,
                Name = name,
                CreatedAt = profile.UpdatedAt,
                UpdatedAt = profile.UpdatedAt
            });
        }
    }

    /// <summary>
    /// Replaces the weekly availability grid.
    ///
    /// Whole-grid replacement rather than a patch, because the app edits it as one screen
    /// and a partial update would leave the user unable to clear a day.
    /// </summary>
    public static void ReplaceAvailability(
        WorkerProfile profile,
        IEnumerable<WorkerAvailabilitySlot> slots,
        DateTime now)
    {
        var validated = slots.Select(ValidateSlot).ToList();

        foreach (var stale in profile.AvailabilitySlots.ToList())
        {
            profile.AvailabilitySlots.Remove(stale);
        }

        foreach (var slot in validated)
        {
            slot.WorkerProfileId = profile.Id;
            slot.CreatedAt = now;
            slot.UpdatedAt = now;

            profile.AvailabilitySlots.Add(slot);
        }
    }

    /// <summary>
    /// One recurring window, checked before it reaches the database.
    ///
    /// The day is bounded because the column has a check constraint, and the times are
    /// ordered because an end before the start is the one mistake the weekly grid screen
    /// makes easily and no worker means.
    /// </summary>
    public static WorkerAvailabilitySlot ValidateSlot(WorkerAvailabilitySlot slot)
    {
        if (slot.DayOfWeek is < 1 or > 7)
        {
            throw new BusinessRuleException("Day must be 1 (Monday) to 7 (Sunday).", "InvalidDayOfWeek");
        }

        if (slot.StartTime < TimeSpan.Zero || slot.EndTime > new TimeSpan(24, 0, 0))
        {
            throw new BusinessRuleException("Times must fall within a single day.", "InvalidSlotTime");
        }

        if (slot.EndTime <= slot.StartTime)
        {
            throw new BusinessRuleException("The end time must be after the start time.", "InvalidSlotRange");
        }

        return slot;
    }

    /// <summary>
    /// Rejects a date of birth that cannot be right, and a person old enough to be
    /// ineligible. Deliberately not a minimum age check: the app has no age policy to
    /// enforce, but a typo of 0199 or 3000 is a mistake worth telling the user about.
    /// </summary>
    public static void ValidateDateOfBirth(DateOnly? dateOfBirth, DateOnly today)
    {
        if (dateOfBirth is not { } value)
        {
            return;
        }

        if (value > today)
        {
            throw new BusinessRuleException("Date of birth cannot be in the future.", "DateOfBirthInFuture");
        }

        if (value.Year < today.Year - 100)
        {
            throw new BusinessRuleException("Please check your date of birth.", "DateOfBirthTooOld");
        }
    }

    /// <summary>Keeps an expected rate inside the range the pay columns and screens expect.</summary>
    public static void ValidateExpectedPay(decimal? expectedPay)
    {
        if (expectedPay is not { } pay)
        {
            return;
        }

        if (pay < 0)
        {
            throw new BusinessRuleException("Expected pay cannot be negative.", "ExpectedPayNegative");
        }

        if (pay > 1_000_000)
        {
            throw new BusinessRuleException("That expected pay looks like a typo.", "ExpectedPayTooHigh");
        }
    }

    /// <summary>Presence is what the chat list's green dot and the worker's availability read.</summary>
    public static void SetPresence(WorkerProfile profile, bool isOnline, DateTime now)
    {
        profile.IsOnline = isOnline;
        profile.LastActiveAt = now;
        profile.UpdatedAt = now;
    }
}
