using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class PlayerRosterHttpTests
{
    [Fact]
    public async Task IntakeContextNamesTheActiveCampaignForOrdinaryMemberAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("intake-context-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(admin, adminEmail, Password, ct);
        await UpdateUserAsync(adminEmail, "Alex", "IntakeAdmin", null, ct);
        var club = await CreateClubAsync(admin, "Intake Context Club", "Austin", "TX", ct);
        await RefreshClubMembershipCookieAsync(admin, ct);
        var seeded = await SeedingHelpers.SeedSeasonAndCampaignAsync(fixture, club.ClubId, adminEmail, "Intake", ct);

        using var member = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("intake-context-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(member, memberEmail, Password, ct);
        await UpdateUserAsync(memberEmail, "Taylor", "IntakeMember", club.ClubId, ct);
        await RefreshClubMembershipCookieAsync(member, ct);

        using var response = await member.GetAsync(
            new Uri(GetPlayerRosterEndpoints.GetIntakeContextUrl(club.ClubId), UriKind.Relative), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var context = await response.Content.ReadFromJsonAsync<PlayerIntakeContext>(ct);
        context.ShouldNotBeNull();
        context.CampaignId.ShouldBe(seeded.CampaignId);
        context.CampaignName.ShouldNotBeNullOrWhiteSpace();
        context.CampaignName.ShouldStartWith("Intake Campaign");

        using var denied = await member.GetAsync(
            new Uri(GetPlayerRosterEndpoints.GetIntakeContextUrl(club.ClubId + 1000000), UriKind.Relative), ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync(ct)).ShouldContain("traceId");
    }

    [Fact]
    public async Task IntakeContextReportsExplicitNullPairWhenNoCampaignIsActiveAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("intake-context-quiet");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(admin, adminEmail, Password, ct);
        await UpdateUserAsync(adminEmail, "Quiet", "Admin", null, ct);
        var club = await CreateClubAsync(admin, "Quiet Intake Club", "Boise", "ID", ct);
        await RefreshClubMembershipCookieAsync(admin, ct);

        await using (var verify = fixture.CreateAdminContext())
        {
            var activeCount = await verify.Campaigns
                .CountAsync(campaign => campaign.ClubId == club.ClubId && campaign.Status == Nova.SharedKernel.Enums.CampaignStatus.Active, ct);
            activeCount.ShouldBe(0);
        }

        using var response = await admin.GetAsync(
            new Uri(GetPlayerRosterEndpoints.GetIntakeContextUrl(club.ClubId), UriKind.Relative), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("campaignId", out var campaignId).ShouldBeTrue();
        document.RootElement.TryGetProperty("campaignName", out var campaignName).ShouldBeTrue();
        campaignId.ValueKind.ShouldBe(JsonValueKind.Null);
        campaignName.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task IntakeContextDeniesAnonymousRequestsAsync()
    {
        using var client = fixture.CreateNovaHttpClient();
        using var response = await client.GetAsync(
            new Uri(GetPlayerRosterEndpoints.GetIntakeContextUrl(1), UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
