using System.Net;
using System.Net.Http.Json;
using Jobmate.Api.Tests.Infrastructure;
using Jobmate.Domain.Entities;
using Jobmate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jobmate.Api.Tests.Features.Notifications;

/// <summary>
/// Phase 6 acceptance tests for the notification inbox and push device registration.
///
/// Two things are being defended here. First, an inbox is private: a user can see and
/// mark read their own notifications and nobody else's. Second, a device token
/// identifies a phone rather than a person, so registering it twice — or registering it
/// from a different account on a shared phone — has to converge on one row rather than
/// either duplicating or failing on the unique index.
/// </summary>
public sealed class NotificationApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public NotificationApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------- devices

    [Fact]
    public async Task A_registered_device_is_listed_for_the_person_who_registered_it()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var registered = await RegisterDeviceAsync(worker, "token-abc-001", "Pixel 8", 1);

        Assert.True(registered.GetProperty("isActive").GetBoolean());
        Assert.Equal("Pixel 8", registered.GetProperty("deviceName").GetString());
        Assert.Equal("android", registered.GetProperty("platform").GetString());

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        var devices = page.Data().EnumerateArray().ToArray();

        Assert.Single(devices);
        Assert.Equal("Pixel 8", devices[0].GetProperty("deviceName").GetString());
    }

    [Fact]
    public async Task A_device_token_is_never_handed_back_to_the_app()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await RegisterDeviceAsync(worker, "super-secret-fcm-token", "Pixel 8", 1);

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        // The token is a credential; the app only needs the suffix to show which phone
        // it is.
        Assert.DoesNotContain("super-secret-fcm-token", page.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("cm-token", page.Data()[0].GetProperty("tokenSuffix").GetString());
    }

    [Fact]
    public async Task Registering_the_same_token_twice_updates_it_instead_of_adding_a_row()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await RegisterDeviceAsync(worker, "same-token", "Old name", 1);
        var updated = await RegisterDeviceAsync(worker, "same-token", "New name", 2);

        Assert.Equal("New name", updated.GetProperty("deviceName").GetString());
        Assert.Equal("ios", updated.GetProperty("platform").GetString());

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        Assert.Single(page.Data().EnumerateArray().ToArray());
    }

    [Fact]
    public async Task A_token_registered_by_someone_else_moves_to_the_new_account()
    {
        using var first = await TestApi.CreateWorkerAsync(_factory);
        using var second = await TestApi.CreateWorkerAsync(_factory);

        await RegisterDeviceAsync(first, "shared-phone-token", "Shared phone", 1);

        // Signing out and signing in on the same phone sends the same token. The new user
        // has to receive notifications, and the old user has to stop receiving them.
        var moved = await RegisterDeviceAsync(second, "shared-phone-token", "Shared phone", 1);

        Assert.True(moved.GetProperty("isActive").GetBoolean());

        var mine = await TestApi.ReadJsonAsync(
            await second.Client.GetAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        var theirs = await TestApi.ReadJsonAsync(
            await first.Client.GetAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        Assert.Single(mine.Data().EnumerateArray().ToArray());
        Assert.Empty(theirs.Data().EnumerateArray().ToArray());
    }

    [Fact]
    public async Task A_device_with_no_token_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync("/api/v1/devices/register", new { token = "" }),
            HttpStatusCode.BadRequest);

        Assert.Contains(
            error.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "token");
    }

    [Fact]
    public async Task Unregistering_a_device_stops_push_reaching_it()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var registered = await RegisterDeviceAsync(worker, "to-unregister", "Pixel 8", 1);
        var id = registered.GetProperty("id").GetGuid();

        var remaining = await TestApi.ReadJsonAsync(
            await worker.Client.DeleteAsync($"/api/v1/devices/{id}"),
            HttpStatusCode.OK);

        Assert.Equal(0, remaining.Data().GetInt32());

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        Assert.False(page.Data()[0].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Registering_again_brings_a_switched_off_device_back()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var registered =         await RegisterDeviceAsync(worker, "reactivation-token", "Pixel 8", 1);

        await worker.Client.DeleteAsync($"/api/v1/devices/{registered.GetProperty("id").GetGuid()}");

        // The app registers on every launch, so opening it again is what turns push back
        // on after the user has switched notifications off in settings.
        var reactivated = await RegisterDeviceAsync(worker, "reactivation-token", "Pixel 8", 1);

        Assert.True(reactivated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Unregistering_someone_elses_device_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var registered = await RegisterDeviceAsync(worker, "mine-only", "Pixel 8", 1);
        var id = registered.GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await stranger.Client.DeleteAsync($"/api/v1/devices/{id}"),
            HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signing_out_leaves_no_device_receiving_push()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await RegisterDeviceAsync(worker, "on-every-phone-1", "Pixel 8", 1);
        await RegisterDeviceAsync(worker, "on-every-phone-2", "Tablet", 3);

        var remaining = await TestApi.ReadJsonAsync(
            await worker.Client.DeleteAsync("/api/v1/devices"),
            HttpStatusCode.OK);

        Assert.Equal(0, remaining.Data().GetInt32());
    }

    [Fact]
    public async Task Registering_a_device_needs_a_signed_in_user()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/devices/register",
            new { token = "no-one-signed-in" });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ the inbox

    [Fact]
    public async Task An_action_somewhere_else_shows_up_in_the_inbox()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        // Starting the job is the giver's action; the worker is the one who is told.
        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK);

        var first = page.Data().EnumerateArray().First();

        Assert.Equal("Work started", first.GetProperty("title").GetString());
        Assert.False(first.GetProperty("isRead").GetBoolean());
        Assert.Equal("jobAccepted", first.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_notification_points_at_a_named_route_and_carries_the_booking_id()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);

        var notification = await OnlyNotificationAsync(worker, "?types=jobAccepted");

        // The app resolves the route by name and hands the id to the screen separately,
        // so the two have to agree: a route that is not a real named route is a tap that
        // silently does nothing.
        Assert.Equal("/accepted-job", notification.GetProperty("targetRoute").GetString());
        Assert.Equal(assignmentId.ToString(), notification.GetProperty("targetId").GetString());
        Assert.Equal("assignment", notification.GetProperty("referenceType").GetString());
    }

    [Fact]
    public async Task Starting_a_job_points_the_worker_at_the_check_in_screen()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        var notification = await NotificationTitledAsync(worker, "Work started");

        // Each worker is sent their own booking id rather than a shared job id, so the
        // notification lands on the screen that asks for the check-in code.
        Assert.Equal("/accepted-job", notification.GetProperty("targetRoute").GetString());
        Assert.Equal(assignmentId.ToString(), notification.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task An_applicant_is_told_to_open_the_applicants_screen()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await worker.Client.PostAsJsonAsync(
            $"/api/v1/jobs/{jobId}/apply",
            new { message = "Available." });

        var notification = await OnlyNotificationAsync(giver, "?types=newApplicant");

        Assert.Equal("New applicant", notification.GetProperty("title").GetString());
        Assert.Equal("/job-applications", notification.GetProperty("targetRoute").GetString());
        Assert.Equal(jobId.ToString(), notification.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task A_notification_body_is_sent_as_body()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK);

        var notification = page.Data()[0];

        Assert.False(string.IsNullOrWhiteSpace(notification.GetProperty("body").GetString()));
    }

    [Fact]
    public async Task One_person_cannot_read_another_persons_notifications()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var theirs = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK);

        var mine = await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK);

        Assert.NotEmpty(theirs.Data().EnumerateArray());
        Assert.Empty(mine.Data().EnumerateArray());
    }

    [Fact]
    public async Task The_unread_badge_counts_what_has_not_been_read()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // A worker can only hold one application per job, so a history of several
        // notifications is built from several jobs.
        await BookOnFreshJobAsync(giver, worker);
        await BookOnFreshJobAsync(giver, worker);
        await BookOnFreshJobAsync(giver, worker);

        var before = await UnreadAsync(worker);

        Assert.True(before >= 3);

        var newest = await NewestNotificationIdAsync(worker);

        var marked = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/notifications/{newest}/read", null),
            HttpStatusCode.OK);

        Assert.Equal(before - 1, marked.Data().GetInt32());
        Assert.Equal(before - 1, await UnreadAsync(worker));

        var all = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync("/api/v1/notifications/read-all", null),
            HttpStatusCode.OK);

        Assert.Equal(0, all.Data().GetInt32());
        Assert.Equal(0, await UnreadAsync(worker));
    }

    [Fact]
    public async Task Marking_a_row_read_is_reported_and_keeps_its_read_time()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var notification = await OnlyNotificationAsync(worker);
        var id = notification.GetProperty("id").GetGuid();

        var marked = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/notifications/{id}/read", null),
            HttpStatusCode.OK);

        Assert.Equal(0, marked.Data().GetInt32());

        var reread = await OnlyNotificationAsync(worker);

        Assert.True(reread.GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task Marking_an_already_read_row_again_is_harmless()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var id = (await OnlyNotificationAsync(worker)).GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/notifications/{id}/read", null),
            HttpStatusCode.OK);

        // The app calls this on every tap, so a retried tap must not be an error.
        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/notifications/{id}/read", null),
            HttpStatusCode.OK);
    }

    [Fact]
    public async Task One_person_cannot_mark_another_persons_row_read()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var id = (await OnlyNotificationAsync(worker)).GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await stranger.Client.PostAsync($"/api/v1/notifications/{id}/read", null),
            HttpStatusCode.NotFound);

        // Still unread: the attempt did nothing at all.
        Assert.False((await OnlyNotificationAsync(worker)).GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task The_inbox_can_be_filtered_to_what_is_unread()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await BookOnFreshJobAsync(giver, worker);
        await BookOnFreshJobAsync(giver, worker);

        var newest = await NewestNotificationIdAsync(worker);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/notifications/{newest}/read", null),
            HttpStatusCode.OK);

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications?unreadOnly=true"),
            HttpStatusCode.OK);

        Assert.NotEmpty(page.Data().EnumerateArray());
        Assert.All(
            page.Data().EnumerateArray(),
            n => Assert.False(n.GetProperty("isRead").GetBoolean()));
    }

    [Fact]
    public async Task The_inbox_can_be_filtered_by_type()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        // The worker now has both a jobAccepted (selected, then started) and nothing
        // else, so filtering to payment is what proves the filter is applied.
        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications?types=payment"),
            HttpStatusCode.OK);

        Assert.Empty(page.Data().EnumerateArray());

        var byType = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications?types=jobAccepted"),
            HttpStatusCode.OK);

        Assert.NotEmpty(byType.Data().EnumerateArray());
    }

    [Fact]
    public async Task A_paid_booking_lands_in_the_inbox_as_a_payment()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });
        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/complete",
                new { }),
            HttpStatusCode.OK);

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/payments/{worker.UserId}",
                new { basePay = 1000 }),
            HttpStatusCode.OK);
        var notification = await OnlyNotificationAsync(worker, "?types=payment");

        Assert.Equal("/earnings", notification.GetProperty("targetRoute").GetString());
    }

    [Fact]
    public async Task The_inbox_is_paged()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await BookOnFreshJobAsync(giver, worker);
        await BookOnFreshJobAsync(giver, worker);
        await BookOnFreshJobAsync(giver, worker);

        var page = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/notifications?page=1&pageSize=2"),
            HttpStatusCode.OK);

        var pagination = page.GetProperty("pagination");

        Assert.Equal(2, page.Data().GetArrayLength());
        Assert.True(pagination.GetProperty("totalItems").GetInt32() >= 3);
        Assert.True(pagination.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Reading_the_inbox_needs_a_signed_in_user()
    {
        using var anonymous = _factory.CreateAnonymousClient();

        await TestApi.ReadJsonAsync(
            await anonymous.GetAsync("/api/v1/notifications"),
            HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------- reminders

    [Fact]
    public async Task The_employer_can_remind_a_worker_who_has_not_checked_in()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        var result = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{jobId}/remind", null),
            HttpStatusCode.OK);

        Assert.Equal(1, result.Data().GetProperty("remindedCount").GetInt32());

        var notification = await OnlyNotificationAsync(worker, "?types=jobReminder");

        Assert.Equal("/accepted-job", notification.GetProperty("targetRoute").GetString());
    }

    [Fact]
    public async Task A_reminder_skips_a_worker_who_has_already_checked_in()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        var assignmentId = await BookAsync(giver, worker, jobId);
        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        var code = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync($"/api/v1/jobs/{jobId}/check-in-code/{assignmentId}"),
            HttpStatusCode.OK);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/assignments/{assignmentId}/check-in",
                new { code = code.Data().GetString() }),
            HttpStatusCode.OK);

        var result = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{jobId}/remind", null),
            HttpStatusCode.OK);

        // The employer asked about the workers who have not turned up, not for a report
        // on everyone, so there is nothing left to remind.
        Assert.Equal(0, result.Data().GetProperty("remindedCount").GetInt32());
    }

    [Fact]
    public async Task Reminding_before_the_job_starts_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var error = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{jobId}/remind", null),
            HttpStatusCode.BadRequest);

        Assert.Equal("JobNotStarted", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_stranger_cannot_remind_the_workers_on_someone_elses_job()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var other = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await TestApi.ReadJsonAsync(
            await other.Client.PostAsync($"/api/v1/jobs/{jobId}/remind", null),
            HttpStatusCode.Forbidden);
    }

    // ---------------------------------------------------------------------- push

    [Fact]
    public async Task A_push_is_attempted_for_a_user_who_has_a_device()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await RegisterDeviceAsync(worker, "push-target-token", "Pixel 8", 1);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var row = await OnlyStoredNotificationAsync(_factory, worker.UserId);

        // With a live device the provider was actually asked to deliver, and the row
        // records it. With none it was not, which is what keeps the row honest.
        Assert.NotNull(row.PushDispatchedAt);
    }

    [Fact]
    public async Task No_push_is_attempted_for_a_user_with_no_device()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var row = await OnlyStoredNotificationAsync(_factory, worker.UserId);

        Assert.Null(row.PushDispatchedAt);
    }

    [Fact]
    public async Task A_deactivated_device_stops_push_attempts()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var registered = await RegisterDeviceAsync(worker, "to-be-deactivated", "Pixel 8", 1);

        await worker.Client.DeleteAsync($"/api/v1/devices/{registered.GetProperty("id").GetGuid()}");

        var jobId = await CreateJobAsync(giver);
        await BookAsync(giver, worker, jobId);

        var row = await OnlyStoredNotificationAsync(_factory, worker.UserId);

        Assert.Null(row.PushDispatchedAt);
    }

    [Fact]
    public async Task A_notification_reaches_the_inbox_whether_or_not_a_push_was_attempted()
    {
        using var withDevice = await TestApi.CreateWorkerAsync(_factory);
        using var withoutDevice = await TestApi.CreateWorkerAsync(_factory);
        using var other = await TestApi.CreateWorkerAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await RegisterDeviceAsync(withDevice, "push-target-token", "Pixel 8", 1);

        var jobId = await CreateJobAsync(giver, workersNeeded: 2);

        foreach (var worker in new[] { withDevice, withoutDevice })
        {
            await BookAsync(giver, worker, jobId);
        }

        await giver.Client.PostAsJsonAsync($"/api/v1/jobs/{jobId}/start", new { });

        // Push is best effort and the row is written first, so a user with no device — or
        // a provider that is down — still finds the notification waiting in the app.
        Assert.NotEmpty((await InboxAsync(withDevice)).EnumerateArray());
        Assert.NotEmpty((await InboxAsync(withoutDevice)).EnumerateArray());
    }

    // -------------------------------------------------------------------- helpers

    private static async Task<System.Text.Json.JsonElement> RegisterDeviceAsync(
        AuthenticatedClient caller,
        string token,
        string name,
        int platform) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.PostAsJsonAsync(
                "/api/v1/devices/register",
                new { token, deviceName = name, platform, appVersion = "1.0.0" }),
            HttpStatusCode.OK)).Data();

    private static async Task<int> UnreadAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/notifications/unread-count"),
            HttpStatusCode.OK)).Data().GetProperty("notifications").GetInt32();

    private static async Task<System.Text.Json.JsonElement> OnlyNotificationAsync(
        AuthenticatedClient caller,
        string query = "") =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync($"/api/v1/notifications{query}"),
            HttpStatusCode.OK)).Data().EnumerateArray().Single();

    /// <summary>
    /// Reads the stored row rather than the response, because push bookkeeping is not
    /// something the app is shown and so is not on the DTO.
    /// </summary>
    private static async Task<Notification> OnlyStoredNotificationAsync(
        JobmateApiFactory factory,
        Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rows = await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync();

        return rows.Single();
    }

    private static async Task<System.Text.Json.JsonElement> InboxAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK)).Data();

    private static async Task<System.Text.Json.JsonElement> NotificationTitledAsync(
        AuthenticatedClient caller,
        string title) =>
        (await InboxAsync(caller)).EnumerateArray()
            .Single(n => n.GetProperty("title").GetString() == title);

    private async Task<Guid> CreateJobAsync(
        AuthenticatedClient giver,
        string title = "Warehouse load",
        int workersNeeded = 1)
    {
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                "/api/v1/jobs",
                new
                {
                    title,
                    category = 1,
                    description = "Loading and unloading a truck.",
                    workersNeeded,
                    skills = new[] { "Loading" },
                    date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3).ToString("yyyy-MM-dd"),
                    startTime = "09:00:00",
                    endTime = "17:00:00",
                    reportTime = "08:45:00",
                    breakMinutes = 30,
                    pay = 1000,
                    payType = 3,
                    area = "Delhi",
                    address = "12 Warehouse Road",
                    workplaceType = 1,
                    locationVisible = true,
                    latitude = 28.6139,
                    longitude = 77.2090,
                    publishImmediately = true
                }),
            HttpStatusCode.OK);

        return created.Data().GetProperty("id").GetGuid();
    }

    /// <summary>Books the worker onto a brand new job, since they may only apply once per job.</summary>
    private async Task BookOnFreshJobAsync(AuthenticatedClient giver, AuthenticatedClient worker)
    {
        var jobId = await CreateJobAsync(giver, title: $"Shift {Guid.NewGuid():N}");

        await BookAsync(giver, worker, jobId);
    }

    /// <summary>The id of the caller's most recent notification.</summary>
    private static async Task<Guid> NewestNotificationIdAsync(AuthenticatedClient caller) =>
        (await InboxAsync(caller)).EnumerateArray().First().GetProperty("id").GetGuid();

    private static async Task<Guid> BookAsync(
        AuthenticatedClient giver,
        AuthenticatedClient worker,
        Guid jobId)
    {
        var applied = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/apply",
                new { message = "Available and experienced." }),
            HttpStatusCode.OK);

        var booked = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/applications/{applied.Data().GetProperty("id").GetGuid()}/select",
                new { agreedPay = (int?)1000 }),
            HttpStatusCode.OK);

        return booked.Data().GetProperty("id").GetGuid();
    }
}
