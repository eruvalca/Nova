using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies tag-definition migration application and PostgreSQL uniqueness/tenant-integrity
/// constraints that the SQLite unit harness cannot reproduce.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TagDefinitionPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the clean Aspire database applied the tag-definition uniqueness migration and created
    /// the filtered unique index used to enforce case-insensitive per-club name uniqueness.
    /// </summary>
    [Fact]
    public async Task Migration_AppliesTagDefinitionUniquenessSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(cancellationToken);

        appliedMigrations.ShouldContain(
            migration => migration.EndsWith("_AddTagDefinitionUniquenessAndCreationOperationId", StringComparison.Ordinal));

        var indexExists = await db.Database
            .SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE indexname = 'IX_PlayerTags_ClubId_NormalizedName'
                ) AS "Value"
                """)
            .SingleAsync(cancellationToken);
        indexExists.ShouldBeTrue("the filtered unique normalized-name index must be present");
    }

    /// <summary>
    /// Verifies PostgreSQL rejects two active tag definitions in the same club whose names differ only
    /// by case, because their normalized names collide on the filtered unique index.
    /// </summary>
    [Fact]
    public async Task NormalizedName_RejectsCaseInsensitiveDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");

        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var clubId = await SeedClubAsync(db, $"Tag Uniqueness Club {suffix}", actorUserId, cancellationToken);

        db.PlayerTags.AddRange(
            CreateTag($"Forward {suffix}", "FORWARD", clubId, actorUserId, Guid.CreateVersion7()),
            CreateTag($"forward {suffix}", "FORWARD", clubId, actorUserId, Guid.CreateVersion7()));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies PostgreSQL permits the same normalized name in different clubs, confirming the
    /// uniqueness constraint is scoped per club rather than global.
    /// </summary>
    [Fact]
    public async Task NormalizedName_AllowsSameNameInDifferentClubs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");

        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var clubAId = await SeedClubAsync(db, $"Tag Cross-Club A {suffix}", actorUserId, cancellationToken);
        var clubBId = await SeedClubAsync(db, $"Tag Cross-Club B {suffix}", actorUserId, cancellationToken);

        db.PlayerTags.AddRange(
            CreateTag($"Shared {suffix}", "SHARED", clubAId, actorUserId, Guid.CreateVersion7()),
            CreateTag($"Shared {suffix}", "SHARED", clubBId, actorUserId, Guid.CreateVersion7()));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies PostgreSQL rejects two tag definitions in the same club with the same
    /// creation-operation identifier, preserving create idempotency under ambiguous commits.
    /// </summary>
    [Fact]
    public async Task CreationOperationId_RejectsDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var creationOperationId = Guid.CreateVersion7();

        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var clubId = await SeedClubAsync(db, $"Tag Idempotency Club {suffix}", actorUserId, cancellationToken);

        db.PlayerTags.AddRange(
            CreateTag($"First {suffix}", $"FIRST{suffix}", clubId, actorUserId, creationOperationId),
            CreateTag($"Second {suffix}", $"SECOND{suffix}", clubId, actorUserId, creationOperationId));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Sets the simulated current user for the fixture-backed tenant contexts.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    /// <summary>
    /// Persists one club and returns its identifier.
    /// </summary>
    /// <param name="db">The admin context used to bypass tenant filters while seeding.</param>
    /// <param name="name">The club name.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="cancellationToken">A token that cancels the seed operation.</param>
    /// <returns>The seeded club identifier.</returns>
    private static async Task<long> SeedClubAsync(
        NovaAdminDbContext db,
        string name,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var club = new ClubEntity
        {
            Name = name,
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);
        return club.ClubId;
    }

    /// <summary>
    /// Creates an active tag-definition entity for persistence-focused constraint tests.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="normalizedName">The case-folded normalized name.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="creationOperationId">The stable creation-operation identifier.</param>
    /// <returns>A new tag-definition entity ready to persist.</returns>
    private static PlayerTagEntity CreateTag(
        string name,
        string normalizedName,
        long clubId,
        long actorUserId,
        Guid creationOperationId) => new()
        {
            Name = name,
            NormalizedName = normalizedName,
            Color = "#AABBCC",
            ClubId = clubId,
            CreationOperationId = creationOperationId,
            CreatedById = actorUserId
        };
}
