using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign placement mutations remain correct when Npgsql retries a failed transaction.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>Verifies opening a Draft waits for placement and cannot enroll while the owning campaign remains Active.</summary>
    [Fact]
    public async Task UpdatePlacement_SerializesCompetingOpening_WithoutCreatingAnotherDecision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        long draftId;
        await using (var seed = fixture.CreateAdminContext())
        {
            var seasonId = await seed.Clubs.Where(row => row.ClubId == clubId)
                .Select(row => row.CurrentSeasonId).SingleAsync(cancellationToken);
            var draft = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Competing opening {Guid.NewGuid():N}",
                StartDate = new DateOnly(2026, 7, 1),
                Status = CampaignStatus.Draft,
                SeasonId = seasonId!.Value,
                ClubId = clubId,
                CreatedById = actorUserId
            };
            seed.Campaigns.Add(draft);
            await seed.SaveChangesAsync(cancellationToken);
            draftId = draft.CampaignId;
        }

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var gate = new AdvisoryLockGateInterceptor();
        ICampaignPlacementService placement = new CampaignPlacementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate),
            fixture.CurrentUser, NullLogger<CampaignPlacementService>.Instance);
        var lifecycle = new CampaignLifecycleService(fixture.CreateTenantContextFactory(), fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        try
        {
            var placementTask = placement.UpdatePlacementAsync(
                new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
            await gate.WaitForAcquiredAsync(cancellationToken);
            var openingTask = lifecycle.OpenAsync(draftId, new OpenCampaignInput { OperationId = Guid.NewGuid() }, cancellationToken);
            await using var probe = fixture.CreateAdminContext();
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(probe, (long.MinValue / 16) + clubId, cancellationToken);
            gate.Release();
            var placementResult = await placementTask;
            var openingResult = await openingTask;
            placementResult.IsSuccess.ShouldBeTrue();
            openingResult.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

            await using var verify = fixture.CreateAdminContext();
            var persisted = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
            persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
            persisted.ConcurrencyToken.ShouldBe(placementResult.Value.ConcurrencyToken);
            var draft = await verify.Campaigns.SingleAsync(row => row.CampaignId == draftId, cancellationToken);
            draft.Status.ShouldBe(CampaignStatus.Draft);
            draft.SeasonOpeningSequence.ShouldBeNull();
            (await verify.PlayerCampaignAssignments.CountAsync(row => row.CampaignId == draftId, cancellationToken)).ShouldBe(0);
            (await verify.ActivityEvents.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(1);
            (await verify.ActivityEvents.AnyAsync(row => row.ClubId == clubId
                && (row.EventKind == ActivityEventKind.CampaignOpened || row.EventKind == ActivityEventKind.PlacementSuperseded), cancellationToken)).ShouldBeFalse();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Verifies supersession locks an earlier decision's team even when the requested outcome has no team.</summary>
    [Fact]
    public async Task UpdatePlacement_LocksPriorTeam_AndSupersedesItsDecisionAfterArchival()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        long priorAssignmentId;
        Guid priorToken;
        await using (var db = fixture.CreateAdminContext())
        {
            var target = await db.PlayerCampaignAssignments.Include(row => row.Campaign)
                .SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
            target.Campaign.SeasonOpeningSequence = 2;
            await db.SaveChangesAsync(cancellationToken);
            var priorCampaign = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Prior team decision {Guid.NewGuid():N}",
                StartDate = new DateOnly(2026, 5, 1),
                SeasonId = target.Campaign.SeasonId,
                Status = CampaignStatus.Closed,
                SeasonOpeningSequence = 1,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ClosedById = actorUserId,
                ClubId = clubId,
                CreatedById = actorUserId
            };
            db.Campaigns.Add(priorCampaign);
            await db.SaveChangesAsync(cancellationToken);
            var prior = new PlayerCampaignAssignmentEntity
            {
                CampaignId = priorCampaign.CampaignId,
                PlayerId = target.PlayerId,
                ClubId = clubId,
                CreatedById = actorUserId,
                TeamId = teamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ConcurrencyToken = Guid.NewGuid(),
                DecisionRecordedAt = DateTimeOffset.UtcNow.AddDays(-2),
                DecisionRecordedById = actorUserId,
                DecisionActorDisplayName = "Earlier recorder"
            };
            db.PlayerCampaignAssignments.Add(prior);
            await db.SaveChangesAsync(cancellationToken);
            priorAssignmentId = prior.PlayerCampaignAssignmentId;
            priorToken = prior.ConcurrencyToken;
        }

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
        ICampaignPlacementService service = new CampaignPlacementService(fixture.CreateTenantContextFactory(), fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);
        await using var archive = fixture.CreateAdminContext();
        await using var transaction = await archive.Database.BeginTransactionAsync(cancellationToken);
        await archive.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-teamId})", cancellationToken);
        var pending = service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, null, expectedToken), cancellationToken);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(archive, -teamId, cancellationToken);
        var team = await archive.Teams.SingleAsync(row => row.TeamId == teamId, cancellationToken);
        team.LifecycleStatus = LifecycleStatus.Archived;
        team.ArchivedAt = DateTimeOffset.UtcNow;
        team.ArchivedById = actorUserId;
        await archive.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = await pending;
        result.IsSuccess.ShouldBeTrue();
        await using var verify = fixture.CreateAdminContext();
        var previous = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == priorAssignmentId, cancellationToken);
        previous.ConcurrencyToken.ShouldBe(priorToken);
        previous.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        previous.TeamId.ShouldBe(teamId);
        var activity = await verify.ActivityEvents.SingleAsync(row => row.ClubId == clubId, cancellationToken);
        activity.EventKind.ShouldBe(ActivityEventKind.PlacementSuperseded);
        var persisted = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        persisted.TeamId.ShouldBeNull();
    }

    /// <summary>PostgreSQL rejects incomplete decision attribution without the test seed normalizer.</summary>
    /// <param name="invalidField">The attribution invariant to violate.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("all")]
    [InlineData("time")]
    [InlineData("actorId")]
    [InlineData("actorName")]
    [InlineData("zeroActor")]
    [InlineData("blankActor")]
    [InlineData("enrollment")]
    public async Task PlacementDecision_RejectsInvalidAttribution_AtDatabaseBoundary(string invalidField)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedPlacementDataAsync(7, Guid.NewGuid().ToString("N"));
        await using var db = fixture.CreateUnnormalizedAdminContext();
        var row = await db.PlayerCampaignAssignments.SingleAsync(x => x.PlayerCampaignAssignmentId == seed.AssignmentId, cancellationToken);
        row.PlacementOutcome = PlacementOutcome.NotSelected;
        row.DecisionRecordedAt = DateTimeOffset.UtcNow;
        row.DecisionRecordedById = 7;
        row.DecisionActorDisplayName = "Test member";
        switch (invalidField)
        {
            case "all":
                row.DecisionRecordedAt = null;
                row.DecisionRecordedById = null;
                row.DecisionActorDisplayName = null;
                break;
            case "time": row.DecisionRecordedAt = null; break;
            case "actorId": row.DecisionRecordedById = null; break;
            case "actorName": row.DecisionActorDisplayName = null; break;
            case "zeroActor": row.DecisionRecordedById = 0; break;
            case "blankActor": row.DecisionActorDisplayName = " "; break;
            case "enrollment": row.PlacementOutcome = PlacementOutcome.Undecided; break;
        }
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
        var provider = exception.InnerException.ShouldBeOfType<Npgsql.PostgresException>();
        provider.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.CheckViolation);
        provider.ConstraintName.ShouldBe("CK_PlayerCampaignAssignments_DecisionAttribution");
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments.SingleAsync(x => x.PlayerCampaignAssignmentId == seed.AssignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        persisted.DecisionRecordedAt.ShouldBeNull();
        persisted.DecisionRecordedById.ShouldBeNull();
        persisted.DecisionActorDisplayName.ShouldBeNull();
    }
    /// <summary>Verifies receipt operation identifiers are unique within a tenant and reusable by another tenant.</summary>
    [Fact]
    public async Task PlacementReceipt_EnforcesTenantScopedOperationUniqueness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var first = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        var second = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        var operationId = Guid.NewGuid();
        await using (var db = fixture.CreateUnnormalizedAdminContext())
        {
            db.PlacementMutationReceipts.AddRange(
                new PlacementMutationReceiptEntity
                {
                    ClubId = first.ClubId,
                    CreatedById = actorUserId,
                    OperationId = operationId,
                    PlayerCampaignAssignmentId = first.AssignmentId,
                    ConcurrencyToken = first.ConcurrencyToken
                },
                new PlacementMutationReceiptEntity
                {
                    ClubId = second.ClubId,
                    CreatedById = actorUserId,
                    OperationId = operationId,
                    PlayerCampaignAssignmentId = second.AssignmentId,
                    ConcurrencyToken = second.ConcurrencyToken
                });
            await db.SaveChangesAsync(cancellationToken);
        }

        await using var duplicate = fixture.CreateUnnormalizedAdminContext();
        duplicate.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
        {
            ClubId = first.ClubId,
            CreatedById = actorUserId,
            OperationId = operationId,
            PlayerCampaignAssignmentId = first.AssignmentId,
            ConcurrencyToken = Guid.NewGuid()
        });
        var exception = await Should.ThrowAsync<DbUpdateException>(() => duplicate.SaveChangesAsync(cancellationToken));
        var providerException = exception.InnerException.ShouldBeOfType<Npgsql.PostgresException>();
        providerException.SqlState.ShouldBe(Npgsql.PostgresErrorCodes.UniqueViolation);
        await using var verify = fixture.CreateAdminContext();
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == first.ClubId, cancellationToken)).ShouldBe(1);
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == second.ClubId, cancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies same-campaign identical saves preserve attribution and never append duplicate activity or receipts.</summary>
    [Fact]
    public async Task UpdatePlacement_IdenticalSavePreservesDecision_AndStaleIdenticalSaveConflicts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
        ICampaignPlacementService service = new CampaignPlacementService(fixture.CreateTenantContextFactory(), fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);
        var first = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
        first.IsSuccess.ShouldBeTrue();
        await using var before = fixture.CreateAdminContext();
        var original = await before.PlayerCampaignAssignments.AsNoTracking().SingleAsync(
            row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        fixture.CurrentUser.UserId = actorUserId == long.MaxValue ? actorUserId - 1 : actorUserId + 1;
        var repeated = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, first.Value.ConcurrencyToken), cancellationToken);
        repeated.IsSuccess.ShouldBeTrue();
        repeated.Value.ConcurrencyToken.ShouldBe(first.Value.ConcurrencyToken);
        var stale = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
        stale.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.DecisionRecordedById.ShouldBe(actorUserId);
        persisted.DecisionRecordedAt.ShouldBe(original.DecisionRecordedAt);
        persisted.ConcurrencyToken.ShouldBe(first.Value.ConcurrencyToken);
        (await verify.ActivityEvents.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(1);
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies placement waits for season advancement and rechecks the committed current-season pointer.</summary>
    [Fact]
    public async Task UpdatePlacement_RejectsNonCurrentSeason_AfterWaitingForSeasonLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignPlacementService(fixture.CreateTenantContextFactory(), fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);
        await using var advance = fixture.CreateAdminContext();
        await using var transaction = await advance.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = (long.MinValue / 16) + clubId;
        await advance.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
        var pending = ((ICampaignPlacementService)service).UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(advance, lockKey, cancellationToken);
        var nextSeason = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Advanced {Guid.NewGuid():N}",
            StartDate = new DateOnly(2027, 1, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        advance.Seasons.Add(nextSeason);
        await advance.SaveChangesAsync(cancellationToken);
        var club = await advance.Clubs.SingleAsync(row => row.ClubId == clubId, cancellationToken);
        club.CurrentSeasonId = nextSeason.SeasonId;
        await advance.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = await pending;
        result.IsSuccess.ShouldBeFalse();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.ConcurrencyToken.ShouldBe(expectedToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        (await verify.ActivityEvents.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(0);
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>Verifies target team lifecycle is reloaded after real PostgreSQL lock contention.</summary>
    [Fact]
    public async Task UpdatePlacement_RejectsArchivedTarget_AfterWaitingForTeamLock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignPlacementService(fixture.CreateTenantContextFactory(), fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);
        await using var archive = fixture.CreateAdminContext();
        await using var transaction = await archive.Database.BeginTransactionAsync(cancellationToken);
        await archive.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({-teamId})", cancellationToken);
        var pending = ((ICampaignPlacementService)service).UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(archive, -teamId, cancellationToken);
        var team = await archive.Teams.SingleAsync(row => row.TeamId == teamId, cancellationToken);
        team.LifecycleStatus = LifecycleStatus.Archived;
        team.ArchivedAt = DateTimeOffset.UtcNow;
        team.ArchivedById = actorUserId;
        await archive.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var result = await pending;
        result.IsSuccess.ShouldBeFalse();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.ConcurrencyToken.ShouldBe(expectedToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        (await verify.ActivityEvents.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// Verifies a transient failure raised before the database commits is retried on a fresh context
    /// and the placement is persisted exactly once.
    /// </summary>
    [Fact]
    public async Task UpdatePlacement_RetriesFailedCommit_AndPersistsReplacementToken()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstTransactionCommitInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignPlacementService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);

        var result = await ((ICampaignPlacementService)service).UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue("a pre-commit transient failure must be retried to a successful placement");
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThanOrEqualTo(3);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, TestContext.Current.CancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(result.Value.ConcurrencyToken);
        persisted.ConcurrencyToken.ShouldNotBe(expectedToken);
        persisted.DecisionRecordedById.ShouldBe(actorUserId);
        persisted.DecisionRecordedAt.ShouldNotBeNull();
        (await verify.PlacementMutationReceipts.CountAsync(receipt => receipt.ClubId == clubId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(activity => activity.ClubId == clubId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Verifies a placement whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious conflict against its own token.
    /// </summary>
    [Fact]
    public async Task UpdatePlacement_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignPlacementService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);

        var result = await ((ICampaignPlacementService)service).UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue("an ambiguous commit must be verified rather than replayed into a conflict");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, TestContext.Current.CancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(result.Value.ConcurrencyToken);
        persisted.ConcurrencyToken.ShouldNotBe(expectedToken);
        (await verify.PlacementMutationReceipts.CountAsync(receipt => receipt.ClubId == clubId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(activity => activity.ClubId == clubId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Verifies a durable receipt recovers the original success after a competing save changes the row token.
    /// </summary>
    [Fact]
    public async Task UpdatePlacement_RecoversOriginalToken_WhenLaterSavePrecedesCommitVerification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var failure = new FailFirstCommittedTransactionInterceptor();
        var gate = new GateReceiptVerificationInterceptor("\"PlacementMutationReceipts\"");
        var firstService = new CampaignPlacementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, failure, gate),
            fixture.CurrentUser, NullLogger<CampaignPlacementService>.Instance);
        var laterService = new CampaignPlacementService(fixture.CreateTenantContextFactory(),
            fixture.CurrentUser, NullLogger<CampaignPlacementService>.Instance);

        try
        {
            var firstTask = ((ICampaignPlacementService)firstService).UpdatePlacementAsync(
                new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
            await gate.WaitForVerificationAttemptAsync(cancellationToken);
            Guid committedToken;
            await using (var locate = fixture.CreateAdminContext())
            {
                committedToken = await locate.PlayerCampaignAssignments
                    .Where(row => row.PlayerCampaignAssignmentId == assignmentId)
                    .Select(row => row.ConcurrencyToken).SingleAsync(cancellationToken);
            }

            var laterResult = await ((ICampaignPlacementService)laterService).UpdatePlacementAsync(
                new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, null, committedToken), cancellationToken);
            laterResult.IsSuccess.ShouldBeTrue();
            gate.Release();
            var firstResult = await firstTask;
            firstResult.IsSuccess.ShouldBeTrue();
            firstResult.Value.ConcurrencyToken.ShouldBe(committedToken);
            firstResult.Value.ConcurrencyToken.ShouldNotBe(laterResult.Value.ConcurrencyToken);
            failure.FailureCount.ShouldBe(1);

            await using var verify = fixture.CreateAdminContext();
            var persisted = await verify.PlayerCampaignAssignments.SingleAsync(
                row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
            persisted.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
            persisted.ConcurrencyToken.ShouldBe(laterResult.Value.ConcurrencyToken);
            (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(2);
            (await verify.ActivityEvents.CountAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBe(2);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Verifies a receipt survives club deletion and recovers an acknowledged-lost placement without replaying it.
    /// </summary>
    [Fact]
    public async Task UpdatePlacement_RecoversOriginalSuccess_WhenClubDeletionPrecedesCommitVerification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var (clubId, teamId, assignmentId, expectedToken) = await SeedPlacementDataAsync(actorUserId, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var failure = new FailFirstCommittedTransactionInterceptor();
        var gate = new GateReceiptVerificationInterceptor("\"PlacementMutationReceipts\"");
        ICampaignPlacementService service = new CampaignPlacementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, failure, gate),
            fixture.CurrentUser, NullLogger<CampaignPlacementService>.Instance);

        try
        {
            var pending = service.UpdatePlacementAsync(
                new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, expectedToken), cancellationToken);
            await gate.WaitForVerificationAttemptAsync(cancellationToken);
            Guid committedToken;
            await using (var delete = fixture.CreateAdminContext())
            {
                committedToken = await delete.PlayerCampaignAssignments
                    .Where(row => row.PlayerCampaignAssignmentId == assignmentId)
                    .Select(row => row.ConcurrencyToken).SingleAsync(cancellationToken);
                var club = await delete.Clubs.SingleAsync(row => row.ClubId == clubId, cancellationToken);
                delete.Clubs.Remove(club);
                await delete.SaveChangesAsync(cancellationToken);
            }

            gate.Release();
            var result = await pending;
            result.IsSuccess.ShouldBeTrue("a deleted aggregate must not erase a successfully committed operation's receipt");
            result.Value.ConcurrencyToken.ShouldBe(committedToken);
            result.Value.ConcurrencyToken.ShouldNotBe(expectedToken);
            failure.FailureCount.ShouldBe(1);
            await using var verify = fixture.CreateAdminContext();
            (await verify.Clubs.AnyAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBeFalse();
            (await verify.PlayerCampaignAssignments.AnyAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken)).ShouldBeFalse();
            (await verify.ActivityEvents.AnyAsync(row => row.ClubId == clubId, cancellationToken)).ShouldBeFalse();
            var receipt = await verify.PlacementMutationReceipts.SingleAsync(row => row.ClubId == clubId, cancellationToken);
            receipt.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
            receipt.ConcurrencyToken.ShouldBe(committedToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Seeds one club, season, campaign, player, team, and participation owned by it so a placement
    /// mutation can exercise the retry path against a committed baseline.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <returns>The seeded club, team, participation, and participation concurrency token.</returns>
    private async Task<(long ClubId, long TeamId, long AssignmentId, Guid ConcurrencyToken)> SeedPlacementDataAsync(
        long actorUserId,
        string suffix)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Retry Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Retry Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Place",
            LastName = $"Retry Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Retry Team {suffix}",
            GraduationYear = 2029,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };

        seed.AddRange(season, campaign, player, team);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var concurrencyToken = Guid.NewGuid();
        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7,
            ConcurrencyToken = concurrencyToken
        };
        seed.Add(assignment);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (club.ClubId, team.TeamId, assignment.PlayerCampaignAssignmentId, concurrencyToken);
    }
}
