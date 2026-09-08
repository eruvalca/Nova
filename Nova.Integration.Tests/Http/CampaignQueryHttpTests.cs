using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies campaign query authorization, validation, serialization, and tenant isolation over HTTP.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignQueryHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides the password used by registered integration-test users.</summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies anonymous rejection, member reads, and administrator-only creation setup.</summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task GetEndpointsRejectAnonymousAndAllowApprovedMemberAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateNovaHttpClient();
        using (var anonResp = await anonymous.GetAsync(new Uri(CampaignEndpoints.GetCampaignList, UriKind.RelativeOrAbsolute), cancellationToken))
        {
            anonResp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using (var anonResp = await anonymous.GetAsync(new Uri(CampaignEndpoints.GetCreationSetup, UriKind.RelativeOrAbsolute), cancellationToken))
        {
            anonResp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using (var anonResp = await anonymous.GetAsync(new Uri(CampaignEndpoints.GetCampaignDetailUrl(1), UriKind.RelativeOrAbsolute), cancellationToken))
        {
            anonResp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("campaign-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        // Seed a season and campaign
        CampaignEntity campaign;
        CampaignEntity draft;
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var userId = await context.Users.Where(u => u.NormalizedEmail == email.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
#pragma warning restore CA1862
            var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "S", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = userId };
            campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "C", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = userId };
            draft = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Draft C", StartDate = new DateOnly(2026, 7, 1), Status = CampaignStatus.Draft, Season = season, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = userId };
            context.AddRange(season, campaign, draft);
            await context.SaveChangesAsync(cancellationToken);
            var trackedClub = await context.Clubs.SingleAsync(
                candidate => candidate.ClubId == club.ClubId,
                cancellationToken);
            trackedClub.CurrentSeasonId = season.SeasonId;
            await context.SaveChangesAsync(cancellationToken);
        }

        using var resp = await client.GetAsync(new Uri(CampaignEndpoints.GetCampaignList, UriKind.RelativeOrAbsolute), cancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var setupResp = await client.GetAsync(new Uri(CampaignEndpoints.GetCreationSetup, UriKind.RelativeOrAbsolute), cancellationToken);
        setupResp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var adminSetupResp = await adminClient.GetAsync(new Uri(CampaignEndpoints.GetCreationSetup, UriKind.RelativeOrAbsolute), cancellationToken);
        adminSetupResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setup = await adminSetupResp.Content.ReadFromJsonAsync<CampaignCreationSetupResult>(cancellationToken);
        setup.ShouldNotBeNull();
        setup.CurrentSeason.ShouldNotBeNull();
        setup.CurrentSeason.Name.ShouldBe("S");

        using var detailResp = await client.GetAsync(new Uri(CampaignEndpoints.GetCampaignDetailUrl(campaign.CampaignId), UriKind.RelativeOrAbsolute), cancellationToken);
        detailResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResp.Content.ReadFromJsonAsync<CampaignDetailResult>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.CampaignId.ShouldBe(campaign.CampaignId);
        detail.Name.ShouldBe("C");
        detail.Status.ShouldBe(CampaignStatus.Active);
        detail.SeasonName.ShouldBe("S");

        using var memberDraftList = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignListUrl("draft"), UriKind.RelativeOrAbsolute),
            cancellationToken);
        memberDraftList.StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberDrafts = await memberDraftList.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
        memberDrafts.ShouldNotBeNull();
        memberDrafts.TotalCount.ShouldBe(0);

        using var memberDraftDetail = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignDetailUrl(draft.CampaignId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        memberDraftDetail.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var adminDraftList = await adminClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignListUrl("draft"), UriKind.RelativeOrAbsolute),
            cancellationToken);
        adminDraftList.StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminDrafts = await adminDraftList.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
        adminDrafts.ShouldNotBeNull();
        adminDrafts.TotalCount.ShouldBe(1);
        adminDrafts.DraftActivePlayerCount.ShouldBe(0);

        using var adminSecondPage = await adminClient.GetAsync(
new Uri($"{CampaignEndpoints.GetCampaignList}?limit=1&page=2", UriKind.RelativeOrAbsolute), cancellationToken);
        adminSecondPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondPage = await adminSecondPage.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
        secondPage.ShouldNotBeNull();
        secondPage.Page.ShouldBe(2);
        secondPage.Limit.ShouldBe(1);
        secondPage.TotalCount.ShouldBe(2);
        secondPage.CurrentSeasonId.ShouldBe(campaign.SeasonId);
        secondPage.Seasons.Single().Campaigns.Single().CampaignId.ShouldBe(draft.CampaignId);

        using var memberSecondPage = await client.GetAsync(
new Uri($"{CampaignEndpoints.GetCampaignList}?limit=1&page=2", UriKind.RelativeOrAbsolute), cancellationToken);
        memberSecondPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var concealedPage = await memberSecondPage.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
        concealedPage.ShouldNotBeNull();
        concealedPage.TotalCount.ShouldBe(1);
        concealedPage.Seasons.ShouldBeEmpty();
        concealedPage.DraftActivePlayerCount.ShouldBeNull();

        using var invalidPage = await client.GetAsync(
new Uri($"{CampaignEndpoints.GetCampaignList}?page=0", UriKind.RelativeOrAbsolute), cancellationToken);
        invalidPage.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = await invalidPage.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        problem.ShouldNotBeNull();
        problem.RootElement.GetProperty("errors").TryGetProperty("Page", out _).ShouldBeTrue();
    }

    /// <summary>Verifies authenticated callers without a club receive forbidden responses.</summary>
    [Fact]
    public async Task GetEndpointsReturnForbiddenForAuthenticatedUserWithoutClubAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var listResponse = await client.GetAsync(new Uri(CampaignEndpoints.GetCampaignList, UriKind.RelativeOrAbsolute), cancellationToken);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var setupResponse = await client.GetAsync(new Uri(CampaignEndpoints.GetCreationSetup, UriKind.RelativeOrAbsolute), cancellationToken);
        setupResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var detailResponse = await client.GetAsync(new Uri(CampaignEndpoints.GetCampaignDetailUrl(1), UriKind.RelativeOrAbsolute), cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Verifies invalid status binding returns correlated validation ProblemDetails.</summary>
    [Fact]
    public async Task GetCampaignsInvalidStatusReturnsValidationProblemWithTraceIdAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-bad");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        _ = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(new Uri($"{CampaignEndpoints.GetCampaignList}?status=bogus", UriKind.RelativeOrAbsolute), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        doc.ShouldNotBeNull();
        doc.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies an invalid limit independently produces validation ProblemDetails with correlation.
    /// </summary>
    [Fact]
    public async Task GetCampaignsInvalidLimitReturnsValidationProblemWithTraceIdAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-bad-limit");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        _ = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(new Uri($"{CampaignEndpoints.GetCampaignList}?limit=0", UriKind.RelativeOrAbsolute), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        doc.ShouldNotBeNull();
        doc.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    /// <summary>Verifies campaign and setup projections cannot leak data across clubs.</summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task TenantIsolationCannotSeeOtherClubsCampaignsAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("campaign-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var clubA = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("campaign-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, clubId: null, cancellationToken);
        var clubB = await CreateClubAsync(memberClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        // Seed campaigns across both clubs
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var adminUserId = await context.Users.Where(u => u.NormalizedEmail == adminEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
#pragma warning restore CA1862
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var memberUserId = await context.Users.Where(u => u.NormalizedEmail == memberEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
#pragma warning restore CA1862

            var seasonA = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "SA", StartDate = new DateOnly(2026, 1, 1), ClubId = clubA.ClubId, CreatedById = adminUserId };
            var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "SB", StartDate = new DateOnly(2026, 1, 1), ClubId = clubB.ClubId, CreatedById = memberUserId };
            var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "CA", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonA, SeasonId = seasonA.SeasonId, ClubId = clubA.ClubId, CreatedById = adminUserId };
            var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "CB", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonB, SeasonId = seasonB.SeasonId, ClubId = clubB.ClubId, CreatedById = memberUserId };
            var playerA = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "A", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubA.ClubId, CreatedById = adminUserId };
            var playerB = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "B", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = memberUserId };
            var teamA = new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "A Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubA.ClubId, CreatedById = adminUserId };
            var teamB = new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "B Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = memberUserId };
            context.AddRange(seasonA, seasonB, campaignA, campaignB, playerA, playerB, teamA, teamB);
            await context.SaveChangesAsync(cancellationToken);
            (await context.Clubs.SingleAsync(club => club.ClubId == clubA.ClubId, cancellationToken))
                .CurrentSeasonId = seasonA.SeasonId;
            (await context.Clubs.SingleAsync(club => club.ClubId == clubB.ClubId, cancellationToken))
                .CurrentSeasonId = seasonB.SeasonId;
            await context.SaveChangesAsync(cancellationToken);
        }

        // MemberClient should only see clubB campaigns
        using var resp = await memberClient.GetAsync(new Uri(CampaignEndpoints.GetCampaignList, UriKind.RelativeOrAbsolute), cancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
        list.ShouldNotBeNull();
        var campaignNames = list.Seasons.SelectMany(s => s.Campaigns).Select(c => c.Name).ToList();
        campaignNames.ShouldNotContain("CA", StringComparer.Ordinal);
        campaignNames.ShouldContain("CB", StringComparer.Ordinal);

        using var setupResp = await memberClient.GetAsync(new Uri(CampaignEndpoints.GetCreationSetup, UriKind.RelativeOrAbsolute), cancellationToken);
        setupResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setup = await setupResp.Content.ReadFromJsonAsync<CampaignCreationSetupResult>(cancellationToken);
        setup.ShouldNotBeNull();
        setup.CurrentSeason.ShouldNotBeNull();
        setup.CurrentSeason.Name.ShouldBe("SB");
        setup.ActivePlayerCount.ShouldBe(1);
        setup.ActiveTeamCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies the detail endpoint returns the caller's campaign payload and hides other clubs'
    /// and missing campaigns behind 404 responses.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task GetCampaignDetailReturnsOwnCampaignAndNotFoundForOtherClubAndMissingIdsAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("campaign-detail-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var clubA = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("campaign-detail-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, clubId: null, cancellationToken);
        var clubB = await CreateClubAsync(memberClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        CampaignEntity campaignA;
        CampaignEntity campaignB;
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var adminUserId = await context.Users.Where(u => u.NormalizedEmail == adminEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
#pragma warning restore CA1862
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var memberUserId = await context.Users.Where(u => u.NormalizedEmail == memberEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
#pragma warning restore CA1862

            var seasonA = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "SA", StartDate = new DateOnly(2026, 1, 1), ClubId = clubA.ClubId, CreatedById = adminUserId };
            var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "SB", StartDate = new DateOnly(2026, 1, 1), ClubId = clubB.ClubId, CreatedById = memberUserId };
            campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "CA", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonA, SeasonId = seasonA.SeasonId, ClubId = clubA.ClubId, CreatedById = adminUserId };
            campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "CB", StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 1), Status = CampaignStatus.Active, Season = seasonB, SeasonId = seasonB.SeasonId, ClubId = clubB.ClubId, CreatedById = memberUserId };
            var playerB = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "B", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = memberUserId };
            context.AddRange(seasonA, seasonB, campaignA, campaignB, playerB);
            await context.SaveChangesAsync(cancellationToken);
            context.Add(new PlayerCampaignAssignmentEntity { PlayerId = playerB.PlayerId, CampaignId = campaignB.CampaignId, ClubId = clubB.ClubId, CreatedById = memberUserId, PlacementOutcome = PlacementOutcome.Undecided });
            await context.SaveChangesAsync(cancellationToken);
        }

        using var detailResp = await memberClient.GetAsync(new Uri(CampaignEndpoints.GetCampaignDetailUrl(campaignB.CampaignId), UriKind.RelativeOrAbsolute), cancellationToken);
        detailResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResp.Content.ReadFromJsonAsync<CampaignDetailResult>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.CampaignId.ShouldBe(campaignB.CampaignId);
        detail.Name.ShouldBe("CB");
        detail.Status.ShouldBe(CampaignStatus.Active);
        detail.StartDate.ShouldBe(new DateOnly(2026, 6, 1));
        detail.PlannedEndDate.ShouldBe(new DateOnly(2026, 8, 1));
        detail.ParticipantCount.ShouldBe(1);
        detail.SeasonName.ShouldBe("SB");

        using var otherClubResp = await memberClient.GetAsync(new Uri(CampaignEndpoints.GetCampaignDetailUrl(campaignA.CampaignId), UriKind.RelativeOrAbsolute), cancellationToken);
        otherClubResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var missingResp = await memberClient.GetAsync(new Uri(CampaignEndpoints.GetCampaignDetailUrl(999999), UriKind.RelativeOrAbsolute), cancellationToken);
        missingResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Creates a unique integration-test email address.</summary>
    /// <param name="prefix">The scenario prefix.</param>
    /// <returns>A unique email address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>Creates a club through the public HTTP endpoint.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created club.</returns>
    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var responseRequestContent = SeedingHelpers.CreateClubMultipartContent($"Club {Guid.NewGuid():N}", "X", "TX");
        using var response = await client.PostAsync(new Uri(ClubEndpoints.Create, UriKind.RelativeOrAbsolute), responseRequestContent, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>Refreshes the authentication cookie after club membership changes.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the refresh operation.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"{ClubEndpoints.Complete}?returnUrl=/dashboard", UriKind.RelativeOrAbsolute), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>Updates a registered user's current club directly for test setup.</summary>
    /// <param name="email">The registered email.</param>
    /// <param name="clubId">The optional club identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the update.</returns>
    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        var context = fixture.CreateAdminContext();
        await using (context)
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
#pragma warning restore CA1862
            user.ClubId = clubId;
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
