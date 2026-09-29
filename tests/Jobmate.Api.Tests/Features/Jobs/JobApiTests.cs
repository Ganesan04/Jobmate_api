using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Domain.Enums;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jobmate.Api.Tests.Features.Jobs;

/// <summary>
/// Phase 1 acceptance tests for the job endpoints, run against the real API pipeline
/// and a real PostgreSQL database so the SQL translation, the role gates and the
/// lifecycle rules are all genuinely exercised.
/// </summary>
public sealed class JobApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public JobApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// xUnit builds a new instance of the test class for every test, so this is the
    /// only place that reliably runs once per test rather than once per run. The host
    /// is started on the first call and the database is emptied afterwards, so no test
    /// can see rows left behind by another.
    /// </summary>
    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_publishes_a_job_and_returns_the_flutter_shape()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var response = await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob());

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.OK);
        var job = envelope.Data();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(envelope.GetProperty("success").GetBoolean());
        Assert.Equal("Warehouse packer", job.GetProperty("title").GetString());
        Assert.Equal("active", job.GetProperty("status").GetString());
        Assert.Equal(3, job.GetProperty("workersNeeded").GetInt32());
        Assert.Equal(0, job.GetProperty("workersFilled").GetInt32());
        Assert.Equal(3, job.GetProperty("remainingWorkers").GetInt32());
        Assert.Equal(1500, job.GetProperty("totalPayout").GetInt32());
        Assert.Equal(2, job.GetProperty("skills").GetArrayLength());
        Assert.NotEqual(Guid.Empty, job.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Create_can_keep_the_job_as_a_draft()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var response = await giver.Client.PostAsJsonAsync(
            "/api/v1/jobs",
            ValidJob(publishImmediately: false));

        var job = (await TestApi.ReadJsonAsync(response, HttpStatusCode.OK)).Data();

        Assert.Equal("draft", job.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_draft_is_invisible_to_the_public_search_and_to_another_giver()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(publishImmediately: false)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        var search = await TestApi.ReadJsonAsync(
            await other.Client.GetAsync("/api/v1/jobs"),
            HttpStatusCode.OK);

        Assert.Empty(search.GetProperty("data").EnumerateArray());

        var direct = await other.Client.GetAsync($"/api/v1/jobs/{id}");

        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
    }

    [Fact]
    public async Task Create_rejects_a_worker_with_forbidden()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var response = await worker.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob());

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_requires_authentication()
    {
        var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync("/api/v1/jobs", ValidJob());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("title", "ab", "Title")]
    [InlineData("pay", 0, "Pay")]
    [InlineData("workersNeeded", 0, "Workers")]
    public async Task Create_returns_field_level_validation_errors(
        string field,
        object invalidValue,
        string expectedFragment)
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var payload = ValidJob() as IDictionary<string, object>;

        payload[field] = invalidValue;

        var response = await giver.Client.PostAsJsonAsync("/api/v1/jobs", payload);

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        Assert.False(envelope.GetProperty("success").GetBoolean());

        var errors = envelope.GetProperty("errors").EnumerateArray().ToList();

        Assert.NotEmpty(errors);
        Assert.Contains(
            errors,
            e => e.GetProperty("field").GetString()!.Contains(field, StringComparison.OrdinalIgnoreCase) &&
                 e.GetProperty("message").GetString()!.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_rejects_a_recurring_job_with_no_weekdays()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var payload = ValidJob(isRecurring: true, weekdays: []);

        var response = await giver.Client.PostAsJsonAsync("/api/v1/jobs", payload);

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        Assert.Contains(
            envelope.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("message").GetString()!.Contains("weekday", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_changes_only_the_fields_that_were_sent()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(publishImmediately: false)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        var updated = await TestApi.ReadJsonAsync(
            await giver.Client.PutAsJsonAsync($"/api/v1/jobs/{id}", new { pay = 2500, title = "Night packer" }),
            HttpStatusCode.OK);

        var job = updated.Data();

        Assert.Equal(2500, job.GetProperty("pay").GetInt32());
        Assert.Equal("Night packer", job.GetProperty("title").GetString());

        // Untouched fields keep their values.
        Assert.Equal(3, job.GetProperty("workersNeeded").GetInt32());
        Assert.Equal("Delhi", job.GetProperty("area").GetString());
    }

    [Fact]
    public async Task Update_is_forbidden_for_another_giver()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob()),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        var response = await other.Client.PutAsJsonAsync($"/api/v1/jobs/{id}", new { pay = 1 });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_is_refused_once_a_worker_has_applied()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob()),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        await BookWorkerAsync(giver, id, worker);

        // The booking locks the posting: a published job with applicants can no longer be
        // edited, so lowering the requirement is refused before the worker count matters.
        var response = await giver.Client.PutAsJsonAsync($"/api/v1/jobs/{id}", new { workersNeeded = 1 });

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("JobHasApplicants", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Update_refuses_to_drop_the_worker_count_below_the_booked_total()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        // A draft is still editable with bookings on it, which is the only state where
        // the worker-count guard is reachable.
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(publishImmediately: false)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        await TestData.SeedBookingAsync(_factory, id, first.Email);
        await TestData.SeedBookingAsync(_factory, id, second.Email);

        var response = await giver.Client.PutAsJsonAsync($"/api/v1/jobs/{id}", new { workersNeeded = 1 });

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("WorkerCountBelowFilled", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Publish_moves_a_draft_to_active()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(publishImmediately: false)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        var published = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{id}/publish", null),
            HttpStatusCode.OK);

        Assert.Equal("active", published.Data().GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, published.Data().GetProperty("publishedAt").ValueKind);
    }

    [Fact]
    public async Task Publish_twice_is_rejected_by_the_lifecycle_rules()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(publishImmediately: false)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{id}/publish", null),
            HttpStatusCode.OK);

        var second = await giver.Client.PostAsync($"/api/v1/jobs/{id}/publish", null);

        var envelope = await TestApi.ReadJsonAsync(second, HttpStatusCode.BadRequest);

        Assert.Equal("JobNotPublishable", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Cancel_closes_the_job_and_stops_it_appearing_in_the_feed()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob()),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        var cancelled = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{id}/cancel",
                new { reason = "Shift no longer needed" }),
            HttpStatusCode.OK);

        Assert.Equal("cancelled", cancelled.Data().GetProperty("status").GetString());

        var search = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/jobs"),
            HttpStatusCode.OK);

        Assert.DoesNotContain(
            search.GetProperty("data").EnumerateArray(),
            job => job.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Cancel_refunds_a_settled_payment_and_keeps_an_in_flight_one_alone()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var settled = await TestApi.CreateWorkerAsync(_factory);
        using var inFlight = await TestApi.CreateWorkerAsync(_factory);

        var jobId = (await CreateJobAsync(giver, title: "Refunded")).GetProperty("id").GetGuid();
        var settledPaymentId = await TestData.SeedPaymentAsync(_factory, jobId, settled.Email, PaymentStatus.Paid);
        var inFlightPaymentId = await TestData.SeedPaymentAsync(_factory, jobId, inFlight.Email, PaymentStatus.Processing);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/cancel", new { reason = "Client withdrew" }),
            HttpStatusCode.OK);

        // Money that already left the giver comes back, because the work was not done.
        Assert.Equal(PaymentStatus.Refunded, await PaymentStatusAsync(settledPaymentId));
        Assert.NotNull(await RefundedAtAsync(settledPaymentId));

        // A charge that is still in flight is settled by the provider first; moving it
        // here would either lose it or refund money that was never captured.
        Assert.Equal(PaymentStatus.Processing, await PaymentStatusAsync(inFlightPaymentId));
    }

    [Fact]
    public async Task Creating_a_job_counts_towards_the_givers_total_postings()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        Assert.Equal(0, await TotalJobsPostedAsync(giver.Email));

        await CreateJobAsync(giver, title: "First", publish: false);
        await CreateJobAsync(giver, title: "Second", publish: false);

        // A draft is work the employer has offered, so both postings are counted; this
        // is the counter the profile card shows.
        Assert.Equal(2, await TotalJobsPostedAsync(giver.Email));
    }

    [Fact]
    public async Task Delete_only_removes_a_draft()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var published = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob()),
            HttpStatusCode.OK);

        var rejected = await giver.Client.DeleteAsync(
            $"/api/v1/jobs/{published.Data().GetProperty("id").GetGuid()}");

        var envelope = await TestApi.ReadJsonAsync(rejected, HttpStatusCode.BadRequest);

        Assert.Equal("JobNotDeletable", envelope.GetProperty("errorCode").GetString());

        var draft = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(publishImmediately: false)),
            HttpStatusCode.OK);

        var id = draft.Data().GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await giver.Client.DeleteAsync($"/api/v1/jobs/{id}"),
            HttpStatusCode.OK);

        var missing = await giver.Client.GetAsync($"/api/v1/jobs/{id}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Search_is_case_insensitive_across_title_company_and_area()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await CreateJobAsync(giver, title: "Warehouse Packer", area: "Noida");
        await CreateJobAsync(giver, title: "Delivery Rider", area: "Gurugram", category: 3);

        var byTitle = await SearchAsync(giver, "?search=warehouse");
        var byArea = await SearchAsync(giver, "?search=noida");
        var byCategory = await SearchAsync(giver, "?categories=3");

        Assert.Single(byTitle.EnumerateArray());
        Assert.Equal("Warehouse Packer", byTitle[0].GetProperty("title").GetString());
        Assert.Single(byArea.EnumerateArray());
        Assert.Single(byCategory.EnumerateArray());
        Assert.Equal("Delivery Rider", byCategory[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Search_treats_like_wildcards_in_the_query_as_literal_text()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await CreateJobAsync(giver, title: "Diwali bonus 50% off run");
        await CreateJobAsync(giver, title: "Ordinary warehouse shift");

        // "%" and "_" are LIKE metacharacters. Unescaped, "%" would match every row and
        // "_" would match the space in "50% off", so the second query has to come back
        // empty while the first finds the job that really contains the character.
        var withPercent = await SearchAsync(giver, "?search=50%25");
        var underscore = await SearchAsync(giver, "?search=50%25_off");

        Assert.Single(withPercent.EnumerateArray());
        Assert.Equal("Diwali bonus 50% off run", withPercent[0].GetProperty("title").GetString());

        Assert.Empty(underscore.EnumerateArray());
    }

    [Fact]
    public async Task Search_pays_and_sorts_in_the_database()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await CreateJobAsync(giver, title: "Low pay", pay: 500);
        await CreateJobAsync(giver, title: "High pay", pay: 5000);
        await CreateJobAsync(giver, title: "Middle pay", pay: 2000);

        var highestFirst = await SearchAsync(giver, "?sortBy=payHigh");
        var lowestFirst = await SearchAsync(giver, "?sortBy=payLow");
        var inRange = await SearchAsync(giver, "?minPay=1000&maxPay=3000");

        Assert.Equal("High pay", highestFirst[0].GetProperty("title").GetString());
        Assert.Equal("Low pay", lowestFirst[0].GetProperty("title").GetString());
        Assert.Single(inRange.EnumerateArray());
        Assert.Equal("Middle pay", inRange[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task Search_rejects_an_unknown_sort_key()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var response = await giver.Client.GetAsync("/api/v1/jobs?sortBy=cheapest");

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        // The field is reported in the camelCase form the app sends, not the C# path.
        Assert.Contains(
            envelope.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "sortBy");
    }

    [Fact]
    public async Task Radius_search_returns_only_jobs_inside_the_circle_with_a_distance()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        // New Delhi, and two points roughly 60 km and 400 km away.
        await CreateJobAsync(giver, title: "Near job", latitude: 28.6139, longitude: 77.2090);
        await CreateJobAsync(giver, title: "Mid job", latitude: 29.0800, longitude: 77.2600);
        await CreateJobAsync(giver, title: "Far job", latitude: 32.0000, longitude: 76.0000);

        var results = await SearchAsync(
            giver,
            "?latitude=28.6139&longitude=77.2090&radiusKm=25&sortBy=nearest");

        var titles = results.EnumerateArray().Select(j => j.GetProperty("title").GetString()).ToList();

        Assert.Equal(["Near job"], titles);

        var distance = results[0].GetProperty("distanceKm").GetDouble();

        Assert.InRange(distance, 0, 1);
    }

    [Fact]
    public async Task Radius_search_requires_both_coordinates()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var response = await giver.Client.GetAsync("/api/v1/jobs?radiusKm=10");

        var envelope = await TestApi.ReadJsonAsync(response, HttpStatusCode.BadRequest);

        Assert.Equal("CoordinatesRequired", envelope.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Search_paginates_with_metadata_and_a_stable_order()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        for (var i = 0; i < 5; i++)
        {
            await CreateJobAsync(giver, title: $"Paged job {i}");
        }

        var first = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/jobs?pageSize=2&sortBy=payHigh&minPay=1"),
            HttpStatusCode.OK);

        var second = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/jobs?page=2&pageSize=2&sortBy=payHigh&minPay=1"),
            HttpStatusCode.OK);

        var pagination = first.GetProperty("pagination");

        Assert.Equal(2, pagination.GetProperty("pageSize").GetInt32());
        Assert.Equal(5, pagination.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, pagination.GetProperty("totalPages").GetInt32());
        Assert.True(pagination.GetProperty("hasNext").GetBoolean());
        Assert.False(pagination.GetProperty("hasPrevious").GetBoolean());

        // No job appears on both pages, which is what the id tie-breaker guarantees.
        var firstIds = first.GetProperty("data").EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).ToList();
        var secondIds = second.GetProperty("data").EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).ToList();

        Assert.Empty(firstIds.Intersect(secondIds));
    }

    [Fact]
    public async Task Search_hides_a_private_address_until_the_worker_is_booked()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/jobs",
                ValidJob(locationVisible: false, address: "Unit 7, Okhla Phase III", latitude: 28.5309, longitude: 77.2465)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        var feed = await SearchAsync(worker.Client, "");

        var listed = feed.EnumerateArray().Single(j => j.GetProperty("id").GetGuid() == id);

        AssertLocationHidden(listed);

        // Asking for the job directly must not leak it either.
        var direct = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync($"/api/v1/jobs/{id}"),
            HttpStatusCode.OK);

        AssertLocationHidden(direct.Data());

        // The giver always sees their own posting in full.
        var owner = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{id}"),
            HttpStatusCode.OK);

        Assert.Equal("Unit 7, Okhla Phase III", owner.Data().GetProperty("address").GetString());
    }

    /// <summary>
    /// A withheld location is an empty address and no coordinates. The API omits null
    /// properties rather than sending them, which is what the Flutter client decodes to a
    /// null, so "absent" and "null" are the same thing here.
    /// </summary>
    private static void AssertLocationHidden(JsonElement job)
    {
        Assert.Equal(string.Empty, job.GetProperty("address").GetString());

        Assert.True(
            !job.TryGetProperty("latitude", out var latitude) || latitude.ValueKind == JsonValueKind.Null,
            "latitude must not be disclosed");
        Assert.True(
            !job.TryGetProperty("longitude", out var longitude) || longitude.ValueKind == JsonValueKind.Null,
            "longitude must not be disclosed");
    }

    [Fact]
    public async Task A_booked_worker_sees_the_private_address()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/jobs",
                ValidJob(locationVisible: false, address: "Unit 7, Okhla Phase III", latitude: 28.5309, longitude: 77.2465)),
            HttpStatusCode.OK);

        var id = created.Data().GetProperty("id").GetGuid();

        await BookWorkerAsync(giver, id, worker);

        var direct = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync($"/api/v1/jobs/{id}"),
            HttpStatusCode.OK);

        Assert.Equal("Unit 7, Okhla Phase III", direct.Data().GetProperty("address").GetString());
        Assert.Equal(28.5309, direct.Data().GetProperty("latitude").GetDouble(), 3);
    }

    [Fact]
    public async Task The_worker_feed_only_returns_open_jobs_with_a_free_slot()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await CreateJobAsync(giver, title: "Open job", workersNeeded: 2);

        // Fully booked, and cancelled, must both be absent.
        var full = await CreateJobAsync(giver, title: "Full job", workersNeeded: 1);
        await BookWorkerAsync(giver, full.GetProperty("id").GetGuid(), worker);

        var cancelled = await CreateJobAsync(giver, title: "Cancelled job");
        await giver.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{cancelled.GetProperty("id").GetGuid()}/cancel",
            new { reason = "no longer needed" });

        var results = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/jobs"),
            HttpStatusCode.OK);

        var titles = results.GetProperty("data").EnumerateArray()
            .Select(j => j.GetProperty("title").GetString())
            .ToList();

        Assert.Equal(["Open job"], titles);
    }

    [Fact]
    public async Task The_giver_feed_is_scoped_to_the_caller()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);

        await CreateJobAsync(giver, title: "Mine");

        var mine = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/giver/jobs"),
            HttpStatusCode.OK);

        var theirs = await TestApi.ReadJsonAsync(
            await other.Client.GetAsync("/api/v1/giver/jobs"),
            HttpStatusCode.OK);

        Assert.Equal(["Mine"], mine.GetProperty("data").EnumerateArray()
            .Select(j => j.GetProperty("title").GetString()));

        Assert.Empty(theirs.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task The_giver_and_worker_feeds_are_role_gated()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await worker.Client.GetAsync("/api/v1/giver/jobs")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await giver.Client.GetAsync("/api/v1/worker/jobs")).StatusCode);
    }

    [Fact]
    public async Task Giver_stats_count_the_callers_jobs_and_applications()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var draft = await CreateJobAsync(giver, title: "Draft", publish: false);
        var active = await CreateJobAsync(giver, title: "Active");

        await TestData.SeedApplicationAsync(_factory, active.GetProperty("id").GetGuid(), worker.Email);

        var stats = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/giver/stats"),
            HttpStatusCode.OK);

        var data = stats.Data();

        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal(1, data.GetProperty("activeJobs").GetInt32());
        Assert.Equal(1, data.GetProperty("draftJobs").GetInt32());
        Assert.Equal(1, data.GetProperty("applications").GetInt32());
        Assert.Equal(1, data.GetProperty("toConfirm").GetInt32());
        Assert.Equal(0, data.GetProperty("totalHired").GetInt32());
    }

    [Fact]
    public async Task Worker_stats_count_applications_and_bookings()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var job = await CreateJobAsync(giver, title: "Booked job");

        await BookWorkerAsync(giver, job.GetProperty("id").GetGuid(), worker);

        var stats = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/stats"),
            HttpStatusCode.OK);

        var data = stats.Data();

        // Selected is no longer an open application, but the booking is.
        Assert.Equal(0, data.GetProperty("applied").GetInt32());
        Assert.Equal(1, data.GetProperty("active").GetInt32());
        Assert.Equal(0, data.GetProperty("earnings").GetInt32());
    }

    [Fact]
    public async Task An_unknown_job_id_is_a_404()
    {
        var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.GetAsync($"/api/v1/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A complete, valid Create Job wizard payload.</summary>
    private static Dictionary<string, object?> ValidJob(
        bool publishImmediately = true,
        bool isRecurring = false,
        List<int>? weekdays = null,
        int workersNeeded = 3,
        int pay = 1200,
        string area = "Delhi",
        string address = "Unit 7, Okhla Phase III",
        bool locationVisible = true,
        double? latitude = 28.6139,
        double? longitude = 77.2090,
        string title = "Warehouse packer",
        int category = 1) =>
        new()
        {
            ["title"] = title,
            ["category"] = category,
            ["description"] = "Packing cartons for the evening dispatch shift.",
            ["workersNeeded"] = workersNeeded,
            ["skills"] = new[] { "Loading", "Packing" },
            ["experience"] = "Fresher",
            ["date"] = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3).ToString("yyyy-MM-dd"),
            ["startTime"] = "09:00:00",
            ["endTime"] = "18:00:00",
            ["reportTime"] = "08:45:00",
            ["isRecurring"] = isRecurring,
            ["weekdays"] = weekdays ?? new List<int>(),
            ["breakMinutes"] = 45,
            ["pay"] = pay,
            ["payType"] = 3,
            ["travelAllowance"] = 200,
            ["foodAllowance"] = 100,
            ["paymentMethod"] = 1,
            ["area"] = area,
            ["address"] = address,
            ["workplaceType"] = 1,
            ["locationVisible"] = locationVisible,
            ["latitude"] = latitude,
            ["longitude"] = longitude,
            ["contactPerson"] = "Site manager",
            ["emergencyContact"] = "+91 99999 88888",
            ["publishImmediately"] = publishImmediately
        };

    private static async Task<JsonElement> CreateJobAsync(
        AuthenticatedClient giver,
        string title,
        bool publish = true,
        int workersNeeded = 2,
        int pay = 1200,
        string area = "Delhi",
        int category = 1,
        double? latitude = 28.6139,
        double? longitude = 77.2090) =>
        (await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/jobs",
                ValidJob(
                    publishImmediately: publish,
                    title: title,
                    workersNeeded: workersNeeded,
                    pay: pay,
                    area: area,
                    category: category,
                    latitude: latitude,
                    longitude: longitude)),
            HttpStatusCode.OK)).Data();

    private static Task<JsonElement> SearchAsync(AuthenticatedClient client, string query) =>
        SearchAsync(client.Client, query);

    /// <summary>Reads a payment straight from the database: the cancel call is the only way to change it, and the tests below are about that change.</summary>
    private async Task<PaymentStatus> PaymentStatusAsync(Guid paymentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Payments
            .Where(p => p.Id == paymentId)
            .Select(p => p.Status)
            .FirstAsync();
    }

    private async Task<DateTime?> RefundedAtAsync(Guid paymentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Payments
            .Where(p => p.Id == paymentId)
            .Select(p => p.RefundedAt)
            .FirstAsync();
    }

    private async Task<int> TotalJobsPostedAsync(string giverEmail)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .GiverProfiles
            .Where(p => p.User!.Email == giverEmail)
            .Select(p => p.TotalJobsPosted)
            .FirstAsync();
    }

    private static async Task<JsonElement> SearchAsync(HttpClient client, string query)
    {
        var envelope = await TestApi.ReadJsonAsync(
            await client.GetAsync($"/api/v1/jobs{query}"),
            HttpStatusCode.OK);

        return envelope.GetProperty("data");
    }

    /// <summary>
    /// Puts the job into the "one worker booked" state, which several Phase 1 rules
    /// depend on. Written through the fixture rather than the API because worker
    /// selection is Phase 3's endpoint, tested on its own.
    /// </summary>
    private Task BookWorkerAsync(AuthenticatedClient giver, Guid jobId, AuthenticatedClient worker) =>
        TestData.SeedBookingAsync(_factory, jobId, worker.Email);
}
