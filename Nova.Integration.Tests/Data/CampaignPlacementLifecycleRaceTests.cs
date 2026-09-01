using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies placement mutations re-check player lifecycle state after waiting for the player lock.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementLifecycleRaceTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies archiving a player while a placement waits for its lock rejects the stale mutation.
    /// </summary>
    [Fact]
    public async Task PlacementConcurrency_RejectsMutation_WhenPlayerArchivesWhileWaitingForLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, int.MaxValue);
        var seed = await SeedPlacementAsync(actorUserId, cancellationToken);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignPlacementService(
            fixture.CreateTenantContextFactory(),
            fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);

        await using var archiveContext = fixture.CreateAdminContext();
        await using var transaction = await archiveContext.Database.BeginTransactionAsync(cancellationToken);
        await archiveContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({seed.PlayerId})",
            cancellationToken);

        var placementTask = service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                seed.AssignmentId,
                PlacementOutcome.NotSelected,
                teamId: null,
                seed.ConcurrencyToken),
            cancellationToken);

        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            archiveContext,
            seed.PlayerId,
            cancellationToken);

        var player = await archiveContext.Players
            .SingleAsync(candidate => candidate.PlayerId == seed.PlayerId, cancellationToken);
        player.LifecycleStatus = LifecycleStatus.Archived;
        player.ArchivedAt = DateTimeOffset.UtcNow;
        player.ArchivedById = actorUserId;
        await archiveContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = await placementTask;
        result.IsT4.ShouldBeTrue();
        result.AsT4.Detail.ShouldBe("Archived players cannot receive new placement decisions.");

        await using var verify = fixture.CreateAdminContext();
        var assignment = await verify.PlayerCampaignAssignments
            .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == seed.AssignmentId, cancellationToken);
        assignment.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        assignment.TeamId.ShouldBeNull();
        assignment.ConcurrencyToken.ShouldBe(seed.ConcurrencyToken);
    }

    private async Task<PlacementSeed> SeedPlacementAsync(long actorUserId, CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var context = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Lifecycle Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        context.Clubs.Add(club);
        await context.SaveChangesAsync(cancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Lifecycle Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Lifecycle Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Lifecycle",
            LastName = suffix,
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };

        context.AddRange(campaign, player);
        await context.SaveChangesAsync(cancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            ConcurrencyToken = Guid.NewGuid()
        };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        return new PlacementSeed(
            club.ClubId,
            player.PlayerId,
            assignment.PlayerCampaignAssignmentId,
            assignment.ConcurrencyToken);
    }

    private sealed record PlacementSeed(
        long ClubId,
        long PlayerId,
        long AssignmentId,
        Guid ConcurrencyToken);
}
