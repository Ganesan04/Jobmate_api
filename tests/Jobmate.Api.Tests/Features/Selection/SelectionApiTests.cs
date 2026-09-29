using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Selection;

/// <summary>
/// Phase 3 acceptance tests for worker selection. The interesting cases here are the
/// ones where two requests race for the same slot, so they are sent in parallel against
/// the real API and a real PostgreSQL database rather than simulated.
/// </summary>
public sealed class SelectionApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public SelectionApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- booking a worker

    [Fact]
    public async Task Selecting_a_shortlisted_applicant_books_them_and_fills_a_slot()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2);
        var applicationId = await ApplyAsync(worker, jobId);

        await ShortlistAsync(giver, applicationId, proposedPay: 1400);

        var booking = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            HttpStatusCode.OK);

        var assignment = booking.Data();

        Assert.Equal(jobId, assignment.GetProperty("jobId").GetGuid());
        Assert.Equal(1400, assignment.GetProperty("agreedPay").GetInt32());
        Assert.Equal("pending", assignment.GetProperty("status").GetString());
        Assert.Equal("pending", assignment.GetProperty("paymentStatus").GetString());
        Assert.True(assignment.GetProperty("isCheckedIn").GetBoolean() is false);

        // The application is no longer a live application, and the job is one worker down.
        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        Assert.Equal(1, job.Data().GetProperty("workersFilled").GetInt32());
        Assert.Equal(1, job.Data().GetProperty("remainingWorkers").GetInt32());

        var applicants = await ApplicantsAsync(giver, jobId);

        Assert.Equal("selected", applicants.GetProperty("data").EnumerateArray().Single()
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task Selecting_moves_the_worker_from_applied_to_active_on_their_dashboard()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await SelectAsync(giver, applicationId);

        var stats = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/stats"),
            HttpStatusCode.OK);

        Assert.Equal(0, stats.Data().GetProperty("applied").GetInt32());
        Assert.Equal(1, stats.Data().GetProperty("active").GetInt32());
    }

    [Fact]
    public async Task The_agreed_pay_falls_back_from_the_request_to_the_posted_pay()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2, pay: 900);
        var noProposal = await ApplyAsync(first, jobId);
        var withProposal = await ApplyAsync(second, jobId);

        await ShortlistAsync(giver, withProposal, proposedPay: 1200);

        // Nothing proposed and nothing in the body: the pay on the posting wins.
        var firstBooking = await SelectAsync(giver, noProposal);

        Assert.Equal(900, firstBooking.GetProperty("agreedPay").GetInt32());

        // A proposal made while shortlisting is used when the body is empty.
        var secondBooking = await SelectAsync(giver, withProposal);

        Assert.Equal(1200, secondBooking.GetProperty("agreedPay").GetInt32());
    }

    [Fact]
    public async Task An_explicit_agreed_pay_overrides_the_proposal()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await ShortlistAsync(giver, applicationId, proposedPay: 1200);

        var booking = await SelectAsync(giver, applicationId, agreedPay: 1500);

        Assert.Equal(1500, booking.GetProperty("agreedPay").GetInt32());
    }

    // ------------------------------------------------------------------- capacity

    [Fact]
    public async Task The_last_slot_cannot_be_booked_twice()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 1);
        var taken = await ApplyAsync(first, jobId);
        var refused = await ApplyAsync(second, jobId);

        await SelectAsync(giver, taken);

        var conflict = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{refused}/select", new { }),
            HttpStatusCode.Conflict);

        Assert.Equal("WorkerLimitReached", conflict.GetProperty("errorCode").GetString());

        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        Assert.Equal(1, job.Data().GetProperty("workersFilled").GetInt32());
    }

    [Fact]
    public async Task Two_givers_racing_for_the_last_slot_produce_one_booking()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        // One slot, two applicants, two requests fired at the same time.
        var jobId = await CreateJobAsync(giver, workersNeeded: 1);
        var firstApplication = await ApplyAsync(first, jobId);
        var secondApplication = await ApplyAsync(second, jobId);

        var responses = await Task.WhenAll(
            giver.Client.PostAsJsonAsync($"/api/v1/applications/{firstApplication}/select", new { }),
            giver.Client.PostAsJsonAsync($"/api/v1/applications/{secondApplication}/select", new { }));

        var statuses = responses.Select(r => r.StatusCode).Order().ToList();

        Assert.Equal(HttpStatusCode.OK, statuses[0]);
        Assert.Equal(HttpStatusCode.Conflict, statuses[1]);

        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        // The job is staffed exactly once: the losing update matched no rows.
        Assert.Equal(1, job.Data().GetProperty("workersFilled").GetInt32());

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task The_same_application_selected_twice_books_the_worker_once()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2);
        var applicationId = await ApplyAsync(worker, jobId);

        var responses = await Task.WhenAll(
            giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }));

        var statuses = responses.Select(r => r.StatusCode).Order().ToList();

        Assert.True(
            statuses.Count(s => s == HttpStatusCode.OK) == 1 && statuses.Count(s => s == HttpStatusCode.Conflict) == 1,
            $"Expected one booking and one conflict but got {string.Join(", ", statuses)}.");

        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        // One booking, one slot, even though both requests passed the domain check.
        Assert.Equal(1, job.Data().GetProperty("workersFilled").GetInt32());

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ------------------------------------------------------------- bulk selection

    [Fact]
    public async Task A_bulk_selection_books_every_worker_and_reports_the_remaining_slots()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);
        using var third = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 3);
        var firstApplication = await ApplyAsync(first, jobId);
        var secondApplication = await ApplyAsync(second, jobId);
        var thirdApplication = await ApplyAsync(third, jobId);

        var result = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new
                {
                    selections = new Dictionary<Guid, int>
                    {
                        [firstApplication] = 1000,
                        [secondApplication] = 1100
                    }
                }),
            HttpStatusCode.OK);

        var data = result.Data();

        Assert.Equal(2, data.GetProperty("selected").GetArrayLength());
        Assert.Equal(2, data.GetProperty("workersFilled").GetInt32());
        Assert.Equal(3, data.GetProperty("workersNeeded").GetInt32());
        Assert.Equal(1, data.GetProperty("remainingSlots").GetInt32());

        var agreedPays = data.GetProperty("selected").EnumerateArray()
            .Select(a => a.GetProperty("agreedPay").GetInt32())
            .Order()
            .ToList();

        Assert.Equal([1000, 1100], agreedPays);

        // The third applicant is untouched and still open.
        var mine = await TestApi.ReadJsonAsync(
            await third.Client.GetAsync("/api/v1/worker/applications"),
            HttpStatusCode.OK);

        Assert.Equal("applied", mine.GetProperty("data").EnumerateArray().Single()
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_bulk_selection_that_does_not_fit_books_nobody()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 1);
        var firstApplication = await ApplyAsync(first, jobId);
        var secondApplication = await ApplyAsync(second, jobId);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new
                {
                    selections = new Dictionary<Guid, int>
                    {
                        [firstApplication] = 1000,
                        [secondApplication] = 1000
                    }
                }),
            HttpStatusCode.Conflict);

        Assert.Equal("WorkerLimitReached", refused.GetProperty("errorCode").GetString());

        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        // All or nothing: the slot taken by the first entry was given back.
        Assert.Equal(0, job.Data().GetProperty("workersFilled").GetInt32());

        var applicants = await ApplicantsAsync(giver, jobId);

        Assert.All(
            applicants.GetProperty("data").EnumerateArray(),
            applicant => Assert.NotEqual("selected", applicant.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task A_bulk_selection_rolls_back_when_one_applicant_is_not_selectable()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 3);
        var good = await ApplyAsync(first, jobId);
        var rejected = await ApplyAsync(second, jobId);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{rejected}/reject", new { reason = "No" }),
            HttpStatusCode.OK);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new
                {
                    selections = new Dictionary<Guid, int>
                    {
                        [good] = 1000,
                        [rejected] = 1000
                    }
                }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidApplicationTransition", refused.GetProperty("errorCode").GetString());

        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        Assert.Equal(0, job.Data().GetProperty("workersFilled").GetInt32());
    }

    [Fact]
    public async Task A_bulk_selection_releases_the_shortlisted_workers_it_did_not_pick()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var picked = await TestApi.CreateWorkerAsync(_factory);
        using var waiting = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 1);
        var pickedApplication = await ApplyAsync(picked, jobId);
        var waitingApplication = await ApplyAsync(waiting, jobId);

        await ShortlistAsync(giver, pickedApplication);
        await ShortlistAsync(giver, waitingApplication);

        var result = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new
                {
                    selections = new Dictionary<Guid, int> { [pickedApplication] = 1000 },
                    releaseUnselectedShortlists = true
                }),
            HttpStatusCode.OK);

        Assert.Equal([waitingApplication], result.Data().GetProperty("releasedApplicationIds")
            .EnumerateArray().Select(id => id.GetGuid()).ToList());

        var mine = await TestApi.ReadJsonAsync(
            await waiting.Client.GetAsync("/api/v1/worker/applications"),
            HttpStatusCode.OK);

        // Back in the open pool, and no longer counted as shortlisted.
        Assert.Equal("applied", mine.GetProperty("data").EnumerateArray().Single()
            .GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_bulk_selection_can_keep_the_shortlist_intact()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var picked = await TestApi.CreateWorkerAsync(_factory);
        using var waiting = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 1);
        var pickedApplication = await ApplyAsync(picked, jobId);
        var waitingApplication = await ApplyAsync(waiting, jobId);

        await ShortlistAsync(giver, pickedApplication);
        await ShortlistAsync(giver, waitingApplication);

        var result = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new
                {
                    selections = new Dictionary<Guid, int> { [pickedApplication] = 1000 },
                    releaseUnselectedShortlists = false
                }),
            HttpStatusCode.OK);

        Assert.Empty(result.Data().GetProperty("releasedApplicationIds").EnumerateArray());

        var mine = await TestApi.ReadJsonAsync(
            await waiting.Client.GetAsync("/api/v1/worker/applications?statuses=shortlisted"),
            HttpStatusCode.OK);

        Assert.Equal(1, mine.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task A_bulk_selection_needs_at_least_one_worker_and_a_sane_pay()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        var empty = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new { selections = new Dictionary<Guid, int>() }),
            HttpStatusCode.BadRequest);

        Assert.Equal("selections", empty.GetProperty("errors")[0].GetProperty("field").GetString());

        var badPay = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new { selections = new Dictionary<Guid, int> { [applicationId] = 0 } }),
            HttpStatusCode.BadRequest);

        Assert.Equal("selections", badPay.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    // -------------------------------------------------------------- who may select

    [Fact]
    public async Task A_worker_cannot_select_themselves()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        var refused = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            HttpStatusCode.Forbidden);

        Assert.Equal("Forbidden", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Another_giver_cannot_select_workers_for_someone_elses_job()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        var refused = await TestApi.ReadJsonAsync(
            await other.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            HttpStatusCode.Forbidden);

        Assert.Equal("Forbidden", refused.GetProperty("errorCode").GetString());

        var bulkRefused = await TestApi.ReadJsonAsync(
            await other.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new { selections = new Dictionary<Guid, int> { [applicationId] = 1000 } }),
            HttpStatusCode.Forbidden);

        Assert.Equal("Forbidden", bulkRefused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_unknown_application_id_is_a_404()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{Guid.NewGuid()}/select", new { }),
            HttpStatusCode.NotFound);

        Assert.Equal("NotFound", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Selecting_requires_authentication()
    {
        var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            $"/api/v1/applications/{Guid.NewGuid()}/select",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_applicant_from_another_job_cannot_be_booked()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2);
        var otherJobId = await CreateJobAsync(giver, title: "Second shift", workersNeeded: 2);
        var applicationId = await ApplyAsync(worker, otherJobId);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/select-workers",
                new { selections = new Dictionary<Guid, int> { [applicationId] = 1000 } }),
            HttpStatusCode.NotFound);

        Assert.Equal("NotFound", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_cancelled_job_takes_no_new_workers()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/cancel", new { reason = "Not needed" }),
            HttpStatusCode.OK);

        // Cancelling expires the open applications, so the applicant is closed before the
        // job's own state is even considered.
        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidApplicationTransition", refused.GetProperty("errorCode").GetString());

        var job = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}"),
            HttpStatusCode.OK);

        Assert.Equal(0, job.Data().GetProperty("workersFilled").GetInt32());
    }

    [Fact]
    public async Task A_job_that_has_started_takes_no_new_workers()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var booked = await TestApi.CreateWorkerAsync(_factory);
        using var applicant = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2);
        var applicationId = await ApplyAsync(applicant, jobId);

        await ShortlistAsync(giver, applicationId);

        // A second worker is booked and the job starts, while the first applicant is still
        // shortlisted, so the job's own status is what refuses the selection.
        await TestData.SeedBookingAsync(_factory, jobId, booked.Email);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{jobId}/start", null),
            HttpStatusCode.OK);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            HttpStatusCode.BadRequest);

        Assert.Equal("JobNotSelectable", refused.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_withdrawn_application_cannot_be_booked()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var applicationId = await ApplyAsync(worker, jobId);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/applications/{applicationId}/withdraw", null),
            HttpStatusCode.OK);

        var refused = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/select", new { }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidApplicationTransition", refused.GetProperty("errorCode").GetString());
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A complete, valid Create Job wizard payload.</summary>
    private static Dictionary<string, object?> ValidJob(
        string title,
        int workersNeeded,
        int pay = 1000) =>
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
            ["pay"] = pay,
            ["payType"] = 3,
            ["area"] = "Delhi",
            ["address"] = "12 Warehouse Road",
            ["workplaceType"] = 1,
            ["locationVisible"] = true,
            ["latitude"] = 28.6139,
            ["longitude"] = 77.2090,
            ["publishImmediately"] = true
        };

    private async Task<Guid> CreateJobAsync(
        AuthenticatedClient giver,
        string title = "Warehouse load",
        int workersNeeded = 1,
        int pay = 1000)
    {
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(title, workersNeeded, pay)),
            HttpStatusCode.OK);

        return created.Data().GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ApplyAsync(AuthenticatedClient worker, Guid jobId)
    {
        var applied = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/apply",
                new { message = "Available and experienced." }),
            HttpStatusCode.OK);

        return applied.Data().GetProperty("id").GetGuid();
    }

    private static async Task ShortlistAsync(
        AuthenticatedClient giver,
        Guid applicationId,
        int? proposedPay = null)
    {
        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applicationId}/shortlist",
                new { proposedPay }),
            HttpStatusCode.OK);
    }

    private static async Task<JsonElement> SelectAsync(
        AuthenticatedClient giver,
        Guid applicationId,
        int? agreedPay = null)
    {
        var booked = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applicationId}/select",
                new { agreedPay }),
            HttpStatusCode.OK);

        return booked.Data();
    }

    private static async Task<JsonElement> ApplicantsAsync(AuthenticatedClient giver, Guid jobId) =>
        await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/applications"),
            HttpStatusCode.OK);
}
