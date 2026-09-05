using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
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

    /// <summary>
    /// Verifies replaying an opening with the same operation identifier returns the identical receipt
    /// and that the opening state, technical enrollments, and a single lifecycle event are persisted.
    /// </summary>
    [Fact]
    public async Task CampaignOpen_ReplaysIdenticalReceipt_AndPersistsOpeningState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("opening-replay", cancellationToken);
        using (client)
        {
            var campaignId = await SeedDraftAsync(clubId, email, activePlayerCount: 2, cancellationToken);
            var operationId = Guid.CreateVersion7();
            using var response = await client.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(campaignId),
                new OpenCampaignInput { OperationId = operationId },
                cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var receipt = (await response.Content.ReadFromJsonAsync<OpenCampaignResult>(cancellationToken))!;
            receipt.EnrolledPlayerCount.ShouldBe(2);
            receipt.Warnings.ShouldBe([CampaignOpeningWarning.NoActiveTeams]);

            using var replayResponse = await client.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(campaignId),
                new OpenCampaignInput { OperationId = operationId },
                cancellationToken);
            replayResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var replayed = (await replayResponse.Content.ReadFromJsonAsync<OpenCampaignResult>(cancellationToken))!;
            replayed.OperationId.ShouldBe(receipt.OperationId);
            replayed.CampaignId.ShouldBe(receipt.CampaignId);
            replayed.OpenedAt.ShouldBe(receipt.OpenedAt, TimeSpan.FromMilliseconds(1));
            replayed.OpenedByUserId.ShouldBe(receipt.OpenedByUserId);
            replayed.EnrolledPlayerCount.ShouldBe(receipt.EnrolledPlayerCount);
            replayed.ActiveTeamCount.ShouldBe(receipt.ActiveTeamCount);
            replayed.Warnings.ShouldBe(receipt.Warnings);

            await using var context = fixture.CreateAdminContext();
            var campaign = await context.Campaigns.SingleAsync(
                candidate => candidate.CampaignId == campaignId, cancellationToken);
            campaign.Status.ShouldBe(CampaignStatus.Active);
            campaign.OpeningOperationId.ShouldBe(operationId);
            campaign.OpenedAt.ShouldNotBeNull();
            campaign.OpenedById.ShouldBe(receipt.OpenedByUserId);
            campaign.SeasonOpeningSequence.ShouldBe(1);
            campaign.InitialEnrolledPlayerCount.ShouldBe(2);
            campaign.InitialActiveTeamCount.ShouldBe(0);
            var assignments = await context.PlayerCampaignAssignments
                .Where(assignment => assignment.CampaignId == campaignId)
                .ToListAsync(cancellationToken);
            assignments.Count.ShouldBe(2);
            assignments.ShouldAllBe(assignment => assignment.PlacementOutcome == PlacementOutcome.Undecided);
            (await context.ActivityEvents.CountAsync(
                activity => activity.CampaignId == campaignId
                    && activity.EventKind == ActivityEventKind.CampaignOpened,
                cancellationToken)).ShouldBe(1);
        }
    }

    /// <summary>
    /// Verifies opening conflicts surface every condition-keyed blocker, including the other Active
    /// campaign's identity, without partially mutating the Draft.
    /// </summary>
    [Fact]
    public async Task CampaignOpen_ReturnsConflict_WithStructuredBlockers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("opening-blockers", cancellationToken);
        using (client)
        {
            var emptyDraftId = await SeedDraftAsync(clubId, email, activePlayerCount: 0, cancellationToken);
            using var noPlayersResponse = await client.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(emptyDraftId),
                new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
                cancellationToken);
            noPlayersResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var noPlayersErrors = await ReadErrorsAsync(noPlayersResponse, cancellationToken);
            noPlayersErrors.ShouldContainKey(CampaignOpeningProblemKeys.NoActivePlayers);

            var draftId = await SeedDraftAsync(clubId, email, activePlayerCount: 1, cancellationToken);
            var blockingCampaignId = await SeedBlockingActiveCampaignAsync(clubId, email, cancellationToken);
            using var blockedResponse = await client.PostAsJsonAsync(
                CampaignEndpoints.OpenUrl(draftId),
                new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
                cancellationToken);
            blockedResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var blockedErrors = await ReadErrorsAsync(blockedResponse, cancellationToken);
            blockedErrors.ShouldContainKey(CampaignOpeningProblemKeys.AnotherCampaignActive);
            blockedErrors[CampaignOpeningProblemKeys.BlockingCampaignId].Single()
                .ShouldBe(blockingCampaignId.ToString());
            blockedErrors[CampaignOpeningProblemKeys.BlockingCampaignName].Single()
                .ShouldNotBeNullOrWhiteSpace();

            await using var context = fixture.CreateAdminContext();
            var draft = await context.Campaigns.SingleAsync(
                candidate => candidate.CampaignId == draftId, cancellationToken);
            draft.Status.ShouldBe(CampaignStatus.Draft);
            draft.OpeningOperationId.ShouldBeNull();
        }
    }

    /// <summary>
    /// Verifies a blocked Draft reports every readiness blocker and warning together with the
    /// blocking Active campaign's identity over the deployed pipeline.
    /// </summary>
    [Fact]
    public async Task CampaignOpeningReadiness_ReportsBlockers_ForBlockedDraft()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("readiness-blocked", cancellationToken);
        using (client)
        {
            var draftId = await SeedDraftAsync(clubId, email, activePlayerCount: 0, cancellationToken);
            var blockingCampaignId = await SeedBlockingActiveCampaignAsync(clubId, email, cancellationToken);

            using var response = await client.GetAsync(
                CampaignEndpoints.GetOpeningReadinessUrl(draftId), cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var readiness = (await response.Content.ReadFromJsonAsync<CampaignOpeningReadinessResult>(cancellationToken))!;
            readiness.CanOpen.ShouldBeFalse();
            readiness.Blockers.ShouldContain(CampaignOpeningBlocker.NoActivePlayers);
            readiness.Blockers.ShouldContain(CampaignOpeningBlocker.AnotherCampaignActive);
            readiness.Warnings.ShouldContain(CampaignOpeningWarning.NoActiveTeams);
            readiness.BlockingCampaign.ShouldNotBeNull();
            readiness.BlockingCampaign.CampaignId.ShouldBe(blockingCampaignId);
            readiness.BlockingCampaign.CampaignName.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>
    /// Verifies Draft deletion persists exactly one durable tombstone and that replaying the
    /// deletion still succeeds without adding another.
    /// </summary>
    [Fact]
    public async Task CampaignDraftDelete_PersistsSingleTombstone_AndReplays()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("delete-tombstone", cancellationToken);
        using (client)
        {
            var campaignId = await SeedDraftAsync(clubId, email, activePlayerCount: 0, cancellationToken);
            using var response = await client.DeleteAsync(
                CampaignEndpoints.DeleteDraftUrl(campaignId), cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            await using var context = fixture.CreateAdminContext();
            (await context.Campaigns.AnyAsync(
                candidate => candidate.CampaignId == campaignId, cancellationToken)).ShouldBeFalse();
            (await context.ActivityEvents.CountAsync(
                activity => activity.CampaignId == campaignId
                    && activity.EventKind == ActivityEventKind.CampaignDraftDeleted,
                cancellationToken)).ShouldBe(1);

            using var replayResponse = await client.DeleteAsync(
                CampaignEndpoints.DeleteDraftUrl(campaignId), cancellationToken);
            replayResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await context.ActivityEvents.CountAsync(
                activity => activity.CampaignId == campaignId
                    && activity.EventKind == ActivityEventKind.CampaignDraftDeleted,
                cancellationToken)).ShouldBe(1);
        }
    }

    /// <summary>Verifies readiness previews only the first five active teams while retaining the full count.</summary>
    [Fact]
    public async Task CampaignOpeningReadiness_ReturnsBoundedActiveTeamPreview()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (client, email, clubId) = await CreateAdministratorAsync("readiness-teams", cancellationToken);
        using (client)
        {
            var draftId = await SeedDraftAsync(clubId, email, activePlayerCount: 1, cancellationToken);
            long[] expectedTeamIds;
            await using (var context = fixture.CreateAdminContext())
            {
                var userId = await context.Users.Where(user => user.NormalizedEmail == email.ToUpperInvariant())
                    .Select(user => user.Id).SingleAsync(cancellationToken);
                var teams = new[] { "Foxtrot", "Echo", "Delta", "Charlie", "Bravo", "Alpha" }
                    .Select(name => new TeamEntity
                    {
                        CreationOperationId = Guid.NewGuid(),
                        Name = name,
                        GraduationYear = 2030,
                        LifecycleStatus = LifecycleStatus.Active,
                        ClubId = clubId,
                        CreatedById = userId
                    }).ToArray();
                context.Teams.AddRange(teams);
                context.Teams.Add(new TeamEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Aardvark archived",
                    GraduationYear = 2030,
                    LifecycleStatus = LifecycleStatus.Archived,
                    ArchivedAt = DateTimeOffset.UtcNow,
                    ArchivedById = userId,
                    ClubId = clubId,
                    CreatedById = userId
                });
                await context.SaveChangesAsync(cancellationToken);
                expectedTeamIds = teams.OrderBy(team => team.Name, StringComparer.Ordinal)
                    .Take(5).Select(team => team.TeamId).ToArray();
            }

            using var response = await client.GetAsync(CampaignEndpoints.GetOpeningReadinessUrl(draftId), cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var readiness = await response.Content.ReadFromJsonAsync<CampaignOpeningReadinessResult>(cancellationToken);
            readiness.ShouldNotBeNull();
            readiness.ActiveTeamCount.ShouldBe(6);
            readiness.ActiveTeams.Select(team => team.TeamId).ShouldBe(expectedTeamIds);
            readiness.ActiveTeams.Select(team => team.Name).ShouldBe(["Alpha", "Bravo", "Charlie", "Delta", "Echo"]);
            readiness.CanOpen.ShouldBeTrue();
            readiness.Warnings.ShouldBeEmpty();
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

    /// <summary>Seeds an Active campaign that blocks another Draft in the club from opening.</summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">The administrator e-mail used to resolve audit ownership.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The blocking Active campaign identifier.</returns>
    private async Task<long> SeedBlockingActiveCampaignAsync(
        long clubId,
        string adminEmail,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var userId = await context.Users
            .Where(user => user.NormalizedEmail == adminEmail.ToUpperInvariant())
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);
        var club = await context.Clubs.SingleAsync(candidate => candidate.ClubId == clubId, cancellationToken);
        var season = club.CurrentSeasonId is long currentSeasonId
            ? await context.Seasons.SingleAsync(candidate => candidate.SeasonId == currentSeasonId, cancellationToken)
            : throw new InvalidOperationException("The club has no current season for the blocking campaign.");
        var suffix = Guid.CreateVersion7().ToString("N");
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            Name = $"Opening Blocker {suffix}",
            StartDate = new DateOnly(2026, 5, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = userId
        };
        context.Add(campaign);
        await context.SaveChangesAsync(cancellationToken);
        return campaign.CampaignId;
    }

    /// <summary>
    /// Reads the <c>errors</c> dictionary from a ProblemDetails payload.
    /// </summary>
    /// <param name="response">The problem-details response.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The structured error dictionary.</returns>
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
