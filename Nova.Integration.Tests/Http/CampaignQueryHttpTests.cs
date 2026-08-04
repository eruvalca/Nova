using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Campaigns;
using Nova.Shared.Clubs;
using System.Text.Json;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Http;

[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignQueryHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

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
        await using (var context = fixture.CreateAdminContext())
        {
            var userId = await context.Users.Where(u => u.NormalizedEmail == email.ToUpperInvariant()).Select(u => u.Id).SingleAsync(cancellationToken);
            var season = new SeasonEntity { Name = "S", StartDate = new DateOnly(2026,1,1), ClubId = club.ClubId, CreatedById = userId };
            var campaign = new CampaignEntity { Name = "C", StartDate = new DateOnly(2026,6,1), Status = CampaignStatus.Active, Season = season, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = userId };
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
    }

    [Fact]
    public async Task GetCampaigns_InvalidStatusOrLimit_ReturnsValidationProblem_WithTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("campaign-bad");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync($"{CampaignEndpoints.GetCampaignList}?status=bogus&limit=0", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        doc.ShouldNotBeNull();
        doc.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }

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

            var seasonA = new SeasonEntity { Name = "SA", StartDate = new DateOnly(2026,1,1), ClubId = clubA.ClubId, CreatedById = adminUserId };
            var seasonB = new SeasonEntity { Name = "SB", StartDate = new DateOnly(2026,1,1), ClubId = clubB.ClubId, CreatedById = memberUserId };
            var campaignA = new CampaignEntity { Name = "CA", StartDate = new DateOnly(2026,6,1), Status = CampaignStatus.Active, Season = seasonA, SeasonId = seasonA.SeasonId, ClubId = clubA.ClubId, CreatedById = adminUserId };
                        var campaignB = new CampaignEntity { Name = "CB", StartDate = new DateOnly(2026,6,1), Status = CampaignStatus.Active, Season = seasonB, SeasonId = seasonB.SeasonId, ClubId = clubB.ClubId, CreatedById = memberUserId };
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

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" }, cancellationToken);
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
}
