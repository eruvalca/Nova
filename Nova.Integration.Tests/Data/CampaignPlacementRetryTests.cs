using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign placement mutations remain correct when Npgsql retries a failed transaction.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient failure raised before the database commits is retried on a fresh context
    /// and the placement is persisted exactly once.
    /// </summary>
    [Fact]
    public async Task UpdatePlacement_RetriesFailedCommit_AndPersistsReplacementToken()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstTransactionCommitInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignPlacementService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);

        var result = await ((ICampaignPlacementService)service).UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue("a pre-commit transient failure must be retried to a successful placement");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, TestContext.Current.CancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(result.Value.ConcurrencyToken);
        persisted.ConcurrencyToken.ShouldNotBe(expectedToken);
    }

    /// <summary>
    /// Verifies a placement whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious conflict against its own token.
    /// </summary>
    [Fact]
    public async Task UpdatePlacement_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignPlacementService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);

        var result = await ((ICampaignPlacementService)service).UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue("an ambiguous commit must be verified rather than replayed into a conflict");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, TestContext.Current.CancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(result.Value.ConcurrencyToken);
        persisted.ConcurrencyToken.ShouldNotBe(expectedToken);
    }

    /// <summary>
    /// Seeds one club, season, campaign, player, team, and participation owned by it so a placement
    /// mutation can exercise the retry path against a committed baseline.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <returns>The seeded club, team, participation, and participation concurrency token.</returns>
    private async Task<(long ClubId, long TeamId, long AssignmentId, Guid ConcurrencyToken)> SeedPlacementDataAsync(
        long actorUserId,
        string suffix)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Retry Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Retry Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Place",
            LastName = $"Retry Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Retry Team {suffix}",
            GraduationYear = 2029,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };

        seed.AddRange(season, campaign, player, team);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var concurrencyToken = Guid.NewGuid();
        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7,
            ConcurrencyToken = concurrencyToken
        };
        seed.Add(assignment);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (club.ClubId, team.TeamId, assignment.PlayerCampaignAssignmentId, concurrencyToken);
    }
}
