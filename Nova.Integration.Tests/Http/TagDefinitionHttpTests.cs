using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Tags;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for tag-definition management, lifecycle, and query endpoints,
/// focused on the response contract that route-metadata assertions cannot prove: status codes,
/// DTO projection, case-insensitive uniqueness, and the active-only choices read path.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TagDefinitionHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies creating a tag definition returns 201 with the persisted DTO, a normalized color, and
    /// the active lifecycle state. No <c>Location</c> header is asserted because the create handler
    /// returns a null location.
    /// </summary>
    [Fact]
    public async Task CreateTagDefinition_ReturnsCreated_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-create-admin", "Create Club", cancellationToken);

        var name = $"Tag-{Guid.CreateVersion7():N}";
        using var response = await client.PostAsJsonAsync(
            TagEndpoints.Create,
            new CreateTagDefinitionInput { Name = name, Color = "#1a2b3c" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TagDefinitionDto>(cancellationToken);
        created.ShouldNotBeNull();
        created.Name.ShouldBe(name);
        created.Color.ShouldBe("#1A2B3C");
        created.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        created.PlayerTagId.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Verifies the create endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task CreateTagDefinition_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsJsonAsync(
            TagEndpoints.Create,
            new CreateTagDefinitionInput { Name = "Anonymous Tag", Color = "#AABBCC" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a second create with the same name in a different case is rejected as a conflict by
    /// the case-insensitive <c>(ClubId, NormalizedName)</c> unique index.
    /// </summary>
    [Fact]
    public async Task CreateTagDefinition_ReturnsConflict_ForDuplicateNameIgnoringCase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-create-duplicate-admin", "Duplicate Club", cancellationToken);

        var name = $"Dup-{Guid.CreateVersion7():N}";
        await CreateTagAsync(client, name, "#112233", cancellationToken);

        using var duplicate = await client.PostAsJsonAsync(
            TagEndpoints.Create,
            new CreateTagDefinitionInput { Name = name.ToUpperInvariant(), Color = "#445566" },
            cancellationToken);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var count = await verify.PlayerTags
            .CountAsync(tag => tag.ClubId == club.ClubId && tag.NormalizedName == name.ToUpperInvariant(), cancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies an invalid create body is rejected with validation ProblemDetails naming both fields,
    /// proving automatic endpoint validation runs before the handler.
    /// </summary>
    [Fact]
    public async Task CreateTagDefinition_ReturnsValidationProblem_ForInvalidBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-create-invalid-admin", "Invalid Create Club", cancellationToken);

        using var response = await client.PostAsJsonAsync(
            TagEndpoints.Create,
            new CreateTagDefinitionInput { Name = "", Color = "not-a-color" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(CreateTagDefinitionInput.Name));
        errors.ShouldContainKey(nameof(CreateTagDefinitionInput.Color));
    }

    /// <summary>
    /// Verifies updating a tag definition returns 200 with the replacement name and normalized color.
    /// </summary>
    [Fact]
    public async Task UpdateTagDefinition_ReturnsOk_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-update-admin", "Update Club", cancellationToken);

        var created = await CreateTagAsync(client, $"Before-{Guid.CreateVersion7():N}", "#AA0000", cancellationToken);

        var newName = $"After-{Guid.CreateVersion7():N}";
        using var response = await client.PutAsJsonAsync(
            TagEndpoints.UpdateUrl(created.PlayerTagId),
            new UpdateTagDefinitionInput { TagId = created.PlayerTagId, Name = newName, Color = "#00aa00" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<TagDefinitionDto>(cancellationToken);
        updated.ShouldNotBeNull();
        updated.PlayerTagId.ShouldBe(created.PlayerTagId);
        updated.Name.ShouldBe(newName);
        updated.Color.ShouldBe("#00AA00");
        updated.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies a route/body tag identifier mismatch is rejected before any persistence.
    /// </summary>
    [Fact]
    public async Task UpdateTagDefinition_ReturnsBadRequest_ForRouteBodyMismatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-update-mismatch-admin", "Mismatch Club", cancellationToken);

        var created = await CreateTagAsync(client, $"Mismatch-{Guid.CreateVersion7():N}", "#111111", cancellationToken);

        using var response = await client.PutAsJsonAsync(
            TagEndpoints.UpdateUrl(created.PlayerTagId + 1),
            new UpdateTagDefinitionInput { TagId = created.PlayerTagId, Name = "Unchanged", Color = "#222222" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies an invalid update body is rejected with validation ProblemDetails naming both fields,
    /// proving automatic endpoint validation runs before the handler's route/body mismatch check.
    /// </summary>
    [Fact]
    public async Task UpdateTagDefinition_ReturnsValidationProblem_ForInvalidBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-update-invalid-admin", "Invalid Update Club", cancellationToken);
        var created = await CreateTagAsync(client, $"InvalidUpdate-{Guid.CreateVersion7():N}", "#111111", cancellationToken);

        using var response = await client.PutAsJsonAsync(
            TagEndpoints.UpdateUrl(created.PlayerTagId),
            new UpdateTagDefinitionInput { TagId = created.PlayerTagId, Name = "", Color = "not-a-color" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(UpdateTagDefinitionInput.Name));
        errors.ShouldContainKey(nameof(UpdateTagDefinitionInput.Color));
    }

    /// <summary>
    /// Verifies archiving and restoring a tag definition round-trips through the lifecycle states.
    /// </summary>
    [Fact]
    public async Task ArchiveThenRestoreTagDefinition_ReturnsNoContent_AndFlipsLifecycle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-lifecycle-admin", "Lifecycle Club", cancellationToken);

        var created = await CreateTagAsync(client, $"Lifecycle-{Guid.CreateVersion7():N}", "#333333", cancellationToken);

        using (var archive = await client.PostAsync(TagEndpoints.ArchiveUrl(created.PlayerTagId), null, cancellationToken))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var archived = await ListTagsAsync(client, lifecycleStatus: "archived", cancellationToken: cancellationToken);
        archived.ShouldContain(tag => tag.PlayerTagId == created.PlayerTagId);

        using (var restore = await client.PostAsync(TagEndpoints.RestoreUrl(created.PlayerTagId), null, cancellationToken))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var active = await ListTagsAsync(client, lifecycleStatus: "active", cancellationToken: cancellationToken);
        active.ShouldContain(tag => tag.PlayerTagId == created.PlayerTagId);
    }

    /// <summary>
    /// Verifies the management list honors lifecycle and search filters and returns the matching set.
    /// </summary>
    [Fact]
    public async Task GetTagDefinitions_ReturnsFilteredList_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-list-admin", "List Club", cancellationToken);

        var alpha = await CreateTagAsync(client, $"Alpha-{Guid.CreateVersion7():N}", "#AAAAAA", cancellationToken);
        var beta = await CreateTagAsync(client, $"Beta-{Guid.CreateVersion7():N}", "#BBBBBB", cancellationToken);
        var gamma = await CreateTagAsync(client, $"Gamma-{Guid.CreateVersion7():N}", "#CCCCCC", cancellationToken);

        using (await client.PostAsync(TagEndpoints.ArchiveUrl(gamma.PlayerTagId), null, cancellationToken))
        {
        }

        var active = await ListTagsAsync(client, lifecycleStatus: "active", cancellationToken: cancellationToken);
        active.Select(tag => tag.PlayerTagId).ShouldBe([alpha.PlayerTagId, beta.PlayerTagId], ignoreOrder: true);

        var archived = await ListTagsAsync(client, lifecycleStatus: "archived", cancellationToken: cancellationToken);
        archived.Select(tag => tag.PlayerTagId).ShouldBe([gamma.PlayerTagId]);

        var search = await ListTagsAsync(client, search: "alp", cancellationToken: cancellationToken);
        search.Select(tag => tag.PlayerTagId).ShouldBe([alpha.PlayerTagId]);
    }

    /// <summary>
    /// Verifies an invalid lifecycle query value is rejected with correlated validation ProblemDetails,
    /// proving automatic query validation runs before the handler.
    /// </summary>
    [Fact]
    public async Task GetTagDefinitions_ReturnsValidationProblem_ForInvalidLifecycleStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-list-invalid-admin", "Invalid List Club", cancellationToken);

        using var response = await client.GetAsync(
            $"{TagEndpoints.GetListTemplate}?lifecycleStatus=bogus",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        document.ShouldNotBeNull();
        document.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies the evaluator choices read path returns only active tag definitions and excludes
    /// archived ones, even when the caller is also a club administrator.
    /// </summary>
    [Fact]
    public async Task GetTagDefinitionChoices_ReturnsOnlyActive_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-choices-admin", "Choices Club", cancellationToken);

        var activeOne = await CreateTagAsync(client, $"Choice-One-{Guid.CreateVersion7():N}", "#111111", cancellationToken);
        var activeTwo = await CreateTagAsync(client, $"Choice-Two-{Guid.CreateVersion7():N}", "#222222", cancellationToken);
        var archived = await CreateTagAsync(client, $"Choice-Archived-{Guid.CreateVersion7():N}", "#333333", cancellationToken);

        using (await client.PostAsync(TagEndpoints.ArchiveUrl(archived.PlayerTagId), null, cancellationToken))
        {
        }

        using var response = await client.GetAsync(TagEndpoints.GetChoicesUrl(), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var choices = await response.Content.ReadFromJsonAsync<List<TagDefinitionDto>>(cancellationToken);
        choices.ShouldNotBeNull();
        choices.Select(tag => tag.PlayerTagId).ShouldBe([activeOne.PlayerTagId, activeTwo.PlayerTagId], ignoreOrder: true);
        choices.ShouldNotContain(tag => tag.PlayerTagId == archived.PlayerTagId);
    }

    /// <summary>
    /// Verifies a non-administrator club member can read the active-only choices and create tags,
    /// but is denied every administration operation: the <c>RequireClubMember</c> read and create
    /// paths plus the <c>RequireClubAdmin</c> policy across update, archive, restore, and the
    /// management list.
    /// </summary>
    [Fact]
    public async Task NonAdminClubMember_CanReadChoicesAndCreate_ButCannotAdminister()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(adminClient, "tag-member-admin", "Member Club", cancellationToken);
        var adminCreated = await CreateTagAsync(adminClient, $"Admin-{Guid.CreateVersion7():N}", "#111111", cancellationToken);

        var memberEmail = UniqueEmail("tag-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await AssignClubMembershipAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using (var choices = await memberClient.GetAsync(TagEndpoints.GetChoicesUrl(), cancellationToken))
        {
            choices.StatusCode.ShouldBe(HttpStatusCode.OK);
            var rows = await choices.Content.ReadFromJsonAsync<List<TagDefinitionDto>>(cancellationToken);
            rows.ShouldNotBeNull();
            rows.Select(tag => tag.PlayerTagId).ShouldContain(adminCreated.PlayerTagId);
        }

        using (var create = await memberClient.PostAsJsonAsync(
            TagEndpoints.Create,
            new CreateTagDefinitionInput { Name = $"Member-{Guid.CreateVersion7():N}", Color = "#222222" },
            cancellationToken))
        {
            create.StatusCode.ShouldBe(HttpStatusCode.Created);
            var createdTag = await create.Content.ReadFromJsonAsync<TagDefinitionDto>(cancellationToken);
            createdTag.ShouldNotBeNull();
            createdTag.Name.ShouldNotBeNullOrEmpty();
        }

        using (var archive = await memberClient.PostAsync(TagEndpoints.ArchiveUrl(adminCreated.PlayerTagId), null, cancellationToken))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var update = await memberClient.PutAsJsonAsync(
            TagEndpoints.UpdateUrl(adminCreated.PlayerTagId),
            new UpdateTagDefinitionInput
            {
                TagId = adminCreated.PlayerTagId,
                Name = adminCreated.Name,
                Color = "#333333"
            },
            cancellationToken))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var list = await memberClient.GetAsync(TagEndpoints.GetListUrl(), cancellationToken))
        {
            list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Archive as the administrator so the member's restore attempt is denied purely by
        // authorization rather than a 404 for an active definition.
        using (var adminArchive = await adminClient.PostAsync(TagEndpoints.ArchiveUrl(adminCreated.PlayerTagId), null, cancellationToken))
        {
            adminArchive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var restore = await memberClient.PostAsync(TagEndpoints.RestoreUrl(adminCreated.PlayerTagId), null, cancellationToken))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>
    /// Verifies updating another club's tag identifier is non-disclosing (404) and leaves it unchanged.
    /// </summary>
    [Fact]
    public async Task UpdateTagDefinition_ReturnsNotFound_ForCrossTenantTag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        _ = await RegisterClubAdminAsync(clubAClient, "tag-update-cross-a", "Update Cross A", cancellationToken);
        var tagA = await CreateTagAsync(clubAClient, $"CrossA-{Guid.CreateVersion7():N}", "#111111", cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "tag-update-cross-b", "Update Cross B", cancellationToken);

        using var response = await clubBClient.PutAsJsonAsync(
            TagEndpoints.UpdateUrl(tagA.PlayerTagId),
            new UpdateTagDefinitionInput { TagId = tagA.PlayerTagId, Name = "Hijacked", Color = "#222222" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var db = fixture.CreateAdminContext();
        var tag = await db.PlayerTags.SingleAsync(t => t.PlayerTagId == tagA.PlayerTagId, cancellationToken);
        tag.Name.ShouldBe(tagA.Name);
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies archiving another club's tag identifier is non-disclosing (404) and leaves it active.
    /// </summary>
    [Fact]
    public async Task ArchiveTagDefinition_ReturnsNotFound_ForCrossTenantTag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        _ = await RegisterClubAdminAsync(clubAClient, "tag-archive-cross-a", "Archive Cross A", cancellationToken);
        var tagA = await CreateTagAsync(clubAClient, $"CrossA-{Guid.CreateVersion7():N}", "#111111", cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "tag-archive-cross-b", "Archive Cross B", cancellationToken);

        using var response = await clubBClient.PostAsync(TagEndpoints.ArchiveUrl(tagA.PlayerTagId), null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var db = fixture.CreateAdminContext();
        var tag = await db.PlayerTags.SingleAsync(t => t.PlayerTagId == tagA.PlayerTagId, cancellationToken);
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies restoring another club's tag identifier is non-disclosing (404) and leaves it active.
    /// </summary>
    [Fact]
    public async Task RestoreTagDefinition_ReturnsNotFound_ForCrossTenantTag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        _ = await RegisterClubAdminAsync(clubAClient, "tag-restore-cross-a", "Restore Cross A", cancellationToken);
        var tagA = await CreateTagAsync(clubAClient, $"CrossA-{Guid.CreateVersion7():N}", "#111111", cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "tag-restore-cross-b", "Restore Cross B", cancellationToken);

        using var response = await clubBClient.PostAsync(TagEndpoints.RestoreUrl(tagA.PlayerTagId), null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var db = fixture.CreateAdminContext();
        var tag = await db.PlayerTags.SingleAsync(t => t.PlayerTagId == tagA.PlayerTagId, cancellationToken);
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Creates a tag definition through the HTTP API and returns its DTO.
    /// </summary>
    private async Task<TagDefinitionDto> CreateTagAsync(
        HttpClient client,
        string name,
        string color,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            TagEndpoints.Create,
            new CreateTagDefinitionInput { Name = name, Color = color },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TagDefinitionDto>(cancellationToken);
        created.ShouldNotBeNull();
        return created;
    }

    /// <summary>
    /// Retrieves the management list with the supplied filters.
    /// </summary>
    private async Task<List<TagDefinitionDto>> ListTagsAsync(
        HttpClient client,
        string? search = null,
        string? lifecycleStatus = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(TagEndpoints.GetListUrl(search, lifecycleStatus), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TagDefinitionListResult>(cancellationToken);
        result.ShouldNotBeNull();
        return result.Items.ToList();
    }

    /// <summary>
    /// Registers a new user, creates a club for them, and refreshes their membership claims so they
    /// act as that club's administrator.
    /// </summary>
    private async Task<ClubDto> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var email = $"{emailPrefix}-{Guid.CreateVersion7():N}@example.com";
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "Club", "Admin", cancellationToken);

        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent($"{clubName} {Guid.CreateVersion7():N}", "Austin", "TX"),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();

        using var refresh = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Found);

        return club;
    }

    /// <summary>
    /// Updates seeded Identity user names using the admin context.
    /// </summary>
    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = null;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Assigns an existing Identity user to a club so their membership cookie carries the club claim.
    /// </summary>
    private async Task AssignClubMembershipAsync(
        string email,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant(),
            cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes the club membership cookie so the client carries an up-to-date club claim.
    /// </summary>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Builds a unique registration email for a test user.
    /// </summary>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Reads the <c>errors</c> dictionary from a validation ProblemDetails payload.
    /// </summary>
    /// <param name="response">The problem-details response.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The validation error dictionary.</returns>
    private static async Task<Dictionary<string, string[]>> ReadErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var errors = document.RootElement.GetProperty("errors");
        return errors.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }
}
