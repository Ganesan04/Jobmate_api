using System.Net;
using System.Net.Http.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Ratings;

/// <summary>
/// Phase 5 acceptance tests for attendance, completion, ratings and work history.
///
/// The chain these cover is the one the app depends on end to end: a job starts, the
/// worker proves they arrived, the booking is closed, the job completes, and only then
/// can either side leave feedback.
/// </summary>
public sealed class RatingApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public RatingApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------- check-in

    [Fact]
    public async Task Starting_a_job_issues_a_check_in_code_the_worker_can_use()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var code = await CheckInCodeAsync(giver, jobId, assignmentId);

        Assert.Equal(4, code.Length);
        Assert.All(code, c => Assert.True(char.IsDigit(c)));

        var checkedIn = await CheckInAsync(worker, assignmentId, code);

        Assert.True(checkedIn.GetProperty("isCheckedIn").GetBoolean());
        Assert.Equal("checkedIn", checkedIn.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_check_in_code_is_not_reachable_by_the_worker()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var response = await worker.Client.GetAsync($"/api/v1/jobs/{jobId}/check-in-code/{assignmentId}");

        // The worker is refused outright rather than shown a code they could use to check
        // in on their own, which is the whole point of the employer reading it out.
        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);

        var ownJobs = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/jobs"),
            HttpStatusCode.OK);

        Assert.DoesNotContain(
            "checkInCode",
            ownJobs.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_wrong_check_in_code_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var code = await CheckInCodeAsync(giver, jobId, assignmentId);
        var wrong = code == "0000" ? "1111" : "0000";

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/assignments/{assignmentId}/check-in",
                new { code = wrong }),
            HttpStatusCode.BadRequest);

        Assert.Equal("CheckInCodeInvalid", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_check_in_code_cannot_be_used_twice()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var code = await CheckInCodeAsync(giver, jobId, assignmentId);

        await CheckInAsync(worker, assignmentId, code);

        var second = await worker.Client.PostAsJsonAsync(
            $"/api/v1/assignments/{assignmentId}/check-in",
            new { code });

        await TestApi.ReadJsonAsync(second, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Checking_in_before_the_job_starts_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        // No code has been issued yet, so the answer is "not started", not "wrong code".
        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/assignments/{assignmentId}/check-in",
                new { code = "1234" }),
            HttpStatusCode.BadRequest);

        Assert.Equal("JobNotStarted", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Another_worker_cannot_check_in_to_someone_elses_booking()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var impostor = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var code = await CheckInCodeAsync(giver, jobId, assignmentId);

        var response = await impostor.Client.PostAsJsonAsync(
            $"/api/v1/assignments/{assignmentId}/check-in",
            new { code });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------ completion

    [Fact]
    public async Task Completing_the_last_booking_completes_the_job()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2);
        var one = await BookAsync(giver, first, jobId);
        var two = await BookAsync(giver, second, jobId);

        await StartAsync(giver, jobId);

        await CompleteAsync(giver, one);

        // One booking is still open, so the job is not finished yet.
        Assert.Equal("inProgress", await JobStatusAsync(giver, jobId));

        await CompleteAsync(giver, two);

        Assert.Equal("completed", await JobStatusAsync(giver, jobId));
    }

    [Fact]
    public async Task Overtime_recorded_at_completion_is_kept_for_the_payout()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var response = await worker.Client.PostAsJsonAsync(
            $"/api/v1/assignments/{assignmentId}/complete",
            new { overtimeAmount = 250 });

        var completed = (await TestApi.ReadJsonAsync(response, HttpStatusCode.OK)).Data();

        Assert.Equal(250, completed.GetProperty("overtimeAmount").GetInt32());
        Assert.Equal(1250, completed.GetProperty("totalPay").GetInt32());
        Assert.Equal("completed", completed.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_stranger_cannot_close_someone_elses_booking()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);

        var response = await stranger.Client.PostAsJsonAsync(
            $"/api/v1/assignments/{assignmentId}/complete",
            new { });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    // --------------------------------------------------------------------- ratings

    [Fact]
    public async Task An_employer_can_rate_the_worker_once_the_job_is_completed()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CompletedJobAsync(giver, worker);

        var response = await giver.Client.PostAsJsonAsync(
            "/api/v1/ratings",
            new { jobId, toUserId = worker.UserId, stars = 5, feedback = "Punctual and careful." });

        var rating = (await TestApi.ReadJsonAsync(response, HttpStatusCode.Created)).Data();

        Assert.Equal(5, rating.GetProperty("stars").GetInt32());
        Assert.Equal("Punctual and careful.", rating.GetProperty("feedback").GetString());
        Assert.Equal(jobId, rating.GetProperty("jobId").GetGuid());
        Assert.Equal(worker.UserId, rating.GetProperty("workerId").GetGuid());
    }

    [Fact]
    public async Task A_worker_can_rate_their_employer_too()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CompletedJobAsync(giver, worker);

        var response = await worker.Client.PostAsJsonAsync(
            "/api/v1/ratings",
            new { jobId, toUserId = giver.UserId, stars = 4, feedback = "Paid on time." });

        var rating = (await TestApi.ReadJsonAsync(response, HttpStatusCode.Created)).Data();

        Assert.Equal(4, rating.GetProperty("stars").GetInt32());
        Assert.Equal(giver.UserId, rating.GetProperty("giverId").GetGuid());
    }

    [Fact]
    public async Task Rating_before_the_job_is_completed_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var error = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/ratings",
                new { jobId, toUserId = worker.UserId, stars = 5 }),
            HttpStatusCode.BadRequest);

        Assert.Equal("JobNotCompleted", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task The_same_person_cannot_be_rated_twice_for_one_job()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CompletedJobAsync(giver, worker);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/ratings",
                new { jobId, toUserId = worker.UserId, stars = 5 }),
            HttpStatusCode.Created);

        var second = await giver.Client.PostAsJsonAsync(
            "/api/v1/ratings",
            new { jobId, toUserId = worker.UserId, stars = 1 });

        await TestApi.ReadJsonAsync(second, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rating_yourself_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CompletedJobAsync(giver, worker);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                "/api/v1/ratings",
                new { jobId, toUserId = worker.UserId, stars = 5 }),
            HttpStatusCode.BadRequest);

        Assert.Equal("SelfRating", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Someone_who_was_never_on_the_job_cannot_be_rated()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var bystander = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CompletedJobAsync(giver, worker);

        var response = await giver.Client.PostAsJsonAsync(
            "/api/v1/ratings",
            new { jobId, toUserId = bystander.UserId, stars = 5 });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_rating_outside_one_to_five_stars_is_rejected()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CompletedJobAsync(giver, worker);

        var error = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/ratings",
                new { jobId, toUserId = worker.UserId, stars = 9 }),
            HttpStatusCode.BadRequest);

        Assert.Contains(
            error.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "stars");
    }

    [Fact]
    public async Task Every_rating_left_about_a_person_is_kept()
    {
        using var first = await TestApi.CreateGiverAsync(_factory);
        using var second = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await CompletedJobAsync(first, worker, 5);
        await CompletedJobAsync(second, worker, 2);

        var page = await TestApi.ReadJsonAsync(
            await first.Client.GetAsync($"/api/v1/ratings?toUserId={worker.UserId}"),
            HttpStatusCode.OK);

        Assert.Equal(2, page.GetProperty("pagination").GetProperty("totalItems").GetInt32());

        var given = page.GetProperty("data").EnumerateArray()
            .Select(r => r.GetProperty("stars").GetInt32())
            .Order()
            .ToArray();

        Assert.Equal([2, 5], given);
    }

    // ---------------------------------------------------------------- work history

    [Fact]
    public async Task The_worker_history_shows_the_booking_with_its_payment_status()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, title: "Night shift");
        var assignmentId = await BookAsync(giver, worker, jobId, agreedPay: 1000);

        await StartAsync(giver, jobId);
        await CompleteAsync(giver, assignmentId);

        var entry = await OnlyHistoryEntryAsync(worker, "/api/v1/worker/work-history");

        Assert.Equal(jobId, entry.GetProperty("jobId").GetGuid());
        Assert.Equal("Night shift", entry.GetProperty("jobTitle").GetString());
        Assert.Equal("pending", entry.GetProperty("paymentStatus").GetString());
        Assert.Equal(1000, entry.GetProperty("earnings").GetInt32());
        Assert.False(entry.GetProperty("ratedByGiver").GetBoolean());
    }

    [Fact]
    public async Task The_history_shows_the_rating_the_employer_left()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await CompletedJobAsync(giver, worker, 5);

        var entry = await OnlyHistoryEntryAsync(worker, "/api/v1/worker/work-history");

        Assert.Equal(5, entry.GetProperty("rating").GetInt32());
        Assert.True(entry.GetProperty("ratedByGiver").GetBoolean());
    }

    [Fact]
    public async Task The_history_records_the_shift_once_the_worker_checks_in()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);
        await CheckInAsync(worker, assignmentId, await CheckInCodeAsync(giver, jobId, assignmentId));
        await CompleteAsync(giver, assignmentId);

        var entry = await OnlyHistoryEntryAsync(worker, "/api/v1/worker/work-history");

        // Checked in and completed within the same second, so the duration is a fraction
        // of an hour rather than a missing value.
        Assert.True(entry.GetProperty("durationHours").GetDouble() >= 0);
    }

    [Fact]
    public async Task The_employer_history_names_the_worker_on_each_line()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var entry = await OnlyHistoryEntryAsync(giver, "/api/v1/giver/work-history");

        Assert.Equal(worker.UserId, entry.GetProperty("counterpartyId").GetGuid());
    }

    [Fact]
    public async Task A_worker_cannot_read_the_employer_history()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var response = await worker.Client.GetAsync("/api/v1/giver/work-history");

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------- helpers

    /// <summary>Books the worker, starts the job and closes the booking, completing it.</summary>
    private async Task<Guid> CompletedJobAsync(
        AuthenticatedClient giver,
        AuthenticatedClient worker,
        int? stars = null)
    {
        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        await StartAsync(giver, jobId);
        await CompleteAsync(giver, assignmentId);

        if (stars is not null)
        {
            await TestApi.ReadJsonAsync(
                await giver.Client.PostAsJsonAsync(
                    "/api/v1/ratings",
                    new { jobId, toUserId = worker.UserId, stars }),
                HttpStatusCode.Created);
        }

        return jobId;
    }

    private static async Task StartAsync(AuthenticatedClient giver, Guid jobId) =>
        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { }),
            HttpStatusCode.OK);

    private static async Task CompleteAsync(AuthenticatedClient caller, Guid assignmentId) =>
        await TestApi.ReadJsonAsync(
            await caller.Client.PostAsJsonAsync($"/api/v1/assignments/{assignmentId}/complete", new { }),
            HttpStatusCode.OK);

    private static async Task<System.Text.Json.JsonElement> CheckInAsync(
        AuthenticatedClient worker,
        Guid assignmentId,
        string code) =>
        (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/assignments/{assignmentId}/check-in",
                new { code }),
            HttpStatusCode.OK)).Data();

    private static async Task<string> CheckInCodeAsync(
        AuthenticatedClient giver,
        Guid jobId,
        Guid assignmentId) =>
        (await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/check-in-code/{assignmentId}"),
            HttpStatusCode.OK)).Data().GetString()!;

    private static async Task<string> JobStatusAsync(AuthenticatedClient giver, Guid jobId) =>
        (await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK)).Data().GetProperty("status").GetString()!;

    private static async Task<System.Text.Json.JsonElement> OnlyHistoryEntryAsync(
        AuthenticatedClient caller,
        string path) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync(path),
            HttpStatusCode.OK)).GetProperty("data").EnumerateArray().Single();

    private async Task<Guid> CreateJobAsync(
        AuthenticatedClient giver,
        string title = "Warehouse load",
        int workersNeeded = 1)
    {
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(title, workersNeeded)),
            HttpStatusCode.OK);

        return created.Data().GetProperty("id").GetGuid();
    }

    private static async Task<Guid> BookAsync(
        AuthenticatedClient giver,
        AuthenticatedClient worker,
        Guid jobId,
        int? agreedPay = null)
    {
        var applied = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/apply",
                new { message = "Available and experienced." }),
            HttpStatusCode.OK);

        var booked = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applied.Data().GetProperty("id").GetGuid()}/select",
                new { agreedPay }),
            HttpStatusCode.OK);

        return booked.Data().GetProperty("id").GetGuid();
    }

    /// <summary>A complete, valid Create Job wizard payload.</summary>
    private static Dictionary<string, object?> ValidJob(string title, int workersNeeded) =>
        new()
        {
            ["title"] = title,
            ["category"] = 1,
            ["description"] = "Loading and unloading a truck.",
            ["workersNeeded"] = workersNeeded,
            ["skills"] = new[] { "Loading" },
            ["date"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3).ToString("yyyy-MM-dd"),
            ["startTime"] = "09:00:00",
            ["endTime"] = "17:00:00",
            ["reportTime"] = "08:45:00",
            ["breakMinutes"] = 30,
            ["pay"] = 1000,
            ["payType"] = 3,
            ["area"] = "Delhi",
            ["address"] = "12 Warehouse Road",
            ["workplaceType"] = 1,
            ["locationVisible"] = true,
            ["latitude"] = 28.6139,
            ["longitude"] = 77.2090,
            ["publishImmediately"] = true
        };
}
