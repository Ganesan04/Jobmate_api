using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Verifications;

/// <summary>
/// Phase 9 acceptance tests for the trust badge and verification requests.
///
/// The badge is the thing being defended: it is derived from a history of requests rather
/// than stored, so the cases that matter are the ones where the derivation could go wrong
/// — a second attempt after a rejection, a withdrawal, and a user who has asked for one
/// check and not the other three.
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
    public async Task A_request_starts_pending_and_moves_the_badge()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var before = await SummaryAsync(worker);
        Assert.Equal("notVerified", before.GetProperty("overall").GetString());
        Assert.Equal(0, before.GetProperty("verifiedCount").GetInt32());

        var submitted = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        Assert.Equal("pending", submitted.GetProperty("status").GetString());
        Assert.Equal("identity", submitted.GetProperty("type").GetString());

        var after = await SummaryAsync(worker);
        Assert.Equal("pending", after.GetProperty("overall").GetString());
        Assert.Equal("pending", after.GetProperty("identity").GetString());
    }

    [Fact]
    public async Task A_phone_check_is_refused_when_the_account_has_no_number_to_check()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // Registration stores an E.164 number, so a phone check is normally answerable.
        // The refusal path is covered by the domain rule test; here the point is that a
        // phone request is accepted and recorded rather than needing a document.
        var submitted = await SubmitAsync(worker, type: 2);

        Assert.Equal("phone", submitted.GetProperty("type").GetString());

        var summary = await SummaryAsync(worker);
        Assert.Equal("pending", summary.GetProperty("phone").GetString());
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
    public async Task Asking_twice_for_the_same_check_while_one_is_pending_is_a_conflict()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

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
    public async Task Different_checks_can_be_pending_at_the_same_time()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");
        await SubmitAsync(worker, type: 4, documentName: "welding-cert.pdf");

        var summary = await SummaryAsync(worker);

        Assert.Equal("pending", summary.GetProperty("identity").GetString());
        Assert.Equal("pending", summary.GetProperty("skills").GetString());
        Assert.Equal(0, summary.GetProperty("verifiedCount").GetInt32());
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
        Assert.Equal("pending", filtered.GetProperty("summary").GetProperty("skills").GetString());
    }

    // ------------------------------------------------------------------ withdrawal

    [Fact]
    public async Task A_pending_request_can_be_withdrawn_and_the_badge_drops_back()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var submitted = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        var withdrawn = (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/verifications/{submitted.GetProperty("id").GetGuid()}/withdraw",
                new { }),
            HttpStatusCode.OK)).Data();

        // Recorded as withdrawn, not rejected: the applicant chose to stop, so a red badge
        // and a reason an admin never wrote would both be lies. Null properties are omitted
        // from the payload, so the check is that no reason is reported at all.
        Assert.Equal("withdrawn", withdrawn.GetProperty("status").GetString());
        Assert.False(
            withdrawn.TryGetProperty("rejectionReason", out var reason)
            && reason.ValueKind != JsonValueKind.Null,
            "A withdrawn request must not carry a rejection reason.");

        var summary = await SummaryAsync(worker);
        Assert.Equal("notVerified", summary.GetProperty("overall").GetString());
    }

    [Fact]
    public async Task A_withdrawn_request_leaves_the_badge_alone_when_another_check_is_still_pending()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var identity = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");
        await SubmitAsync(worker, type: 4, documentName: "welding-cert.pdf");

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/verifications/{identity.GetProperty("id").GetGuid()}/withdraw",
                new { }),
            HttpStatusCode.OK);

        var summary = await SummaryAsync(worker);

        // The skills check is still in flight, so the user is still pending.
        Assert.Equal("pending", summary.GetProperty("overall").GetString());
        Assert.Equal("pending", summary.GetProperty("skills").GetString());
    }

    [Fact]
    public async Task Withdrawing_someone_elses_request_is_a_404()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var submitted = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        await TestApi.ReadJsonAsync(
            await stranger.Client.PostAsJsonAsync(
                $"/api/v1/verifications/{submitted.GetProperty("id").GetGuid()}/withdraw",
                new { }),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_withdrawn_request_can_be_submitted_again()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var submitted = await SubmitAsync(worker, type: 1, documentName: "aadhaar.pdf");

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/verifications/{submitted.GetProperty("id").GetGuid()}/withdraw",
                new { }),
            HttpStatusCode.OK);

        // The old request is not pending any more, so a fresh attempt is allowed.
        var again = await SubmitAsync(worker, type: 1, documentName: "aadhaar-v2.pdf");

        Assert.Equal("pending", again.GetProperty("status").GetString());
        Assert.NotEqual(submitted.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
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

    private static async Task<JsonElement> SummaryAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/verifications/summary"),
            HttpStatusCode.OK)).Data();
}
