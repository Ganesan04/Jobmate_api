using Jobmate.Application.DTOs.Admin;
using Jobmate.Domain.Entities;

namespace Jobmate.Application.Common.Mapping;

/// <summary>
/// Projections for the admin tables.
///
/// Unlike every user-facing mapping here, these expose phone numbers, emails and exact
/// areas. That is the point of the admin screens — an admin has to be able to identify
/// and contact the person in front of them — so the privacy boundary is drawn at the
/// controller, where the Admin role is required, rather than by quietly omitting fields.
/// </summary>
public static class AdminMappings
{
    /// <summary>
    /// A row in the users table.
    /// </summary>
    /// <param name="jobsCount">Jobs posted (givers) or applications made (workers).</param>
    /// <param name="rating">Average stars received, or null when nobody has rated them.</param>
    public static AdminUserDto ToAdminDto(
        this User user,
        int jobsCount = 0,
        decimal? rating = null) => new()
    {
        Id = user.Id,
        Name = user.FullName,
        Phone = user.PhoneNumber,
        Email = user.Email ?? string.Empty,
        Role = user.Role,
        Verification = user.VerificationStatus,
        IsActive = user.IsActive,
        JoinedOn = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        Area = user.Area,
        JobsCount = jobsCount,
        Rating = rating ?? 0m,

        // The full number is one column away, but the table only ever needs enough of it
        // to tell two similarly named accounts apart when reading a complaint out.
        PhoneLast4 = LastFourDigits(user.PhoneNumber)
    };

    /// <summary>
    /// A row in the jobs table.
    /// </summary>
    /// <param name="applicants">How many people applied. A count rather than a loaded collection.</param>
    public static AdminJobDto ToAdminDto(this Job job, int applicants = 0) => new()
    {
        Id = job.Id,
        Title = job.Title,
        Company = job.Company,
        GiverId = job.GiverId,
        GiverName = job.Giver?.FullName ?? string.Empty,
        Category = job.Category,
        Status = job.Status,
        Pay = job.Pay,
        PayType = job.PayType,
        WorkersNeeded = job.WorkersNeeded,
        WorkersFilled = job.WorkersFilled,
        Applicants = applicants,
        Area = job.Area,
        JobDate = job.JobDate,
        CreatedAt = job.CreatedAt
    };

    /// <summary>
    /// The job detail panel: the row plus the applicant and money breakdown.
    /// </summary>
    public static AdminJobDetailDto ToAdminDetailDto(
        this Job job,
        int applicants,
        int shortlisted,
        int selected,
        int paid,
        decimal totalPaid,
        int openDisputes) => new()
    {
        Job = job.ToAdminDto(applicants),
        TotalApplications = applicants,
        ShortlistedCount = shortlisted,
        SelectedCount = selected,
        PaidCount = paid,
        TotalPaid = totalPaid,
        OpenDisputes = openDisputes
    };

    /// <summary>
    /// The last four digits of a phone number, or null when it is not a number at all.
    /// A leading zero is kept as part of the returned string, so "0432" does not silently
    /// become 432 and read as a different number.
    /// </summary>
    private static int? LastFourDigits(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());

        return digits.Length < 4 ? null : int.TryParse(digits[^4..], out var last) ? last : null;
    }
}
