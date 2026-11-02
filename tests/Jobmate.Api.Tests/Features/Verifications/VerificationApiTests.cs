using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Domain.Entities;
using Jobmate.Domain.Enums;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jobmate.Api.Tests.Features.Verifications;

/// <summary>
/// Phase 9 acceptance tests for the trust badge and verification requests.
///
/// A submitted request is approved on the spot, and the badge belongs to the person: it
/// is derived from their whole request history and shows the same in worker and work giver
/// mode. Pending requests only exist from before auto-approval, so the withdrawal tests
/// write one straight into the database.
/// </summary>
public sealed class VerificationApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public VerificationApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------ submitting

    [Fact]
    public async Task A_request_is_approved_immediately_and_moves_the_badge()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var before = await SummaryAsync(worker);
        Assert.Equal("notVerified", before.GetProperty("overall").GetString());
        Assert.Equal(0, before.GetProperty("verifiedCount").GetInt32());

        var submitted = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        Assert.Equal("approved", submitted.GetProperty("status").GetString());
        Assert.Equal("verified", submitted.GetProperty("badgeStatus").GetString());
        Assert.Equal("identity", submitted.GetProperty("type").GetString());
        Assert.True(submitted.TryGetProperty("reviewedAt", out _));

        var after = await SummaryAsync(worker);
        Assert.Equal("verified", after.GetProperty("identity").GetString());
        Assert.Equal(1, after.GetProperty("verifiedCount").GetInt32());

        // One of the four badge checks is not the whole badge.
        Assert.Equal("notVerified", after.GetProperty("overall").GetString());
    }

    [Fact]
    public async Task Approval_sends_the_verification_approved_notification()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var submitted = await SubmitAsync(worker, type: 2);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var notification = await db.Notifications.SingleAsync(n =>
            n.UserId == worker.UserId && n.ReferenceType == NotificationReferenceType.Verification);

        Assert.Equal("Verification approved", notification.Title);
        Assert.Equal(submitted.GetProperty("id").GetGuid(), notification.ReferenceId);
    }

    [Fact]
    public async Task A_phone_check_needs_no_document()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // Registration stores an E.164 number, so a phone check is answerable without an
        // upload. The refusal path is covered by the domain rule test.
        var submitted = await SubmitAsync(worker, type: 2);

        Assert.Equal("phone", submitted.GetProperty("type").GetString());
        Assert.Equal("verified", (await SummaryAsync(worker)).GetProperty("phone").GetString());
    }

    [Fact]
    public async Task An_identity_request_without_a_document_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/verifications", new { type = 1 }),
            HttpStatusCode.BadRequest);

        Assert.Contains("documentName", error.GetProperty("errors")
            .EnumerateArray().Select(e => e.GetProperty("field").GetString()));
    }

    [Fact]
    public async Task A_check_left_pending_from_before_auto_approval_blocks_a_second_one()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SeedPendingAsync(worker, VerificationType.Identity);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/verifications", new
            {
                type = 1,
                documentName = "aadhaar-v2.pdf"
            }),
            HttpStatusCode.Conflict);

        Assert.Equal("VerificationAlreadyPending", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Every_badge_check_approved_turns_the_whole_badge_green()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAllBadgeChecksAsync(worker);

        var summary = await SummaryAsync(worker);

        Assert.Equal("verified", summary.GetProperty("overall").GetString());
        Assert.Equal(4, summary.GetProperty("verifiedCount").GetInt32());
    }

    [Fact]
    public async Task The_badge_belongs_to_the_person_and_shows_in_both_modes()
    {
        // Verified in worker mode, then switched to giver mode: the same badge has to be on
        // the giver profile, the user and the summary, because it is about who the person
        // is, not which side of the marketplace they are on today.
        var account = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAllBadgeChecksAsync(account);

        var asWorker = await MyProfileAsync(account);
        Assert.Equal("verified", asWorker.GetProperty("user").GetProperty("verification").GetString());
        Assert.Equal("verified", asWorker.GetProperty("worker").GetProperty("verificationStatus").GetString());

        using var asGiver = await account.SwitchRoleAsync(UserRole.WorkGiver);

        var giverView = await MyProfileAsync(asGiver);
        Assert.Equal("workGiver", giverView.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("verified", giverView.GetProperty("user").GetProperty("verification").GetString());
        Assert.Equal("verified", giverView.GetProperty("giver").GetProperty("verificationStatus").GetString());
        Assert.Equal("verified", (await SummaryAsync(asGiver)).GetProperty("overall").GetString());
    }

    // ------------------------------------------------------------------ the history

    [Fact]
    public async Task The_history_is_scoped_to_the_caller()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        var theirs = (await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync("/api/v1/verifications"),
            HttpStatusCode.OK)).Data();

        Assert.Empty(theirs.GetProperty("requests").EnumerateArray());
        Assert.Equal("notVerified", theirs.GetProperty("summary").GetProperty("overall").GetString());
    }

    [Fact]
    public async Task The_history_can_be_filtered_to_one_kind_of_check()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");
        await SubmitAsync(worker, type: 4, documentName: "welding-cert.pdf");

        var filtered = (await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/verifications?type=1"),
            HttpStatusCode.OK)).Data();

        var requests = filtered.GetProperty("requests").EnumerateArray().ToArray();
        Assert.Single(requests);
        Assert.Equal("identity", requests[0].GetProperty("type").GetString());

        // The summary is never filtered: the badge is the whole picture regardless of
        // which checklist row the app is showing.
        Assert.Equal("verified", filtered.GetProperty("summary").GetProperty("skills").GetString());
    }

    // ------------------------------------------------------------------ withdrawal

    [Fact]
    public async Task An_approved_request_cannot_be_withdrawn()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var submitted = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/verifications/{submitted.GetProperty("id").GetGuid()}/withdraw",
                new { }),
            HttpStatusCode.BadRequest);

        Assert.Equal("VerificationAlreadyReviewed", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_pending_request_can_be_withdrawn_and_the_badge_drops_back()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var pendingId = await SeedPendingAsync(worker, VerificationType.Identity);

        var withdrawn = (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/verifications/{pendingId}/withdraw", new { }),
            HttpStatusCode.OK)).Data();

        // Recorded as withdrawn, not rejected: the applicant chose to stop. Null properties
        // are omitted from the payload, so the check is that no reason is reported at all.
        Assert.Equal("withdrawn", withdrawn.GetProperty("status").GetString());
        Assert.False(
            withdrawn.TryGetProperty("rejectionReason", out var reason)
            && reason.ValueKind != JsonValueKind.Null,
            "A withdrawn request must not carry a rejection reason.");

        var summary = await SummaryAsync(worker);
        Assert.Equal("notVerified", summary.GetProperty("overall").GetString());
    }

    [Fact]
    public async Task Withdrawing_someone_elses_request_is_a_404()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var pendingId = await SeedPendingAsync(worker, VerificationType.Identity);

        await TestApi.ReadJsonAsync(
            await stranger.Client.PostAsJsonAsync($"/api/v1/verifications/{pendingId}/withdraw", new { }),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_withdrawn_request_can_be_submitted_again_and_is_approved()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var pendingId = await SeedPendingAsync(worker, VerificationType.Identity);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync($"/api/v1/verifications/{pendingId}/withdraw", new { }),
            HttpStatusCode.OK);

        // The old request is not pending any more, so a fresh attempt is allowed.
        var again = await SubmitAsync(worker, type: 1, documentName: "aadhaar-v2.pdf");

        Assert.Equal("approved", again.GetProperty("status").GetString());
        Assert.NotEqual(pendingId, again.GetProperty("id").GetGuid());
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
                documentName,
                note = "Please check."
            }),
            HttpStatusCode.Created)).Data();

    /// <summary>Identity, phone, email and skills: the four checks the overall badge needs.</summary>
    private static async Task SubmitAllBadgeChecksAsync(AuthenticatedClient caller)
    {
        await SubmitAsync(caller, type: 1, documentName: "aadhaar.pdf");
        await SubmitAsync(caller, type: 2);
        await SubmitAsync(caller, type: 3);
        await SubmitAsync(caller, type: 4, documentName: "welding-cert.pdf");
    }

    private static async Task<JsonElement> SummaryAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/verifications/summary"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> MyProfileAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/profile/me"),
            HttpStatusCode.OK)).Data();

    /// <summary>
    /// A request in the state submissions made before auto-approval: no API call can
    /// produce one now, so it is written directly.
    /// </summary>
    private async Task<Guid> SeedPendingAsync(AuthenticatedClient owner, VerificationType type)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var request = new VerificationRequest
        {
            UserId = owner.UserId,
            Type = type,
            Status = VerificationRequestStatus.Pending,
            DocumentName = "legacy.pdf",
            CreatedAt = DateTime.UtcNow
        };

        db.VerificationRequests.Add(request);

        var user = await db.Users.SingleAsync(u => u.Id == owner.UserId);
        user.VerificationStatus = VerificationStatus.Pending;

        await db.SaveChangesAsync();

        return request.Id;
    }
}
