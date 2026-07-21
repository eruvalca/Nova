using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Entities.Base;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies archival lifecycle migration application and PostgreSQL metadata consistency constraints.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class ArchivalLifecyclePostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the clean Aspire database applied the archival lifecycle migration.
    /// </summary>
    [Fact]
    public async Task Migration_AppliesArchivalLifecycleSchema()
    {
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        appliedMigrations.ShouldContain(
            migration => migration.EndsWith("_AddArchivalLifecycle", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies each lifecycle-managed table rejects partial archive provenance.
    /// </summary>
    /// <param name="target">The lifecycle-managed table to test.</param>
    [Theory]
    [InlineData(LifecycleTarget.Player)]
    [InlineData(LifecycleTarget.Team)]
    [InlineData(LifecycleTarget.TagDefinition)]
    public async Task ArchiveMetadataConstraint_RejectsPartialProvenance(LifecycleTarget target)
    {
        var entity = await SeedTargetAsync(target);
        entity.LifecycleStatus = LifecycleStatus.Archived;
        entity.ArchivedAt = DateTimeOffset.UtcNow;
        entity.ArchivedById = null;

        await using var db = fixture.CreateAdminContext();
        db.Update(entity);

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies each lifecycle-managed table rejects archive provenance while the status is Active.
    /// </summary>
    /// <param name="target">The lifecycle-managed table to test.</param>
    [Theory]
    [InlineData(LifecycleTarget.Player)]
    [InlineData(LifecycleTarget.Team)]
    [InlineData(LifecycleTarget.TagDefinition)]
    public async Task ArchiveMetadataConstraint_RejectsProvenance_ForActiveStatus(LifecycleTarget target)
    {
        var entity = await SeedTargetAsync(target);
        entity.LifecycleStatus = LifecycleStatus.Active;
        entity.ArchivedAt = DateTimeOffset.UtcNow;
        entity.ArchivedById = Random.Shared.NextInt64(1, long.MaxValue);

        await using var db = fixture.CreateAdminContext();
        db.Update(entity);

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies the lifecycle constraint rejects values outside the shared Active/Archived representation.
    /// </summary>
    [Fact]
    public async Task LifecycleConstraint_RejectsUndefinedStatus()
    {
        var entity = await SeedTargetAsync(LifecycleTarget.Player);
        entity.LifecycleStatus = (LifecycleStatus)99;

        await using var db = fixture.CreateAdminContext();
        db.Update(entity);

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies lifecycle status concurrency prevents a stale transition from overwriting archive provenance.
    /// </summary>
    [Fact]
    public async Task LifecycleConcurrency_RejectsStaleTransition()
    {
        var seeded = await SeedTargetAsync(LifecycleTarget.TagDefinition);
        var tagDefinitionId = ((PlayerTagEntity)seeded).PlayerTagId;
        await using var first = fixture.CreateAdminContext();
        await using var stale = fixture.CreateAdminContext();

        var firstCopy = await first.PlayerTags
            .SingleAsync(tag => tag.PlayerTagId == tagDefinitionId, TestContext.Current.CancellationToken);
        var staleCopy = await stale.PlayerTags
            .SingleAsync(tag => tag.PlayerTagId == tagDefinitionId, TestContext.Current.CancellationToken);

        firstCopy.LifecycleStatus = LifecycleStatus.Archived;
        firstCopy.ArchivedAt = DateTimeOffset.UtcNow;
        firstCopy.ArchivedById = Random.Shared.NextInt64(1, long.MaxValue);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        staleCopy.LifecycleStatus = LifecycleStatus.Archived;
        staleCopy.ArchivedAt = DateTimeOffset.UtcNow;
        staleCopy.ArchivedById = Random.Shared.NextInt64(1, long.MaxValue);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => stale.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Seeds one lifecycle target in a unique club and returns it detached for invalid-state mutation.
    /// </summary>
    /// <param name="target">The lifecycle-managed entity type to seed.</param>
    /// <returns>The seeded lifecycle entity.</returns>
    private async Task<ArchivableEntity> SeedTargetAsync(LifecycleTarget target)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var db = fixture.CreateAdminContext();
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var club = new ClubEntity
        {
            Name = $"Lifecycle Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ArchivableEntity entity = target switch
        {
            LifecycleTarget.Player => new PlayerEntity
            {
                FirstName = "Lifecycle",
                LastName = suffix,
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            },
            LifecycleTarget.Team => new TeamEntity
            {
                Name = $"Team {suffix}",
                GraduationYear = 2030,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            },
            LifecycleTarget.TagDefinition => new PlayerTagEntity
            {
                Name = $"Tag {suffix}",
                Color = "#ffffff",
                ClubId = club.ClubId,
                CreatedById = actorUserId
            },
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        db.Add(entity);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Entry(entity).State = EntityState.Detached;
        return entity;
    }

    /// <summary>
    /// Identifies the lifecycle-managed table under test.
    /// </summary>
    public enum LifecycleTarget
    {
        /// <summary>
        /// Targets the Players table.
        /// </summary>
        Player,

        /// <summary>
        /// Targets the Teams table.
        /// </summary>
        Team,

        /// <summary>
        /// Targets the PlayerTags table.
        /// </summary>
        TagDefinition,
    }
}
