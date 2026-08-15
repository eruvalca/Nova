using System.Net;
using System.Net.Http.Json;
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
/// End-to-end HTTP coverage for the campaign placement roster and summary endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

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
        var seeded = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

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
        var seeded = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

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
        var seeded = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

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
        var seeded = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

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
        var seeded = await SeedPlacementDataAsync(otherClub.ClubId, otherEmail, cancellationToken);

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
        var seeded = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

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

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

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
    /// Seeds a club campaign with five mixed-outcome participations and one team.
    /// </summary>
    /// <param name="clubId">The club identifier to seed into.</param>
    /// <param name="email">The seeding user's email, used to resolve the user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the seeding.</param>
    /// <returns>The seeded campaign, team, and assignment identifiers.</returns>
    private async Task<PlacementSeedData> SeedPlacementDataAsync(long clubId, string email, CancellationToken cancellationToken)
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
}
