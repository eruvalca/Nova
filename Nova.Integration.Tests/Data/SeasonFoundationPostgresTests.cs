using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Seasons;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
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

    /// <summary>Verifies an advancement predecessor cannot reference another club's season.</summary>
    [Fact]
    public async Task CreationPredecessorForeignKey_RejectsCrossClubSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorId = Random.Shared.NextInt64(1, long.MaxValue);
        var clubA = NewClub($"Predecessor FK A {suffix}", actorId);
        var clubB = NewClub($"Predecessor FK B {suffix}", actorId);
        db.Clubs.AddRange(clubA, clubB);
        await db.SaveChangesAsync(cancellationToken);
        var seasonB = NewSeason($"Predecessor B {suffix}", clubB.ClubId, actorId);
        db.Seasons.Add(seasonB);
        await db.SaveChangesAsync(cancellationToken);
        var invalid = NewSeason($"Predecessor A {suffix}", clubA.ClubId, actorId);
        invalid.CreationPreviousSeasonId = seasonB.SeasonId;
        db.Seasons.Add(invalid);

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

        var result = await new SeasonCommandService(
            factory,
            fixture.CurrentUser,
            NullLogger<SeasonCommandService>.Instance).CreateAsync(
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
        committed.CreationKind.ShouldBe(SeasonCreationKind.Standalone);
        committed.CreationPreviousSeasonId.ShouldBeNull();
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

        var result = await new SeasonCommandService(
            factory,
            fixture.CurrentUser,
            NullLogger<SeasonCommandService>.Instance).StartNextAsync(
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
        var committed = await verify.Seasons.SingleAsync(
            season => season.ClubId == club.ClubId && season.CreationOperationId == operationId,
            cancellationToken);
        committed.CreationKind.ShouldBe(SeasonCreationKind.Advancement);
        committed.CreationPreviousSeasonId.ShouldBe(current.SeasonId);
        (await verify.Clubs.SingleAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken)).CurrentSeasonId.ShouldBe(result.Value.CurrentSeason.SeasonId);
    }

    /// <summary>
    /// Verifies campaign metadata takes the club-season lock before the campaign lock, so a season
    /// update waiting behind it observes the committed campaign dates and rejects an invalid window.
    /// </summary>
    [Fact]
    public async Task CampaignMetadataAndSeasonUpdate_PreserveCampaignWindow_WhenMetadataWinsSeasonLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedMutationRaceAsync(closedCampaign: false, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId);
        var metadataGate = new AdvisoryLockGateInterceptor();
        var metadataService = new CampaignMetadataService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                metadataGate),
            fixture.CurrentUser,
            NullLogger<CampaignMetadataService>.Instance);
        var seasonService = new SeasonCommandService(
            new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<SeasonCommandService>.Instance);

        var metadataTask = metadataService.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = seed.CampaignId,
                Name = seed.CampaignName,
                SeasonId = seed.SeasonId,
                StartDate = new DateOnly(2026, 6, 1),
                PlannedEndDate = new DateOnly(2026, 11, 30)
            },
            cancellationToken);

        try
        {
            await metadataGate.WaitForAcquiredAsync(cancellationToken);
            var seasonTask = seasonService.UpdateAsync(
                seed.SeasonId,
                new UpdateSeasonInput
                {
                    ExpectedConcurrencyToken = seed.SeasonConcurrencyToken,
                    Name = seed.SeasonName,
                    StartDate = new DateOnly(2026, 1, 1),
                    EndDate = new DateOnly(2026, 6, 30)
                },
                cancellationToken);

            await using var lockProbe = fixture.CreateAdminContext();
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
                lockProbe,
                ClubSeasonLockKey(seed.ClubId),
                cancellationToken);
            metadataGate.Release();

            var metadataResult = await metadataTask;
            var seasonResult = await seasonTask;
            metadataResult.IsSuccess.ShouldBeTrue();
            seasonResult.IsProblem.ShouldBeTrue();
            seasonResult.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        }
        finally
        {
            metadataGate.Release();
        }

        await using var verify = fixture.CreateAdminContext();
        var season = await verify.Seasons.SingleAsync(
            candidate => candidate.SeasonId == seed.SeasonId,
            cancellationToken);
        var campaign = await verify.Campaigns.SingleAsync(
            candidate => candidate.CampaignId == seed.CampaignId,
            cancellationToken);
        season.EndDate.ShouldBe(new DateOnly(2026, 12, 31));
        campaign.EndDate.ShouldBe(new DateOnly(2026, 11, 30));
        campaign.EndDate!.Value.ShouldBeLessThanOrEqualTo(season.EndDate!.Value);
    }

    /// <summary>
    /// Verifies reopen takes the club-season lock before the campaign lock, so advancement waiting
    /// behind it observes the Active campaign and cannot make that campaign historical.
    /// </summary>
    [Fact]
    public async Task CampaignReopenAndAdvancement_RejectAdvancement_WhenReopenWinsSeasonLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedMutationRaceAsync(closedCampaign: true, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId);
        var reopenGate = new AdvisoryLockGateInterceptor();
        var reopenService = new CampaignLifecycleService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                reopenGate),
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
        var seasonService = new SeasonCommandService(
            new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<SeasonCommandService>.Instance);

        var reopenTask = reopenService.ReopenAsync(seed.CampaignId, cancellationToken);

        try
        {
            await reopenGate.WaitForAcquiredAsync(cancellationToken);
            var advancementTask = seasonService.StartNextAsync(
                new StartNextSeasonInput
                {
                    OperationId = Guid.CreateVersion7(),
                    ExpectedCurrentSeasonId = seed.SeasonId,
                    Name = $"Next {seed.Suffix}",
                    StartDate = new DateOnly(2027, 1, 1)
                },
                cancellationToken);

            await using var lockProbe = fixture.CreateAdminContext();
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
                lockProbe,
                ClubSeasonLockKey(seed.ClubId),
                cancellationToken);
            reopenGate.Release();

            var reopenResult = await reopenTask;
            var advancementResult = await advancementTask;
            reopenResult.IsT0.ShouldBeTrue();
            advancementResult.IsProblem.ShouldBeTrue();
            advancementResult.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        }
        finally
        {
            reopenGate.Release();
        }

        await using var verify = fixture.CreateAdminContext();
        (await verify.Clubs.SingleAsync(
            club => club.ClubId == seed.ClubId,
            cancellationToken)).CurrentSeasonId.ShouldBe(seed.SeasonId);
        (await verify.Campaigns.SingleAsync(
            campaign => campaign.CampaignId == seed.CampaignId,
            cancellationToken)).Status.ShouldBe(CampaignStatus.Active);
        (await verify.Seasons.AnyAsync(
            season => season.ClubId == seed.ClubId && season.Name == $"Next {seed.Suffix}",
            cancellationToken)).ShouldBeFalse();
    }

    private void ActAs(long actorUserId, long clubId)
    {
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
    }

    private async Task<SeasonMutationRaceSeed> SeedMutationRaceAsync(
        bool closedCampaign,
        CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var club = NewClub($"Season Mutation Race {suffix}", actorUserId);
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);
        var season = NewSeason($"Season {suffix}", club.ClubId, actorUserId);
        season.EndDate = new DateOnly(2026, 12, 31);
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 15),
            Status = closedCampaign ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closedCampaign ? DateTimeOffset.UtcNow : null,
            ClosedById = closedCampaign ? actorUserId : null,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        return new SeasonMutationRaceSeed(
            club.ClubId,
            season.SeasonId,
            campaign.CampaignId,
            actorUserId,
            suffix,
            season.Name,
            campaign.Name,
            season.ConcurrencyToken);
    }

    private static long ClubSeasonLockKey(long clubId) => (long.MinValue / 16) + clubId;

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

    private sealed record SeasonMutationRaceSeed(
        long ClubId,
        long SeasonId,
        long CampaignId,
        long ActorUserId,
        string Suffix,
        string SeasonName,
        string CampaignName,
        Guid SeasonConcurrencyToken);

    private sealed class FixtureDbContextFactory(NovaAppHostFixture fixture)
        : IDbContextFactory<NovaDbContext>
    {
        public NovaDbContext CreateDbContext() => fixture.CreateTenantContext();

        public Task<NovaDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(fixture.CreateTenantContext());
    }
}
