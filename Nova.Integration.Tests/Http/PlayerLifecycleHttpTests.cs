using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for player archive/restore lifecycle endpoints and shared conflict payload contracts.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerLifecycleHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies a club admin can archive and restore through HTTP without rewriting assignment history.
    /// </summary>
    [Fact]
    public async Task PlayerLifecycleEndpoints_ArchiveRestoreRoundTrip_PreservesAssignmentHistoryAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("player-lifecycle-roundtrip-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Avery", "ArchiveAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Roundtrip Rangers", "Round Rock", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var seeded = await SeedResolvedPlayerAsync(club.ClubId, cancellationToken);

        using (var archive = await adminClient.PostAsync(PlayerEndpoints.ArchiveUrl(seeded.PlayerId), content: null, cancellationToken))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var restore = await adminClient.PostAsync(PlayerEndpoints.RestoreUrl(seeded.PlayerId), content: null, cancellationToken))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using var verify = fixture.CreateAdminContext();
        var player = await verify.Players.SingleAsync(p => p.PlayerId == seeded.PlayerId, cancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        player.ArchivedAt.ShouldBeNull();
        player.ArchivedById.ShouldBeNull();

        var assignments = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerId == seeded.PlayerId)
            .OrderBy(assignment => assignment.CampaignId)
            .Select(assignment => new
            {
                assignment.CampaignId,
                assignment.PlacementOutcome,
                assignment.TeamId
            })
            .ToListAsync(cancellationToken);

        assignments.Count.ShouldBe(2);
        assignments.ShouldContain(a => a.CampaignId == seeded.ActiveCampaignId && a.PlacementOutcome == PlacementOutcome.Assigned && a.TeamId == seeded.TeamId);
        assignments.ShouldContain(a => a.CampaignId == seeded.HistoricalCampaignId && a.PlacementOutcome == PlacementOutcome.Assigned && a.TeamId == seeded.TeamId);
    }

    /// <summary>
    /// Verifies archive conflicts return structured blocker payloads grouped by campaign.
    /// </summary>
    [Fact]
    public async Task ArchiveEndpoint_ReturnsStructuredBlockers_ForUndecidedActiveParticipationAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("player-lifecycle-blockers-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Blair", "BlockerAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Blocker Borough", "Dallas", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var seeded = await SeedBlockedPlayerAsync(club.ClubId, cancellationToken);

        using var archive = await adminClient.PostAsync(PlayerEndpoints.ArchiveUrl(seeded.PlayerId), content: null, cancellationToken);
        archive.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await archive.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        problem.TryGetArchiveBlockers(out var blockers).ShouldBeTrue();
        blockers.Count.ShouldBe(1);
        blockers[0].CampaignId.ShouldBe(seeded.CampaignId);
        blockers[0].CampaignName.ShouldBe("Active Blocker Campaign");
        blockers[0].ParticipationIds.ShouldBe([seeded.ParticipationId]);
    }

    /// <summary>
    /// Verifies non-admin callers are forbidden and cross-tenant ids are not disclosed.
    /// </summary>
    [Fact]
    public async Task ArchiveEndpoint_ReturnsForbiddenForNonAdmin_AndNotFoundForCrossTenantAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAAdminClient = fixture.CreateNovaHttpClient();
        using var clubAMemberClient = fixture.CreateNovaHttpClient();
        using var clubBAdminClient = fixture.CreateNovaHttpClient();

        var clubAAdminEmail = UniqueEmail("player-lifecycle-cluba-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAAdminClient, clubAAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(clubAAdminEmail, "Casey", "ClubAAdmin", clubId: null, cancellationToken);
        var clubA = await CreateClubAsync(clubAAdminClient, "Alpha Club", "Austin", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAAdminClient, cancellationToken);

        var clubAMemberEmail = UniqueEmail("player-lifecycle-cluba-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAMemberClient, clubAMemberEmail, Password, cancellationToken);
        await UpdateUserAsync(clubAMemberEmail, "Morgan", "ClubAMember", clubA.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAMemberClient, cancellationToken);

        var clubBAdminEmail = UniqueEmail("player-lifecycle-clubb-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubBAdminClient, clubBAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(clubBAdminEmail, "Riley", "ClubBAdmin", clubId: null, cancellationToken);
        var clubB = await CreateClubAsync(clubBAdminClient, "Bravo Club", "Boston", "MA", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubBAdminClient, cancellationToken);

        var clubAPlayerId = await SeedSimplePlayerAsync(clubA.ClubId, cancellationToken);
        var clubBPlayerId = await SeedSimplePlayerAsync(clubB.ClubId, cancellationToken);

        using (var forbidden = await clubAMemberClient.PostAsync(PlayerEndpoints.ArchiveUrl(clubAPlayerId), content: null, cancellationToken))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var notFound = await clubAAdminClient.PostAsync(PlayerEndpoints.ArchiveUrl(clubBPlayerId), content: null, cancellationToken))
        {
            notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// Creates a club over HTTP for the authenticated caller.
    /// </summary>
    /// <param name="client">The caller client.</param>
    /// <param name="name">The club name.</param>
    /// <param name="city">The club city.</param>
    /// <param name="state">The club state.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput
        {
            Name = name,
            City = city,
            State = state
        }, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    /// <summary>
    /// Refreshes claims in the authentication cookie after a club membership mutation.
    /// </summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes once refresh is confirmed.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Updates seeded Identity user names and optional club membership using the admin context.
    /// </summary>
    /// <param name="email">The user email to update.</param>
    /// <param name="firstName">The first name.</param>
    /// <param name="lastName">The last name.</param>
    /// <param name="clubId">The optional club assignment.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds a player with one active and one closed assigned participation for history-preservation verification.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>Identifiers used by assertions.</returns>
    private async Task<(long PlayerId, long TeamId, long ActiveCampaignId, long HistoricalCampaignId)> SeedResolvedPlayerAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var actorUserId = await context.Users.Where(u => u.ClubId == clubId).Select(u => u.Id).FirstAsync(cancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Season-{Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);

        var activeCampaign = new CampaignEntity
        {
            Name = "Active History Campaign",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var historicalCampaign = new CampaignEntity
        {
            Name = "Historical Closed Campaign",
            StartDate = new DateOnly(2025, 6, 1),
            EndDate = new DateOnly(2025, 7, 1),
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow,
            ClosedById = actorUserId,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Campaigns.AddRange(activeCampaign, historicalCampaign);

        var team = new TeamEntity
        {
            Name = $"Team-{Guid.CreateVersion7():N}",
            GraduationYear = 2030,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Teams.Add(team);

        var player = new PlayerEntity
        {
            FirstName = "Resolved",
            LastName = "History",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Players.Add(player);
        await context.SaveChangesAsync(cancellationToken);

        context.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = activeCampaign.CampaignId,
                TeamId = team.TeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = clubId,
                CreatedById = actorUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = historicalCampaign.CampaignId,
                TeamId = team.TeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = clubId,
                CreatedById = actorUserId
            });
        await context.SaveChangesAsync(cancellationToken);

        return (player.PlayerId, team.TeamId, activeCampaign.CampaignId, historicalCampaign.CampaignId);
    }

    /// <summary>
    /// Seeds a player blocked from archive by one active undecided participation.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The identifiers used by blocker assertions.</returns>
    private async Task<(long PlayerId, long CampaignId, long ParticipationId)> SeedBlockedPlayerAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var actorUserId = await context.Users.Where(u => u.ClubId == clubId).Select(u => u.Id).FirstAsync(cancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Season-{Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            Name = "Active Blocker Campaign",
            StartDate = new DateOnly(2026, 8, 1),
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Campaigns.Add(campaign);

        var player = new PlayerEntity
        {
            FirstName = "Blocked",
            LastName = "Player",
            DateOfBirth = new DateOnly(2013, 2, 2),
            GraduationYear = 2031,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Players.Add(player);
        await context.SaveChangesAsync(cancellationToken);

        var participation = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            PlacementOutcome = PlacementOutcome.Undecided,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.PlayerCampaignAssignments.Add(participation);
        await context.SaveChangesAsync(cancellationToken);

        return (player.PlayerId, campaign.CampaignId, participation.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Seeds a simple player with no campaign assignments.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded player identifier.</returns>
    private async Task<long> SeedSimplePlayerAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var actorUserId = await context.Users.Where(u => u.ClubId == clubId).Select(u => u.Id).FirstAsync(cancellationToken);

        var player = new PlayerEntity
        {
            FirstName = "Simple",
            LastName = Guid.CreateVersion7().ToString("N"),
            DateOfBirth = new DateOnly(2011, 1, 1),
            GraduationYear = 2029,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Players.Add(player);
        await context.SaveChangesAsync(cancellationToken);
        return player.PlayerId;
    }

    /// <summary>
    /// Creates a unique test email.
    /// </summary>
    /// <param name="prefix">A human-readable scenario prefix.</param>
    /// <returns>A unique email value.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
