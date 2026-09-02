using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Features.Seasons;
using Nova.Shared.Features.Seasons;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>Verifies PostgreSQL enforces the tenant-consistent current-season pointer.</summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class SeasonFoundationPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>Verifies the incremental season foundation migration is applied.</summary>
    [Fact]
    public async Task Migration_AppliesCurrentSeasonFoundation()
    {
        await using var db = fixture.CreateAdminContext();
        var migrations = await db.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken);

        migrations.ShouldContain(
            migration => migration.EndsWith("_AddCurrentSeasonFoundation", StringComparison.Ordinal));
    }

    /// <summary>Verifies a club cannot point at another club's season.</summary>
    [Fact]
    public async Task CurrentSeasonForeignKey_RejectsCrossClubPointer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorId = Random.Shared.NextInt64(1, long.MaxValue);
        var clubA = NewClub($"Season FK A {suffix}", actorId);
        var clubB = NewClub($"Season FK B {suffix}", actorId);
        db.Clubs.AddRange(clubA, clubB);
        await db.SaveChangesAsync(cancellationToken);
        var seasonA = NewSeason($"Season A {suffix}", clubA.ClubId, actorId);
        var seasonB = NewSeason($"Season B {suffix}", clubB.ClubId, actorId);
        db.Seasons.AddRange(seasonA, seasonB);
        await db.SaveChangesAsync(cancellationToken);

        clubA.CurrentSeasonId = seasonB.SeasonId;

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>Verifies adding history cannot create another current-season marker.</summary>
    [Fact]
    public async Task AddingHistoricalSeason_DoesNotChangePointer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorId = Random.Shared.NextInt64(1, long.MaxValue);
        var club = NewClub($"Season Pointer {suffix}", actorId);
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);
        var current = NewSeason($"Current {suffix}", club.ClubId, actorId);
        db.Seasons.Add(current);
        await db.SaveChangesAsync(cancellationToken);
        club.CurrentSeasonId = current.SeasonId;
        await db.SaveChangesAsync(cancellationToken);

        db.Seasons.Add(NewSeason($"History {suffix}", club.ClubId, actorId));
        await db.SaveChangesAsync(cancellationToken);

        db.ChangeTracker.Clear();
        (await db.Clubs.SingleAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken)).CurrentSeasonId.ShouldBe(current.SeasonId);
    }

    /// <summary>Verifies ambiguous commits recover only a season installed as current.</summary>
    [Fact]
    public async Task CreateSeason_RecoversCommittedCurrentSeason_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorId = Random.Shared.NextInt64(1, long.MaxValue);
        await using var seed = fixture.CreateAdminContext();
        var club = NewClub($"Season Retry {Guid.NewGuid():N}", actorId);
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(cancellationToken);
        fixture.CurrentUser.UserId = actorId;
        fixture.CurrentUser.ClubId = club.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var interceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            interceptor);
        var operationId = Guid.NewGuid();

        var result = await new SeasonCommandService(factory, fixture.CurrentUser).CreateAsync(
            new CreateSeasonInput
            {
                OperationId = operationId,
                Name = "Recovered Current",
                StartDate = new DateOnly(2026, 1, 1)
            },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        interceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var committed = await verify.Seasons.SingleAsync(
            season => season.ClubId == club.ClubId && season.CreationOperationId == operationId,
            cancellationToken);
        (await verify.Clubs.SingleAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken)).CurrentSeasonId.ShouldBe(committed.SeasonId);
    }

    /// <summary>Verifies a transient save failure retries advancement with a fresh transaction.</summary>
    [Fact]
    public async Task StartNextSeason_RetriesTransientSaveFailure_WithoutDuplicateSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorId = Random.Shared.NextInt64(1, long.MaxValue);
        await using var seed = fixture.CreateAdminContext();
        var club = NewClub($"Season Advance Retry {Guid.NewGuid():N}", actorId);
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(cancellationToken);
        var current = NewSeason("Retry Current", club.ClubId, actorId);
        seed.Seasons.Add(current);
        await seed.SaveChangesAsync(cancellationToken);
        club.CurrentSeasonId = current.SeasonId;
        await seed.SaveChangesAsync(cancellationToken);
        fixture.CurrentUser.UserId = actorId;
        fixture.CurrentUser.ClubId = club.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var interceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            interceptor);
        var operationId = Guid.NewGuid();

        var result = await new SeasonCommandService(factory, fixture.CurrentUser).StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = operationId,
                ExpectedCurrentSeasonId = current.SeasonId,
                Name = "Retry Next",
                StartDate = new DateOnly(2027, 1, 1)
            },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        interceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Seasons.CountAsync(
            season => season.ClubId == club.ClubId && season.CreationOperationId == operationId,
            cancellationToken)).ShouldBe(1);
        (await verify.Clubs.SingleAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken)).CurrentSeasonId.ShouldBe(result.Value.CurrentSeason.SeasonId);
    }

    /// <summary>Creates a unique club.</summary>
    private static ClubEntity NewClub(string name, long actorId)
        => new()
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            City = "Austin",
            State = "TX",
            CreatedById = actorId
        };

    /// <summary>Creates a unique season owned by the supplied club.</summary>
    private static SeasonEntity NewSeason(string name, long clubId, long actorId)
        => new()
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            StartDate = new DateOnly(2026, 1, 1),
            ConcurrencyToken = Guid.NewGuid(),
            ClubId = clubId,
            CreatedById = actorId
        };
}
