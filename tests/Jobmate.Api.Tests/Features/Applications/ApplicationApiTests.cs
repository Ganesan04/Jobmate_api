using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Applications;

/// <summary>
/// Phase 2 acceptance tests for the application flow, run against the real API pipeline
/// and a real PostgreSQL database so the domain rules, the role gates and the SQL behind
/// the applicant list are genuinely exercised.
/// </summary>
public sealed class ApplicationApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public ApplicationApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_worker_applies_to_an_open_job_and_the_giver_sees_the_applicant()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        var applied = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new
            {
                message = "I have done this kind of loading work before.",
                expectedPay = 900,
                availableFrom = "Tomorrow morning"
            }),
            HttpStatusCode.OK);

        var application = applied.Data();

        Assert.Equal(jobId, application.GetProperty("jobId").GetGuid());
        Assert.Equal("applied", application.GetProperty("status").GetString());
        Assert.Equal(900, application.GetProperty("expectedPay").GetInt32());
        Assert.Equal("Tomorrow morning", application.GetProperty("availableFrom").GetString());

        // The applicant list is the giver's view of the same row, projected into the
        // shape the Flutter Select Worker screen renders.
        var applicants = await ApplicantsAsync(giver, jobId);

        Assert.Single(applicants.GetProperty("data").EnumerateArray());
        Assert.Equal(1, applicants.GetProperty("pagination").GetProperty("totalItems").GetInt32());

        var applicant = applicants.GetProperty("data").EnumerateArray().Single();

        Assert.Equal(worker.Email, applicant.GetProperty("worker").GetProperty("email").GetString());
        Assert.Equal("applied", applicant.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Opening_the_applicant_list_marks_an_application_as_viewed()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        // Applied until somebody actually looks at the applicant list.
        var before = (await WorkerApplicationsAsync(worker)).GetProperty("data").EnumerateArray().Single();

        Assert.Equal("applied", before.GetProperty("status").GetString());

        // Nulls are omitted from the payload, so an unread application simply has no
        // viewedAt key at all.
        Assert.False(before.TryGetProperty("viewedAt", out _));

        await ApplicantsAsync(giver, jobId);

        var afterView = (await WorkerApplicationsAsync(worker)).GetProperty("data").EnumerateArray().Single();

        Assert.Equal("viewed", afterView.GetProperty("status").GetString());
        Assert.True(afterView.TryGetProperty("viewedAt", out var viewedAt));
        Assert.Equal(JsonValueKind.String, viewedAt.ValueKind);
        Assert.Equal(applicationId, afterView.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Applying_twice_is_a_conflict_rather_than_a_second_application()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await ApplyAsync(worker, jobId);

        var repeated = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Again" }),
            HttpStatusCode.Conflict);

        Assert.Equal("AlreadyApplied", repeated.GetProperty("errorCode").GetString());
        Assert.Equal(1, (await ApplicantsAsync(giver, jobId)).GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task A_giver_cannot_apply_because_the_apply_route_is_worker_only()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        // An account is either a giver or a worker, so the role gate refuses this before
        // the handler runs. The domain's own-job rule is the backstop for a token whose
        // role claim disagrees with the job's giver.
        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Me" }),
            HttpStatusCode.Forbidden);

        Assert.Equal("Forbidden", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_worker_cannot_apply_to_a_draft()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, publish: false);

        var refused = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Early" }),
            HttpStatusCode.BadRequest);

        Assert.Equal("JobNotOpen", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_worker_cannot_apply_once_every_slot_is_filled()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(first, jobId);

        // The one slot is taken by a booking, which is what a selection produces.
        await TestData.SeedBookingAsync(_factory, jobId, first.Email);

        var refused = await TestApi.ReadJsonAsync(
            await second.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Too late" }),
            HttpStatusCode.BadRequest);

        Assert.Equal("WorkerLimitReached", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Applying_to_an_unknown_job_is_a_404()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/jobs/{Guid.NewGuid()}/apply", new { message = "Hello" }),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Applying_requires_authentication_and_the_worker_role()
    {
        using var anonymous = _factory.CreateAnonymousClient();
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        var unauthenticated = await TestApi.ReadJsonAsync(
            await anonymous.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Hello" }),
            HttpStatusCode.Unauthorized);

        Assert.False(unauthenticated.GetProperty("success").GetBoolean());

        // A giver is authenticated but not a worker, so the role gate refuses it.
        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Hello" }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_worker_withdraws_their_own_application()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        var withdrawn = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/applications/{applicationId}/withdraw", null),
            HttpStatusCode.OK);

        Assert.Equal("withdrawn", withdrawn.Data().GetProperty("status").GetString());

        // The list still shows the row, closed rather than deleted, so the history is
        // not lost.
        var mine = await WorkerApplicationsAsync(worker);

        Assert.Equal("withdrawn", mine.GetProperty("data").EnumerateArray().Single().GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_withdrawn_application_cannot_be_withdrawn_again()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await worker.Client.PostAsync($"/api/v1/applications/{applicationId}/withdraw", null);

        var repeated = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/applications/{applicationId}/withdraw", null),
            HttpStatusCode.BadRequest);

        Assert.Equal("ApplicationClosed", repeated.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task One_worker_cannot_withdraw_another_workers_application()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var owner = await TestApi.CreateWorkerAsync(_factory);
        using var other = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(owner, jobId);

        await TestApi.ReadJsonAsync(
            await other.Client.PostAsync($"/api/v1/applications/{applicationId}/withdraw", null),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_giver_shortlists_an_applicant_with_a_proposed_pay()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        var shortlisted = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applicationId}/shortlist",
                new { proposedPay = 1200 }),
            HttpStatusCode.OK);

        Assert.Equal("shortlisted", shortlisted.Data().GetProperty("status").GetString());
        Assert.Equal(1200, shortlisted.Data().GetProperty("proposedPay").GetInt32());

        // The applicant card shows shortlisted, while the worker-facing view keeps the
        // underlying status.
        var applicant = (await ApplicantsAsync(giver, jobId)).GetProperty("data").EnumerateArray().Single();

        Assert.Equal("shortlisted", applicant.GetProperty("status").GetString());
        Assert.Equal("shortlisted", applicant.GetProperty("applicationStatus").GetString());
    }

    [Fact]
    public async Task A_giver_rejects_an_applicant_and_the_reason_is_stored()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        var rejected = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applicationId}/reject",
                new { reason = "Wanted someone with a van" }),
            HttpStatusCode.OK);

        Assert.Equal("rejected", rejected.Data().GetProperty("status").GetString());
        Assert.Equal("Wanted someone with a van", rejected.Data().GetProperty("rejectionReason").GetString());

        // A rejected application is terminal, so it cannot be shortlisted afterwards.
        var afterwards = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/shortlist", new { }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidApplicationTransition", afterwards.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Only_the_poster_can_review_applicants()
    {
        using var owner = await TestApi.CreateGiverAsync(_factory);
        using var stranger = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(owner);
        var applicationId = await ApplyAsync(worker, jobId);

        // The applicant list is the employer's, not a public record of who applied.
        await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync($"/api/v1/jobs/{jobId}/applications"),
            HttpStatusCode.Forbidden);

        await TestApi.ReadJsonAsync(
            await stranger.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/shortlist", new { }),
            HttpStatusCode.Forbidden);

        await TestApi.ReadJsonAsync(
            await stranger.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/reject", new { }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_worker_cannot_see_or_review_someone_elses_application_list()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var other = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync($"/api/v1/jobs/{jobId}/applications"),
            HttpStatusCode.Forbidden);

        await TestApi.ReadJsonAsync(
            await other.Client.GetAsync($"/api/v1/jobs/{jobId}/applications"),
            HttpStatusCode.Forbidden);

        // The worker's own list is the only application data a worker can read, and it
        // holds only their row.
        var mine = await WorkerApplicationsAsync(other);

        Assert.Empty(mine.GetProperty("data").EnumerateArray());
        Assert.Equal(0, mine.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.NotEqual(Guid.Empty, applicationId);
    }

    [Fact]
    public async Task The_applicant_list_filters_by_status_search_and_rating()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var keep = await TestApi.CreateWorkerAsync(_factory, area: "Noida");
        using var drop = await TestApi.CreateWorkerAsync(_factory, area: "Jaipur");

        var jobId = await CreateJobAsync(giver);
        var keepId = await ApplyAsync(keep, jobId);
        await ApplyAsync(drop, jobId);

        await giver.Client.PostAsJsonAsync($"/api/v1/applications/{keepId}/shortlist", new { });

        var shortlisted = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications?statuses=shortlisted"),
            HttpStatusCode.OK);

        Assert.Equal(1, shortlisted.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(keep.Email,
            shortlisted.GetProperty("data").EnumerateArray().Single().GetProperty("worker").GetProperty("email").GetString());

        // The text filter reaches the worker's area, which is where an employer looks
        // for someone nearby.
        var byArea = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications?search=jaipur"),
            HttpStatusCode.OK);

        Assert.Equal(1, byArea.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(drop.Email,
            byArea.GetProperty("data").EnumerateArray().Single().GetProperty("worker").GetProperty("email").GetString());

        // Every new worker starts at a zero rating, so a minimum of one excludes them.
        var rated = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications?minRating=1"),
            HttpStatusCode.OK);

        Assert.Equal(0, rated.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task The_applicant_list_sorts_and_pages_in_the_database()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(first, jobId, expectedPay: 500);
        await ApplyAsync(second, jobId, expectedPay: 1500);

        var cheapest = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications?sortBy=expectedPayLow&pageSize=1"),
            HttpStatusCode.OK);

        var page = cheapest.GetProperty("pagination");

        Assert.Equal(2, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
        Assert.False(page.GetProperty("hasPrevious").GetBoolean());
        Assert.Equal(500, cheapest.GetProperty("data").EnumerateArray().Single()
            .GetProperty("expectedPay").GetInt32());

        var dearest = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications?sortBy=expectedPayHigh&pageSize=1&page=2"),
            HttpStatusCode.OK);

        // Paging has to move, otherwise the two pages show the same applicant.
        Assert.Equal(500, dearest.GetProperty("data").EnumerateArray().Single().GetProperty("expectedPay").GetInt32());
        Assert.True(dearest.GetProperty("pagination").GetProperty("hasPrevious").GetBoolean());
    }

    [Fact]
    public async Task An_unknown_applicant_sort_key_is_rejected()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications?sortBy=cheapest"),
            HttpStatusCode.BadRequest);

        Assert.Equal("sortBy", refused.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task The_worker_application_list_filters_by_status_and_job_status()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var openJobId = await CreateJobAsync(giver);
        var openApplicationId = await ApplyAsync(worker, openJobId);

        // The second job leaves the active pool by being cancelled, which also expires
        // that application. Both rows stay in the worker's history.
        var closedJobId = await CreateJobAsync(giver, title: "Closed soon");
        var closedApplicationId = await ApplyAsync(worker, closedJobId);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{openApplicationId}/shortlist", new { }),
            HttpStatusCode.OK);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{closedJobId}/cancel", new { reason = "No longer needed" }),
            HttpStatusCode.OK);

        var all = await WorkerApplicationsAsync(worker);
        Assert.Equal(2, all.GetProperty("pagination").GetProperty("totalItems").GetInt32());

        var shortlisted = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/applications?statuses=shortlisted"),
            HttpStatusCode.OK);

        Assert.Equal(1, shortlisted.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(openJobId, shortlisted.GetProperty("data").EnumerateArray().Single()
            .GetProperty("jobId").GetGuid());

        // Filtering by the job's own status splits the two rows.
        var activeOnly = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/applications?jobStatus=active"),
            HttpStatusCode.OK);

        Assert.Equal(1, activeOnly.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(openJobId, activeOnly.GetProperty("data").EnumerateArray().Single()
            .GetProperty("jobId").GetGuid());

        var cancelledOnly = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/applications?jobStatus=cancelled"),
            HttpStatusCode.OK);

        Assert.Equal(1, cancelledOnly.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(closedApplicationId, cancelledOnly.GetProperty("data").EnumerateArray().Single()
            .GetProperty("id").GetGuid());

        // The job summary travels with the row so the app can render a card offline.
        var card = all.GetProperty("data").EnumerateArray()
            .Single(a => a.GetProperty("jobId").GetGuid() == openJobId)
            .GetProperty("job");

        Assert.Equal(openJobId, card.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_rejected_application_is_closed_to_the_worker_list()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/reject", new { reason = "No" });

        var rejected = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/applications?statuses=rejected"),
            HttpStatusCode.OK);

        Assert.Equal(1, rejected.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task The_giver_and_worker_stats_count_applications()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);
        await ApplyAsync(worker, jobId, expectConflict: true);

        var giverStats = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/giver/stats"),
            HttpStatusCode.OK);

        Assert.Equal(1, giverStats.Data().GetProperty("applications").GetInt32());

        var workerStats = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/stats"),
            HttpStatusCode.OK);

        // The worker's dashboard counter follows the application, and only the one real
        // application is counted.
        Assert.Equal(1, workerStats.Data().GetProperty("applied").GetInt32());
    }

    [Fact]
    public async Task An_application_for_a_cancelled_job_is_closed_and_never_applied_again()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/cancel", new { reason = "No longer needed" });

        // Cancelling the job expires the application, and the unique index still holds
        // the worker's place in the history.
        var mine = await WorkerApplicationsAsync(worker);

        Assert.Equal("expired", mine.GetProperty("data").EnumerateArray().Single().GetProperty("status").GetString());

        var refused = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/apply", new { message = "Still?" }),
            HttpStatusCode.Conflict);

        Assert.Equal("AlreadyApplied", refused.GetProperty("errorCode").GetString());
        Assert.NotEqual(Guid.Empty, applicationId);
    }

    [Fact]
    public async Task An_unknown_application_id_is_a_404()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{Guid.NewGuid()}/shortlist", new { }),
            HttpStatusCode.NotFound);
    }

    /// <summary>A complete, valid Create Job wizard payload.</summary>
    private static Dictionary<string, object?> ValidJob(string title, bool publishImmediately = true, int workersNeeded = 1) =>
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
            ["publishImmediately"] = publishImmediately
        };

    private async Task<Guid> CreateJobAsync(
        AuthenticatedClient giver,
        string title = "Warehouse load",
        bool publish = true)
    {
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(title, publish)),
            HttpStatusCode.OK);

        return created.Data().GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ApplyAsync(
        AuthenticatedClient worker,
        Guid jobId,
        int? expectedPay = null,
        bool expectConflict = false)
    {
        var response = await worker.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/apply",
            new { message = "Available and experienced.", expectedPay });

        if (expectConflict)
        {
            await TestApi.ReadJsonAsync(response, HttpStatusCode.Conflict);

            return Guid.Empty;
        }

        var applied = await TestApi.ReadJsonAsync(response, HttpStatusCode.OK);

        return applied.Data().GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ApplicantsAsync(AuthenticatedClient giver, Guid jobId) =>
        await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications"),
            HttpStatusCode.OK);

    private static async Task<JsonElement> WorkerApplicationsAsync(AuthenticatedClient worker) =>
        await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/applications"),
            HttpStatusCode.OK);
}
