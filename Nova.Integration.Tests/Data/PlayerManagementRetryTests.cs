using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies player create and update mutations remain correct when Npgsql retries a failed
/// transaction.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerManagementRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies PostgreSQL rejects two players in the same club with the same creation-operation
    /// identifier.
    /// </summary>
    [Fact]
    public async Task CreationOperationId_RejectsDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var creationOperationId = Guid.CreateVersion7();

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using var db = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            Name = $"Idempotency Constraint Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        db.Players.AddRange(
            CreatePlayer("First", club.ClubId, actorUserId, creationOperationId),
            CreatePlayer("Second", club.ClubId, actorUserId, creationOperationId));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies a player transaction that committed before a transient connection failure is
    /// recognized by its stable operation identifier and is not replayed as a duplicate insert.
    /// </summary>
    [Fact]
    public async Task Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long activeCampaignId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            var club = new ClubEntity
            {
                Name = $"Ambiguous Commit Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = actorUserId
            };
            seed.Clubs.Add(club);
            await seed.SaveChangesAsync(cancellationToken);

            var season = new SeasonEntity
            {
                Name = $"Ambiguous Commit Season {suffix}",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Seasons.Add(season);
            await seed.SaveChangesAsync(cancellationToken);

            var campaign = new CampaignEntity
            {
                Name = $"Ambiguous Commit Campaign {suffix}",
                StartDate = new DateOnly(2026, 6, 1),
                Status = CampaignStatus.Active,
                SeasonId = season.SeasonId,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Campaigns.Add(campaign);
            await seed.SaveChangesAsync(cancellationToken);

            clubId = club.ClubId;
            activeCampaignId = campaign.CampaignId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new PlayerManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<PlayerManagementService>.Instance);

        var result = await service.CreateAsync(
            new CreatePlayerInput
            {
                FirstName = "Ambiguous",
                LastName = "Commit",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030
            },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var players = await verify.Players
            .Where(player => player.ClubId == clubId
                && player.FirstName == "Ambiguous"
                && player.LastName == "Commit")
            .Select(player => player.PlayerId)
            .ToListAsync(cancellationToken);
        players.ShouldBe([result.Value.PlayerId]);

        var assignments = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerId == result.Value.PlayerId)
            .Select(assignment => assignment.CampaignId)
            .ToListAsync(cancellationToken);
        assignments.ShouldBe([activeCampaignId]);
    }

    /// <summary>
    /// Verifies a transient post-save failure during player creation rolls back and retries with a
    /// fresh context and transaction.
    /// </summary>
    [Fact]
    public async Task Create_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long activeCampaignId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            var club = new ClubEntity
            {
                Name = $"Retry Create Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = actorUserId
            };
            seed.Clubs.Add(club);
            await seed.SaveChangesAsync(cancellationToken);

            var season = new SeasonEntity
            {
                Name = $"Retry Create Season {suffix}",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Seasons.Add(season);
            await seed.SaveChangesAsync(cancellationToken);

            var activeCampaign = new CampaignEntity
            {
                Name = $"Retry Create Campaign {suffix}",
                StartDate = new DateOnly(2026, 6, 1),
                Status = CampaignStatus.Active,
                SeasonId = season.SeasonId,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Campaigns.Add(activeCampaign);
            await seed.SaveChangesAsync(cancellationToken);

            clubId = club.ClubId;
            activeCampaignId = activeCampaign.CampaignId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new PlayerManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<PlayerManagementService>.Instance);

        var result = await service.CreateAsync(
            new CreatePlayerInput
            {
                FirstName = "Retry",
                LastName = "Create",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030
            },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(4);

        await using var verify = fixture.CreateAdminContext();
        var createdPlayers = await verify.Players
            .Where(player => player.ClubId == clubId && player.FirstName == "Retry" && player.LastName == "Create")
            .Select(player => new { player.PlayerId, player.ClubId, player.LifecycleStatus })
            .ToListAsync(cancellationToken);
        createdPlayers.Count.ShouldBe(1);
        createdPlayers[0].PlayerId.ShouldBe(result.Value.PlayerId);
        createdPlayers[0].LifecycleStatus.ShouldBe(LifecycleStatus.Active);

        var assignments = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerId == result.Value.PlayerId)
            .Select(assignment => assignment.CampaignId)
            .ToListAsync(cancellationToken);
        assignments.ShouldBe([activeCampaignId]);
    }

    /// <summary>
    /// Verifies a transient post-save failure during player update rolls back and retries with a
    /// fresh context and transaction.
    /// </summary>
    [Fact]
    public async Task Update_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long playerId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            var club = new ClubEntity
            {
                Name = $"Retry Update Club {suffix}",
                City = "Seattle",
                State = "WA",
                CreatedById = actorUserId
            };
            seed.Clubs.Add(club);
            await seed.SaveChangesAsync(cancellationToken);

            var player = new PlayerEntity
            {
                FirstName = "Before",
                LastName = "Retry",
                DateOfBirth = new DateOnly(2011, 5, 1),
                GraduationYear = 2029,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Players.Add(player);
            await seed.SaveChangesAsync(cancellationToken);

            clubId = club.ClubId;
            playerId = player.PlayerId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new PlayerManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<PlayerManagementService>.Instance);

        var result = await service.UpdateAsync(
            new UpdatePlayerInput
            {
                PlayerId = playerId,
                FirstName = "After",
                LastName = "Retry",
                DateOfBirth = new DateOnly(2011, 5, 1),
                GraduationYear = 2030
            },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var updatedPlayer = await verify.Players
            .Where(player => player.PlayerId == playerId)
            .Select(player => new
            {
                player.FirstName,
                player.LastName,
                player.GraduationYear
            })
            .SingleAsync(cancellationToken);

        updatedPlayer.FirstName.ShouldBe("After");
        updatedPlayer.LastName.ShouldBe("Retry");
        updatedPlayer.GraduationYear.ShouldBe(2030);
    }

    /// <summary>
    /// Sets the current simulated user for the fixture-backed tenant contexts.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isAdmin">Whether the simulated user is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isAdmin;
    }

    /// <summary>
    /// Creates a player entity for persistence-focused idempotency tests.
    /// </summary>
    /// <param name="firstName">The player's first name.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="creationOperationId">The stable creation-operation identifier.</param>
    /// <returns>A new player entity ready to persist.</returns>
    private static PlayerEntity CreatePlayer(
        string firstName,
        long clubId,
        long actorUserId,
        Guid creationOperationId) => new()
    {
        FirstName = firstName,
        LastName = "Idempotency",
        DateOfBirth = new DateOnly(2012, 1, 1),
        GraduationYear = 2030,
        ClubId = clubId,
        CreationOperationId = creationOperationId,
        CreatedById = actorUserId
    };
}
