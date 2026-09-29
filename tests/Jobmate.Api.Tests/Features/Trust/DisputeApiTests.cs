using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Jobmate.Infrastructure.Persistence;

namespace Jobmate.Api.Tests.Features.Trust;

/// <summary>
/// Disputes and support reports: raising one, holding the money it is about, and closing
/// it out. The money cases go through the same database the wallet reads, because the
/// point of holding a payment is that the payout stops counting — a test that only checked
/// the response body would pass even if the hold had done nothing.
/// </summary>
public sealed class DisputeApiTests : IAsyncLifetime
{
    private readonly JobmateApiFactory _factory = new();

    public Task InitializeAsync() => _factory.ResetAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- raising a dispute

    [Fact]
    public async Task A_worker_can_dispute_a_booking_they_are_part_of()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var dispute = await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);

        Assert.Equal("notPaid", dispute.GetProperty("reason").GetString()!);
        Assert.Equal("open", dispute.GetProperty("status").GetString());
        Assert.Equal(jobId, dispute.GetProperty("jobId").GetGuid());
        Assert.Equal(1000, dispute.GetProperty("amount").GetInt32());

        // Aimed at the employer, worked out from the booking rather than the request.
        Assert.Equal(giver.UserId, dispute.GetProperty("againstUserId").GetGuid());
        Assert.Equal(worker.UserId, dispute.GetProperty("raisedByUserId").GetGuid());

        // Named, not just an id, because the app shows a person to the reader.
        Assert.False(string.IsNullOrWhiteSpace(dispute.GetProperty("againstUser").GetString()));
        Assert.Equal("Warehouse load", dispute.GetProperty("jobTitle").GetString());
    }

    [Fact]
    public async Task An_employer_can_dispute_the_worker_they_booked()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var dispute = await RaiseAsync(giver, jobId, ReportReason.FakeJob, 0);

        Assert.Equal(worker.UserId, dispute.GetProperty("againstUserId").GetGuid());
        Assert.Equal(giver.UserId, dispute.GetProperty("raisedByUserId").GetGuid());
    }

    [Fact]
    public async Task Somebody_with_nothing_to_do_with_the_job_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var envelope = await TestApi.ReadJsonAsync(
            await stranger.Client.PostAsJsonAsync("/api/v1/disputes", new
            {
                jobId,
                reason = (int)ReportReason.NotPaid,
                description = "I was not paid for this one.",
                amount = 1000
            }),
            HttpStatusCode.Forbidden);

        Assert.False(envelope.GetProperty("success").GetBoolean());
        Assert.Contains("participants", envelope.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task A_dispute_against_a_job_that_does_not_exist_is_a_404()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var envelope = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/disputes", new
            {
                jobId = Guid.NewGuid(),
                reason = (int)ReportReason.NotPaid,
                description = "I was not paid for this one.",
                amount = 100
            }),
            HttpStatusCode.NotFound);

        Assert.Equal("NotFound", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_description_too_short_to_be_usable_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var envelope = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/disputes", new
            {
                jobId,
                reason = (int)ReportReason.NotPaid,
                description = "no",
                amount = 100
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("DescriptionTooShort", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_negative_amount_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var envelope = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/disputes", new
            {
                jobId,
                reason = (int)ReportReason.NotPaid,
                description = "They shorted me on the agreed amount.",
                amount = -50
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("AmountInvalid", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_second_dispute_about_the_same_job_is_refused_while_the_first_is_open()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);

        var envelope = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/disputes", new
            {
                jobId,
                reason = (int)ReportReason.NoShow,
                description = "Sending this again because nothing happened.",
                amount = 0
            }),
            HttpStatusCode.Conflict);

        Assert.Equal("DisputeAlreadyOpen", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task The_other_side_is_told_a_dispute_was_raised()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        await RaiseAsync(worker, jobId, ReportReason.UnsafeWorkplace, 0);

        var mine = (await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK)).Data();

        var titles = mine.EnumerateArray()
            .Select(n => n.GetProperty("title").GetString())
            .ToList();

        Assert.Contains("A dispute has been raised", titles);
    }

    // ------------------------------------------------------------------ who can read

    [Fact]
    public async Task Both_participants_see_the_dispute_and_a_bystander_does_not()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var dispute = await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);
        var id = dispute.GetProperty("id").GetGuid();

        Assert.Equal(id, (await DetailAsync(worker, id)).GetProperty("id").GetGuid());
        Assert.Equal(id, (await DetailAsync(giver, id)).GetProperty("id").GetGuid());

        // Told it does not exist rather than that it is off limits, so a stranger cannot
        // use the difference to find out which bookings have cases open against them.
        var envelope = await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync($"/api/v1/disputes/{id}"),
            HttpStatusCode.NotFound);

        Assert.Equal("NotFound", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task The_list_covers_both_sides_and_can_be_filtered_to_open_ones()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var dispute = await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);
        var id = dispute.GetProperty("id").GetGuid();

        // The raiser and the named party both get it; a third party gets nothing.
        Assert.Single((await MineAsync(worker)).EnumerateArray());
        Assert.Single((await MineAsync(giver)).EnumerateArray());

        await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/review", new { }),
            HttpStatusCode.OK);

        // Exact status match rather than a "still live" bucket, so the list and the admin
        // queue agree on what a status means: under review is a state of its own.
        Assert.Single((await MineAsync(worker, "?status=underReview")).EnumerateArray());
        Assert.Empty((await MineAsync(worker, "?status=resolved")).EnumerateArray());
        Assert.Single((await MineAsync(worker)).EnumerateArray());
    }

    [Fact]
    public async Task Reading_disputes_needs_a_signed_in_user()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        await TestApi.ReadJsonAsync(
            await anonymous.GetAsync("/api/v1/disputes"),
            HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ the money

    [Fact]
    public async Task A_dispute_about_pay_holds_the_settled_payment()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        await TestData.SeedPaymentAsync(_factory, jobId, worker.Email, PaymentStatus.Paid);

        await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);

        Assert.Equal(PaymentStatus.Disputed, await PaymentStatusAsync(jobId));
    }

    [Fact]
    public async Task A_dispute_that_names_no_amount_holds_nobodys_money()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        await TestData.SeedPaymentAsync(_factory, jobId, worker.Email, PaymentStatus.Paid);

        // An unsafe-workplace complaint has nothing to do with pay, so it must not be
        // usable to freeze somebody's earnings by opening one.
        await RaiseAsync(worker, jobId, ReportReason.UnsafeWorkplace, 0);

        Assert.Equal(PaymentStatus.Paid, await PaymentStatusAsync(jobId));
    }

    [Fact]
    public async Task Disputing_money_that_was_never_paid_does_not_fail_the_dispute()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        // No payment row at all: the complaint is "I was never paid", which is exactly the
        // case where there is nothing to hold back.
        var dispute = await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1500);

        Assert.Equal("open", dispute.GetProperty("status").GetString());
    }

    // ------------------------------------------------------------- the admin queue

    [Fact]
    public async Task The_queue_shows_open_cases_by_default_and_closed_ones_on_request()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var first = await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);
        var secondJob = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, secondJob, worker.Email);
        var second = await RaiseAsync(worker, secondJob, ReportReason.NoShow, 0);

        await CloseAsync(admin, second.GetProperty("id").GetGuid(), "resolved", "The worker was a no show.");

        var open = await QueueAsync(admin);
        Assert.Single(open.EnumerateArray());

        var closed = await QueueAsync(admin, "?status=resolved");
        Assert.Single(closed.EnumerateArray());
        Assert.Equal(
            second.GetProperty("id").GetGuid(),
            closed[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task The_queue_searches_the_description_and_the_notes()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);

        var dispute = await RaiseAsync(
            worker, jobId, ReportReason.NotPaid, 1000, "The forklift was missing a hydraulic hose.");

        Assert.Single((await QueueAsync(admin, "?search=hydraulic")).EnumerateArray());
        Assert.Empty((await QueueAsync(admin, "?search=payroll")).EnumerateArray());

        // The admin's own words become searchable too, which is how a closed case is
        // found again once the detail screen is out of reach. A status has to be named as
        // well, because the queue only shows live cases unless asked otherwise.
        await CloseAsync(
            admin, dispute.GetProperty("id").GetGuid(), "resolved", "Refund arranged by the giver.");
        Assert.Single((await QueueAsync(admin, "?status=resolved&search=refund")).EnumerateArray());
        Assert.Empty((await QueueAsync(admin, "?search=refund")).EnumerateArray());
    }

    [Fact]
    public async Task The_queue_is_closed_to_ordinary_users()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/admin/disputes"),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Taking_a_dispute_for_review_marks_it_under_review_once()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        var dispute = await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000);
        var id = dispute.GetProperty("id").GetGuid();

        var reviewing = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/review", new { }),
            HttpStatusCode.OK);

        Assert.Equal("underReview", reviewing.Data().GetProperty("status").GetString());

        // Twice is refused rather than silently accepted, so nobody mistakes a second
        // admin's tap for progress.
        var again = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/review", new { }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidDisputeTransition", again.GetProperty("errorCode").GetString());
    }

    // ------------------------------------------------------------ closing a dispute

    [Fact]
    public async Task Closing_a_dispute_records_who_decided_and_tells_both_sides()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();

        var closed = await CloseAsync(admin, id, "resolved", "The giver refunded the shortfall.");

        Assert.Equal("resolved", closed.GetProperty("status").GetString());
        Assert.Equal("The giver refunded the shortfall.", closed.GetProperty("notes").GetString());
        Assert.False(closed.GetProperty("resolvedAt").ValueKind == JsonValueKind.Null);
        Assert.False(string.IsNullOrWhiteSpace(closed.GetProperty("resolvedBy").GetString()));

        foreach (var party in new[] { worker, giver })
        {
            var titles = (await TestApi.ReadJsonAsync(
                await party.Client.GetAsync("/api/v1/notifications"),
                HttpStatusCode.OK)).Data().EnumerateArray()
                .Select(n => n.GetProperty("title").GetString())
                .ToList();

            Assert.Contains("Dispute resolved", titles);
        }
    }

    [Fact]
    public async Task Closing_a_dispute_releases_the_money_it_was_holding()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        await TestData.SeedPaymentAsync(_factory, jobId, worker.Email, PaymentStatus.Paid);

        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();
        Assert.Equal(PaymentStatus.Disputed, await PaymentStatusAsync(jobId));

        await CloseAsync(admin, id, "resolved", "Underpaid by 400, the rest was paid.");

        Assert.Equal(PaymentStatus.Paid, await PaymentStatusAsync(jobId));
    }

    [Fact]
    public async Task A_rejected_dispute_also_releases_the_money()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        await TestData.SeedPaymentAsync(_factory, jobId, worker.Email, PaymentStatus.Paid);

        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();
        await CloseAsync(admin, id, "rejected", "The full amount was paid on the day.");

        Assert.Equal(PaymentStatus.Paid, await PaymentStatusAsync(jobId));
    }

    [Fact]
    public async Task Closing_without_saying_why_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();

        var envelope = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/resolve", new
            {
                status = (int)DisputeStatus.Resolved,
                resolution = "   "
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("ResolutionRequired", envelope.GetProperty("errorCode").GetString());

        // And it is still open, because the refused attempt changed nothing.
        Assert.Equal("open", (await DetailAsync(worker, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_dispute_cannot_be_closed_twice()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();

        await CloseAsync(admin, id, "resolved", "Refunded in full.");

        var again = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/resolve", new
            {
                status = (int)DisputeStatus.Rejected,
                resolution = "Actually this was fine after all."
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidDisputeTransition", again.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Only_an_admin_can_close_a_dispute()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();

        // The employer is a participant, not the arbiter. If they could close their own
        // complaint, "dispute" would mean "silence the other side".
        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/resolve", new
            {
                status = (int)DisputeStatus.Rejected,
                resolution = "Closing this myself."
            }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_dispute_can_be_closed_without_being_taken_for_review_first()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await TestData.SeedBookingAsync(_factory, jobId, worker.Email);
        var id = (await RaiseAsync(worker, jobId, ReportReason.NotPaid, 1000)).GetProperty("id").GetGuid();

        // A complaint that is obviously bogus should not have to be picked up before it can
        // be closed, or the review step becomes something to work around.
        var closed = await CloseAsync(admin, id, "rejected", "The full amount was paid on the day.");

        Assert.Equal("rejected", closed.GetProperty("status").GetString());
    }

    // ------------------------------------------------------------- support reports

    [Fact]
    public async Task A_report_can_be_filed_about_a_job()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var report = (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.FakeJob,
                details = "The listing asked for a joining fee before any work."
            }),
            HttpStatusCode.Created)).Data();

        Assert.Equal("fakeJob", report.GetProperty("reason").GetString()!);
        Assert.False(report.GetProperty("isResolved").GetBoolean());
        Assert.Equal(string.Empty, report.GetProperty("relatedJob").GetString());

        var queue = (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync("/api/v1/admin/support-reports"),
            HttpStatusCode.OK)).Data();

        var row = queue[0];
        Assert.Equal(report.GetProperty("id").GetGuid(), row.GetProperty("id").GetGuid());
        Assert.Equal(worker.UserId, row.GetProperty("reportedByUserId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("reportedBy").GetString()));
    }

    [Fact]
    public async Task A_report_names_the_job_it_was_filed_against()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        var report = (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.UnsafeWorkplace,
                details = "No safety gear was provided on site.",
                relatedJobId = jobId.ToString()
            }),
            HttpStatusCode.Created)).Data();

        Assert.Equal("Warehouse load", report.GetProperty("relatedJob").GetString());

        var queue = (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync("/api/v1/admin/support-reports"),
            HttpStatusCode.OK)).Data();

        Assert.Equal(jobId.ToString(), queue[0].GetProperty("relatedJobId").GetString());
    }

    [Fact]
    public async Task A_report_with_nothing_in_it_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var envelope = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.FakeJob,
                details = "bad"
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("DetailsTooShort", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_report_can_be_marked_dealt_with_and_put_back()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var id = (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/support-reports", new
            {
                reason = (int)ReportReason.UnsafeWorkplace,
                details = "The site had no first aid kit on the truck."
            }),
            HttpStatusCode.Created)).Data().GetProperty("id").GetGuid();

        var done = await ResolveReportAsync(admin, id, true);
        Assert.True(done.GetProperty("isResolved").GetBoolean());
        Assert.False(done.GetProperty("resolvedAt").ValueKind == JsonValueKind.Null);

        // Filtered views follow the flag, so the queue can show only what is outstanding.
        Assert.Empty((await ReportsAsync(admin, "?isResolved=false")).EnumerateArray());
        Assert.Single((await ReportsAsync(admin, "?isResolved=true")).EnumerateArray());

        var reopened = await ResolveReportAsync(admin, id, false);
        Assert.False(reopened.GetProperty("isResolved").GetBoolean());

        // Absent rather than null: the API omits empty values, so a client reads a missing
        // resolvedAt as "not yet dealt with" without a special case for null.
        Assert.False(reopened.TryGetProperty("resolvedAt", out _));
    }

    [Fact]
    public async Task The_report_queue_is_closed_to_ordinary_users()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/admin/support-reports"),
            HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------- helpers

    private static async Task<JsonElement> RaiseAsync(
        AuthenticatedClient raiser,
        Guid jobId,
        ReportReason reason,
        int amount,
        string description = "The agreed amount was never paid in full.") =>
        (await TestApi.ReadJsonAsync(
            await raiser.Client.PostAsJsonAsync("/api/v1/disputes", new
            {
                jobId,
                reason = (int)reason,
                description,
                amount
            }),
            HttpStatusCode.Created)).Data();

    private static async Task<JsonElement> DetailAsync(AuthenticatedClient caller, Guid id) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync($"/api/v1/disputes/{id}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> MineAsync(AuthenticatedClient caller, string query = "") =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync($"/api/v1/disputes{query}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> QueueAsync(AuthenticatedClient admin, string query = "") =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/disputes{query}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> CloseAsync(
        AuthenticatedClient admin,
        Guid id,
        string status,
        string resolution) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync($"/api/v1/admin/disputes/{id}/resolve", new
            {
                status = (int)Enum.Parse<DisputeStatus>(status, ignoreCase: true),
                resolution
            }),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> ReportsAsync(AuthenticatedClient admin, string query) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync($"/api/v1/admin/support-reports{query}"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> ResolveReportAsync(
        AuthenticatedClient admin,
        Guid id,
        bool isResolved) =>
        (await TestApi.ReadJsonAsync(
            await admin.Client.PostAsync($"/api/v1/admin/support-reports/{id}/resolve?isResolved={isResolved}", null),
            HttpStatusCode.OK)).Data();

    private async Task<PaymentStatus> PaymentStatusAsync(Guid jobId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Payments.Where(p => p.JobId == jobId).Select(p => p.Status).FirstAsync();
    }

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
