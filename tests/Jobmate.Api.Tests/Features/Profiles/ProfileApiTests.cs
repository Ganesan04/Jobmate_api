using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobmate.Api.Tests.Infrastructure;
using Xunit;

namespace Jobmate.Api.Tests.Features.Profiles;

/// <summary>
/// Phase 8 acceptance tests for profiles, saved jobs and media.
///
/// Two things are being defended here. The first is that a profile edit is a partial
/// update — a form that fails on one field must not wipe the rest. The second, and the
/// reason the public endpoint has its own DTO at all, is that one user's contact details
/// never reach another user, while a private identity document never reaches anyone but
/// its owner and an admin.
/// </summary>
public sealed class ProfileApiTests : IClassFixture<JobmateApiFactory>, IAsyncLifetime
{
    private readonly JobmateApiFactory _factory;

    public ProfileApiTests(JobmateApiFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.StartHostAsync();
        await _factory.ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------------- reading

    [Fact]
    public async Task A_worker_sees_their_own_profile_with_the_role_fields()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var profile = await MyProfileAsync(worker);

        Assert.Equal(worker.UserId, profile.GetProperty("user").GetProperty("id").GetGuid());
        Assert.Equal("worker", profile.GetProperty("worker").GetProperty("user").GetProperty("role").GetString());

        // Null properties are omitted rather than written as null, so the absent half of
        // the profile is simply not on the payload.
        Assert.False(profile.TryGetProperty("giver", out _));
    }

    [Fact]
    public async Task An_employer_sees_a_giver_profile_instead()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var profile = await MyProfileAsync(giver);

        Assert.Equal("workGiver", profile.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("individual", profile.GetProperty("giver").GetProperty("kind").GetString());
        Assert.False(profile.TryGetProperty("worker", out _));
    }

    [Fact]
    public async Task A_stranger_sees_a_public_profile_without_contact_details()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        await UpdateWorkerAsync(worker, new
        {
            bio = "Twelve years on site.",
            skills = new[] { "Carpentry", "Tiling" }
        });

