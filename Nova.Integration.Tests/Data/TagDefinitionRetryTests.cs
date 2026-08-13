using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using OneOf.Types;
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
    /// Verifies an update that committed ambiguously, then lost a race to a newer edit, still reports
    /// success via its durable receipt without replaying and overwriting the newer content.
    /// </summary>
    /// <remarks>
    /// The first update commits ambiguously and pauses just before its receipt-verification read. A
    /// newer update then commits different content. Verification consults the first update's durable
    /// operation receipt rather than comparing the mutable name/color, so the paused update reports
    /// success and never overwrites the newer content.
    /// </remarks>
    [Fact]
    public async Task Update_AmbiguousCommitThenNewerEdit_DoesNotOverwriteNewerContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Update Race Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Original Race Tag {suffix}", clubId, actorUserId);
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var gateInterceptor = new GateReceiptVerificationInterceptor("\"TagDefinitionMutationReceipts\"");
        var firstFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor,
            gateInterceptor);
        var secondFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var firstService = new TagDefinitionService(
            firstFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);
        var secondService = new TagDefinitionService(
            secondFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

        Task<ServiceResult<TagDefinitionDto>> firstEdit;
        try
        {
            // The first update pauses after its ambiguous commit, just before verification reads the receipt.
            firstEdit = firstService.UpdateAsync(
                new UpdateTagDefinitionInput { TagId = tagId, Name = "First edit name", Color = "#111111" },
                cancellationToken);
            await gateInterceptor.WaitForVerificationAttemptAsync(cancellationToken);

            // A newer update commits different content while the first is paused at verification.
            var newerResult = await secondService.UpdateAsync(
                new UpdateTagDefinitionInput { TagId = tagId, Name = "Newer edit name", Color = "#222222" },
                cancellationToken);
            newerResult.IsSuccess.ShouldBeTrue("the newer edit must commit while the first is paused at verification");

            gateInterceptor.Release();
            var firstResult = await firstEdit;
            firstResult.IsSuccess.ShouldBeTrue(
                "the paused edit must verify against its durable receipt and report success");
        }
        finally
        {
            gateInterceptor.Release();
        }

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerTags
            .Where(tag => tag.PlayerTagId == tagId)
            .Select(tag => new { tag.Name, tag.Color })
            .SingleAsync(cancellationToken);
        persisted.Name.ShouldBe("Newer edit name", "the newer edit must survive and the paused edit must not replay");
        persisted.Color.ShouldBe("#222222");
    }

    /// <summary>
    /// Verifies an archive transition that committed ambiguously is verified via its durable receipt
    /// (not the mutable lifecycle status) and reports success without replaying the transition.
    /// </summary>
    [Fact]
    public async Task Archive_VerifiesCommittedTransition_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Archive Ambiguous Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Archive Ambiguous Tag {suffix}", clubId, actorUserId);
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TagDefinitionLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

        var result = await service.ArchiveAsync(tagId, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var status = await verify.PlayerTags
            .Where(tag => tag.PlayerTagId == tagId)
            .Select(tag => tag.LifecycleStatus)
            .SingleAsync(cancellationToken);
        status.ShouldBe(LifecycleStatus.Archived);
    }

    /// <summary>
    /// Verifies a restore transition that committed ambiguously is verified via its durable receipt
    /// (not the mutable lifecycle status) and reports success without replaying the transition.
    /// </summary>
    [Fact]
    public async Task Restore_VerifiesCommittedTransition_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Restore Ambiguous Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Restore Ambiguous Tag {suffix}", clubId, actorUserId);
            tag.LifecycleStatus = LifecycleStatus.Archived;
            tag.ArchivedAt = DateTimeOffset.UtcNow;
            tag.ArchivedById = actorUserId;
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TagDefinitionLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

        var result = await service.RestoreAsync(tagId, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var status = await verify.PlayerTags
            .Where(tag => tag.PlayerTagId == tagId)
            .Select(tag => tag.LifecycleStatus)
            .SingleAsync(cancellationToken);
        status.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies an archive that committed ambiguously, then lost a race to a newer restore, still
    /// reports success via its durable receipt without replaying and overwriting the restored status.
    /// </summary>
    /// <remarks>
    /// The archive pauses just before its receipt-verification read. A newer restore then commits and
    /// flips the tag back to Active. Verification consults the archive's durable receipt rather than
    /// the mutable lifecycle status, so the paused archive reports success and never overwrites the
    /// restored Active status back to Archived.
    /// </remarks>
    [Fact]
    public async Task Archive_AmbiguousCommitThenRestore_DoesNotOverwriteRestoredStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Archive Race Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Archive Race Tag {suffix}", clubId, actorUserId);
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var gateInterceptor = new GateReceiptVerificationInterceptor("\"TagDefinitionMutationReceipts\"");
        var archiveFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor,
            gateInterceptor);
        var restoreFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var archiveService = new TagDefinitionLifecycleService(
            archiveFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);
        var restoreService = new TagDefinitionLifecycleService(
            restoreFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

        Task<ServiceResult<Success>> archive;
        try
        {
            // The archive pauses after its ambiguous commit, just before verification reads the receipt.
            archive = archiveService.ArchiveAsync(tagId, cancellationToken);
            await gateInterceptor.WaitForVerificationAttemptAsync(cancellationToken);

            // A newer restore commits while the archive is paused at verification.
            var restoreResult = await restoreService.RestoreAsync(tagId, cancellationToken);
            restoreResult.IsSuccess.ShouldBeTrue("the newer restore must commit while the archive is paused at verification");

            gateInterceptor.Release();
            var archiveResult = await archive;
            archiveResult.IsSuccess.ShouldBeTrue(
                "the paused archive must verify against its durable receipt and report success");
        }
        finally
        {
            gateInterceptor.Release();
        }

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var status = await verify.PlayerTags
            .Where(tag => tag.PlayerTagId == tagId)
            .Select(tag => tag.LifecycleStatus)
            .SingleAsync(cancellationToken);
        status.ShouldBe(LifecycleStatus.Active, "the restore must survive and the paused archive must not replay");
    }

    /// <summary>
    /// Verifies a restore that committed ambiguously, then lost a race to a newer archive, still
    /// reports success via its durable receipt without replaying and overwriting the archived status.
    /// </summary>
    /// <remarks>
    /// The restore pauses just before its receipt-verification read. A newer archive then commits and
    /// flips the tag back to Archived. Verification consults the restore's durable receipt rather than
    /// the mutable lifecycle status, so the paused restore reports success and never overwrites the
    /// archived status back to Active.
    /// </remarks>
    [Fact]
    public async Task Restore_AmbiguousCommitThenArchive_DoesNotOverwriteArchivedStatus()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long tagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Restore Race Club {suffix}", actorUserId, cancellationToken);

            var tag = CreateTag($"Restore Race Tag {suffix}", clubId, actorUserId);
            tag.LifecycleStatus = LifecycleStatus.Archived;
            tag.ArchivedAt = DateTimeOffset.UtcNow;
            tag.ArchivedById = actorUserId;
            seed.PlayerTags.Add(tag);
            await seed.SaveChangesAsync(cancellationToken);
            tagId = tag.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var gateInterceptor = new GateReceiptVerificationInterceptor("\"TagDefinitionMutationReceipts\"");
        var restoreFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor,
            gateInterceptor);
        var archiveFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var restoreService = new TagDefinitionLifecycleService(
            restoreFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);
        var archiveService = new TagDefinitionLifecycleService(
            archiveFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

        Task<ServiceResult<Success>> restore;
        try
        {
            // The restore pauses after its ambiguous commit, just before verification reads the receipt.
            restore = restoreService.RestoreAsync(tagId, cancellationToken);
            await gateInterceptor.WaitForVerificationAttemptAsync(cancellationToken);

            // A newer archive commits while the restore is paused at verification.
            var archiveResult = await archiveService.ArchiveAsync(tagId, cancellationToken);
            archiveResult.IsSuccess.ShouldBeTrue("the newer archive must commit while the restore is paused at verification");

            gateInterceptor.Release();
            var restoreResult = await restore;
            restoreResult.IsSuccess.ShouldBeTrue(
                "the paused restore must verify against its durable receipt and report success");
        }
        finally
        {
            gateInterceptor.Release();
        }

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var status = await verify.PlayerTags
            .Where(tag => tag.PlayerTagId == tagId)
            .Select(tag => tag.LifecycleStatus)
            .SingleAsync(cancellationToken);
        status.ShouldBe(LifecycleStatus.Archived, "the archive must survive and the paused restore must not replay");
    }

    /// <summary>
    /// Verifies the shared club-roster advisory lock serializes a create-new against a restore-archived
    /// when both would push the club toward the active-definition cap, so exactly one mutation succeeds
    /// and the club never exceeds the cap.
    /// </summary>
    /// <remarks>
    /// Creation acquires the club-roster lock before its active-count probe; restore acquires its tag
    /// lock first and then the same club-roster lock before its probe. The gate pauses creation after it
    /// has acquired the club lock, so the restore is deterministically queued behind it and observes the
    /// post-create active count of exactly the cap, returning a conflict instead of overflowing.
    /// </remarks>
    [Fact]
    public async Task CreateAndRestore_AdvisoryClubLock_AllowsExactlyOnePastTheActiveCap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        long clubId;
        long archivedTagId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(seed, $"Tag Cap Race Club {suffix}", actorUserId, cancellationToken);

            // One below the cap so a single create-new succeeds; the archived definition is the restore
            // contender that would push the club past the cap.
            for (var i = 0; i < TagDefinitionLimits.MaxActiveTagDefinitions - 1; i++)
            {
                seed.PlayerTags.Add(CreateTag($"Active Cap Tag {suffix} {i}", clubId, actorUserId));
            }

            var archived = CreateTag($"Archived Cap Tag {suffix}", clubId, actorUserId);
            archived.LifecycleStatus = LifecycleStatus.Archived;
            archived.ArchivedAt = DateTimeOffset.UtcNow;
            archived.ArchivedById = actorUserId;
            seed.PlayerTags.Add(archived);

            await seed.SaveChangesAsync(cancellationToken);
            archivedTagId = archived.PlayerTagId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var gate = new AdvisoryLockGateInterceptor();
        var createFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            gate);
        var restoreFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var createService = new TagDefinitionService(
            createFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);
        var restoreService = new TagDefinitionLifecycleService(
            restoreFactory,
            fixture.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

        Task<ServiceResult<TagDefinitionDto>> create;
        Task<ServiceResult<Success>> restore;
        try
        {
            // Creation acquires the club-roster lock and pauses on it, holding the lock.
            create = createService.CreateAsync(
                new CreateTagDefinitionInput { Name = $"New Cap Tag {suffix}", Color = "#123456" },
                cancellationToken);
            await gate.WaitForAcquiredAsync(cancellationToken);

            // Restore acquires its tag lock (a different key) and then queues behind creation on the
            // shared club-roster lock.
            restore = restoreService.RestoreAsync(archivedTagId, cancellationToken);

            gate.Release();
        }
        finally
        {
            gate.Release();
        }

        var createResult = await create;
        var restoreResult = await restore;

        createResult.IsSuccess.ShouldBeTrue(
            "creation holds the club lock first and must land the final slot under the cap");
        restoreResult.IsProblem.ShouldBeTrue(
            "restore is serialized behind creation and must observe the cap already reached");
        restoreResult.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var activeCount = await verify.PlayerTags
            .CountAsync(
                tag => tag.ClubId == clubId && tag.LifecycleStatus == LifecycleStatus.Active,
                cancellationToken);
        activeCount.ShouldBe(TagDefinitionLimits.MaxActiveTagDefinitions);
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
