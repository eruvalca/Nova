using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies tag-definition create and update mutations remain correct when Npgsql retries a failed
/// transaction, including ambiguous commits and probe-then-write uniqueness races.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TagDefinitionRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient provider failure while the create mutation is still probing for a
    /// duplicate name is retried with a fresh context and leaves exactly one tag definition behind.
    /// </summary>
    [Fact]
    public async Task Create_RetriesWithFreshContext_AfterTransientReadFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var tagName = $"Retry Read Tag {suffix}";
        long clubId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Retry Read Club {suffix}", actorUserId, cancellationToken);
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstPlayerTagReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TagDefinitionService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        var result = await service.CreateAsync(
            new CreateTagDefinitionInput { Name = tagName, Color = "#112233" },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThan(1);

        await using var verify = fixture.CreateAdminContext();
        var createdTags = await verify.PlayerTags
            .Where(tag => tag.ClubId == clubId && tag.Name == tagName)
            .Select(tag => tag.PlayerTagId)
            .ToListAsync(cancellationToken);
        createdTags.ShouldBe([result.Value.PlayerTagId]);
    }

    /// <summary>
    /// Verifies a tag-definition transaction that committed before a transient connection failure is
    /// recognized by its stable operation identifier and is not replayed as a duplicate insert.
    /// </summary>
    [Fact]
    public async Task Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var tagName = $"Ambiguous Commit Tag {suffix}";
        long clubId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Ambiguous Commit Club {suffix}", actorUserId, cancellationToken);
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TagDefinitionService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        var result = await service.CreateAsync(
            new CreateTagDefinitionInput { Name = tagName, Color = "#445566" },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var createdTags = await verify.PlayerTags
            .Where(tag => tag.ClubId == clubId && tag.Name == tagName)
            .Select(tag => tag.PlayerTagId)
            .ToListAsync(cancellationToken);
        createdTags.ShouldBe([result.Value.PlayerTagId]);
    }

    /// <summary>
    /// Verifies a transient post-save failure during tag-definition creation rolls back and retries
    /// with a fresh context and transaction without leaving a duplicate tag definition behind.
    /// </summary>
    [Fact]
    public async Task Create_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var tagName = $"Retry Create Tag {suffix}";
        long clubId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Retry Create Club {suffix}", actorUserId, cancellationToken);
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TagDefinitionService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        var result = await service.CreateAsync(
            new CreateTagDefinitionInput { Name = tagName, Color = "#778899" },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThan(1);

        await using var verify = fixture.CreateAdminContext();
        var createdTags = await verify.PlayerTags
            .Where(tag => tag.ClubId == clubId && tag.Name == tagName)
            .Select(tag => tag.PlayerTagId)
            .ToListAsync(cancellationToken);
        createdTags.ShouldBe([result.Value.PlayerTagId]);
    }

    /// <summary>
    /// Verifies a transient post-save failure during a tag-definition update rolls back and retries
    /// with a fresh context and transaction.
    /// </summary>
    [Fact]
    public async Task Update_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var updatedName = $"After Retry {suffix}";
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Retry Update Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Before Retry {suffix}", clubId, actorUserId);
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TagDefinitionService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        var result = await service.UpdateAsync(
            new UpdateTagDefinitionInput { TagId = tagId, Name = updatedName, Color = "#AABBCC" },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThan(1);

        await using var verify = fixture.CreateAdminContext();
        var updatedTag = await verify.PlayerTags
            .Where(tag => tag.PlayerTagId == tagId)
            .Select(tag => new { tag.Name, tag.Color })
            .SingleAsync(cancellationToken);

        updatedTag.Name.ShouldBe(updatedName);
        updatedTag.Color.ShouldBe("#AABBCC");
    }

    /// <summary>
    /// Verifies an update that loses the race to the unique normalized-name index is translated into a
    /// conflict instead of letting the provider exception escape.
    /// </summary>
    /// <remarks>
    /// The service probes for a duplicate before writing, so the losing update can only reach the
    /// database constraint when the conflicting tag definition appears after that probe. Committing the
    /// conflicting definition from an independent context immediately after the probe reproduces that
    /// window deterministically instead of relying on two updates interleaving by chance.
    /// </remarks>
    [Fact]
    public async Task Update_ReportsConflict_WhenDuplicateAppearsAfterTheProbe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var contestedName = $"Contested {suffix}";
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Update Conflict Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Original {suffix}", clubId, actorUserId);
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var conflictInterceptor = new InsertAfterPlayerTagExistsProbeInterceptor(async () =>
        {
            await using var conflicting = fixture.CreateAdminContext();
            conflicting.PlayerTags.Add(CreateTag(contestedName, clubId, actorUserId));
            await conflicting.SaveChangesAsync(CancellationToken.None);
        });

        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            conflictInterceptor);
        var service = new TagDefinitionService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        var result = await service.UpdateAsync(
            new UpdateTagDefinitionInput { TagId = tagId, Name = contestedName, Color = "#DDE0EE" },
            cancellationToken);

        conflictInterceptor.InsertCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("the losing update must surface as a conflict, not a provider exception");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var names = await verify.PlayerTags
            .Where(tag => tag.ClubId == clubId)
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken);

        names.ShouldBe([$"Original {suffix}", contestedName], ignoreOrder: true);
    }

    /// <summary>
    /// Verifies a create that loses the race to the unique normalized-name index is translated into a
    /// conflict instead of letting the provider exception escape.
    /// </summary>
    /// <remarks>
    /// The service probes for a duplicate before inserting, so the losing create can only reach the
    /// database constraint when the conflicting tag definition appears after that probe. Committing the
    /// conflicting definition from an independent context immediately after the probe reproduces that
    /// window deterministically instead of relying on two creates interleaving by chance.
    /// </remarks>
    [Fact]
    public async Task Create_ReportsConflict_WhenDuplicateAppearsAfterTheProbe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var contestedName = $"Contested Create {suffix}";
        long clubId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Create Conflict Club {suffix}", actorUserId, cancellationToken);
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var conflictInterceptor = new InsertAfterPlayerTagExistsProbeInterceptor(async () =>
        {
            await using var conflicting = fixture.CreateAdminContext();
            conflicting.PlayerTags.Add(CreateTag(contestedName, clubId, actorUserId));
            await conflicting.SaveChangesAsync(CancellationToken.None);
        });

        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            conflictInterceptor);
        var service = new TagDefinitionService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        var result = await service.CreateAsync(
            new CreateTagDefinitionInput { Name = contestedName, Color = "#0A0B0C" },
            cancellationToken);

        conflictInterceptor.InsertCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("the losing create must surface as a conflict, not a provider exception");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var names = await verify.PlayerTags
            .Where(tag => tag.ClubId == clubId)
            .Select(tag => tag.Name)
            .ToListAsync(cancellationToken);

        names.ShouldBe([contestedName]);
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
    /// Creates an active tag-definition entity for persistence-focused retry tests.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <returns>A new tag-definition entity ready to persist.</returns>
    private static PlayerTagEntity CreateTag(string name, long clubId, long actorUserId) => new()
    {
        Name = name,
        NormalizedName = name.Trim().ToUpperInvariant(),
        Color = "#00FF00",
        ClubId = clubId,
        CreatedById = actorUserId
    };
}