        var public_ = (await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync($"/api/v1/profile/{worker.UserId}"),
            HttpStatusCode.OK)).Data();

        Assert.Equal(worker.UserId, public_.GetProperty("id").GetGuid());
        // Alphabetical: there is no rank column, so the list must at least be stable
        // between two loads of the same profile.
        Assert.Equal(
            new[] { "Carpentry", "Tiling" },
            public_.GetProperty("skills").EnumerateArray().Select(s => s.GetString()!).ToArray());

        // The whole point of the separate public shape: none of these exist on it.
        foreach (var forbidden in new[] { "phone", "email", "latitude", "longitude" })
        {
            Assert.False(
                public_.TryGetProperty(forbidden, out _),
                $"A public profile must not expose {forbidden}.");
        }
    }

    [Fact]
    public async Task Reading_a_profile_that_does_not_exist_is_a_404()
    {
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        await TestApi.ReadJsonAsync(
            await stranger.Client.GetAsync($"/api/v1/profile/{Guid.NewGuid()}"),
            HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------- editing

    [Fact]
    public async Task A_worker_saves_their_profile()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var saved = await UpdateWorkerAsync(worker, new
        {
            bio = "Painter, ten years.",
            experience = "Residential and commercial.",
            location = "Noida",
            expectedPay = 1200,
            expectedPayType = 1,
            skills = new[] { "Painting", "Plastering" }
        });

        Assert.Equal("Painter, ten years.", saved.GetProperty("bio").GetString());
        Assert.Equal("Noida", saved.GetProperty("location").GetString());
        Assert.Equal(1200, saved.GetProperty("expectedPay").GetInt32());
        Assert.Equal(
            new[] { "Painting", "Plastering" },
            saved.GetProperty("skills").EnumerateArray().Select(s => s.GetString()!).ToArray());
    }

    [Fact]
    public async Task A_partial_update_leaves_the_fields_it_did_not_mention_alone()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await UpdateWorkerAsync(worker, new
        {
            bio = "Original bio.",
            experience = "Original experience.",
            skills = new[] { "Welding" }
        });

        // Only the bio is sent this time.
        var saved = await UpdateWorkerAsync(worker, new { bio = "Updated bio." });

        Assert.Equal("Updated bio.", saved.GetProperty("bio").GetString());
        Assert.Equal("Original experience.", saved.GetProperty("experience").GetString());
        Assert.Equal(
            new[] { "Welding" },
            saved.GetProperty("skills").EnumerateArray().Select(s => s.GetString()!).ToArray());
    }

    [Fact]
    public async Task Skills_are_a_set_so_a_duplicate_is_collapsed_rather_than_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var saved = await UpdateWorkerAsync(worker, new
        {
            skills = new[] { "Welding", " welding ", "WELDING", "Plumbing" }
        });

        // Trimmed and de-duplicated ignoring case, so the unique index is never reached.
        Assert.Equal(
            new[] { "Plumbing", "Welding" },
            saved.GetProperty("skills").EnumerateArray().Select(s => s.GetString()!).ToArray());
    }

    [Fact]
    public async Task Sending_an_empty_skill_list_clears_the_skills()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await UpdateWorkerAsync(worker, new { skills = new[] { "Welding" } });
        var cleared = await UpdateWorkerAsync(worker, new { skills = Array.Empty<string>() });

        Assert.Empty(cleared.GetProperty("skills").EnumerateArray());
    }

    [Fact]
    public async Task A_date_of_birth_in_the_future_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PutAsJsonAsync(
                "/api/v1/profile/worker",
                new { dateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1).ToString("yyyy-MM-dd") }),
            HttpStatusCode.BadRequest);

        Assert.Equal("DateOfBirthInFuture", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_worker_cannot_edit_an_employer_profile()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        await TestApi.ReadJsonAsync(
            await worker.Client.PutAsJsonAsync("/api/v1/profile/giver", new { businessName = "Not Mine" }),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_employer_cannot_post_a_blank_business_name()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await giver.Client.PutAsJsonAsync("/api/v1/profile/giver", new { businessName = "   " }),
            HttpStatusCode.BadRequest);

        Assert.Equal("BusinessNameRequired", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_employer_saves_their_business_details()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var saved = (await TestApi.ReadJsonAsync(
            await giver.Client.PutAsJsonAsync("/api/v1/profile/giver", new
            {
                kind = 2,
                businessName = "Sharma Facilities",
                businessType = "Facility management",
                location = "Gurugram",
                contactPerson = "Ravi Sharma",
                gstNumber = "29ABCDE1234F1Z5"
            }),
            HttpStatusCode.OK)).Data();

        Assert.Equal("Sharma Facilities", saved.GetProperty("organisation").GetString());
        Assert.Equal("business", saved.GetProperty("kind").GetString());
        Assert.Equal("Gurugram", saved.GetProperty("location").GetString());
    }

    // -------------------------------------------------------------- availability

    [Fact]
    public async Task A_worker_saves_and_then_clears_their_week()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var saved = await SetAvailabilityAsync(worker, new
        {
            slots = new object[]
            {
                new { dayOfWeek = 1, startTime = "09:00:00", endTime = "17:00:00" },
                new { dayOfWeek = 6, startTime = "10:00:00", endTime = "14:00:00", isAvailable = false }
            }
        });

        var slots = saved.GetProperty("availabilitySlots").EnumerateArray().ToArray();
        Assert.Equal(2, slots.Length);
        Assert.Equal("Mon", slots[0].GetProperty("shortLabel").GetString());
        Assert.Equal("Sat", slots[1].GetProperty("shortLabel").GetString());

        // Replacement, not merge: an empty grid really does clear the week.
        var cleared = await SetAvailabilityAsync(worker, new { slots = Array.Empty<object>() });
        Assert.Empty(cleared.GetProperty("availabilitySlots").EnumerateArray());
    }

    [Fact]
    public async Task A_slot_that_ends_before_it_starts_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PutAsJsonAsync("/api/v1/profile/availability", new
            {
                slots = new[] { new { dayOfWeek = 1, startTime = "17:00:00", endTime = "09:00:00" } }
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidSlotRange", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_day_outside_monday_to_sunday_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PutAsJsonAsync("/api/v1/profile/availability", new
            {
                slots = new[] { new { dayOfWeek = 9, startTime = "09:00:00", endTime = "17:00:00" } }
            }),
            HttpStatusCode.BadRequest);

        Assert.Equal("InvalidDayOfWeek", error.GetProperty("errorCode").GetString());
    }

    // ----------------------------------------------------------------- presence

    [Fact]
    public async Task Presence_is_what_the_other_side_sees_as_the_online_dot()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        Assert.False(await IsOnlineAsync(giver, worker.UserId));

        await TestApi.ReadJsonAsync(
            await worker.Client.PutAsJsonAsync("/api/v1/profile/presence", new { isOnline = true }),
            HttpStatusCode.OK);

        Assert.True(await IsOnlineAsync(giver, worker.UserId));

        await TestApi.ReadJsonAsync(
            await worker.Client.PutAsJsonAsync("/api/v1/profile/presence", new { isOnline = false }),
            HttpStatusCode.OK);

        Assert.False(await IsOnlineAsync(giver, worker.UserId));
    }

    [Fact]
    public async Task An_employer_has_no_presence_to_set()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await TestApi.ReadJsonAsync(
            await giver.Client.PutAsJsonAsync("/api/v1/profile/presence", new { isOnline = true }),
            HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------- media

    [Fact]
    public async Task A_profile_photo_uploads_and_is_attachable()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        using var form = ImageForm("photo.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);
        var response = await worker.Client.PostAsync("/api/v1/profile/image", form);

        var profile = (await TestApi.ReadJsonAsync(response, HttpStatusCode.OK)).Data();
        var url = profile.GetProperty("profileImageUrl").GetString()!;

        Assert.StartsWith("/api/v1/media/profile-images/", url);

        // A profile photo is public to any signed-in user, unlike a document.
        var fetched = await stranger.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_file_type_that_is_not_allowed_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        using var form = ImageForm("payload.exe", "application/octet-stream", [0x4D, 0x5A]);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync("/api/v1/profile/image", form),
            HttpStatusCode.BadRequest);

        Assert.Equal("UnsupportedFileType", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        using var form = ImageForm("empty.png", "image/png", []);

        var error = await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync("/api/v1/profile/image", form),
            HttpStatusCode.BadRequest);

        Assert.Equal("EmptyUpload", error.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task An_identity_document_is_private_to_its_owner_and_an_admin()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var stranger = await TestApi.CreateWorkerAsync(_factory);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46])
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/pdf") }
        }, "file", "aadhaar.pdf");
        form.Add(new StringContent("identity"), "type");

        var document = (await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync("/api/v1/profile/documents", form),
            HttpStatusCode.Created)).Data();

        var url = document.GetProperty("url").GetString()!;
        Assert.Equal("pending", document.GetProperty("status").GetString());

        // The owner can read their own document.
        Assert.Equal(HttpStatusCode.OK, (await worker.Client.GetAsync(url)).StatusCode);

        // A stranger cannot. And the answer is a 404, not a 403, so the response does not
        // confirm that the document exists.
        await TestApi.ReadJsonAsync(await stranger.Client.GetAsync(url), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_media_path_that_tries_to_climb_out_of_the_folder_is_a_404()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        // Encoded so the separators survive routing as a single segment.
        var response = await worker.Client.GetAsync(
            "/api/v1/media/profile-images/..%2F..%2Fappsettings.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- saved jobs

    [Fact]
    public async Task A_worker_saves_lists_and_unsaves_a_job()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/worker/saved-jobs/{jobId}", null),
            HttpStatusCode.Created);

        var list = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/saved-jobs"),
            HttpStatusCode.OK);

        Assert.Equal(1, list.GetProperty("pagination").GetProperty("totalItems").GetInt32());
        Assert.Equal(
            jobId,
            list.GetProperty("data")[0].GetProperty("job").GetProperty("id").GetGuid());

        var unsaved = (await TestApi.ReadJsonAsync(
            await worker.Client.DeleteAsync($"/api/v1/worker/saved-jobs/{jobId}"),
            HttpStatusCode.OK)).Data();

        Assert.True(unsaved.GetBoolean());

        var afterRemoval = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/saved-jobs"),
            HttpStatusCode.OK);

        Assert.Equal(0, afterRemoval.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task Saving_the_same_job_twice_keeps_one_bookmark()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/worker/saved-jobs/{jobId}", null),
            HttpStatusCode.Created);

        // A double tap on the save icon is not a conflict.
        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/worker/saved-jobs/{jobId}", null),
            HttpStatusCode.Created);

        var list = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/saved-jobs"),
            HttpStatusCode.OK);

        Assert.Equal(1, list.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task Unsaving_a_job_that_was_never_saved_reports_false_rather_than_failing()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);

        var removed = (await TestApi.ReadJsonAsync(
            await worker.Client.DeleteAsync($"/api/v1/worker/saved-jobs/{Guid.NewGuid()}"),
            HttpStatusCode.OK)).Data();

        Assert.False(removed.GetBoolean());
    }

    [Fact]
    public async Task Saved_jobs_are_hidden_once_the_job_is_no_longer_open()
    {
        using var worker = await TestApi.CreateWorkerAsync(_factory);
        using var giver = await TestApi.CreateGiverAsync(_factory);

        var jobId = await CreateJobAsync(giver);

        await TestApi.ReadJsonAsync(
            await worker.Client.PostAsync($"/api/v1/worker/saved-jobs/{jobId}", null),
            HttpStatusCode.Created);

        // Cancelling rather than deleting: a published job keeps its history, which is the
        // rule the API already enforces for deletes.
        await TestApi.ReadJsonAsync(
            await giver.Client.PostAsync($"/api/v1/jobs/{jobId}/cancel", null),
            HttpStatusCode.OK);

        var visible = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/saved-jobs"),
            HttpStatusCode.OK);

        Assert.Equal(0, visible.GetProperty("pagination").GetProperty("totalItems").GetInt32());

        // The bookmark row itself survives, so the list can be asked for closed jobs.
        var withClosed = await TestApi.ReadJsonAsync(
            await worker.Client.GetAsync("/api/v1/worker/saved-jobs?includeClosed=true"),
            HttpStatusCode.OK);

        Assert.Equal(1, withClosed.GetProperty("pagination").GetProperty("totalItems").GetInt32());
    }

    [Fact]
    public async Task An_employer_has_no_saved_jobs_list()
    {
        using var giver = await TestApi.CreateGiverAsync(_factory);

        await TestApi.ReadJsonAsync(
            await giver.Client.GetAsync("/api/v1/worker/saved-jobs"),
            HttpStatusCode.Forbidden);
    }

    // -------------------------------------------------------------------- helpers

    private static async Task<JsonElement> MyProfileAsync(AuthenticatedClient caller) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.GetAsync("/api/v1/profile/me"),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> UpdateWorkerAsync(AuthenticatedClient caller, object payload) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.PutAsJsonAsync("/api/v1/profile/worker", payload),
            HttpStatusCode.OK)).Data();

    private static async Task<JsonElement> SetAvailabilityAsync(AuthenticatedClient caller, object payload) =>
        (await TestApi.ReadJsonAsync(
            await caller.Client.PutAsJsonAsync("/api/v1/profile/availability", payload),
            HttpStatusCode.OK)).Data();

    private static async Task<bool> IsOnlineAsync(AuthenticatedClient viewer, Guid userId) =>
        (await TestApi.ReadJsonAsync(
            await viewer.Client.GetAsync($"/api/v1/profile/{userId}"),
            HttpStatusCode.OK)).Data().GetProperty("isOnline").GetBoolean();

    private static MultipartFormDataContent ImageForm(string fileName, string contentType, byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent();
        form.Add(content, "file", fileName);

        return form;
    }

    private async Task<Guid> CreateJobAsync(AuthenticatedClient giver, string title = "Warehouse load")
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
