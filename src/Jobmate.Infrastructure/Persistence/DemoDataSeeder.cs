using Jobmate.Application.Common.Interfaces;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Domain.Rules;
using Jobmate.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Jobmate.Infrastructure.Persistence;

/// <summary>
/// A small, coherent marketplace to click through: a few employers, a few workers with
/// skills and history, live jobs in different states, an application, a booking with a
/// settled payment, a conversation, verification history and a support report. Every
/// account has both a worker and a work giver profile; the role below is only the mode
/// it starts in.
///
/// Development data only, and it refuses to run anywhere it could reach a real person.
/// Two independent gates, either of which is enough:
/// the host environment must be <c>Development</c>, and <c>Seed:DemoData</c> must be
/// switched on. Both being required is deliberate — a missing configuration key and a
/// mis-set environment are different mistakes, and it should take both to end up with
/// fictional employers and workers in a live database.
///
/// The scenario is written through the domain rules where they apply, so the seeded rows
/// are the same shape the API produces rather than a parallel set of hand-written ones.
/// </summary>
public sealed class DemoDataSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IConfiguration configuration,
    ILogger<DemoDataSeeder> logger)
{
    public const string EnabledKey = "Seed:DemoData";

    public const string DemoPasswordKey = "Seed:DemoPassword";

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled(configuration))
        {
            return;
        }

        if (await db.Users.AsNoTracking().AnyAsync(u => u.Email!.EndsWith("@demo.jobmate.test"), cancellationToken))
        {
            logger.LogInformation("Demo accounts already present; skipping demo seed");
            return;
        }

        var password = configuration[DemoPasswordKey];

        if (string.IsNullOrWhiteSpace(password))
        {
            // A warning rather than a throw: a development convenience is not something the
            // system needs to start. The
            // alternative would be for this seeder to invent a password of its own, and a
            // known credential is exactly what should never be written into a database by
            // a default. appsettings.Development.json sets the key for local runs.
            logger.LogWarning(
                "Demo data is enabled but {Key} is not set, so no demo accounts were created. Set it to seed them.",
                DemoPasswordKey);

            return;
        }

        var hash = passwordHasher.Hash(password);
        var jobDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(4);

        // ------------------------------------------------------------------ people

        var givers = new[]
        {
            NewUser("Anita", "Sharma", "anita", UserRole.WorkGiver, hash, "+919812300001",
                VerificationStatus.Verified, "Delhi"),
            NewUser("Rahul", "Mehta", "rahul", UserRole.WorkGiver, hash, "+919812300002",
                VerificationStatus.Pending, "Gurugram"),
            NewUser("Sunita", "Yadav", "sunita", UserRole.WorkGiver, hash, "+919812300003",
                VerificationStatus.NotVerified, "Noida")
        };

        var workers = new[]
        {
            NewUser("Deepak", "Kumar", "deepak", UserRole.Worker, hash, "+919822300001",
                VerificationStatus.Verified, "Delhi"),
            NewUser("Priya", "Singh", "priya", UserRole.Worker, hash, "+919822300002",
                VerificationStatus.Verified, "Delhi"),
            NewUser("Imran", "Khan", "imran", UserRole.Worker, hash, "+919822300003",
                VerificationStatus.Rejected, "Gurugram"),
            NewUser("Lakshmi", "Rao", "lakshmi", UserRole.Worker, hash, "+919822300004",
                VerificationStatus.NotVerified, "Noida")
        };

        db.Users.AddRange(givers);
        db.Users.AddRange(workers);
        await db.SaveChangesAsync(cancellationToken);

        var karwar = NewGiverProfile(givers[0], GiverKind.Business, "Karwar Logistics",
            "Regional transport and warehousing, running since 2011.", "+919812300001");
        var mehta = NewGiverProfile(givers[1], GiverKind.Individual, "Mehta Construction",
            "Small residential builds and repairs.", null);
        var yadav = NewGiverProfile(givers[2], GiverKind.Business, "Yadav Sweets & Events",
            "Event staffing and hospitality.", "+919812300003");

        db.GiverProfiles.AddRange(karwar, mehta, yadav);

        var deepak = NewWorkerProfile(workers[0], "Warehouse and loading work for nine years.",
            "Delhi", ["Loading", "Packing"], completed: 14, available: true, online: true);
        var priya = NewWorkerProfile(workers[1], "Trained in electrical fitting and appliance repair.",
            "Delhi", ["Electrical", "Repair"], completed: 6, available: true, online: false);
        var imran = NewWorkerProfile(workers[2], "Carpentry and site prep.",
            "Gurugram", ["Carpentry"], completed: 2, available: true, online: true);
        var lakshmi = NewWorkerProfile(workers[3], "Looking for my first warehouse shift.",
            "Noida", ["Packing"], completed: 0, available: true, online: false);

        db.WorkerProfiles.AddRange(deepak, priya, imran, lakshmi);

        // Every account can work and hire, so each one gets the profile for the mode it
        // did not start in, the same pair registration creates.
        db.WorkerProfiles.AddRange(givers.Select(giver => new WorkerProfile
        {
            UserId = giver.Id,
            Location = giver.Area,
            CreatedAt = Now.AddDays(-45).UtcDateTime
        }));

        db.GiverProfiles.AddRange(workers.Select(worker => new GiverProfile
        {
            UserId = worker.Id,
            Kind = GiverKind.Individual,
            BusinessName = worker.FullName,
            ContactPerson = worker.FullName,
            Location = worker.Area,
            CreatedAt = Now.AddDays(-45).UtcDateTime
        }));

        await db.SaveChangesAsync(cancellationToken);

        // -------------------------------------------------------------------- jobs

        var warehouse = NewJob(givers[0], "Karwar Logistics", "Warehouse load", JobCategory.Delivery,
            "Loading and unloading a truck. Pallets must be stacked no higher than chest height.",
            workersNeeded: 3, filled: 0, jobDate, pay: 900, area: "Delhi",
            address: "12 Warehouse Road", urgent: true, skills: ["Loading", "Packing"]);

        var electrician = NewJob(givers[1], "Mehta Construction", "Fitting work, 2-bed flat",
            JobCategory.Construction,
            "Replace two ceiling fittings and test the distribution board after a leak.",
            workersNeeded: 1, filled: 0, jobDate, pay: 1200, area: "Gurugram",
            address: "4B Sector 14", urgent: false, skills: ["Electrical"]);

        var eventStaff = NewJob(givers[2], "Yadav Sweets & Events", "Event staff, Saturday evening",
            JobCategory.Events,
            "Serving and clearing at a wedding reception. Smart dress, no leather shoes.",
            workersNeeded: 6, filled: 0, jobDate.AddDays(1), pay: 700, area: "Noida",
            address: "Sector 18 Market", urgent: false, skills: ["Serving"]);

        var completedJob = NewJob(givers[0], "Karwar Logistics", "Stock count, Sunday",
            JobCategory.Delivery,
            "Count pallets against the stock sheet and note any damage.",
            workersNeeded: 1, filled: 1, jobDate.AddDays(-9), pay: 800, area: "Delhi",
            address: "12 Warehouse Road", urgent: false, skills: ["Packing"]);

        completedJob.Status = JobStatus.Completed;
        completedJob.CompletedAt = Now.AddDays(-8).UtcDateTime;
        completedJob.StartedAt = Now.AddDays(-9).UtcDateTime;

        db.Jobs.AddRange(warehouse, electrician, eventStaff, completedJob);
        await db.SaveChangesAsync(cancellationToken);

        // A recurring job, because the weekday rows are the one part of a job that is
        // normalised out and would otherwise never be seen in a development database.
        var recurring = NewJob(givers[1], "Mehta Construction", "Site tidy, weekday mornings",
            JobCategory.Cleaning,
            "Clear the site and sweep before the trades start.",
            workersNeeded: 1, filled: 0, jobDate, pay: 600, area: "Gurugram",
            address: "4B Sector 14", urgent: false, skills: ["Cleaning"]);

        recurring.JobType = JobType.Recurring;
        recurring.IsRecurring = true;
        recurring.Status = JobStatus.Active;
        recurring.PublishedAt = Now.AddDays(-2).UtcDateTime;

        db.Jobs.Add(recurring);
        db.JobWeekdays.AddRange(
            new JobWeekday { JobId = recurring.Id, DayOfWeek = 1 },
            new JobWeekday { JobId = recurring.Id, DayOfWeek = 2 },
            new JobWeekday { JobId = recurring.Id, DayOfWeek = 3 },
            new JobWeekday { JobId = recurring.Id, DayOfWeek = 4 },
            new JobWeekday { JobId = recurring.Id, DayOfWeek = 5 });
        await db.SaveChangesAsync(cancellationToken);

        // -------------------------------------------------- applications and bookings

        db.JobApplications.AddRange(
            new JobApplication
            {
                JobId = warehouse.Id,
                WorkerId = workers[0].Id,
                Message = "Nine years of loading, can start at 8am.",
                Status = ApplicationStatus.Applied,
                AppliedAt = Now.AddHours(-30).UtcDateTime
            },
            new JobApplication
            {
                JobId = warehouse.Id,
                WorkerId = workers[1].Id,
                Message = "Available and have a pallet truck of my own.",
                Status = ApplicationStatus.Applied,
                AppliedAt = Now.AddHours(-26).UtcDateTime
            },
            new JobApplication
            {
                JobId = electrician.Id,
                WorkerId = workers[2].Id,
                Message = "I do this daily, can bring my own tools.",
                Status = ApplicationStatus.Applied,
                AppliedAt = Now.AddHours(-20).UtcDateTime
            },
            // The application the completed booking came from. A booking is the result of
            // accepting somebody, so it carries a foreign key back to that application —
            // seeding the booking without one is a row the database will not accept.
            new JobApplication
            {
                JobId = completedJob.Id,
                WorkerId = workers[0].Id,
                Message = "Can do the stock count, I am free all Sunday.",
                Status = ApplicationStatus.Accepted,
                AppliedAt = Now.AddDays(-11).UtcDateTime
            });

        await db.SaveChangesAsync(cancellationToken);

        var accepted = await db.JobApplications
            .AsNoTracking()
            .SingleAsync(a => a.JobId == completedJob.Id, cancellationToken);

        var booking = new WorkerAssignment
        {
            JobId = completedJob.Id,
            WorkerId = workers[0].Id,
            GiverId = givers[0].Id,
            ApplicationId = accepted.Id,
            AgreedPay = completedJob.Pay,
            Status = AssignmentStatus.Completed,
            CheckInCode = "4417",
            ConfirmedAt = Now.AddDays(-10).UtcDateTime,
            CheckedInAt = Now.AddDays(-9).UtcDateTime,
            CompletedAt = Now.AddDays(-8).UtcDateTime,
            RowVersion = 0
        };

        db.WorkerAssignments.Add(booking);
        await db.SaveChangesAsync(cancellationToken);

        // The fee comes from the rule rather than a number typed in here, so the money on
        // the demo payment screen agrees with the ledger. A hardcoded figure drifts the
        // first time the percentage changes.
        var payout = PaymentRules.Calculate(completedJob.Pay, null);

        db.Payments.Add(new Payment
        {
            JobId = completedJob.Id,
            WorkerId = workers[0].Id,
            GiverId = givers[0].Id,
            AssignmentId = booking.Id,
            Amount = payout.BaseAmount,
            OvertimeAmount = payout.OvertimeAmount,
            PlatformFee = payout.PlatformFee,
            TotalAmount = payout.GiverTotal,
            AgreedPaySnapshot = completedJob.Pay,
            Status = PaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Upi,
            TransactionReference = "demo_txn_" + Guid.NewGuid().ToString("N")[..12],
            PaidAt = Now.AddDays(-7).UtcDateTime,
            CreatedAt = Now.AddDays(-9).UtcDateTime,
            IdempotencyKey = Guid.NewGuid().ToString("N")
        });

        db.Ratings.Add(new Rating
        {
            JobId = completedJob.Id,
            FromUserId = givers[0].Id,
            ToUserId = workers[0].Id,
            Stars = 5,
            Comment = "Counted accurately and finished an hour early.",
            WorkerAssignmentId = booking.Id,
            RatingDirection = RatingDirection.GiverToWorker,
            CreatedAt = Now.AddDays(-7).UtcDateTime
        });

        // The averages live on the profiles, not on the user, and are recomputed by
        // RatingRules on every write — so the demo figures are placed directly.
        deepak.RatingCount = 14;
        deepak.AverageRating = 4.6m;
        karwar.RatingCount = 1;
        karwar.AverageRating = 5.0m;

        // -------------------------------------------------------------- a conversation

        var conversation = new Conversation
        {
            JobId = warehouse.Id,
            LastMessageAt = Now.AddHours(-3).UtcDateTime,
            CreatedAt = Now.AddDays(-2).UtcDateTime
        };

        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);

        db.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = givers[0].Id,
                LastReadAt = Now.AddHours(-3).UtcDateTime
            },
            new ConversationParticipant
            {
                ConversationId = conversation.Id,
                UserId = workers[0].Id,
                LastReadAt = Now.AddHours(-2).UtcDateTime
            });

        db.Messages.AddRange(
            new Message
            {
                ConversationId = conversation.Id,
                SenderId = givers[0].Id,
                MessageText = "Are you free for Thursday morning?",
                SentAt = Now.AddHours(-4).UtcDateTime
            },
            new Message
            {
                ConversationId = conversation.Id,
                SenderId = workers[0].Id,
                MessageText = "Yes, I can be there by 8:30. Should I bring gloves?",
                SentAt = Now.AddHours(-3).UtcDateTime
            },
            new Message
            {
                ConversationId = conversation.Id,
                SenderId = givers[0].Id,
                MessageText = "Bring a pair, we are short. See you then.",
                SentAt = Now.AddHours(-2).UtcDateTime
            });

        // ------------------------------------------- verification history and a report

        // Approved requests, the only kind submission now produces, so the badge roll-up
        // has something to fold on a fresh development database.
        db.VerificationRequests.AddRange(
            NewVerificationRequest(workers[0], VerificationType.Identity, "deepak_aadhaar.pdf", "Aadhaar, both sides."),
            NewVerificationRequest(workers[1], VerificationType.Identity, "priya_aadhaar.pdf", null),
            NewVerificationRequest(workers[2], VerificationType.Identity, "imran_aadhaar.pdf", null),
            NewVerificationRequest(workers[3], VerificationType.Identity, "lakshmi_aadhaar.pdf", "First time uploading."));

        db.SupportReports.Add(new SupportReport
        {
            UserId = workers[1].Id,
            Reason = ReportReason.UnsafeWorkplace,
            Details = "The site had no safety helmets available and nobody on the crew had one.",
            RelatedJobId = electrician.Id.ToString(),
            IsResolved = false,
            CreatedAt = Now.AddDays(-1).UtcDateTime
        });

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded demo data: {Givers} employers, {Workers} workers, {Jobs} jobs. Sign in with any @demo.jobmate.test account.",
            givers.Length,
            workers.Length,
            5);
    }

    /// <summary>
    /// Whether demo data may be seeded. Both gates have to pass, and both are checked
    /// here so a caller cannot accidentally satisfy only one of them.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        string.Equals(
            configuration[EnabledKey],
            "true",
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            configuration["ASPNETCORE_ENVIRONMENT"] ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- factories

    private static User NewUser(
        string firstName,
        string lastName,
        string handle,
        UserRole role,
        string passwordHash,
        string phone,
        VerificationStatus status,
        string area) =>
        new()
        {
            FirstName = firstName,
            LastName = lastName,
            Email = EmailAddress.Create($"{handle}@demo.jobmate.test").Value,

            // The same normalising the registration endpoint does, from the same E.164
            // values, rather than a hand-rolled slice of the string: the seeded accounts
            // have to be storable under the same rules as a real signup.
            PhoneNumber = PhoneNumber.Create(phone).E164,
            PasswordHash = passwordHash,
            ActiveRole = role,
            IsActive = true,

            // A verified phone and a green badge are different things, and the demo needs
            // to show both states: the number is confirmed on every account here, while
            // the badge varies.
            IsVerified = true,
            VerificationStatus = status,
            Area = area,
            CreatedAt = Now.AddDays(-45).UtcDateTime,
            UpdatedAt = Now.AddDays(-45).UtcDateTime
        };

    private static GiverProfile NewGiverProfile(
        User user,
        GiverKind kind,
        string businessName,
        string description,
        string? contactPhone) =>
        new()
        {
            UserId = user.Id,
            Kind = kind,
            BusinessName = businessName,
            BusinessType = kind == GiverKind.Business ? "Company" : null,
            Description = description,
            Location = user.Area,
            ContactPerson = $"{user.FirstName} {user.LastName}",
            ContactPhone = contactPhone,
            TotalJobsPosted = 1,
            CreatedAt = Now.AddDays(-45).UtcDateTime
        };

    private static WorkerProfile NewWorkerProfile(
        User user,
        string bio,
        string area,
        IReadOnlyList<string> skills,
        int completed,
        bool available,
        bool online)
    {
        var profile = new WorkerProfile
        {
            UserId = user.Id,
            Bio = bio,
            Experience = $"{completed} jobs completed through Jobmate.",
            Location = area,
            Availability = available ? "Weekday mornings and Saturdays" : "Not currently looking",
            ExpectedPay = 1000m,
            ExpectedPayType = PayType.Daily,
            IsAvailable = available,
            IsOnline = online,
            LastActiveAt = Now.AddHours(-2).UtcDateTime,
            TotalCompletedJobs = completed,
            TotalApplications = completed + 3,
            CreatedAt = Now.AddDays(-45).UtcDateTime
        };

        foreach (var skill in skills)
        {
            profile.Skills.Add(new WorkerSkill { Name = skill });
        }

        return profile;
    }

    private static Job NewJob(
        User giver,
        string company,
        string title,
        JobCategory category,
        string description,
        int workersNeeded,
        int filled,
        DateOnly jobDate,
        int pay,
        string area,
        string address,
        bool urgent,
        IReadOnlyList<string> skills)
    {
        var job = new Job
        {
            GiverId = giver.Id,
            Company = company,
            Title = title,
            Category = category,
            Description = description,
            WorkersNeeded = workersNeeded,
            WorkersFilled = filled,
            Experience = "No experience needed; on the job training is given.",
            JobDate = jobDate,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            ReportTime = new TimeOnly(8, 45),
            BreakMinutes = 30,
            Pay = pay,
            PayType = PayType.Daily,
            TravelAllowance = 0,
            FoodAllowance = 100,
            PaymentMethod = PaymentMethod.Upi,
            Area = area,
            Address = address,
            Latitude = 28.6139,
            Longitude = 77.2090,
            WorkplaceType = WorkplaceType.OnSite,
            LocationVisible = true,
            ContactPerson = giver.FirstName,
            EmergencyContact = "+919812300000",
            IsUrgent = urgent,
            Status = JobStatus.Active,
            PublishedAt = Now.AddDays(-1).UtcDateTime,
            CreatedAt = Now.AddDays(-1).UtcDateTime
        };

        foreach (var skill in skills)
        {
            job.Skills.Add(new JobSkill { Name = skill });
        }

        return job;
    }

    private static VerificationRequest NewVerificationRequest(
        User worker,
        VerificationType type,
        string document,
        string? note) =>
        new()
        {
            UserId = worker.Id,
            Type = type,
            Status = VerificationRequestStatus.Approved,
            DocumentName = document,
            Note = note,
            ReviewedAt = Now.AddDays(-3).UtcDateTime,
            CreatedAt = Now.AddDays(-3).UtcDateTime
        };
}
