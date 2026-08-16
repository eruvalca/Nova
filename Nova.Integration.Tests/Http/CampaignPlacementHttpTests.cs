using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the campaign placement update, roster, and summary endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for placement updates.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var response = await anonymousClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(1),
            new UpdateCampaignPlacementInput(1, PlacementOutcome.Assigned, 2, Guid.NewGuid()),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an authenticated club member without administrator rights receives a forbidden response.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-member-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("placement-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await memberClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club administrator can update a placement and receives a replacement concurrency token.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsOk_WithReplacementToken_AndPersistsPlacement_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var success = await response.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
        success.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        success.ConcurrencyToken.ShouldNotBe(token);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(success.ConcurrencyToken);
    }

    /// <summary>
    /// Verifies a route/body identifier mismatch is rejected with a bad-request problem.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsBadRequest_WhenRouteAndBodyAssignmentIdsDiffer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-mismatch");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId + 1, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("The player campaign assignment identifier in the route does not match the request body.");
    }

    /// <summary>
    /// Verifies an Assigned outcome without a team is rejected by endpoint validation.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsValidationProblem_WhenAssignedOutcomeLacksTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-no-team");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies an unparseable JSON payload is rejected before the handler runs. The framework's
    /// body binding throws BadHttpRequestException, which the API exception-handler pipeline
    /// surfaces as a 500 server error in the current foundation — the placement endpoint itself
    /// never executes. The pinned 500 is tracked as known foundation debt in
    /// https://github.com/eruvalca/Nova/issues/91; this assertion stays until the pipeline maps
    /// BadHttpRequestException to 400 ProblemDetails.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsServerError_ForUnparseableJsonBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-malformed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, _) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new StringContent("{ not json", Encoding.UTF8, "application/json"),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    /// <summary>
    /// Verifies another club's participation is hidden behind a not-found response.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsNotFound_ForCrossTenantAssignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("placement-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(ownerClub.ClubId, ownerEmail, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("placement-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);

        using var response = await otherClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies a stale concurrency token conflicts and never overwrites the winning update.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_AndPreservesWinner_WhenTokenIsStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-stale");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        var newerToken = Guid.NewGuid();
        await using (var update = fixture.CreateAdminContext())
        {
            var participation = await update.PlayerCampaignAssignments
                .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
            participation.PlacementOutcome = PlacementOutcome.Withdrawn;
            participation.ConcurrencyToken = newerToken;
            await update.SaveChangesAsync(cancellationToken);
        }

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("The placement was changed by another user. Reload it and try again.");

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        persisted.TeamId.ShouldBeNull();
        persisted.ConcurrencyToken.ShouldBe(newerToken);
    }

    /// <summary>
    /// Verifies a closed campaign rejects placement mutations with a conflict.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_ForClosedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-closed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(
            club.ClubId, email, cancellationToken, closedCampaign: true);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Closed campaigns are read-only and cannot accept placement changes.");
    }

    /// <summary>
    /// Verifies an archived player rejects placement mutations with a conflict.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_ForArchivedPlayer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-archived-player");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(
            club.ClubId, email, cancellationToken, archivedPlayer: true);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Archived players cannot receive new placement decisions.");
    }

    /// <summary>
    /// Verifies an ineligible team is rejected with a validation problem naming the team field.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsValidationProblem_ForIneligibleTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-ineligible");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(
            club.ClubId, email, cancellationToken, teamGraduationYear: 2031);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies a current-club member receives a bounded, ordered placement page with the
    /// persisted fields needed for a placement update.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsOrderedPage_ForCurrentClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-roster");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(5);
        roster.Items.Count.ShouldBe(5);

        roster.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe(
            [
                seeded.ZoeAdamsAssignedId,
                seeded.ZoeAdamsUndecidedId,
                seeded.AmyBrownId,
                seeded.CaraChenId,
                seeded.DrewDavisId
            ]);

        var assigned = roster.Items[0];
        assigned.PlayerId.ShouldBeGreaterThan(0);
        assigned.DisplayName.ShouldBe("Zoe Adams");
        assigned.GraduationYear.ShouldBe(2028);
        assigned.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        assigned.Team.ShouldNotBeNull();
        assigned.Team!.TeamId.ShouldBe(seeded.TeamId);
        assigned.ConcurrencyToken.ShouldNotBe(Guid.Empty);

        roster.Items.ShouldAllBe(item => item.ConcurrencyToken != Guid.Empty);
        roster.Items.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.DisplayName));
    }

    /// <summary>
    /// Verifies the graduation-year and unresolved-only filters compose and bound the page.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ComposesGraduationYearAndUnresolvedOnlyFilters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-roster-filters");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var byYearResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                GraduationYear = 2028,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);
        byYearResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var byYear = await byYearResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        byYear.ShouldNotBeNull();
        byYear.TotalCount.ShouldBe(2);
        byYear.Items.ShouldAllBe(item => item.GraduationYear == 2028);

        using var unresolvedResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                UnresolvedOnly = true,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);
        unresolvedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unresolved = await unresolvedResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        unresolved.ShouldNotBeNull();
        unresolved.TotalCount.ShouldBe(2);
        unresolved.Items.ShouldAllBe(item => item.PlacementOutcome == PlacementOutcome.Undecided);

        using var composedResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                GraduationYear = 2028,
                UnresolvedOnly = true,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);
        composedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var composed = await composedResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        composed.ShouldNotBeNull();
        composed.TotalCount.ShouldBe(1);
        composed.Items.Single().PlayerCampaignAssignmentId.ShouldBe(seeded.AmyBrownId);
    }

    /// <summary>
    /// Verifies paging slices the deterministic ordering with stable total counts.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_PagesDeterministicallyAcrossPages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-roster-paging");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var firstResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 1,
                PageSize = 2
            }),
            cancellationToken);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        first.ShouldNotBeNull();
        first.TotalCount.ShouldBe(5);
        first.Items.Count.ShouldBe(2);
        first.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([seeded.ZoeAdamsAssignedId, seeded.ZoeAdamsUndecidedId]);

        using var secondResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 2,
                PageSize = 2
            }),
            cancellationToken);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        second.ShouldNotBeNull();
        second.TotalCount.ShouldBe(5);
        second.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([seeded.AmyBrownId, seeded.CaraChenId]);

        using var thirdResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 3,
                PageSize = 2
            }),
            cancellationToken);
        thirdResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var third = await thirdResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        third.ShouldNotBeNull();
        third.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([seeded.DrewDavisId]);
    }

    /// <summary>
    /// Verifies the summary reports accurate whole-campaign counts independent of roster filters.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummary_ReturnsWholeCampaignCounts_IndependentOfRosterFilters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-summary");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<CampaignPlacementSummaryDto>(cancellationToken);
        summary.ShouldNotBeNull();
        summary.AssignedCount.ShouldBe(1);
        summary.NotSelectedCount.ShouldBe(1);
        summary.WithdrawnCount.ShouldBe(1);
        summary.UndecidedCount.ShouldBe(2);
        summary.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Verifies a least-privileged club member without the ClubAdmin role can read both placement routes.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnPayload_ForLeastPrivilegedClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The club creator becomes a ClubAdmin, so a second user proves ordinary member access.
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("placement-least-privileged");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, memberEmail, cancellationToken);

        using var rosterResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = seeded.CampaignId }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await rosterResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(5);

        using var summaryResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<CampaignPlacementSummaryDto>(cancellationToken);
        summary.ShouldNotBeNull();
        summary.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Verifies anonymous callers receive unauthorized responses for both placement routes.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var rosterResponse = await anonymousClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = 1 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var summaryResponse = await anonymousClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(1),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies authenticated callers without a club receive forbidden responses for both routes.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var rosterResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = 1 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var summaryResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(1),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies cross-tenant campaign reads are rejected with non-disclosing not-found ProblemDetails.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnNotFound_ForCrossTenantCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var currentClient = fixture.CreateNovaHttpClient();
        var currentEmail = UniqueEmail("placement-cross-tenant-current");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(currentClient, currentEmail, Password, cancellationToken);
        await UpdateUserAsync(currentEmail, clubId: null, cancellationToken);
        await CreateClubAsync(currentClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(currentClient, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("placement-cross-tenant-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(otherClub.ClubId, otherEmail, cancellationToken);

        using var rosterResponse = await currentClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = seeded.CampaignId }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var rosterDocument = await JsonDocument.ParseAsync(
            await rosterResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        rosterDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        rosterDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();

        using var summaryResponse = await currentClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var summaryDocument = await JsonDocument.ParseAsync(
            await summaryResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        summaryDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        summaryDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies invalid explicit query values are rejected with validation ProblemDetails before the handler runs.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsValidationProblem_ForInvalidExplicitQueryValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-invalid-query");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
            "/api/campaigns/1/placements?graduationYear=0&pageSize=101",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies an overflowing page offset is rejected by endpoint input validation.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsValidationProblem_ForOverflowingPageOffset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-overflowing-page-offset");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
            $"/api/campaigns/1/placements?page={int.MaxValue}&pageSize=2",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("errors")
            .TryGetProperty(nameof(GetCampaignPlacementRosterInput.Page), out _)
            .ShouldBeTrue();
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies default paging applies when both query values are omitted at the endpoint boundary.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_AppliesDefaultPaging_WhenPageAndPageSizeAreOmitted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-default-paging");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            $"/api/campaigns/{seeded.CampaignId}/placements",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.Page.ShouldBe(GetCampaignPlacementRosterInput.DefaultPage);
        roster.PageSize.ShouldBe(GetCampaignPlacementRosterInput.DefaultPageSize);
        roster.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Seeds a club, season, campaign, player, participation, and team through the admin context.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="closedCampaign">Whether the campaign should be seeded as closed.</param>
    /// <param name="archivedPlayer">Whether the player should be seeded as archived.</param>
    /// <param name="teamGraduationYear">The team graduation-year cutoff.</param>
    /// <returns>The seeded assignment id, team id, and assignment concurrency token.</returns>
    private async Task<(long AssignmentId, long TeamId, Guid ConcurrencyToken)> SeedPlacementDataAsync(
        long clubId,
        string adminEmail,
        CancellationToken cancellationToken,
        bool closedCampaign = false,
        bool archivedPlayer = false,
        int teamGraduationYear = 2029)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity
        {
            Name = $"Placement Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = user.Id
        };
        var campaign = new CampaignEntity
        {
            Name = $"Placement Campaign {suffix}",
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
            FirstName = "Place",
            LastName = $"Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = archivedPlayer ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archivedPlayer ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ArchivedById = archivedPlayer ? user.Id : null,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var team = new TeamEntity
        {
            Name = $"Team {suffix}",
            GraduationYear = teamGraduationYear,
            ClubId = clubId,
            CreatedById = user.Id
        };

        context.AddRange(season, campaign, player, team);
        await context.SaveChangesAsync(cancellationToken);

        var concurrencyToken = Guid.NewGuid();
        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = user.Id,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7,
            ConcurrencyToken = concurrencyToken
        };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        return (assignment.PlayerCampaignAssignmentId, team.TeamId, concurrencyToken);
    }

    /// <summary>
    /// Seeds a club campaign with five mixed-outcome participations and one team.
    /// </summary>
    /// <param name="clubId">The club identifier to seed into.</param>
    /// <param name="email">The seeding user's email, used to resolve the user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the seeding.</param>
    /// <returns>The seeded campaign, team, and assignment identifiers.</returns>
    private async Task<PlacementSeedData> SeedPlacementQueryDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);

        var season = new SeasonEntity { Name = "Placement Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity { Name = "Placement Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
        var team = new TeamEntity { Name = "Alpha", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };

        var zoeAdamsAssigned = new PlayerEntity { FirstName = "Zoe", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var zoeAdamsUndecided = new PlayerEntity { FirstName = "Zoe", LastName = "Adams", DateOfBirth = new DateOnly(2010, 2, 2), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var amyBrown = new PlayerEntity { FirstName = "Amy", LastName = "Brown", DateOfBirth = new DateOnly(2011, 3, 3), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var caraChen = new PlayerEntity { FirstName = "Cara", LastName = "Chen", DateOfBirth = new DateOnly(2011, 4, 4), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var drewDavis = new PlayerEntity { FirstName = "Drew", LastName = "Davis", DateOfBirth = new DateOnly(2012, 5, 5), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };

        context.AddRange(season, campaign, team, zoeAdamsAssigned, zoeAdamsUndecided, amyBrown, caraChen, drewDavis);
        await context.SaveChangesAsync(cancellationToken);

        var assignment1 = new PlayerCampaignAssignmentEntity { PlayerId = zoeAdamsAssigned.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Assigned, TeamId = team.TeamId };
        var assignment2 = new PlayerCampaignAssignmentEntity { PlayerId = zoeAdamsUndecided.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Undecided };
        var assignment3 = new PlayerCampaignAssignmentEntity { PlayerId = amyBrown.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Undecided };
        var assignment4 = new PlayerCampaignAssignmentEntity { PlayerId = caraChen.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.NotSelected };
        var assignment5 = new PlayerCampaignAssignmentEntity { PlayerId = drewDavis.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Withdrawn };

        context.AddRange(assignment1, assignment2, assignment3, assignment4, assignment5);
        await context.SaveChangesAsync(cancellationToken);

        return new PlacementSeedData(
            campaign.CampaignId,
            team.TeamId,
            assignment1.PlayerCampaignAssignmentId,
            assignment2.PlayerCampaignAssignmentId,
            assignment3.PlayerCampaignAssignmentId,
            assignment4.PlayerCampaignAssignmentId,
            assignment5.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Identifiers produced by the placement seeding helper.
    /// </summary>
    private sealed record PlacementSeedData(
        long CampaignId,
        long TeamId,
        long ZoeAdamsAssignedId,
        long ZoeAdamsUndecidedId,
        long AmyBrownId,
        long CaraChenId,
        long DrewDavisId);

    /// <summary>
    /// Updates a user's club membership through the fixture admin context.
    /// </summary>
    /// <param name="email">The user's e-mail address.</param>
    /// <param name="clubId">The club identifier to assign, or <see langword="null"/> to clear membership.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
        => await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken);

    /// <summary>
    /// Generates a unique e-mail address for a test user.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique e-mail address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Creates a club through the real HTTP endpoint and returns the club DTO.
    /// </summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club.</returns>
    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>
    /// Completes the club-membership flow so the client carries the refreshed membership cookie.
    /// </summary>
    /// <param name="client">The HTTP client whose membership cookie should be refreshed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
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
