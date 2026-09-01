using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies player lifecycle mutations remain correct when Npgsql retries a failed transaction.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerLifecycleRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient post-save failure rolls back and retries with database state loaded by a fresh context.
    /// </summary>
    [Fact]
    public async Task PlayerLifecycle_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        long clubId;
        long playerId;

        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using (var seed = fixture.CreateAdminContext())
        {
            var club = new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Retry Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = actorUserId
            };
            seed.Clubs.Add(club);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

            var player = new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = "Retry",
                LastName = suffix,
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Players.Add(player);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

            clubId = club.ClubId;
            playerId = player.PlayerId;
        }

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new PlayerLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<PlayerLifecycleService>.Instance);

        var result = await service.ArchiveAsync(playerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var playerStatus = await verify.Players
            .Where(player => player.PlayerId == playerId)
            .Select(player => player.LifecycleStatus)
            .SingleAsync(TestContext.Current.CancellationToken);
        playerStatus.ShouldBe(LifecycleStatus.Archived);
    }

    /// <summary>
    /// Verifies an archive whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious "already archived" conflict.
    /// </summary>
    [Fact]
    public async Task PlayerArchive_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, playerId) = await SeedClubAndPlayerAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new PlayerLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<PlayerLifecycleService>.Instance);

        var result = await service.ArchiveAsync(playerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var player = await verify.Players
            .Where(candidate => candidate.PlayerId == playerId)
            .Select(candidate => new { candidate.LifecycleStatus, candidate.ArchivedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        player.ArchivedById.ShouldBe(actorUserId);
    }

    /// <summary>
    /// Verifies the same ambiguous-commit protection applies to restore.
    /// </summary>
    [Fact]
    public async Task PlayerRestore_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, playerId) = await SeedClubAndPlayerAsync(actorUserId, suffix, archived: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new PlayerLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<PlayerLifecycleService>.Instance);

        var result = await service.RestoreAsync(playerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var player = await verify.Players
            .Where(candidate => candidate.PlayerId == playerId)
            .Select(candidate => new { candidate.LifecycleStatus, candidate.ArchivedAt })
            .SingleAsync(TestContext.Current.CancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        player.ArchivedAt.ShouldBeNull();
    }

    /// <summary>
    /// Seeds one club and one player owned by it, bypassing tenant filters.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <param name="archived">Whether the seeded player starts archived.</param>
    /// <returns>The seeded club and player identifiers.</returns>
    private async Task<(long ClubId, long PlayerId)> SeedClubAndPlayerAsync(
        long actorUserId,
        string suffix,
        bool archived = false)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Player Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Retry",
            LastName = suffix,
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            LifecycleStatus = archived ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archived ? DateTimeOffset.UtcNow : null,
            ArchivedById = archived ? actorUserId : null
        };
        seed.Players.Add(player);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (club.ClubId, player.PlayerId);
    }
}
