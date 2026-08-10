using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the campaign tag application add and remove endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignTagApplicationHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for both tag-application mutations.
    /// </summary>
    [Fact]
    public async Task CampaignTagApplicationMutations_ReturnUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var applyResponse = await anonymousClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(1, 1),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var removeResponse = await anonymousClient.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(42),
            cancellationToken);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies authenticated callers without a club receive forbidden responses for both mutations.
    /// </summary>
    [Fact]
    public async Task CampaignTagApplicationMutations_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-apply-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var applyResponse = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(1, 1),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var removeResponse = await client.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(42),
            cancellationToken);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a least-privileged club member can apply a tag and the row is persisted with the member as creator.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReturnsCreated_AndPersistsRow_ForLeastPrivilegedClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The club creator becomes a ClubAdmin by the create-club flow, so a second
        // non-admin user is required to prove ordinary member apply access.
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("tag-apply-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("tag-apply-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var (campaignId, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var applyResponse = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);

        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var success = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);
        success.CampaignTagApplicationId.ShouldBeGreaterThan(0);

        await using var context = fixture.CreateAdminContext();
        var member = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == memberEmail.ToUpperInvariant(), cancellationToken);
        var persisted = await context.CampaignTagApplications
            .SingleOrDefaultAsync(candidate => candidate.CampaignTagApplicationId == success.CampaignTagApplicationId, cancellationToken);
        persisted.ShouldNotBeNull();
        persisted.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
        persisted.PlayerTagId.ShouldBe(tagId);
        persisted.ClubId.ShouldBe(club.ClubId);
        persisted.CreatedById.ShouldBe(member.Id);
    }

    /// <summary>
    /// Verifies an invalid apply body is rejected with validation ProblemDetails naming both fields.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReturnsValidationProblem_ForInvalidBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-apply-validation");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            new { playerCampaignAssignmentId = 0, playerTagId = 0 },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(ApplyCampaignTagApplicationInput.PlayerCampaignAssignmentId));
        errors.ShouldContainKey(nameof(ApplyCampaignTagApplicationInput.PlayerTagId));
    }

    /// <summary>
    /// Verifies applying the same tag twice to the same participation returns a conflict.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReturnsConflict_ForDuplicateApplication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-apply-duplicate");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, email, cancellationToken);

        using var first = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var duplicate = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await duplicate.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("The selected tag has already been applied to this participation.");
    }

    /// <summary>
    /// Verifies applying a tag to a participation in a closed campaign returns a conflict.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReturnsConflict_ForClosedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-apply-closed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, email, cancellationToken, closedCampaign: true);

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Closed campaigns are read-only and cannot accept tag applications.");
    }

    /// <summary>
    /// Verifies applying an archived tag definition returns a conflict.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReturnsConflict_ForArchivedTagDefinition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-apply-archived");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, email, cancellationToken, archivedTag: true);

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Archived tag definitions cannot be applied.");
    }

    /// <summary>
    /// Verifies cross-tenant and nonexistent participation/tag identifiers return non-disclosing not-found responses.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReturnsNotFound_ForCrossTenantAndMissingTargets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var currentClient = fixture.CreateNovaHttpClient();
        var currentEmail = UniqueEmail("tag-apply-current");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(currentClient, currentEmail, Password, cancellationToken);
        await UpdateUserAsync(currentEmail, clubId: null, cancellationToken);
        var currentClub = await CreateClubAsync(currentClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(currentClient, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("tag-apply-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        var (_, otherTagId, otherAssignmentId) = await SeedTagApplicationDataAsync(otherClub.ClubId, otherEmail, cancellationToken);

        // Cross-tenant assignment + tag must be non-disclosing.
        using var crossTenantResponse = await currentClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(otherAssignmentId, otherTagId),
            cancellationToken);
        crossTenantResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nonexistent identifiers must also be non-disclosing.
        using var missingResponse = await currentClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(999_999, 999_999),
            cancellationToken);
        missingResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The club that owns the data must be unaffected by the current tenant's club.
        currentClub.ClubId.ShouldNotBe(otherClub.ClubId);
    }

    /// <summary>
    /// Verifies a non-owner, non-admin club member cannot remove another member's tag application.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReturnsForbidden_ForNonOwnerNonAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("tag-remove-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("tag-remove-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var applyResponse = await ownerClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var applied = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);

        using var otherMemberClient = fixture.CreateNovaHttpClient();
        var otherMemberEmail = UniqueEmail("tag-remove-other-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherMemberClient, otherMemberEmail, Password, cancellationToken);
        await UpdateUserAsync(otherMemberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherMemberClient, cancellationToken);

        using var removeResponse = await otherMemberClient.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(applied.CampaignTagApplicationId),
            cancellationToken);

        removeResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var document = await JsonDocument.ParseAsync(
            await removeResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Only the applying user or a club administrator can remove this tag application.");
    }

    /// <summary>
    /// Verifies the applying owner can remove their own tag application and the row is deleted.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReturnsNoContent_AndDeletesRow_ForOwner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-remove-owner-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, email, cancellationToken);

        using var applyResponse = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var applied = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);

        using var removeResponse = await client.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(applied.CampaignTagApplicationId),
            cancellationToken);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.CampaignTagApplications
            .SingleOrDefaultAsync(candidate => candidate.CampaignTagApplicationId == applied.CampaignTagApplicationId, cancellationToken);
        persisted.ShouldBeNull();
    }

    /// <summary>
    /// Verifies a club administrator can remove a tag application applied by another member.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReturnsNoContent_ForClubAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("tag-remove-admin-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("tag-remove-member-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var applyResponse = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var applied = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);

        using var removeResponse = await adminClient.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(applied.CampaignTagApplicationId),
            cancellationToken);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies removing an already-removed tag application returns a not-found response.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReturnsNotFound_ForAlreadyRemovedApplication()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-remove-stale");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, email, cancellationToken);

        using var applyResponse = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var applied = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);

        using var firstRemove = await client.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(applied.CampaignTagApplicationId),
            cancellationToken);
        firstRemove.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var secondRemove = await client.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(applied.CampaignTagApplicationId),
            cancellationToken);
        secondRemove.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies deleting with a non-positive route value returns validation ProblemDetails.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReturnsValidationProblem_ForNonPositiveId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-remove-zero-id");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.DeleteAsync(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(0),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(RemoveCampaignTagApplicationInput.CampaignTagApplicationId));
    }

    /// <summary>
    /// Verifies a successful apply is reflected in the participant detail payload the tag drawer consumes.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_IsReflected_InParticipantDetailAppliedTags()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("tag-refresh-story");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, tagId, assignmentId) = await SeedTagApplicationDataAsync(club.ClubId, email, cancellationToken);

        using var applyResponse = await client.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            ValidApplyInput(assignmentId, tagId),
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var applied = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);

        using var detailResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.AppliedTags.ShouldContain(
            tag => tag.CampaignTagApplicationId == applied.CampaignTagApplicationId
                && tag.PlayerTagId == tagId);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    private static ApplyCampaignTagApplicationInput ValidApplyInput(long assignmentId, long tagId)
        => new() { PlayerCampaignAssignmentId = assignmentId, PlayerTagId = tagId };

    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds an active season, campaign, player, tag, and participation for the given club.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="closedCampaign">Whether the campaign should be seeded as closed.</param>
    /// <param name="archivedTag">Whether the tag definition should be seeded as archived.</param>
    /// <returns>The campaign, tag, and participation identifiers.</returns>
    private async Task<(long CampaignId, long TagId, long AssignmentId)> SeedTagApplicationDataAsync(
        long clubId,
        string email,
        CancellationToken cancellationToken,
        bool closedCampaign = false,
        bool archivedTag = false)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity { Name = $"Tag App Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity
        {
            Name = $"Tag App Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = closedCampaign ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closedCampaign ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ClosedById = closedCampaign ? user.Id : null,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var player = new PlayerEntity
        {
            FirstName = "Tag",
            LastName = $"Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var playerTag = new PlayerTagEntity
        {
            Name = $"Tag {suffix}",
            Color = "#00CC00",
            LifecycleStatus = archivedTag ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archivedTag ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ArchivedById = archivedTag ? user.Id : null,
            ClubId = clubId,
            CreatedById = user.Id
        };

        context.AddRange(season, campaign, player, playerTag);
        await context.SaveChangesAsync(cancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = user.Id,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7
        };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        return (campaign.CampaignId, playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId);
    }

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
