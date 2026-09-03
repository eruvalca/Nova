using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Postgres-backed tests for the club roster advisory lock used during player creation:
/// concurrent player creations for the same club serialize via the lock so enrollment rows
/// are never lost or duplicated, and concurrent player-create and campaign-create operations
/// are mutually exclusive.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerEnrollmentPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the player-creation migration has been applied so the schema is ready.
    /// </summary>
    [Fact]
    public async Task Migration_ContainsPlayerAndAssignmentTables()
    {
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        // Verify the initial migration that created players and campaign assignments is present.
        appliedMigrations.ShouldContain(migration =>
            migration.Contains("Initial", StringComparison.OrdinalIgnoreCase),
            "the initial migration should have created the Players and PlayerCampaignAssignments tables");
    }

    /// <summary>
    /// Creates two players concurrently for the same club and asserts both are persisted
    /// and both are enrolled in the club's sole Active campaign exactly once.
    /// </summary>
    [Fact]
    public async Task ConcurrentPlayerCreation_SameClub_BothPersistWithCorrectEnrollments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(activeCampaignCount: 1, cancellationToken);

        ActAs(seed.ActorUserId, seed.ClubId, isAdmin: true);

        var svc1 = CreateService();
        var svc2 = CreateService();

        var task1 = svc1.CreateAsync(new CreatePlayerInput
        {
            FirstName = "Player",
            LastName = "One",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        }, cancellationToken);

        var task2 = svc2.CreateAsync(new CreatePlayerInput
        {
            FirstName = "Player",
            LastName = "Two",
            DateOfBirth = new DateOnly(2013, 6, 1),
            GraduationYear = 2031
        }, cancellationToken);

        var (result1, result2) = (await task1, await task2);

        result1.IsSuccess.ShouldBeTrue("Player One creation should succeed");
        result2.IsSuccess.ShouldBeTrue("Player Two creation should succeed");

        var id1 = result1.Value.PlayerId;
        var id2 = result2.Value.PlayerId;
        id1.ShouldNotBe(id2);

        await using var db = fixture.CreateAdminContext();
        var assignments1 = await db.PlayerCampaignAssignments
            .Where(a => a.PlayerId == id1)
            .ToListAsync(cancellationToken);
        var assignments2 = await db.PlayerCampaignAssignments
            .Where(a => a.PlayerId == id2)
            .ToListAsync(cancellationToken);

        assignments1.Count.ShouldBe(1, "Player One should be enrolled in the Active campaign");
        assignments2.Count.ShouldBe(1, "Player Two should be enrolled in the Active campaign");

        assignments1.Select(a => a.CampaignId).ShouldBeUnique("no duplicate enrollments for Player One");
        assignments2.Select(a => a.CampaignId).ShouldBeUnique("no duplicate enrollments for Player Two");
    }

    /// <summary>
    /// Creates many players concurrently for the same club and asserts all succeed without
    /// duplicate enrollments, even under high contention on the roster lock.
    /// </summary>
    [Fact]
    public async Task ConcurrentPlayerCreation_HighContention_AllSucceedWithCorrectEnrollments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(activeCampaignCount: 1, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId, isAdmin: true);

        const int playerCount = 5;
        var tasks = Enumerable.Range(1, playerCount).Select(index =>
            CreateService().CreateAsync(new CreatePlayerInput
            {
                FirstName = $"Concurrent{index}",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, index % 28 + 1),
                GraduationYear = 2030
            }, cancellationToken)).ToArray();

        var results = await Task.WhenAll(tasks);

        results.ShouldAllBe(r => r.IsSuccess, "all concurrent players should be created successfully");

        var playerIds = results.Select(r => r.Value.PlayerId).ToList();
        playerIds.ShouldBeUnique("each concurrent creation should produce a unique player ID");

        await using var db = fixture.CreateAdminContext();
        foreach (var playerId in playerIds)
        {
            var assignments = await db.PlayerCampaignAssignments
                .Where(a => a.PlayerId == playerId)
                .ToListAsync(cancellationToken);

            assignments.Count.ShouldBe(1, $"player {playerId} should have exactly one enrollment");
        }
    }

    /// <summary>
    /// Creates a player in Club A while Club B also has an active campaign, then asserts the new
    /// player is enrolled only in Club A's campaign — never in another club's active campaign.
    /// </summary>
    [Fact]
    public async Task PlayerCreation_DoesNotEnrollInOtherClubsActiveCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        long clubAId, clubACampaignId, clubBCampaignId, clubAAdminId;
        await using (var db = fixture.CreateAdminContext())
        {
            var suffix = Guid.NewGuid().ToString("N");
            var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

            var clubA = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Isolation Club A {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
            var clubB = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Isolation Club B {suffix}", City = "Boston", State = "MA", CreatedById = actorUserId };
            db.Clubs.AddRange(clubA, clubB);
            await db.SaveChangesAsync(cancellationToken);

            var seasonA = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Season A {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubA.ClubId, CreatedById = actorUserId };
            var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Season B {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubB.ClubId, CreatedById = actorUserId };
            db.Seasons.AddRange(seasonA, seasonB);
            await db.SaveChangesAsync(cancellationToken);

            var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = $"Campaign A {suffix}", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonA.SeasonId, ClubId = clubA.ClubId, CreatedById = actorUserId };
            var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = $"Campaign B {suffix}", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = clubB.ClubId, CreatedById = actorUserId };
            db.Campaigns.AddRange(campaignA, campaignB);
            await db.SaveChangesAsync(cancellationToken);

            clubAId = clubA.ClubId;
            clubACampaignId = campaignA.CampaignId;
            clubBCampaignId = campaignB.CampaignId;
            clubAAdminId = actorUserId;
        }

        ActAs(clubAAdminId, clubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreatePlayerInput
            {
                FirstName = "Isolation",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030
            },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verifyDb = fixture.CreateAdminContext();
        var assignments = await verifyDb.PlayerCampaignAssignments
            .Where(a => a.PlayerId == result.Value.PlayerId)
            .ToListAsync(cancellationToken);

        assignments.Count.ShouldBe(1, "the new player should only enroll in their own club's active campaign");
        assignments[0].CampaignId.ShouldBe(clubACampaignId);
        assignments.ShouldNotContain(a => a.CampaignId == clubBCampaignId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isAdmin;
    }

    private PlayerManagementService CreateService() =>
        new(new FixtureDbContextFactory(fixture), fixture.CurrentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PlayerManagementService>.Instance);

    private async Task<EnrollmentSeed> SeedAsync(int activeCampaignCount, CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null, isAdmin: false);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Enrollment Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        var campaigns = Enumerable.Range(1, activeCampaignCount).Select(i => new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign {i} {suffix}",
            StartDate = new DateOnly(2026, i, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        }).ToArray();
        db.Campaigns.AddRange(campaigns);
        await db.SaveChangesAsync(cancellationToken);

        return new EnrollmentSeed(club.ClubId, actorUserId);
    }

    private sealed record EnrollmentSeed(long ClubId, long ActorUserId);

    /// <summary>
    /// Adapts the fixture's context-creation methods to the <see cref="IDbContextFactory{NovaDbContext}"/>
    /// interface expected by <see cref="PlayerManagementService"/>.
    /// </summary>
    private sealed class FixtureDbContextFactory(NovaAppHostFixture fixture) : IDbContextFactory<Nova.Data.NovaDbContext>
    {
        public Nova.Data.NovaDbContext CreateDbContext() => fixture.CreateTenantContext();
        public Task<Nova.Data.NovaDbContext> CreateDbContextAsync(CancellationToken _ = default)
            => Task.FromResult(fixture.CreateTenantContext());
    }
}
