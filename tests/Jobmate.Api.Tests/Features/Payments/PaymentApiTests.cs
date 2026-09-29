using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jobmate.Api.Tests.Features.Payments;

/// <summary>
/// Phase 4 acceptance tests for payments.
///
/// The cases that matter most here are the ones where charging money twice would be
/// possible: a retried request, a double tap, two givers racing for the same booking,
/// and a refund that only an administrator may perform.
/// </summary>
public sealed class PaymentApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public PaymentApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------ releasing payment

    [Fact]
    public async Task Releasing_payment_returns_a_receipt_with_the_fee_worked_out_server_side()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, pay: 1000);
        await BookAsync(giver, worker, jobId, agreedPay: 1000);

        var receipt = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
                new { basePay = 1000, overtime = 0, method = "upi" }),
            HttpStatusCode.OK);

        var data = receipt.Data();

        Assert.Equal("paid", data.GetProperty("status").GetString());
        Assert.Equal(1000, data.GetProperty("base").GetInt32());
        Assert.Equal(0, data.GetProperty("overtime").GetInt32());

        // 5% of 1000 is 50, so the giver is charged 1050 and the worker nets 950.
        Assert.Equal(50, data.GetProperty("fee").GetInt32());
        Assert.Equal(1050, data.GetProperty("totalAmount").GetInt32());
        Assert.Equal(1050, data.GetProperty("amount").GetInt32());
        Assert.Equal(950, data.GetProperty("workerNetAmount").GetInt32());
        Assert.Equal("INR", data.GetProperty("currency").GetString());
        Assert.StartsWith("PAY-", data.GetProperty("paymentId").GetString());
        Assert.Equal(jobId, data.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task Overtime_is_added_before_the_fee_is_taken()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var data = (await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
                new { basePay = 1000, overtime = 200, method = "cash" }),
            HttpStatusCode.OK)).Data();

        // The fee is 5% of 1200, not 5% of 1000, because overtime is part of the payout.
        Assert.Equal(60, data.GetProperty("fee").GetInt32());
        Assert.Equal(1260, data.GetProperty("totalAmount").GetInt32());
        Assert.Equal(1140, data.GetProperty("workerNetAmount").GetInt32());
        Assert.Equal("cash", data.GetProperty("method").GetString());
    }

    [Fact]
    public async Task A_paid_booking_shows_paid_for_the_worker()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        Assert.Equal("pending", await PaymentStatusOfBookingAsync(assignmentId));

        await PayAsync(giver, worker, jobId, 1000);

        Assert.Equal("paid", await PaymentStatusOfBookingAsync(assignmentId));
    }

    [Fact]
    public async Task Paying_twice_returns_the_original_receipt_instead_of_charging_again()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var first = await PayAsync(giver, worker, jobId, 1000);
        var second = await PayAsync(giver, worker, jobId, 1000);

        Assert.Equal(
            first.GetProperty("id").GetGuid(),
            second.GetProperty("id").GetGuid());

        // One booking, one payment row: the database enforces it and the retry is
        // resolved before the insert is even attempted.
        Assert.Equal(1, await CountPaymentsAsync(jobId));
    }

    [Fact]
    public async Task A_different_idempotency_key_cannot_pay_the_same_booking_twice()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        await PayAsync(giver, worker, jobId, 1000, key: "first-attempt");

        var second = await PayAsync(giver, worker, jobId, 5000, key: "second-attempt");

        // The second key would have charged 5000, but the booking is already paid, so
        // the original receipt comes back and the extra money is never taken.
        Assert.Equal(1000, second.GetProperty("base").GetInt32());
        Assert.Equal(1, await CountPaymentsAsync(jobId));
    }

    [Fact]
    public async Task Paying_for_a_booking_that_does_not_exist_is_a_404()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        var response = await giver.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
            new { basePay = 1000 });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_givers_booking_cannot_be_paid()
    {
        using var owner = await TestApi.CreateGiverAsync(_factory);
        using var stranger = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(owner);
        await BookAsync(owner, worker, jobId);

        var response = await stranger.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
            new { basePay = 1000 });

        // Not a 404: the booking exists, this giver simply may not pay for it.
        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_worker_cannot_release_payment()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var response = await worker.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
            new { basePay = 1000 });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_zero_or_negative_amount_is_rejected()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var response = await giver.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
            new { basePay = 0 });

        var error = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        Assert.Contains(
            error.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "basePay");

        Assert.Equal(0, await CountPaymentsAsync(jobId));
    }

    [Fact]
    public async Task A_failed_charge_is_kept_so_the_booking_can_be_paid_again()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // A single rupee leaves the worker 0 after the 5% fee rounds away, which the
            // gateway refuses: the request is well formed but cannot be settled.
            var assignment = await db.WorkerAssignments.FirstAsync(a => a.JobId == jobId);

            db.Payments.Add(new Domain.Entities.Payment
            {
                JobId = jobId,
                WorkerId = assignment.WorkerId,
                GiverId = assignment.GiverId,
                AssignmentId = assignment.Id,
                Amount = 1,
                OvertimeAmount = 0,
                PlatformFee = 1,
                TotalAmount = 2,
                AgreedPaySnapshot = assignment.AgreedPay,
                Status = Domain.Enums.PaymentStatus.Failed,
                FailureReason = "Declined by the issuer.",
                IdempotencyKey = $"assignment:{assignment.Id}"
            });

            await db.SaveChangesAsync();
        }

        // The failed attempt is re-armed rather than duplicated, so the booking still
        // has exactly one payment row when the retry settles.
        var receipt = await PayAsync(giver, worker, jobId, 2000);

        Assert.Equal("paid", receipt.GetProperty("status").GetString());
        Assert.Equal(2000, receipt.GetProperty("base").GetInt32());
        Assert.Equal(1, await CountPaymentsAsync(jobId));
    }

    // ------------------------------------------------------------------ confirming

    [Fact]
    public async Task Confirming_a_settled_payment_is_harmless()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);
        var paymentId = paid.GetProperty("id").GetGuid();

        var confirmed = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/payments/{paymentId}/confirm",
                new { transactionReference = "BANK-REF-1" }),
            HttpStatusCode.OK);

        Assert.Equal("paid", confirmed.Data().GetProperty("status").GetString());
        Assert.Equal(1, await CountPaymentsAsync(jobId));
    }

    [Fact]
    public async Task Only_the_giver_who_paid_can_confirm_it()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var stranger = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);
        var paymentId = paid.GetProperty("id").GetGuid();

        var response = await stranger.Client.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/confirm",
            new { transactionReference = "BANK-REF-2" });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    // --------------------------------------------------------------------- refunds

    [Fact]
    public async Task An_administrator_can_refund_a_settled_payment()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);
        var paymentId = paid.GetProperty("id").GetGuid();

        var refunded = await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/payments/{paymentId}/refund",
                new { reason = "Worker never turned up." }),
            HttpStatusCode.OK);

        Assert.Equal("refunded", refunded.Data().GetProperty("status").GetString());
        Assert.Equal(1050, refunded.Data().GetProperty("totalAmount").GetInt32());
    }

    [Fact]
    public async Task A_giver_cannot_refund_their_own_payment()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);
        var paymentId = paid.GetProperty("id").GetGuid();

        var response = await giver.Client.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/refund",
            new { reason = "Changed my mind." });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_worker_cannot_refund_their_own_earnings()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);
        var paymentId = paid.GetProperty("id").GetGuid();

        var response = await worker.Client.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/refund",
            new { reason = "I want my money back." });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_refund_needs_a_reason()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);

        var response = await admin.Client.PostAsJsonAsync(
            $"/api/v1/payments/{paid.GetProperty("id").GetGuid()}/refund",
            new { reason = "   " });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_payment_cannot_be_refunded_twice()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var paid = await PayAsync(giver, worker, jobId, 1000);
        var paymentId = paid.GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await admin.Client.PostAsJsonAsync(
                $"/api/v1/payments/{paymentId}/refund",
                new { reason = "First reversal." }),
            HttpStatusCode.OK);

        var second = await admin.Client.PostAsJsonAsync(
            $"/api/v1/payments/{paymentId}/refund",
            new { reason = "Second reversal." });

        await TestApi.ReadJsonAsync(second, HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------------- listing

    [Fact]
    public async Task The_giver_sees_their_own_payments_only()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        var mine = await CreateJobAsync(giver, "My job");
        var theirs = await CreateJobAsync(other, "Their job");

        await BookAsync(giver, first, mine);
        await BookAsync(other, second, theirs);

        await PayAsync(giver, first, mine, 1000);
        await PayAsync(other, second, theirs, 4000);

        var page = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/payments"),
            HttpStatusCode.OK);

        Assert.Equal(1, page.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(
            mine,
            page.GetProperty("data").EnumerateArray().Single().GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task A_giver_can_filter_payments_by_status()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var paidJob = await CreateJobAsync(giver, "Paid job");
        var pendingJob = await CreateJobAsync(giver, "Pending job");

        await BookAsync(giver, worker, paidJob);
        await BookAsync(giver, worker, pendingJob);

        await PayAsync(giver, worker, paidJob, 1000);

        var page = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/payments?statuses=paid"),
            HttpStatusCode.OK);

        Assert.Equal(1, page.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task An_administrator_sees_every_payment()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);
        using var admin = await TestData.CreateAdminAsync(_factory);

        var mine = await CreateJobAsync(giver);
        var theirs = await CreateJobAsync(other);

        await BookAsync(giver, first, mine);
        await BookAsync(other, second, theirs);

        await PayAsync(giver, first, mine, 1000);
        await PayAsync(other, second, theirs, 4000);

        var page = await TestApi.ReadJsonAsync(
            await admin.Client.GetAsync("/api/v1/payments"),
            HttpStatusCode.OK);

        Assert.Equal(2, page.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    // --------------------------------------------------------------------- earnings

    [Fact]
    public async Task The_worker_wallet_shows_the_net_for_a_paid_booking()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, "Loading work");
        await BookAsync(giver, worker, jobId, agreedPay: 1000);

        await PayAsync(giver, worker, jobId, 1000);

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/earnings"),
            HttpStatusCode.OK);

        var line = page.GetProperty("data").EnumerateArray().Single();

        Assert.Equal(950, line.GetProperty("amount").GetInt32());
        Assert.Equal("paid", line.GetProperty("status").GetString());
        Assert.Equal("Loading work", line.GetProperty("jobTitle").GetString());
        Assert.StartsWith("PAY-", line.GetProperty("paymentId").GetString());
    }

    [Fact]
    public async Task A_booking_that_is_not_paid_yet_is_still_listed_as_pending()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId, agreedPay: 1000);

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/earnings"),
            HttpStatusCode.OK);

        var line = page.GetProperty("data").EnumerateArray().Single();

        Assert.Equal("pending", line.GetProperty("status").GetString());

        // The pending figure is the net the worker will actually receive, not the
        // 1000 headline, so the wallet never promises money the fee will take.
        Assert.Equal(950, line.GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task The_wallet_summary_separates_earned_from_pending_money()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var paidJob = await CreateJobAsync(giver, "Paid job");
        var pendingJob = await CreateJobAsync(giver, "Pending job");

        await BookAsync(giver, worker, paidJob, agreedPay: 1000);
        await BookAsync(giver, worker, pendingJob, agreedPay: 2000);

        await PayAsync(giver, worker, paidJob, 1000);

        var wallet = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/wallet"),
            HttpStatusCode.OK)).Data();

        Assert.Equal(950, wallet.GetProperty("totalEarned").GetInt32());
        Assert.Equal(1900, wallet.GetProperty("pendingAmount").GetInt32());
        Assert.Equal(0, wallet.GetProperty("disputedAmount").GetInt32());
        Assert.Equal(2, wallet.GetProperty("entryCount").GetInt32());
    }

    [Fact]
    public async Task The_monthly_summary_groups_money_by_month()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId, agreedPay: 1000);

        await PayAsync(giver, worker, jobId, 1000);

        var summary = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/earnings/summary"),
            HttpStatusCode.OK);

        var month = summary.Data().EnumerateArray().Single();

        Assert.Equal(
            DateTime.UtcNow.ToString("yyyy-MM"),
            month.GetProperty("month").GetString());
        Assert.Equal(950, month.GetProperty("earned").GetInt32());
        Assert.Equal(0, month.GetProperty("pending").GetInt32());
    }

    [Fact]
    public async Task A_giver_cannot_read_the_worker_wallet()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var response = await giver.Client.GetAsync("/api/v1/worker/wallet");

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_worker_only_sees_their_own_earnings()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var mine = await TestApi.CreateWorkerAsync(_factory);
        using var theirs = await TestApi.CreateWorkerAsync(_factory);

        var forMine = await CreateJobAsync(giver, "For me");
        var forThem = await CreateJobAsync(giver, "For them");

        await BookAsync(giver, mine, forMine);
        await BookAsync(giver, theirs, forThem);

        await PayAsync(giver, mine, forMine, 1000);
        await PayAsync(giver, theirs, forThem, 7000);

        var page = await TestApi.ReadJsonAsync(
            await mine.Client.GetAsync("/api/v1/worker/earnings"),
            HttpStatusCode.OK);

        Assert.Equal(1, page.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task Two_simultaneous_payment_requests_charge_the_giver_once()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var workerId = WorkerId(worker);

        // A double tap on "Release payment" is the case the idempotency key exists for.
        var responses = await Task.WhenAll(
            Task.Run(() => giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/payments/{workerId}",
                new { basePay = 1000, idempotencyKey = "double-tap" })),
            Task.Run(() => giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/payments/{workerId}",
                new { basePay = 1000, idempotencyKey = "double-tap" })));

        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
        }

        var receipts = await Task.WhenAll(responses.Select(r => TestApi.ReadJsonAsync(r)));

        // Both callers see the same payment, and only one row exists.
        Assert.Equal(
            receipts[0].Data().GetProperty("id").GetGuid(),
            receipts[1].Data().GetProperty("id").GetGuid());

        Assert.Equal(1, await CountPaymentsAsync(jobId));
    }

    // ------------------------------------------------------------------- helpers

    private async Task<Guid> CreateJobAsync(
        AuthenticatedClient giver,
        string title = "Warehouse load",
        int pay = 1000)
    {
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(title, pay)),
            HttpStatusCode.OK);

        return created.Data().GetProperty("id").GetGuid();
    }

    /// <summary>Books the worker for the job and returns the assignment id.</summary>
    private async Task<Guid> BookAsync(
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

        var applicationId = applied.Data().GetProperty("id").GetGuid();

        var booked = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applicationId}/select",
                new { agreedPay }),
            HttpStatusCode.OK);

        return booked.Data().GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PayAsync(
        AuthenticatedClient giver,
        AuthenticatedClient worker,
        Guid jobId,
        int basePay,
        string? key = null)
    {
        var response = await giver.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/payments/{WorkerId(worker)}",
            new { basePay, overtime = 0, method = "upi", idempotencyKey = key });

        return (await TestApi.ReadJsonAsync(response, HttpStatusCode.OK)).Data();
    }

    private static Guid WorkerId(AuthenticatedClient worker) => worker.UserId;

    /// <summary>
    /// The booking's payment status as the app sees it, read through the domain's own
    /// projection rather than by inspecting the payment row: a booking with no payment
    /// yet is "pending", not "none", and the test should not encode a second opinion.
    /// </summary>
    private async Task<string> PaymentStatusOfBookingAsync(Guid assignmentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var assignment = await db.WorkerAssignments
            .Include(a => a.Payment)
            .AsNoTracking()
            .FirstAsync(a => a.Id == assignmentId);

        return assignment.PaymentStatus.ToString().ToLowerInvariant();
    }

    private async Task<int> CountPaymentsAsync(Guid jobId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Payments.CountAsync(p => p.JobId == jobId);
    }

    /// <summary>A complete, valid Create Job wizard payload.</summary>
    private static Dictionary<string, object?> ValidJob(string title, int pay) =>
        new()
        {
            ["title"] = title,
            ["category"] = 1,
            ["description"] = "Loading and unloading a truck.",
            ["workersNeeded"] = 1,
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
}
