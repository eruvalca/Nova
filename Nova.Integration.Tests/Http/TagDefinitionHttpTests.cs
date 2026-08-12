using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Tags;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the club-scoped tag-definition API: authorization boundaries,
/// normalized uniqueness, lifecycle transitions, and response-contract behavior.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TagDefinitionHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies a club administrator can create a tag definition and that the row is persisted with a
    /// normalized (uppercase) name key, and that the 201 response has no <c>Location</c> header.
    /// </summary>
    [Fact]
    public async Task CreateTag_ReturnsCreatedWithId_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-create-admin", "Tag Create Club", cancellationToken);
        var tagName = $"Striker-{Guid.CreateVersion7():N}";

        using var response = await client.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = tagName, Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldBeNull();

        var created = await response.Content.ReadFromJsonAsync<TagDefinitionMutationSuccess>(cancellationToken);
        created.ShouldNotBeNull();
        created.TagDefinitionId.ShouldBeGreaterThan(0);

        await using var verify = fixture.CreateAdminContext();
        var row = await verify.PlayerTags
            .SingleAsync(tag => tag.PlayerTagId == created.TagDefinitionId, cancellationToken);
        row.ClubId.ShouldBe(club.ClubId);
        row.Name.ShouldBe(tagName);
        row.NormalizedName.ShouldBe(tagName.ToUpperInvariant());
        row.Color.ShouldBe("#FF5733");
        row.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies the create endpoint rejects a tag definition with an invalid color format.
    /// </summary>
    [Fact]
    public async Task CreateTag_ReturnsValidationProblem_ForInvalidColor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-create-invalid-admin", "Tag Invalid Club", cancellationToken);

        using var response = await client.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = $"Bad Color-{Guid.CreateVersion7():N}", Color = "red" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies the create endpoint rejects a duplicate name case-insensitively via the normalized key.
    /// </summary>
    [Fact]
    public async Task CreateTag_ReturnsConflict_ForDuplicateNameCaseInsensitive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-create-dup-admin", "Tag Dup Club", cancellationToken);
        var tagName = $"Duplicate-{Guid.CreateVersion7():N}";

        using (var first = await client.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = tagName, Color = "#FF5733" },
            cancellationToken))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var second = await client.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = tagName.ToLowerInvariant(), Color = "#112233" },
            cancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var count = await verify.PlayerTags
            .CountAsync(tag => tag.ClubId == club.ClubId && tag.NormalizedName == tagName.ToUpperInvariant(), cancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a non-administrator club member cannot create a tag definition.
    /// </summary>
    [Fact]
    public async Task CreateTag_ReturnsForbidden_ForNonAdminClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var club = await CreateAdminAndMemberAsync(
            adminClient, memberClient, "tag-create-member", "Tag Member Club", cancellationToken);

        using var response = await memberClient.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = $"Member Tag-{Guid.CreateVersion7():N}", Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await using var verify = fixture.CreateAdminContext();
        var count = await verify.PlayerTags.CountAsync(tag => tag.ClubId == club.ClubId, cancellationToken);
        count.ShouldBe(0);
    }

    /// <summary>
    /// Verifies the create endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task CreateTag_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = "Anonymous Tag", Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a club administrator can update a tag definition and the normalized key is refreshed.
    /// </summary>
    [Fact]
    public async Task UpdateTag_ReturnsOk_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-update-admin", "Tag Update Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Original Name", "#FF5733", cancellationToken);
        var newName = $"Renamed-{Guid.CreateVersion7():N}";

        using var response = await client.PutAsJsonAsync(
            TagDefinitionEndpoints.UpdateUrl(tagId),
            new UpdateTagDefinitionInput { TagDefinitionId = tagId, Name = newName, Color = "#00AA11" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TagDefinitionMutationSuccess>(cancellationToken);
        updated.ShouldNotBeNull();
        updated.TagDefinitionId.ShouldBe(tagId);

        await using var verify = fixture.CreateAdminContext();
        var row = await verify.PlayerTags
            .SingleAsync(tag => tag.PlayerTagId == tagId && tag.ClubId == club.ClubId, cancellationToken);
        row.Name.ShouldBe(newName);
        row.NormalizedName.ShouldBe(newName.ToUpperInvariant());
        row.Color.ShouldBe("#00AA11");
    }

    /// <summary>
    /// Verifies the update endpoint rejects a request whose route identifier does not match the body.
    /// </summary>
    [Fact]
    public async Task UpdateTag_ReturnsBadRequest_WhenRouteAndBodyIdsDiffer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-update-mismatch-admin", "Tag Mismatch Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Mismatch Tag", "#FF5733", cancellationToken);

        using var response = await client.PutAsJsonAsync(
            TagDefinitionEndpoints.UpdateUrl(tagId),
            new UpdateTagDefinitionInput { TagDefinitionId = tagId + 1, Name = "Mismatch Tag", Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies updating a tag definition from another club is reported as not found.
    /// </summary>
    [Fact]
    public async Task UpdateTag_ReturnsNotFound_ForWrongClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubOneClient = fixture.CreateNovaHttpClient();
        using var clubTwoClient = fixture.CreateNovaHttpClient();

        var clubOne = await RegisterClubAdminAsync(clubOneClient, "tag-update-wrong-admin", "Tag Wrong One", cancellationToken);
        await RegisterClubAdminAsync(clubTwoClient, "tag-update-wrong-admin2", "Tag Wrong Two", cancellationToken);

        var tagId = await CreateTagAsync(clubOneClient, $"Club One Tag-{Guid.CreateVersion7():N}", "#FF5733", cancellationToken);

        using var response = await clubTwoClient.PutAsJsonAsync(
            TagDefinitionEndpoints.UpdateUrl(tagId),
            new UpdateTagDefinitionInput { TagDefinitionId = tagId, Name = "Hijacked", Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var verify = fixture.CreateAdminContext();
        var row = await verify.PlayerTags.SingleAsync(tag => tag.PlayerTagId == tagId, cancellationToken);
        row.ClubId.ShouldBe(clubOne.ClubId);
        row.Name.ShouldNotBe("Hijacked");
    }

    /// <summary>
    /// Verifies archived tag definitions cannot be edited.
    /// </summary>
    [Fact]
    public async Task UpdateTag_ReturnsConflict_ForArchivedDefinition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-update-archived-admin", "Tag Archived Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Archived Edit", "#FF5733", cancellationToken);
        await ArchiveTagAsync(client, tagId, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            TagDefinitionEndpoints.UpdateUrl(tagId),
            new UpdateTagDefinitionInput { TagDefinitionId = tagId, Name = "Archived Edit", Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies updating a tag to an existing name is rejected as a conflict.
    /// </summary>
    [Fact]
    public async Task UpdateTag_ReturnsConflict_ForDuplicateName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-update-dup-admin", "Tag Update Dup Club", cancellationToken);
        var firstTag = $"First-{Guid.CreateVersion7():N}";
        var secondTag = $"Second-{Guid.CreateVersion7():N}";
        var firstId = await CreateTagAsync(client, firstTag, "#FF5733", cancellationToken);
        var secondId = await CreateTagAsync(client, secondTag, "#112233", cancellationToken);

        using var response = await client.PutAsJsonAsync(
            TagDefinitionEndpoints.UpdateUrl(secondId),
            new UpdateTagDefinitionInput { TagDefinitionId = secondId, Name = firstTag, Color = "#112233" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var count = await verify.PlayerTags
            .CountAsync(tag => tag.ClubId == club.ClubId && tag.NormalizedName == firstTag.ToUpperInvariant(), cancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a non-administrator club member cannot update a tag definition.
    /// </summary>
    [Fact]
    public async Task UpdateTag_ReturnsForbidden_ForNonAdminClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        await CreateAdminAndMemberAsync(adminClient, memberClient, "tag-update-member", "Tag Update Member Club", cancellationToken);
        var tagId = await CreateTagAsync(adminClient, "Member No Edit", "#FF5733", cancellationToken);

        using var response = await memberClient.PutAsJsonAsync(
            TagDefinitionEndpoints.UpdateUrl(tagId),
            new UpdateTagDefinitionInput { TagDefinitionId = tagId, Name = "Member No Edit", Color = "#FF5733" },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies archiving a tag definition flips its lifecycle status and stamps archive provenance.
    /// </summary>
    [Fact]
    public async Task ArchiveTag_ReturnsOk_AndArchivesRow_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-archive-admin", "Tag Archive Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Archive Me", "#FF5733", cancellationToken);

        using var response = await client.PostAsync(TagDefinitionEndpoints.ArchiveUrl(tagId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var success = await response.Content.ReadFromJsonAsync<TagDefinitionMutationSuccess>(cancellationToken);
        success.ShouldNotBeNull();
        success.TagDefinitionId.ShouldBe(tagId);

        await using var verify = fixture.CreateAdminContext();
        var row = await verify.PlayerTags
            .SingleAsync(tag => tag.PlayerTagId == tagId && tag.ClubId == club.ClubId, cancellationToken);
        row.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        row.ArchivedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Verifies archiving an already-archived tag definition is a conflict.
    /// </summary>
    [Fact]
    public async Task ArchiveTag_ReturnsConflict_ForAlreadyArchived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-archive-conflict-admin", "Tag Archive Conflict Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Archive Twice", "#FF5733", cancellationToken);
        await ArchiveTagAsync(client, tagId, cancellationToken);

        using var response = await client.PostAsync(TagDefinitionEndpoints.ArchiveUrl(tagId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies restoring an archived tag definition clears archive provenance.
    /// </summary>
    [Fact]
    public async Task RestoreTag_ReturnsOk_AndRestoresRow_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "tag-restore-admin", "Tag Restore Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Restore Me", "#FF5733", cancellationToken);
        await ArchiveTagAsync(client, tagId, cancellationToken);

        using var response = await client.PostAsync(TagDefinitionEndpoints.RestoreUrl(tagId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var success = await response.Content.ReadFromJsonAsync<TagDefinitionMutationSuccess>(cancellationToken);
        success.ShouldNotBeNull();
        success.TagDefinitionId.ShouldBe(tagId);

        await using var verify = fixture.CreateAdminContext();
        var row = await verify.PlayerTags
            .SingleAsync(tag => tag.PlayerTagId == tagId && tag.ClubId == club.ClubId, cancellationToken);
        row.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        row.ArchivedAt.ShouldBeNull();
    }

    /// <summary>
    /// Verifies restoring an already-active tag definition is a conflict.
    /// </summary>
    [Fact]
    public async Task RestoreTag_ReturnsConflict_ForAlreadyActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-restore-conflict-admin", "Tag Restore Conflict Club", cancellationToken);
        var tagId = await CreateTagAsync(client, "Restore Twice", "#FF5733", cancellationToken);

        using var response = await client.PostAsync(TagDefinitionEndpoints.RestoreUrl(tagId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies a non-administrator club member cannot archive a tag definition.
    /// </summary>
    [Fact]
    public async Task ArchiveTag_ReturnsForbidden_ForNonAdminClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        await CreateAdminAndMemberAsync(adminClient, memberClient, "tag-archive-member", "Tag Archive Member Club", cancellationToken);
        var tagId = await CreateTagAsync(adminClient, "Member No Archive", "#FF5733", cancellationToken);

        using var response = await memberClient.PostAsync(TagDefinitionEndpoints.ArchiveUrl(tagId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies archiving a missing tag definition is reported as not found.
    /// </summary>
    [Fact]
    public async Task ArchiveTag_ReturnsNotFound_ForMissingDefinition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-archive-missing-admin", "Tag Archive Missing Club", cancellationToken);

        using var response = await client.PostAsync(TagDefinitionEndpoints.ArchiveUrl(999_999_999), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies active definitions are visible to a non-administrator club member.
    /// </summary>
    [Fact]
    public async Task GetActiveTags_ReturnsActiveTags_ForNonAdminClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        await CreateAdminAndMemberAsync(adminClient, memberClient, "tag-active-member", "Tag Active Member Club", cancellationToken);
        var activeName = $"Active-{Guid.CreateVersion7():N}";
        var archivedName = $"Retired-{Guid.CreateVersion7():N}";
        var activeId = await CreateTagAsync(adminClient, activeName, "#FF5733", cancellationToken);
        var archivedId = await CreateTagAsync(adminClient, archivedName, "#112233", cancellationToken);
        await ArchiveTagAsync(adminClient, archivedId, cancellationToken);

        using var response = await memberClient.GetAsync(TagDefinitionEndpoints.ListActive, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<TagDefinitionSummary>>(cancellationToken);
        tags.ShouldNotBeNull();
        tags.Select(tag => tag.TagDefinitionId).ShouldContain(activeId);
        tags.Select(tag => tag.TagDefinitionId).ShouldNotContain(archivedId);
        tags.ShouldAllBe(tag => !string.IsNullOrEmpty(tag.Name) && !string.IsNullOrEmpty(tag.Color));
    }

    /// <summary>
    /// Verifies the active list endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task GetActiveTags_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.GetAsync(TagDefinitionEndpoints.ListActive, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an out-of-range limit on the active list is rejected as a validation problem.
    /// </summary>
    [Fact]
    public async Task GetActiveTags_ReturnsValidationProblem_ForInvalidLimit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "tag-active-limit-admin", "Tag Active Limit Club", cancellationToken);

        using var response = await client.GetAsync($"{TagDefinitionEndpoints.ListActive}?limit=101", cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies a club administrator sees archived definitions and a non-admin member is forbidden.
    /// </summary>
    [Fact]
    public async Task GetArchivedTags_ReturnsArchivedTags_ForClubAdmin_AndForbiddenForMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        await CreateAdminAndMemberAsync(adminClient, memberClient, "tag-archived-member", "Tag Archived Member Club", cancellationToken);
        var tagId = await CreateTagAsync(adminClient, $"Retire-{Guid.CreateVersion7():N}", "#FF5733", cancellationToken);
        await ArchiveTagAsync(adminClient, tagId, cancellationToken);

        using var adminResponse = await adminClient.GetAsync(TagDefinitionEndpoints.ListArchived, cancellationToken);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = await adminResponse.Content.ReadFromJsonAsync<List<TagDefinitionSummary>>(cancellationToken);
        archived.ShouldNotBeNull();
        archived.Select(tag => tag.TagDefinitionId).ShouldContain(tagId);

        using var memberResponse = await memberClient.GetAsync(TagDefinitionEndpoints.ListArchived, cancellationToken);
        memberResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Registers a new user, creates a club for them, and refreshes their membership claims so they
    /// act as that club's administrator.
    /// </summary>
    /// <param name="client">The caller client that will hold the authentication cookie.</param>
    /// <param name="emailPrefix">A human-readable scenario prefix for the generated email.</param>
    /// <param name="clubName">The club name to create.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private async Task<ClubDto> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var email = $"{emailPrefix}-{Guid.CreateVersion7():N}@example.com";
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserNamesAsync(email, "Club", "Admin", cancellationToken);

        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput
        {
            Name = $"{clubName} {Guid.CreateVersion7():N}",
            City = "Austin",
            State = "TX"
        }, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();

        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        return club;
    }

    /// <summary>
    /// Registers a club administrator and a second standard member in the same club using the
    /// least-privilege pattern (membership assigned directly rather than through the join-request flow).
    /// </summary>
    /// <param name="adminClient">The caller client for the club administrator.</param>
    /// <param name="memberClient">The caller client for the standard member.</param>
    /// <param name="emailPrefix">A human-readable scenario prefix for the generated emails.</param>
    /// <param name="clubName">The club name to create.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private async Task<ClubDto> CreateAdminAndMemberAsync(
        HttpClient adminClient,
        HttpClient memberClient,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var club = await RegisterClubAdminAsync(adminClient, $"{emailPrefix}-admin", clubName, cancellationToken);

        var memberEmail = $"{emailPrefix}-member-{Guid.CreateVersion7():N}@example.com";
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserClubAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        return club;
    }

    /// <summary>
    /// Creates a tag definition through the HTTP API and returns its identifier.
    /// </summary>
    /// <param name="client">An authenticated club-administrator client.</param>
    /// <param name="name">The tag definition name.</param>
    /// <param name="color">The tag color in #RRGGBB format.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created tag-definition identifier.</returns>
    private static async Task<long> CreateTagAsync(
        HttpClient client,
        string name,
        string color,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            TagDefinitionEndpoints.Create,
            new CreateTagDefinitionInput { Name = name, Color = color },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var success = await response.Content.ReadFromJsonAsync<TagDefinitionMutationSuccess>(cancellationToken);
        success.ShouldNotBeNull();
        return success.TagDefinitionId;
    }

    /// <summary>
    /// Archives a tag definition through the HTTP API.
    /// </summary>
    /// <param name="client">An authenticated club-administrator client.</param>
    /// <param name="tagDefinitionId">The tag-definition identifier to archive.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the archive request finishes.</returns>
    private static async Task ArchiveTagAsync(HttpClient client, long tagDefinitionId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(TagDefinitionEndpoints.ArchiveUrl(tagDefinitionId), null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Updates seeded Identity user names using the admin context.
    /// </summary>
    /// <param name="email">The user email to update.</param>
    /// <param name="firstName">The first name.</param>
    /// <param name="lastName">The last name.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    private async Task UpdateUserNamesAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant(),
            cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Assigns a user to a club directly using the admin context, bypassing the join-request flow.
    /// </summary>
    /// <param name="email">The user email to update.</param>
    /// <param name="clubId">The club identifier to assign.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    private async Task UpdateUserClubAsync(string email, long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant(),
            cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes the club-membership claims cookie so the client acts as an approved club member.
    /// </summary>
    /// <param name="client">The caller client whose membership claims should be refreshed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the claims are refreshed.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }
}
