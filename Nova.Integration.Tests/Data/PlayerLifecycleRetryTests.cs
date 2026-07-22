using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Security;
using Npgsql;
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

    /// <summary>
    /// Creates retry-enabled tenant contexts while tracking how many execution attempts requested a context.
    /// </summary>
    private sealed class RetryingTenantDbContextFactory(
        string connectionString,
        ICurrentUserProvider currentUser,
        FailFirstSaveChangesInterceptor failureInterceptor) : IDbContextFactory<NovaDbContext>
    {
        /// <summary>
        /// Tracks the number of contexts created by the service.
        /// </summary>
        private int _createdContextCount;

        /// <summary>
        /// Gets the number of contexts created for execution-strategy setup and mutation attempts.
        /// </summary>
        public int CreatedContextCount => Volatile.Read(ref _createdContextCount);

        /// <inheritdoc />
        public NovaDbContext CreateDbContext() => CreateContext();

        /// <inheritdoc />
        public ValueTask<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(CreateContext());

        /// <summary>
        /// Creates one retry-enabled tenant context with the transient-failure interceptor attached.
        /// </summary>
        /// <returns>A new tenant context owned by the caller.</returns>
        private NovaDbContext CreateContext()
        {
            Interlocked.Increment(ref _createdContextCount);
            var options = new DbContextOptionsBuilder<NovaDbContext>()
                .UseNpgsql(
                    connectionString,
                    providerOptions => providerOptions.EnableRetryOnFailure(
                        maxRetryCount: 1,
                        maxRetryDelay: TimeSpan.Zero,
                        errorCodesToAdd: null))
                .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)
                .AddInterceptors(new TenantSaveChangesInterceptor(), failureInterceptor)
                .Options;

            return new NovaDbContext(options, currentUser);
        }
    }

    /// <summary>
    /// Simulates one transient provider failure after an update executes but before its transaction commits.
    /// </summary>
    private sealed class FailFirstSaveChangesInterceptor : SaveChangesInterceptor
    {
        /// <summary>
        /// Controls whether the next completed save should fail.
        /// </summary>
        private int _shouldFail = 1;

        /// <summary>
        /// Tracks the number of injected transient failures.
        /// </summary>
        private int _failureCount;

        /// <summary>
        /// Gets the number of transient failures injected by this interceptor.
        /// </summary>
        public int FailureCount => Volatile.Read(ref _failureCount);

        /// <inheritdoc />
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _shouldFail, 0) == 1)
            {
                Interlocked.Increment(ref _failureCount);
                throw new NpgsqlException("Simulated transient save failure.", new TimeoutException());
            }

            return ValueTask.FromResult(result);
        }
    }
}
