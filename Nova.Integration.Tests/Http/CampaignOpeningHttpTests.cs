using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;
using static Nova.Integration.Tests.Http.SeedingHelpers;

namespace Nova.Integration.Tests.Http;

/// <summary>Verifies readiness, opening, and Draft deletion through the deployed HTTP pipeline.</summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignOpeningHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies all opening routes reject anonymous callers and non-administrator members.</summary>
    [Fact]
    public async Task OpeningRoutes_EnforceClubAdministratorPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateNovaHttpClient();
        using var anonymousReadiness = await anonymous.GetAsync(CampaignEndpoints.GetOpeningReadinessUrl(1), cancellationToken);
        using var anonymousOpen = await anonymous.PostAsJsonAsync(
            CampaignEndpoints.OpenUrl(1),
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);
        using var anonymousDelete = await anonymous.DeleteAsync(CampaignEndpoints.DeleteDraftUrl(1), cancellationToken);
        anonymousReadiness.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymousOpen.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymousDelete.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("opening-policy-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("opening-policy-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var memberReadiness = await memberClient.GetAsync(CampaignEndpoints.GetOpeningReadinessUrl(1), cancellationToken);
        using var memberOpen = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.OpenUrl(1),
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);
        using var memberDelete = await memberClient.DeleteAsync(CampaignEndpoints.DeleteDraftUrl(1), cancellationToken);
        memberReadiness.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        memberOpen.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        memberDelete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Verifies readiness, opening receipt serialization, and idempotent Draft deletion.</summary>
    [Fact]
    public async Task OpeningRoutes_ReturnExpectedSuccessContracts_ForAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("opening-success", cancellationToken);
        using (client)
        {
            var openCampaignId = await SeedDraftAsync(clubId, email, activePlayerCount: 2, cancellationToken);
            var deleteCampaignId = await SeedDraftAsync(clubId, email, activePlayerCount: 0, cancellationToken);

            using var readinessResponse = await client.GetAsync(
                CampaignEndpoints.GetOpeningReadinessUrl(openCampaignId),
                cancellationToken);
            readinessResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var readiness = await readinessResponse.Content.ReadFromJsonAsync<CampaignOpeningReadinessResult>(cancellationToken);
            readiness.ShouldNotBeNull();
            readiness.CampaignId.ShouldBe(openCampaignId);
            readiness.ActivePlayerCount.ShouldBe(2);
            readiness.CanOpen.ShouldBeTrue();
            readiness.Warnings.ShouldBe([CampaignOpeningWarning.NoActiveTeams]);

            var operationId = Guid.CreateVersion7();
            using var openResponse = await client.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(openCampaignId),
                new OpenCampaignInput { OperationId = operationId },
                cancellationToken);
            openResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var receipt = await openResponse.Content.ReadFromJsonAsync<OpenCampaignResult>(cancellationToken);
            receipt.ShouldNotBeNull();
            receipt.OperationId.ShouldBe(operationId);
            receipt.CampaignId.ShouldBe(openCampaignId);
            receipt.OpenedAt.ShouldNotBe(default);
            receipt.OpenedByUserId.ShouldBeGreaterThan(0);
            receipt.EnrolledPlayerCount.ShouldBe(2);

            using var deleteResponse = await client.DeleteAsync(
                CampaignEndpoints.DeleteDraftUrl(deleteCampaignId),
                cancellationToken);
            deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            using var replayResponse = await client.DeleteAsync(
                CampaignEndpoints.DeleteDraftUrl(deleteCampaignId),
                cancellationToken);
            replayResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    /// <summary>Verifies invalid opening bodies return correlated bad-request details.</summary>
    /// <param name="payload">The invalid request payload.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{}")]
    public async Task OpenCampaign_ReturnsCorrelatedBadRequest_ForInvalidBody(string payload)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("opening-invalid-body", cancellationToken);
        using (client)
        {
            var campaignId = await SeedDraftAsync(clubId, email, activePlayerCount: 1, cancellationToken);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(CampaignEndpoints.OpenUrl(campaignId), content, cancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            document.RootElement.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
            string.IsNullOrWhiteSpace(traceId.GetString()).ShouldBeFalse();
        }
    }

    /// <summary>Verifies tenant isolation and lifecycle conflicts from all three opening routes.</summary>
    [Fact]
    public async Task OpeningRoutes_MapNotFoundAndConflictStates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (ownerClient, ownerEmail, ownerClubId) = await CreateAdministratorAsync("opening-owner", cancellationToken);
        var (otherClient, _, _) = await CreateAdministratorAsync("opening-other", cancellationToken);
        using (ownerClient)
        using (otherClient)
        {
            var campaignId = await SeedDraftAsync(ownerClubId, ownerEmail, activePlayerCount: 1, cancellationToken);
            using var crossTenantReadiness = await otherClient.GetAsync(
                CampaignEndpoints.GetOpeningReadinessUrl(campaignId),
                cancellationToken);
            using var crossTenantOpen = await otherClient.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(campaignId),
                new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
                cancellationToken);
            using var crossTenantDelete = await otherClient.DeleteAsync(
                CampaignEndpoints.DeleteDraftUrl(campaignId),
                cancellationToken);
            crossTenantReadiness.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            crossTenantOpen.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            crossTenantDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

            using var openResponse = await ownerClient.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(campaignId),
                new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
                cancellationToken);
            openResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var activeReadiness = await ownerClient.GetAsync(
                CampaignEndpoints.GetOpeningReadinessUrl(campaignId),
                cancellationToken);
            using var reopenResponse = await ownerClient.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(campaignId),
                new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
                cancellationToken);
            using var deleteActiveResponse = await ownerClient.DeleteAsync(
                CampaignEndpoints.DeleteDraftUrl(campaignId),
                cancellationToken);
            activeReadiness.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            reopenResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            deleteActiveResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            var historicalDraftId = await SeedDraftAsync(
                ownerClubId,
                ownerEmail,
                activePlayerCount: 0,
                cancellationToken,
                makeCurrentSeason: false);
            using var historicalReadiness = await ownerClient.GetAsync(
                CampaignEndpoints.GetOpeningReadinessUrl(historicalDraftId),
                cancellationToken);
            historicalReadiness.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    /// <summary>Creates and signs in a club administrator.</summary>
    /// <param name="prefix">The unique identity prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The authenticated client, e-mail address, and club identifier.</returns>
    private async Task<(HttpClient Client, string Email, long ClubId)> CreateAdministratorAsync(
        string prefix,
        CancellationToken cancellationToken)
    {
        var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail(prefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);
        await UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (client, email, club.ClubId);
    }

    /// <summary>Seeds one Draft and optional active players for an administrator's club.</summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">The administrator e-mail used to resolve audit ownership.</param>
    /// <param name="activePlayerCount">The number of active players to seed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="makeCurrentSeason">Whether the Draft's season becomes current.</param>
    /// <returns>The Draft campaign identifier.</returns>
    private async Task<long> SeedDraftAsync(
        long clubId,
        string adminEmail,
        int activePlayerCount,
        CancellationToken cancellationToken,
        bool makeCurrentSeason = true)
    {
        await using var context = fixture.CreateAdminContext();
        var userId = await context.Users
            .Where(user => user.NormalizedEmail == adminEmail.ToUpperInvariant())
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);
        var club = await context.Clubs.SingleAsync(candidate => candidate.ClubId == clubId, cancellationToken);
        var suffix = Guid.CreateVersion7().ToString("N");
        var season = makeCurrentSeason && club.CurrentSeasonId is long currentSeasonId
            ? await context.Seasons.SingleAsync(candidate => candidate.SeasonId == currentSeasonId, cancellationToken)
            : new SeasonEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                Name = $"Opening Season {suffix}",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = clubId,
                CreatedById = userId
            };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            Name = $"Opening Draft {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Draft,
            Season = season,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = userId
        };
        context.Add(campaign);
        for (var index = 0; index < activePlayerCount; index++)
        {
            context.Add(new PlayerEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                FirstName = "Opening",
                LastName = $"Player {index + 1} {suffix}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = clubId,
                CreatedById = userId
            });
        }

        await context.SaveChangesAsync(cancellationToken);
        if (makeCurrentSeason && club.CurrentSeasonId is null)
        {
            club.CurrentSeasonId = season.SeasonId;
            await context.SaveChangesAsync(cancellationToken);
        }

        return campaign.CampaignId;
    }
}
