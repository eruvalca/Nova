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
                Name = $"Retry Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = actorUserId
            };
            seed.Clubs.Add(club);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

            var player = new PlayerEntity
            {
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
}
