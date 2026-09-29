using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Messaging;

/// <summary>
/// Phase 7 acceptance tests for chat.
///
/// The behaviour that matters is not just that a message round-trips — it is that the
/// unread badge, which <c>GET /notifications/unread-count</c> already reads from
/// <c>ConversationParticipant.UnreadCount</c>, stays correct. These tests therefore assert
/// the badge after every action that should change it.
/// </summary>
public sealed class MessagingApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public MessagingApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------ starting

    [Fact]
    public async Task Two_people_involved_in_a_job_can_open_a_thread()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversation = await StartAsync(worker, giver.UserId, jobId);

        Assert.Equal(giver.UserId, conversation.GetProperty("participantId").GetGuid());
        Assert.Equal(jobId, conversation.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task A_stranger_cannot_open_a_thread_about_someone_elses_job()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        // The stranger was never on the job, so the pair is not one that may talk about it.
        var response = await stranger.Client.PostAsJsonAsync(
            "/api/v1/conversations",
            new { participantId = worker.UserId, jobId });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Opening_a_thread_with_yourself_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                "/api/v1/conversations",
                new { participantId = worker.UserId }),
            HttpStatusCode.BadRequest);

        Assert.Equal("SelfConversation", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Asking_for_the_same_thread_twice_returns_the_one_that_exists()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var first = await StartAsync(worker, giver.UserId, jobId);
        var second = await StartAsync(worker, giver.UserId, jobId);

        // The job card's Message button and the chat list both lead here, so a double tap
        // has to land in one thread rather than two.
        Assert.Equal(
            first.GetProperty("id").GetGuid(),
            second.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_general_thread_needs_no_job()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var other = await TestApi.CreateWorkerAsync(_factory);

        var conversation = await StartAsync(worker, other.UserId);

        Assert.Equal(Guid.Empty, conversation.GetProperty("jobId").GetGuid());
        Assert.Equal("General enquiry", conversation.GetProperty("jobContext").GetString());
    }

    // ------------------------------------------------------------------ sending

    [Fact]
    public async Task A_message_reaches_the_other_persons_thread()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        var sent = await SendAsync(worker, conversationId, "Is the shift still going ahead?");

        Assert.Equal("Is the shift still going ahead?", sent.GetProperty("text").GetString());
        Assert.True(sent.GetProperty("isMine").GetBoolean());

        var received = await MessagesAsync(giver, conversationId);

        var only = received.GetProperty("data").EnumerateArray().Single();
        Assert.Equal("Is the shift still going ahead?", only.GetProperty("text").GetString());

        // Seen from the recipient's side the same row is not theirs.
        Assert.False(only.GetProperty("isMine").GetBoolean());
        Assert.Equal(worker.UserId, only.GetProperty("senderId").GetGuid());
    }

    [Fact]
    public async Task An_empty_message_is_refused()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var conversationId = await StartGeneralAsync(worker, giver.UserId);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/conversations/{conversationId}/messages",
                new { text = "   " }),
            HttpStatusCode.BadRequest);

        Assert.Contains(
            error.GetProperty("errors").EnumerateArray(),
            e => e.GetProperty("field").GetString() == "text");
    }

    [Fact]
    public async Task Someone_outside_the_thread_cannot_post_into_it()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        var response = await stranger.Client.PostAsJsonAsync(
            $"/api/v1/conversations/{conversationId}/messages",
            new { text = "Hello?" });

        await TestApi.ReadJsonAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_outside_the_thread_cannot_read_it()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync($"/api/v1/conversations/{conversationId}/messages"),
            HttpStatusCode.Forbidden);

        await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync($"/api/v1/conversations/{conversationId}"),
            HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------- unread counts

    [Fact]
    public async Task Sending_a_message_raises_the_recipients_badge()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        Assert.Equal(0, await MessageBadgeAsync(giver));

        await SendAsync(worker, conversationId, "Are you still hiring?");
        await SendAsync(worker, conversationId, "And for which date?");

        Assert.Equal(2, await MessageBadgeAsync(giver));

        // The sender's own badge stays at zero: you cannot have unread messages you sent.
        Assert.Equal(0, await MessageBadgeAsync(worker));
    }

    [Fact]
    public async Task Opening_the_thread_clears_the_badge()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await SendAsync(worker, conversationId, "One");
        await SendAsync(worker, conversationId, "Two");

        Assert.Equal(2, await MessageBadgeAsync(giver));

        var cleared = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/conversations/{conversationId}/read", null),
            HttpStatusCode.OK);

        // The answer is how many were cleared, so the app can fix its badge without a
        // second round trip.
        Assert.Equal(2, cleared.Data().GetInt32());
        Assert.Equal(0, await MessageBadgeAsync(giver));
    }

    [Fact]
    public async Task Clearing_a_thread_twice_is_harmless()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await SendAsync(worker, conversationId, "Hello");

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/conversations/{conversationId}/read", null),
            HttpStatusCode.OK);

        var second = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/conversations/{conversationId}/read", null),
            HttpStatusCode.OK);

        // A retried screen open clears nothing and is still a success.
        Assert.Equal(0, second.Data().GetInt32());
    }

    [Fact]
    public async Task A_new_message_reopens_a_cleared_badge()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await SendAsync(worker, conversationId, "First");

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/conversations/{conversationId}/read", null),
            HttpStatusCode.OK);

        await SendAsync(worker, conversationId, "Second");

        Assert.Equal(1, await MessageBadgeAsync(giver));
    }

    [Fact]
    public async Task A_new_message_notifies_the_recipient()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await SendAsync(worker, conversationId, "Do you have a forklift licence?");

        var page = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/notifications"),
            HttpStatusCode.OK);

        var row = page.GetProperty("data").EnumerateArray()
            .Single(n => n.GetProperty("targetId").GetString() == conversationId.ToString());

        Assert.Equal("/chat", row.GetProperty("targetRoute").GetString());
        Assert.Contains("Do you have", row.GetProperty("body").GetString()!);
    }

    // ------------------------------------------------------------------- the list

    [Fact]
    public async Task The_list_names_the_other_person_and_the_job()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver, "Night shift loading");
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await SendAsync(worker, conversationId, "Can I start at 8 instead of 9?");

        var row = (await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/conversations"),
            HttpStatusCode.OK)).Data().EnumerateArray().Single();

        Assert.Equal(worker.UserId, row.GetProperty("participantId").GetGuid());
        Assert.Equal("Night shift loading", row.GetProperty("jobContext").GetString());
        Assert.Equal("Can I start at 8 instead of 9?", row.GetProperty("lastMessage").GetString());
        Assert.Equal(1, row.GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task The_most_recent_thread_sorts_first()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var firstJob = await CreateJobAsync(giver, "First job");
        var secondJob = await CreateJobAsync(giver, "Second job");
        await ApplyAsync(worker, firstJob);
        await ApplyAsync(worker, secondJob);

        var firstThread = (await StartAsync(worker, giver.UserId, firstJob)).GetProperty("id").GetGuid();
        var secondThread = (await StartAsync(worker, giver.UserId, secondJob)).GetProperty("id").GetGuid();

        await SendAsync(worker, firstThread, "About the first job");

        var list = (await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/conversations"),
            HttpStatusCode.OK)).Data().EnumerateArray().ToList();

        // The thread that has actually been talked in floats above the empty one.
        Assert.Equal(firstJob, list[0].GetProperty("jobId").GetGuid());
        Assert.Equal(secondJob, list[1].GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task Archiving_hides_the_thread_and_stops_it_counting()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await SendAsync(worker, conversationId, "Are you still hiring?");

        Assert.Equal(1, await MessageBadgeAsync(giver));

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/conversations/{conversationId}/archive",
                new { archived = true }),
            HttpStatusCode.OK);

        var visible = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/conversations"),
            HttpStatusCode.OK);

        Assert.Empty(visible.Data().EnumerateArray());

        // An invisible thread must not keep inflating the badge.
        Assert.Equal(0, await MessageBadgeAsync(giver));
    }

    [Fact]
    public async Task Archiving_is_private_to_the_person_who_did_it()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/conversations/{conversationId}/archive",
                new { archived = true }),
            HttpStatusCode.OK);

        // Deleting a thread would take the other person's copy with it, which is why this
        // is an archive and not a delete.
        var theirs = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/conversations"),
            HttpStatusCode.OK);

        Assert.Single(theirs.Data().EnumerateArray());
    }

    [Fact]
    public async Task Sending_again_brings_an_archived_thread_back()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var jobId = await CreateJobAsync(giver);
        await ApplyAsync(worker, jobId);

        var conversationId = (await StartAsync(worker, giver.UserId, jobId)).GetProperty("id").GetGuid();

        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync(
                $"/api/v1/conversations/{conversationId}/archive",
                new { archived = true }),
            HttpStatusCode.OK);

        await SendAsync(worker, conversationId, "Sorry, one more question.");

        var theirs = await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/conversations"),
            HttpStatusCode.OK);

        Assert.Single(theirs.Data().EnumerateArray());
        Assert.Equal(1, await MessageBadgeAsync(giver));
    }

    // ------------------------------------------------------------------ paging

    [Fact]
    public async Task A_chat_page_comes_back_oldest_first()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var conversationId = await StartGeneralAsync(worker, giver.UserId);

        await SendAsync(worker, conversationId, "First");
        await SendAsync(worker, conversationId, "Second");
        await SendAsync(worker, conversationId, "Third");

        var page = await MessagesAsync(giver, conversationId, newestFirst: true, pageSize: 2);

        var texts = page.GetProperty("data").EnumerateArray()
            .Select(m => m.GetProperty("text").GetString()!)
            .ToArray();

        // The newest page, in the order a chat screen draws it.
        Assert.Equal(["Second", "Third"], texts);
        Assert.Equal(3, page.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    // -------------------------------------------------------------------- helpers

    private static async Task<int> MessageBadgeAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/notifications/unread-count"),
            HttpStatusCode.OK)).Data().GetProperty("messages").GetInt32();

    private static async Task<JsonElement> StartAsync(
        AuthenticatedClient caller,
        Guid participantId,
        Guid? jobId = null)
    {
        var response = await caller.Client.PostAsJsonAsync(
            "/api/v1/conversations",
            jobId is { } id
                ? new { participantId, jobId = id }
                : (object)new { participantId });

        return (await TestApi.ReadJsonAsync(response, HttpStatusCode.OK)).Data();
    }

    private static async Task<Guid> StartGeneralAsync(AuthenticatedClient caller, Guid participantId) =>
        (await StartAsync(caller, participantId)).GetProperty("id").GetGuid();

    private static async Task<JsonElement> SendAsync(
        AuthenticatedClient caller,
        Guid conversationId,
        string text)
    {
        var response = await caller.Client.PostAsJsonAsync(
            $"/api/v1/conversations/{conversationId}/messages",
            new { text });

        return (await TestApi.ReadJsonAsync(response, HttpStatusCode.Created)).Data();
    }

    private static async Task<JsonElement> MessagesAsync(
        AuthenticatedClient caller,
        Guid conversationId,
        bool newestFirst = true,
        int? pageSize = null) =>
        await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync(
                $"/api/v1/conversations/{conversationId}/messages?newestFirst={newestFirst}" +
                (pageSize is { } size ? $"&pageSize={size}" : string.Empty)),
            HttpStatusCode.OK);

    private static async Task ApplyAsync(AuthenticatedClient worker, Guid jobId) =>
        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsJsonAsync(
                $"/api/v1/jobs/{jobId}/apply",
                new { message = "Available and experienced." }),
            HttpStatusCode.OK);

    private async Task<Guid> CreateJobAsync(
        AuthenticatedClient giver,
        string title = "Warehouse load")
    {
        var created = await TestApi.ReadJsonAsync(
            await giver.Client.PostAsJsonAsync("/api/v1/jobs", ValidJob(title)),
            HttpStatusCode.OK);

        return created.Data().GetProperty("id").GetGuid();
    }

    /// <summary>A complete, valid Create Job wizard payload.</summary>
    private static Dictionary<string, object?> ValidJob(string title) =>
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
