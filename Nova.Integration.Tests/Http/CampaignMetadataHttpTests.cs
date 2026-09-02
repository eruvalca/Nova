using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the administrator campaign/season metadata correction endpoints:
/// anonymous/member/administrator authorization and non-disclosing cross-tenant isolation.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignMetadataHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    // ── Campaign metadata ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the campaign metadata endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task UpdateCampaignMetadata_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignMetadata,
            ValidCampaignMetadataInput(campaignId: 1, seasonId: 1),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an authenticated non-administrator club member cannot update campaign metadata.
    /// </summary>
    [Fact]
    public async Task UpdateCampaignMetadata_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "metadata-member-admin", cancellationToken);
        var seed = await SeedingHelpers.SeedSeasonAndCampaignAsync(
            fixture, admin.Club.ClubId, admin.Email, "Metadata Member", cancellationToken);
        await RegisterClubMemberAsync(memberClient, "metadata-member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignMetadata,
            ValidCampaignMetadataInput(seed.CampaignId, seed.SeasonId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club administrator can update campaign metadata and receives the corrected result.
    /// </summary>
    [Fact]
    public async Task UpdateCampaignMetadata_ReturnsOk_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(client, "metadata-ok-admin", cancellationToken);
        var seed = await SeedingHelpers.SeedSeasonAndCampaignAsync(
            fixture, admin.Club.ClubId, admin.Email, "Metadata Ok", cancellationToken);
        var input = ValidCampaignMetadataInput(seed.CampaignId, seed.SeasonId);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignMetadata,
            input,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<UpdateCampaignMetadataResult>(cancellationToken);
        updated.ShouldNotBeNull();
        updated.CampaignId.ShouldBe(seed.CampaignId);
        updated.Name.ShouldBe(input.Name);
        updated.SeasonId.ShouldBe(seed.SeasonId);
    }

    /// <summary>
    /// Verifies another club's campaign is hidden as non-disclosing 404 and left unchanged.
    /// </summary>
    [Fact]
    public async Task UpdateCampaignMetadata_ReturnsNotFound_ForCrossTenantCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAClient, "metadata-xclub-a", cancellationToken);
        var seed = await SeedingHelpers.SeedSeasonAndCampaignAsync(
            fixture, clubA.Club.ClubId, clubA.Email, "Metadata Cross A", cancellationToken);
        var originalName = await ReadCampaignNameAsync(seed.CampaignId, cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "metadata-xclub-b", cancellationToken);

        using var response = await clubBClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignMetadata,
            ValidCampaignMetadataInput(seed.CampaignId, seed.SeasonId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        (await ReadCampaignNameAsync(seed.CampaignId, cancellationToken)).ShouldBe(originalName);
    }

    // ── Season metadata ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the season metadata endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task UpdateSeasonMetadata_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PutAsJsonAsync(
            SeasonEndpoints.Detail(1),
            ValidSeasonMetadataInput(Guid.NewGuid()),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an authenticated non-administrator club member cannot update season metadata.
    /// </summary>
    [Fact]
    public async Task UpdateSeasonMetadata_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "season-member-admin", cancellationToken);
        var seed = await SeedingHelpers.SeedSeasonAndCampaignAsync(
            fixture, admin.Club.ClubId, admin.Email, "Season Member", cancellationToken);
        await RegisterClubMemberAsync(memberClient, "season-member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PutAsJsonAsync(
            SeasonEndpoints.Detail(seed.SeasonId),
            ValidSeasonMetadataInput(await ReadSeasonTokenAsync(seed.SeasonId, cancellationToken)),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club administrator can update season metadata and receives the corrected result.
    /// </summary>
    [Fact]
    public async Task UpdateSeasonMetadata_ReturnsOk_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(client, "season-ok-admin", cancellationToken);
        var seed = await SeedingHelpers.SeedSeasonAndCampaignAsync(
            fixture, admin.Club.ClubId, admin.Email, "Season Ok", cancellationToken);
        var input = ValidSeasonMetadataInput(await ReadSeasonTokenAsync(seed.SeasonId, cancellationToken));

        using var response = await client.PutAsJsonAsync(
            SeasonEndpoints.Detail(seed.SeasonId),
            input,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<SeasonSummary>(cancellationToken);
        updated.ShouldNotBeNull();
        updated.SeasonId.ShouldBe(seed.SeasonId);
        updated.Name.ShouldBe(input.Name);
    }

    /// <summary>
    /// Verifies another club's season is hidden as non-disclosing 404 and left unchanged.
    /// </summary>
    [Fact]
    public async Task UpdateSeasonMetadata_ReturnsNotFound_ForCrossTenantSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAClient, "season-xclub-a", cancellationToken);
        var seed = await SeedingHelpers.SeedSeasonAndCampaignAsync(
            fixture, clubA.Club.ClubId, clubA.Email, "Season Cross A", cancellationToken);
        var originalName = await ReadSeasonNameAsync(seed.SeasonId, cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "season-xclub-b", cancellationToken);

        using var response = await clubBClient.PutAsJsonAsync(
            SeasonEndpoints.Detail(seed.SeasonId),
            ValidSeasonMetadataInput(await ReadSeasonTokenAsync(seed.SeasonId, cancellationToken)),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        (await ReadSeasonNameAsync(seed.SeasonId, cancellationToken)).ShouldBe(originalName);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static UpdateCampaignMetadataInput ValidCampaignMetadataInput(long campaignId, long seasonId) => new()
    {
        CampaignId = campaignId,
        Name = $"Updated Campaign {Guid.CreateVersion7():N}",
        SeasonId = seasonId,
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30)
    };

    private static UpdateSeasonInput ValidSeasonMetadataInput(Guid concurrencyToken) => new()
    {
        ExpectedConcurrencyToken = concurrencyToken,
        Name = $"Updated Season {Guid.CreateVersion7():N}",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = null
    };

    private async Task<(ClubDto Club, string Email)> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, null, cancellationToken, "Campaign", "Admin");
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (club, email);
    }

    private async Task RegisterClubMemberAsync(
        HttpClient client,
        string emailPrefix,
        long clubId,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken, "Campaign", "Member");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
    }

    private async Task<string> ReadCampaignNameAsync(long campaignId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        return await context.Campaigns
            .Where(campaign => campaign.CampaignId == campaignId)
            .Select(campaign => campaign.Name)
            .SingleAsync(cancellationToken);
    }

    private async Task<string> ReadSeasonNameAsync(long seasonId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        return await context.Seasons
            .Where(season => season.SeasonId == seasonId)
            .Select(season => season.Name)
            .SingleAsync(cancellationToken);
    }

    private async Task<Guid> ReadSeasonTokenAsync(long seasonId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        return await context.Seasons
            .Where(season => season.SeasonId == seasonId)
            .Select(season => season.ConcurrencyToken)
            .SingleAsync(cancellationToken);
    }
}
