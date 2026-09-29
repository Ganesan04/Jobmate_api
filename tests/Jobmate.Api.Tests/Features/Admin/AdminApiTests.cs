using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Admin;

/// <summary>
/// Phase 10 acceptance tests for the admin back office.
///
/// The subject is the boundary: an ordinary user must not reach any of it, and a decision
/// recorded on one screen has to show up on the screen that reads it. The cases that
/// matter are the ones where a careless implementation would be plausible — approving a
/// request twice, letting a review overwrite itself, an admin suspending themselves, and
/// the stored status drifting from the badge the applicant sees.
/// </summary>
public sealed class AdminApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public AdminApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------- the boundary

    [Fact]
    public async Task A_worker_cannot_reach_the_admin_endpoints()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        foreach (var path in new[]
                 {
                     "/api/v1/admin/dashboard",
                     "/api/v1/admin/verifications",
                     "/api/v1/admin/users",
                     "/api/v1/admin/jobs"
                 })
        {
            await TestApi.ReadJsonAsync(
                await worker.Client.GetAsync(path),
                HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_reach_the_admin_endpoints()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        await TestApi.ReadJsonAsync(
            await anonymous.GetAsync("/api/v1/admin/users"),
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_admin_can_read_the_dashboard()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);

        var stats = (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync("/api/v1/admin/dashboard"),
            HttpStatusCode.OK)).Data();

        // The reset empties the users table, so only the admin itself is counted.
        Assert.Equal(1, stats.GetProperty("totalUsers").GetInt32());
    }

    // ------------------------------------------------------------------- the queue

    [Fact]
    public async Task The_queue_lists_pending_requests_oldest_first_with_the_applicant()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var first = await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");
        await SubmitAsync(worker, type: 4, documentName: "welding-cert.pdf");

        var rows = await QueueAsync(admin);

        Assert.Equal(2, rows.GetArrayLength());

        // Oldest first: the first request is the one that has waited longest.
        Assert.Equal(first.GetProperty("id").GetGuid(), rows[0].GetProperty("id").GetGuid());

        // The applicant's name and phone number are here on purpose — an admin checking a
        // document needs to be able to call the person.
        Assert.Equal("Test Worker", rows[0].GetProperty("userName").GetString());
        Assert.Contains("9", rows[0].GetProperty("userPhone").GetString());
        Assert.Equal("id-front.jpg", rows[0].GetProperty("document").GetString());
        Assert.Equal("pending", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_decided_request_leaves_the_default_queue()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var request = await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");

        await ApproveAsync(admin, request.GetProperty("id").GetGuid());

        Assert.Empty((await QueueAsync(admin)).EnumerateArray());

        // It is still reachable, just not in the queue.
        var approved = await QueueAsync(admin, "?status=approved");

        Assert.Single(approved.EnumerateArray());
    }

    [Fact]
    public async Task A_withdrawn_request_never_reaches_the_queue()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var request = await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");

        await worker.Client.PostAsJsonAsync(
            $"/api/v1/verifications/{request.GetProperty("id").GetGuid()}/withdraw",
            new { });

        // An admin must not be handed work the applicant already took back, and the
        // pending filter is what keeps it out.
        Assert.Empty((await QueueAsync(admin)).EnumerateArray());
    }

    [Fact]
    public async Task The_queue_can_be_filtered_to_one_kind_of_check()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");
        await SubmitAsync(worker, type: 4, documentName: "welding-cert.pdf");

        var rows = await QueueAsync(admin, "?type=4");

        Assert.Single(rows.EnumerateArray());
        Assert.Equal("skills", rows[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_queue_can_be_searched_by_document_name()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "passport-scan.jpg");

        // Two requests for the same type cannot both be pending, so the second attempt is
        // expected to be refused; what matters here is that the first one is findable.
        var rows = await QueueAsync(admin, "?search=passport");

        Assert.Single(rows.EnumerateArray());
        Assert.Equal("passport-scan.jpg", rows[0].GetProperty("document").GetString());
    }

    [Fact]
    public async Task A_search_term_containing_a_wildcard_does_not_match_everything()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");

        // An unescaped % would turn this into "match every pending row".
        Assert.Empty((await QueueAsync(admin, "?search=%25")).EnumerateArray());
    }

    // ------------------------------------------------------------------- decisions

    [Fact]
    public async Task Approving_moves_the_applicants_badge_and_records_the_reviewer()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var request = await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");

        var approved = (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/verifications/{request.GetProperty("id").GetGuid()}/approve",
                new { }),
            HttpStatusCode.OK)).Data();

        // Note the vocabulary: the row's `status` is the Flutter badge value, so an
        // approved request reads as "verified" here while the queue filter that found it
        // was `status=approved`. The admin DTO is documented as badge-shaped; the two
        // are deliberately different enums.
        Assert.Equal("verified", approved.GetProperty("status").GetString());
        Assert.Equal("Test Admin", approved.GetProperty("reviewedBy").GetString());

        // One approved check out of four is not a verified person, and nothing is in
        // flight either, so the badge reads not-verified rather than pending: pending would
        // claim somebody is working on it.
        var summary = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/verifications/summary"),
            HttpStatusCode.OK)).Data();

        Assert.Equal("verified", summary.GetProperty("identity").GetString());
        Assert.Equal("notVerified", summary.GetProperty("overall").GetString());
        Assert.Equal(1, summary.GetProperty("verifiedCount").GetInt32());
    }

    [Fact]
    public async Task Approving_all_four_consumer_checks_turns_the_badge_green()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        foreach (var type in new[] { 1, 2, 3, 4 })
        {
            // Identity and skills need a document; phone and email are checked against the
            // account itself, which is why the submission validator only requires one for
            // the first kind.
            var request = await SubmitAsync(worker, type, documentName: "proof.pdf");
            await ApproveAsync(admin, request.GetProperty("id").GetGuid());
        }

        var summary = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/verifications/summary"),
            HttpStatusCode.OK)).Data();

        Assert.Equal("verified", summary.GetProperty("overall").GetString());
        Assert.Equal(4, summary.GetProperty("verifiedCount").GetInt32());
    }

    [Fact]
    public async Task A_rejection_carries_a_reason_the_applicant_can_act_on()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var request = await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");

        var rejected = (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/verifications/{request.GetProperty("id").GetGuid()}/reject",
                new { rejectionReason = "The photo is too dark to read." }),
            HttpStatusCode.OK)).Data();

        Assert.Equal("rejected", rejected.GetProperty("status").GetString());
        Assert.Equal("The photo is too dark to read.", rejected.GetProperty("rejectionReason").GetString());

        // The applicant sees the reason on their own history, which is the whole point of
        // refusing with one.
        var history = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/verifications"),
            HttpStatusCode.OK)).Data();

        var row = history.GetProperty("requests").EnumerateArray().Single();
        Assert.Equal("The photo is too dark to read.", row.GetProperty("rejectionReason").GetString());
        Assert.Equal("rejected", row.GetProperty("badgeStatus").GetString());
    }

    [Fact]
    public async Task A_request_cannot_be_decided_twice()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var id = (await SubmitAsync(worker, type: 1, documentName: "id-front.jpg"))
            .GetProperty("id").GetGuid();

        await ApproveAsync(admin, id);

        // A second admin opening the same row must not be able to overwrite the first
        // decision, which is what makes a queue safe to work through in any order.
        var error = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/verifications/{id}/reject",
                new { rejectionReason = "Changed my mind." }),
            HttpStatusCode.BadRequest);

        Assert.Equal("AlreadyReviewed", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_rejection_without_a_reason_is_refused_and_leaves_the_request_pending()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var id = (await SubmitAsync(worker, type: 1, documentName: "id-front.jpg"))
            .GetProperty("id").GetGuid();

        // The dangerous version of this bug: an empty reason used to be read as "no reason
        // given, so approve", which turned an intended refusal into an approval.
        var error = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/verifications/{id}/reject",
                new { rejectionReason = "   " }),
            HttpStatusCode.BadRequest);

        Assert.Equal("RejectionReasonRequired", error.GetProperty("errorCode").GetString());

        // Still in the queue, so the admin can retry with a real reason.
        Assert.Single((await QueueAsync(admin)).EnumerateArray());
    }

    [Fact]
    public async Task The_stored_user_status_matches_the_badge_the_applicant_sees()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // Reject identity, then approve phone. Folding decisions forward one at a time
        // would let the second approval quietly clear the rejection and leave the two
        // screens disagreeing.
        var identity = await SubmitAsync(worker, type: 1, documentName: "id-front.jpg");
        var phone = await SubmitAsync(worker, type: 2);

        await RejectAsync(admin, identity.GetProperty("id").GetGuid(), "Unreadable document.");
        await ApproveAsync(admin, phone.GetProperty("id").GetGuid());

        var applicantBadge = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/verifications/summary"),
            HttpStatusCode.OK)).Data().GetProperty("overall").GetString();

        var adminView = await UsersAsync(admin, $"?search={worker.Email}");

        Assert.Equal("rejected", applicantBadge);
        Assert.Equal("rejected", adminView[0].GetProperty("verification").GetString());
    }

    [Fact]
    public async Task A_decision_notifies_the_applicant()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var id = (await SubmitAsync(worker, type: 1, documentName: "id-front.jpg"))
            .GetProperty("id").GetGuid();

        await ApproveAsync(admin, id);

        var notifications = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK)).Data();

        var titles = notifications.EnumerateArray()
            .Select(n => n.GetProperty("title").GetString())
            .ToArray();

        Assert.Contains("Verification approved", titles);
    }

    [Fact]
    public async Task Deciding_a_request_that_does_not_exist_is_a_404()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);

        await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/verifications/{Guid.NewGuid()}/approve",
                new { }),
            HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------- users table

    [Fact]
    public async Task The_users_table_counts_a_givers_jobs_and_a_workers_applications()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/apply",
                new { message = "Available and experienced." }),
            HttpStatusCode.OK);

        Assert.Equal(1, (await UsersAsync(admin, $"?search={giver.Email}"))[0]
            .GetProperty("jobsCount").GetInt32());

        Assert.Equal(1, (await UsersAsync(admin, $"?search={worker.Email}"))[0]
            .GetProperty("jobsCount").GetInt32());
    }

    [Fact]
    public async Task The_users_table_reports_the_last_four_digits_of_a_number()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var row = (await UsersAsync(admin, $"?search={worker.Email}"))[0];

        Assert.Equal(int.Parse(worker.Phone[^4..]), row.GetProperty("phoneLast4").GetInt32());
    }

    [Fact]
    public async Task A_suspended_account_is_listed_but_marked_and_can_be_filtered_out()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SetStatusAsync(admin, worker.UserId, isActive: false);

        // Still listed by default, flagged rather than hidden: an admin looking into a
        // complaint needs to see that the account exists and is suspended.
        var listed = (await UsersAsync(admin, $"?search={worker.Email}"))[0];
        Assert.False(listed.GetProperty("isActive").GetBoolean());

        // The filters are how the table is actually used day to day.
        Assert.Empty((await UsersAsync(admin, $"?search={worker.Email}&isActive=true"))
            .EnumerateArray());

        Assert.Single((await UsersAsync(admin, $"?search={worker.Email}&isActive=false"))
            .EnumerateArray());
    }

    [Fact]
    public async Task Suspending_an_account_keeps_its_jobs_and_can_be_reinstated()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await SetStatusAsync(admin, giver.UserId, isActive: false);

        // A suspension is a flag, not a delete: the job is still there to be reinstated
        // rather than left as a half-deleted trail.
        var job = (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/jobs/{jobId}"),
            HttpStatusCode.OK)).Data();

        Assert.Equal(jobId, job.GetProperty("job").GetProperty("id").GetGuid());

        var reinstated = await SetStatusAsync(admin, giver.UserId, isActive: true);
        Assert.True(reinstated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task An_admin_cannot_suspend_their_own_account()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);

        // Locking yourself out is unrecoverable without database access.
        var error = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/users/{admin.UserId}/status",
                new { isActive = false }),
            HttpStatusCode.BadRequest);

        Assert.Equal("CannotSuspendSelf", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Setting_the_status_an_account_already_has_is_a_no_op()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // A newly registered account is already active, so this must not report a change.
        var response = await admin.Client.PostAsJsonAsync(
            $"/api/v1/admin/users/{worker.UserId}/status",
            new { isActive = true });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------- jobs table

    [Fact]
    public async Task The_jobs_table_counts_applicants_without_loading_them()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        foreach (var worker in new[] { first, second })
        {
            await TestApi.ReadJsonAsync(
                await worker.Client.PostAsJsonAsync(
                    $"/api/v1/jobs/{jobId}/apply",
                    new { message = "Available and experienced." }),
                HttpStatusCode.OK);
        }

        // The jobs table is searched by title and company, not by the poster's account —
        // the giver's email is not one of its columns.
        var row = (await JobsAsync(admin, "?search=Warehouse"))[0];

        Assert.Equal(2, row.GetProperty("applicants").GetInt32());
        Assert.Equal(0, row.GetProperty("workersFilled").GetInt32());
    }

    [Fact]
    public async Task The_job_detail_reports_the_applicant_pipeline_and_the_money()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        var applied = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/apply",
                new { message = "Available and experienced." }),
            HttpStatusCode.OK);

        var applicationId = applied.Data().GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applicationId}/select",
                new { agreedPay = 1000 }),
            HttpStatusCode.OK);

        var detail = (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/jobs/{jobId}"),
            HttpStatusCode.OK)).Data();

        Assert.Equal(1, detail.GetProperty("totalApplications").GetInt32());
        Assert.Equal(1, detail.GetProperty("shortlistedCount").GetInt32());
        Assert.Equal(1, detail.GetProperty("selectedCount").GetInt32());

        // Nothing has been paid yet, so the money columns are zero rather than absent.
        Assert.Equal(0, detail.GetProperty("paidCount").GetInt32());
        Assert.Equal(0m, detail.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task A_job_that_does_not_exist_is_a_404()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);

        await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/jobs/{Guid.NewGuid()}"),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_tables_are_paged()
    {
        using var admin = await TestData.CreateAdminAsync(_factory);

        for (var i = 0; i < 3; i++)
        {
            await TestApi.CreateWorkerAsync(_factory);
        }

        var envelope = await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync("/api/v1/admin/users?pageSize=2"),
            HttpStatusCode.OK);

        var pagination = envelope.GetProperty("pagination");

        Assert.Equal(2, envelope.GetProperty("data").GetArrayLength());
        Assert.Equal(2, pagination.GetProperty("pageSize").GetInt32());
        Assert.True(pagination.GetProperty("totalItems").GetInt32() >= 4);
        Assert.True(pagination.GetProperty("hasNext").GetBoolean());
    }

    // -------------------------------------------------------------------- helpers

    private static async Task<JsonElement> SubmitAsync(
        AuthenticatedClient caller,
        int type,
        string? documentName = null) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.PostAsJsonAsync("/api/v1/verifications", new
            {
                type,
                documentName
            }),
            HttpStatusCode.Created)).Data();

    private static async Task<JsonElement> QueueAsync(
        AuthenticatedClient admin,
        string query = "") =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/verifications{query}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> ApproveAsync(AuthenticatedClient admin, Guid id) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/verifications/{id}/approve", new { }),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> RejectAsync(AuthenticatedClient admin, Guid id, string reason) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/verifications/{id}/reject",
                new { rejectionReason = reason }),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> UsersAsync(AuthenticatedClient admin, string query) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/users{query}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> JobsAsync(AuthenticatedClient admin, string query) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/jobs{query}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> SetStatusAsync(
        AuthenticatedClient admin,
        Guid userId,
        bool isActive) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/admin/users/{userId}/status",
                new { isActive }),
            HttpStatusCode.OK)).Data();

    private static async Task<Guid> CreateJobAsync(AuthenticatedClient giver) =>
        (await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob()),
            HttpStatusCode.OK)).Data().GetProperty("id").GetGuid();

    private static Dictionary<string, object?> ValidJob() => new()
    {
        ["title"] = "Warehouse load",
        ["category"] = 1,
        ["description"] = "Loading and unloading a truck.",
        ["workersNeeded"] = 1,
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
