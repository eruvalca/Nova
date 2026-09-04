using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign opening serializes its active-player snapshot with roster writers on PostgreSQL.
/// </summary>
/// <param name="fixture">The shared Aspire-backed database fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignOpeningRosterRaceTests(NovaAppHostFixture fixture)
{
    /// <summary>Verifies opening waits for player creation and enrolls the committed roster.</summary>
    [Fact]
    public async Task CampaignOpen_WaitsForPlayerCreation_AndEnrollsCommittedRoster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedDraftAsync(activePlayerCount: 1, cancellationToken);
        ActAsAdmin(seed.ActorUserId, seed.ClubId);
        var gate = new AdvisoryLockGateInterceptor();
        var playerService = new PlayerManagementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate),
            fixture.CurrentUser,
            NullLogger<PlayerManagementService>.Instance);
        var campaignService = CreateCampaignService(new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor()));

        var playerTask = playerService.CreateAsync(
            new CreatePlayerInput
            {
                FirstName = "Concurrent",
                LastName = "Creation",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030
            },
            cancellationToken);
        await gate.WaitForAcquiredAsync(cancellationToken);
        var openTask = campaignService.OpenAsync(
            seed.CampaignId,
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);

        await using var probe = fixture.CreateAdminContext();
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            probe,
            (long.MinValue / 4) + seed.ClubId,
            cancellationToken);
        gate.Release();

        (await playerTask).IsSuccess.ShouldBeTrue();
        var receipt = (await openTask).Value.ShouldBeOfType<OpenCampaignResult>();
        receipt.EnrolledPlayerCount.ShouldBe(2);

        await using var verify = fixture.CreateAdminContext();
        (await verify.PlayerCampaignAssignments.CountAsync(
            assignment => assignment.CampaignId == seed.CampaignId,
            cancellationToken)).ShouldBe(2);
    }

    /// <summary>Verifies opening waits for player archival and excludes the committed archived player.</summary>
    [Fact]
    public async Task CampaignOpen_WaitsForPlayerArchive_AndEnrollsOnlyCommittedActiveRoster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedDraftAsync(activePlayerCount: 2, cancellationToken);
        ActAsAdmin(seed.ActorUserId, seed.ClubId);
        var gate = new AdvisoryLockGateInterceptor();
        var playerService = new PlayerLifecycleService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate),
            fixture.CurrentUser,
            NullLogger<PlayerLifecycleService>.Instance);
        var campaignService = CreateCampaignService(new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor()));

        var archiveTask = playerService.ArchiveAsync(seed.PlayerIds[0], cancellationToken);
        await gate.WaitForAcquiredAsync(cancellationToken);
        var openTask = campaignService.OpenAsync(
            seed.CampaignId,
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);

        await using var probe = fixture.CreateAdminContext();
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            probe,
            (long.MinValue / 4) + seed.ClubId,
            cancellationToken);
        gate.Release();

        (await archiveTask).IsSuccess.ShouldBeTrue();
        var receipt = (await openTask).Value.ShouldBeOfType<OpenCampaignResult>();
        receipt.EnrolledPlayerCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var enrolledPlayerIds = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == seed.CampaignId)
            .Select(assignment => assignment.PlayerId)
            .ToListAsync(cancellationToken);
        enrolledPlayerIds.ShouldBe([seed.PlayerIds[1]]);
    }

    /// <summary>Seeds a Draft in the current season and the requested active roster.</summary>
    /// <param name="activePlayerCount">The number of active players to create.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The identifiers needed by the competing operations.</returns>
    private async Task<RosterRaceSeed> SeedDraftAsync(int activePlayerCount, CancellationToken cancellationToken)
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.CreateVersion7().ToString("N");
        await using var context = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            Name = $"Opening Roster Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        context.Add(club);
        await context.SaveChangesAsync(cancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            Name = $"Opening Roster Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        context.Add(season);
        await context.SaveChangesAsync(cancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        await context.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            Name = $"Opening Roster Draft {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Draft,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var players = Enumerable.Range(0, activePlayerCount)
            .Select(index => new PlayerEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                FirstName = "Roster",
                LastName = $"Player {index + 1} {suffix}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            })
            .ToList();
        context.Add(campaign);
        context.AddRange(players);
        await context.SaveChangesAsync(cancellationToken);

        return new RosterRaceSeed(
            club.ClubId,
            campaign.CampaignId,
            actorUserId,
            players.Select(player => player.PlayerId).ToList());
    }

    /// <summary>Sets the flow-local test actor to the seeded club administrator.</summary>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    private void ActAsAdmin(long actorUserId, long clubId)
    {
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
    }

    /// <summary>Creates the campaign lifecycle service for a supplied live-database factory.</summary>
    /// <param name="factory">The retry-enabled tenant context factory.</param>
    /// <returns>The configured lifecycle service.</returns>
    private CampaignLifecycleService CreateCampaignService(
        IDbContextFactory<Nova.Data.NovaDbContext> factory)
        => new(factory, fixture.CurrentUser, NullLogger<CampaignLifecycleService>.Instance);

    /// <summary>Carries identifiers for one roster race scenario.</summary>
    /// <param name="ClubId">The club identifier.</param>
    /// <param name="CampaignId">The Draft campaign identifier.</param>
    /// <param name="ActorUserId">The acting administrator identifier.</param>
    /// <param name="PlayerIds">The seeded active-player identifiers.</param>
    private sealed record RosterRaceSeed(
        long ClubId,
        long CampaignId,
        long ActorUserId,
        IReadOnlyList<long> PlayerIds);
}
