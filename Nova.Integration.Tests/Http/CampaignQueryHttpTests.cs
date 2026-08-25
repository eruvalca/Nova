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
/// Verifies campaign query authorization, validation, serialization, and tenant isolation over HTTP.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignQueryHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides the password used by registered integration-test users.</summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies anonymous rejection and approved-member access for both routes.</summary>
    [Fact]
    public async Task GetEndpoints_RejectAnonymous_AndAllowApprovedMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateNovaHttpClient();
        using (var anonResp = await anonymous.GetAsync(CampaignEndpoints.GetCampaignList, cancellationToken))
        {
            anonResp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using (var anonResp = await anonymous.GetAsync(CampaignEndpoints.GetCreationSetup, cancellationToken))
        {
            anonResp.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        using (var anonResp = await anonymous.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(1), cancellationToken))
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
        await using (var context = fixture.CreateAdminContext())
        {
            var userId = await context.Users.Where(u => u.NormalizedEmail == email.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
            var season = new SeasonEntity { Name = "S", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = userId };
            campaign = new CampaignEntity { Name = "C", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = userId };
            context.AddRange(season, campaign);
            await context.SaveChangesAsync(cancellationToken);
        }

        using var resp = await client.GetAsync(CampaignEndpoints.GetCampaignList, cancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var setupResp = await client.GetAsync(CampaignEndpoints.GetCreationSetup, cancellationToken);
        setupResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setup = await setupResp.Content.ReadFromJsonAsync<CampaignCreationSetupResult>(cancellationToken);
        setup.ShouldNotBeNull();
        setup.TotalSeasonCount.ShouldBe(1);
        setup.Seasons.Count.ShouldBe(1);
        setup.Seasons[0].Name.ShouldBe("S");
        setup.ActivePlayerCount.ShouldBe(0);
        setup.ActiveTeamCount.ShouldBe(0);

        using var detailResp = await client.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(campaign.CampaignId), cancellationToken);
        detailResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResp.Content.ReadFromJsonAsync<CampaignDetailResult>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.CampaignId.ShouldBe(campaign.CampaignId);
        detail.Name.ShouldBe("C");
        detail.Status.ShouldBe(CampaignStatus.Active);
        detail.SeasonName.ShouldBe("S");
    }

    /// <summary>Verifies authenticated callers without a club receive forbidden responses.</summary>
    [Fact]
    public async Task GetEndpoints_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var listResponse = await client.GetAsync(CampaignEndpoints.GetCampaignList, cancellationToken);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var setupResponse = await client.GetAsync(CampaignEndpoints.GetCreationSetup, cancellationToken);
        setupResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var detailResponse = await client.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(1), cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Verifies invalid status binding returns correlated validation ProblemDetails.</summary>
    [Fact]
    public async Task GetCampaigns_InvalidStatus_ReturnsValidationProblem_WithTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-bad");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync($"{CampaignEndpoints.GetCampaignList}?status=bogus", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        doc.ShouldNotBeNull();
        doc.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    /// <summary>
    /// Verifies an invalid limit independently produces validation ProblemDetails with correlation.
    /// </summary>
    [Fact]
    public async Task GetCampaigns_InvalidLimit_ReturnsValidationProblem_WithTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-bad-limit");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        _ = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync($"{CampaignEndpoints.GetCampaignList}?limit=0", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        doc.ShouldNotBeNull();
        doc.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

    /// <summary>Verifies campaign and setup projections cannot leak data across clubs.</summary>
    [Fact]
    public async Task TenantIsolation_CannotSeeOtherClubsCampaigns()
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
        await using (var context = fixture.CreateAdminContext())
        {
            var adminUserId = await context.Users.Where(u => u.NormalizedEmail == adminEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
            var memberUserId = await context.Users.Where(u => u.NormalizedEmail == memberEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);

            var seasonA = new SeasonEntity { Name = "SA", StartDate = new DateOnly(2026, 1, 1), ClubId = clubA.ClubId, CreatedById = adminUserId };
            var seasonB = new SeasonEntity { Name = "SB", StartDate = new DateOnly(2026, 1, 1), ClubId = clubB.ClubId, CreatedById = memberUserId };
            var campaignA = new CampaignEntity { Name = "CA", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonA, SeasonId = seasonA.SeasonId, ClubId = clubA.ClubId, CreatedById = adminUserId };
            var campaignB = new CampaignEntity { Name = "CB", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonB, SeasonId = seasonB.SeasonId, ClubId = clubB.ClubId, CreatedById = memberUserId };
            var playerA = new PlayerEntity { FirstName = "A", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubA.ClubId, CreatedById = adminUserId };
            var playerB = new PlayerEntity { FirstName = "B", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = memberUserId };
            var teamA = new TeamEntity { Name = "A Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubA.ClubId, CreatedById = adminUserId };
            var teamB = new TeamEntity { Name = "B Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = memberUserId };
            context.AddRange(seasonA, seasonB, campaignA, campaignB, playerA, playerB, teamA, teamB);
            await context.SaveChangesAsync(cancellationToken);
        }

        // MemberClient should only see clubB campaigns
        using var resp = await memberClient.GetAsync(CampaignEndpoints.GetCampaignList, cancellationToken);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
        list.ShouldNotBeNull();
        var campaignNames = list.Seasons.SelectMany(s => s.Campaigns).Select(c => c.Name).ToList();
        campaignNames.ShouldNotContain("CA");
        campaignNames.ShouldContain("CB");

        using var setupResp = await memberClient.GetAsync(CampaignEndpoints.GetCreationSetup, cancellationToken);
        setupResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setup = await setupResp.Content.ReadFromJsonAsync<CampaignCreationSetupResult>(cancellationToken);
        setup.ShouldNotBeNull();
        setup.Seasons.Select(season => season.Name).ShouldNotContain("SA");
        setup.Seasons.Select(season => season.Name).ShouldContain("SB");
        setup.ActivePlayerCount.ShouldBe(1);
        setup.ActiveTeamCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies the detail endpoint returns the caller's campaign payload and hides other clubs'
    /// and missing campaigns behind 404 responses.
    /// </summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsOwnCampaign_AndNotFoundForOtherClubAndMissingIds()
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
        await using (var context = fixture.CreateAdminContext())
        {
            var adminUserId = await context.Users.Where(u => u.NormalizedEmail == adminEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
            var memberUserId = await context.Users.Where(u => u.NormalizedEmail == memberEmail.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);

            var seasonA = new SeasonEntity { Name = "SA", StartDate = new DateOnly(2026, 1, 1), ClubId = clubA.ClubId, CreatedById = adminUserId };
            var seasonB = new SeasonEntity { Name = "SB", StartDate = new DateOnly(2026, 1, 1), ClubId = clubB.ClubId, CreatedById = memberUserId };
            campaignA = new CampaignEntity { Name = "CA", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonA, SeasonId = seasonA.SeasonId, ClubId = clubA.ClubId, CreatedById = adminUserId };
            campaignB = new CampaignEntity { Name = "CB", StartDate = new DateOnly(2026, 6, 1), EndDate = new DateOnly(2026, 8, 1), Status = CampaignStatus.Active, Season = seasonB, SeasonId = seasonB.SeasonId, ClubId = clubB.ClubId, CreatedById = memberUserId };
            var playerB = new PlayerEntity { FirstName = "B", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = memberUserId };
            context.AddRange(seasonA, seasonB, campaignA, campaignB, playerB);
            await context.SaveChangesAsync(cancellationToken);
            context.Add(new PlayerCampaignAssignmentEntity { PlayerId = playerB.PlayerId, CampaignId = campaignB.CampaignId, ClubId = clubB.ClubId, CreatedById = memberUserId, PlacementOutcome = PlacementOutcome.Undecided });
            await context.SaveChangesAsync(cancellationToken);
        }

        using var detailResp = await memberClient.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(campaignB.CampaignId), cancellationToken);
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

        using var otherClubResp = await memberClient.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(campaignA.CampaignId), cancellationToken);
        otherClubResp.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var missingResp = await memberClient.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(999999), cancellationToken);
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
        using var response = await client.PostAsync(ClubEndpoints.Create, SeedingHelpers.CreateClubMultipartContent($"Club {Guid.NewGuid():N}", "X", "TX"), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>Refreshes the authentication cookie after club membership changes.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the refresh operation.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>Updates a registered user's current club directly for test setup.</summary>
    /// <param name="email">The registered email.</param>
    /// <param name="clubId">The optional club identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the update.</returns>
    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }
}
