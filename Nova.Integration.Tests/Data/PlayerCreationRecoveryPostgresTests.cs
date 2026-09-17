using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Common;
using Nova.Features.Players;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>Proves immutable creation recovery and serialization with actual PostgreSQL lock waiters.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed partial class PlayerCreationRecoveryPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>Competing requests either replay one operation or settle the second as a duplicate.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task ConcurrentCommandsSerializeAndCommitOnePlayerAsync(bool sameOperation, bool distinctActors)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var input = Input(seed.ClubId);
        var gate = new AdvisoryLockGateInterceptor(advisoryLocksToSkip: 1);
        var first = Service(gate).CreateAsync(input, ct);
        await gate.WaitForAcquiredAsync(ct);
        if (distinctActors)
        {
            await using var db = fixture.CreateAdminContext();
            var other = new NovaUserEntity { FirstName = "Other", LastName = "Member", ClubId = seed.ClubId };
            db.Users.Add(other);
            await db.SaveChangesAsync(ct);
            ActAs(seed with { ActorUserId = other.Id });
        }
        var second = Service().CreateAsync(sameOperation ? input : input with { OperationId = Guid.CreateVersion7() }, ct);
        try { await WaitForLockAsync(distinctActors ? (long.MinValue / 32) + seed.ClubId : (long.MinValue / 64) + seed.ActorUserId, ct); }
        finally { gate.Release(); }
        var committed = await first;
        committed.IsSuccess.ShouldBeTrue();
        var competing = await second;
        if (sameOperation) { competing.Value.ShouldBe(committed.Value); }
        else
        {
            competing.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
            PlayerCreationProblems.TryGetDuplicate(competing.Problem, out var duplicate).ShouldBeTrue();
            duplicate!.PlayerId.ShouldBe(committed.Value.Player.PlayerId);
        }
        await AssertCountsAsync(seed, 1, 1, sameOperation ? 1 : 2, ct);
    }

    /// <summary>Persisted removal takes precedence when stale claims resume after membership serialization.</summary>
    [Fact]
    public async Task MembershipRemovalWhileWaitingPreventsCreationAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        await using var removal = fixture.CreateAdminContext();
        await using var transaction = await removal.Database.BeginTransactionAsync(ct);
        await removal.AcquireUserMembershipLockAsync(seed.ActorUserId, ct);
        await removal.AcquireClubMembershipLockAsync(seed.ClubId, ct);
        var pending = Service().CreateAsync(Input(seed.ClubId), ct);
        await WaitForLockAsync((long.MinValue / 64) + seed.ActorUserId, ct);
        var user = await removal.Users.SingleAsync(x => x.Id == seed.ActorUserId, ct);
        user.ClubId = null;
        await removal.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        (await pending).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        await AssertCountsAsync(seed, 0, 0, 0, ct);
    }

    /// <summary>An operation that expires while blocked cannot commit at a later lock boundary.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryDuringRosterOrCampaignWaitRollsBackAsync(bool campaignLock)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var clock = new CreationClock(DateTimeOffset.UtcNow);
        var input = Input(seed.ClubId) with { OperationId = Guid.CreateVersion7(clock.GetUtcNow()) };
        PlayerCreationOperation.TryGetDeadline(input.OperationId, clock.GetUtcNow(), out var deadline).ShouldBeTrue();
        await using var blocker = fixture.CreateAdminContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(ct);
        if (campaignLock) { await blocker.AcquireCampaignMutationLockAsync(seed.CampaignId, ct); }
        else { await blocker.AcquireClubRosterLockAsync(seed.ClubId, ct); }
        var pending = Service(clock: clock).CreateAsync(input, ct);
        await WaitForLockAsync(campaignLock ? long.MinValue + seed.CampaignId : (long.MinValue / 4) + seed.ClubId, ct);
        clock.Now = deadline;
        await transaction.CommitAsync(ct);
        var result = await pending;
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        PlayerCreationProblems.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
        await AssertCountsAsync(seed, 0, 0, 0, ct);
    }

    /// <summary>Receipt verification returns the original evidence despite intervening aggregate changes.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("profile")]
    [InlineData("campaign")]
    [InlineData("player")]
    [InlineData("membership")]
    [InlineData("club")]
    public async Task LostAcknowledgementUsesReceiptAfterLaterMutationAsync(string mutation)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var input = Input(seed.ClubId);
        var gate = new GatedLostPlacementAcknowledgementInterceptor();
        var pending = Service(gate).CreateAsync(input, ct);
        await gate.WaitForCommitAsync(ct);
        long playerId;
        long participationId;
        try
        {
            await using var db = fixture.CreateAdminContext();
            var player = await db.Players.SingleAsync(x => x.ClubId == seed.ClubId, ct);
            playerId = player.PlayerId;
            participationId = await db.PlayerCampaignAssignments.Where(x => x.PlayerId == playerId)
                .Select(x => x.PlayerCampaignAssignmentId).SingleAsync(ct);
            await ApplyLaterMutationAsync(db, seed, player, mutation, ct);
        }
        finally { gate.Release(); }
        var result = await pending;
        gate.FailureCount.ShouldBe(1);
        if (mutation is "membership" or "club")
        {
            result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
            PlayerCreationProblems.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
        }
        else
        {
            result.Value.Player.PlayerId.ShouldBe(playerId);
            result.Value.Player.FirstName.ShouldBe(input.FirstName);
            result.Value.Enrollment.ShouldNotBeNull();
            result.Value.Enrollment.CampaignId.ShouldBe(seed.CampaignId);
            result.Value.Enrollment.CampaignName.ShouldBe("Original campaign");
            result.Value.Enrollment.PlayerCampaignAssignmentId.ShouldBe(participationId);
            (await Service().CreateAsync(input, ct)).Value.ShouldBe(result.Value);
        }
        await using var verify = fixture.CreateAdminContext();
        (await verify.PlayerCreationReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(1);
    }

    /// <summary>A lifecycle writer's committed state governs enrollment when creation had to wait.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationRereadsCampaignAfterContendingOpenOrCloseAsync(bool close)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(close ? CampaignStatus.Active : CampaignStatus.Draft, ct);
        ActAs(seed);
        fixture.CurrentUser.IsClubAdmin = true;
        await using (var db = fixture.CreateAdminContext())
        {
            var roles = await db.Roles.ToListAsync(ct);
            var role = roles.Single(x => string.Equals(x.Name, Roles.ClubAdmin, StringComparison.Ordinal));
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = seed.ActorUserId, RoleId = role.Id });
            if (!close)
            {
                db.Players.Add(new PlayerEntity
                {
                    ClubId = seed.ClubId,
                    CreatedById = seed.ActorUserId,
                    CreationOperationId = Guid.CreateVersion7(),
                    FirstName = "Existing",
                    LastName = "Participant",
                    DateOfBirth = new DateOnly(2012, 1, 1),
                    GraduationYear = 2030
                });
            }
            await db.SaveChangesAsync(ct);
        }
        var gate = new AdvisoryLockGateInterceptor();
        var lifecycle = new CampaignLifecycleService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate),
            fixture.CurrentUser, NullLogger<CampaignLifecycleService>.Instance);
        var changing = ChangeCampaignAsync(lifecycle, seed.CampaignId, close, ct);
        await gate.WaitForAcquiredAsync(ct);
        var creating = Service().CreateAsync(Input(seed.ClubId), ct);
        try { await WaitForLockAsync(close ? (long.MinValue / 64) + seed.ActorUserId : (long.MinValue / 16) + seed.ClubId, ct); }
        finally { gate.Release(); }
        await changing;
        var result = await creating;
        result.IsSuccess.ShouldBeTrue();
        if (close) { result.Value.Enrollment.ShouldBeNull(); }
        else { result.Value.Enrollment!.CampaignId.ShouldBe(seed.CampaignId); }
        await AssertCountsAsync(seed, close ? 1 : 2, close ? 0 : 2, 1, ct);
    }

    /// <summary>Runs a real profile writer before duplicate classification resumes.</summary>
    [Fact]
    public async Task CreationRejectsIdentityCommittedByContendingEditAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Draft, ct);
        ActAs(seed);
        var original = (await Service().CreateAsync(Input(seed.ClubId) with { FirstName = "Before" }, ct)).Value;
        var gate = new AdvisoryLockGateInterceptor();
        var editing = Service(gate).UpdateAsync(new UpdatePlayerInput
        {
            PlayerId = original.Player.PlayerId,
            FirstName = "Avery",
            LastName = "Recovery",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        }, ct);
        await gate.WaitForAcquiredAsync(ct);
        var creating = Service().CreateAsync(Input(seed.ClubId), ct);
        try { await WaitForLockAsync((long.MinValue / 64) + seed.ActorUserId, ct); }
        finally { gate.Release(); }
        (await editing).IsSuccess.ShouldBeTrue();
        (await creating).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await AssertCountsAsync(seed, 1, 0, 2, ct);
    }

    private static async Task ApplyLaterMutationAsync(Nova.Data.NovaAdminDbContext db, Seed seed,
        PlayerEntity player, string mutation, CancellationToken ct)
    {
        switch (mutation)
        {
            case "profile": player.FirstName = "Later"; break;
            case "campaign": db.Campaigns.Remove(await db.Campaigns.SingleAsync(x => x.CampaignId == seed.CampaignId, ct)); break;
            case "player": db.Players.Remove(player); break;
            case "membership": (await db.Users.SingleAsync(x => x.Id == seed.ActorUserId, ct)).ClubId = null; break;
            case "club": db.Clubs.Remove(await db.Clubs.SingleAsync(x => x.ClubId == seed.ClubId, ct)); break;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Retention bounds the global database delete, orders tied expiries, and keeps live deleted-club receipts.</summary>
    [Fact]
    public async Task CleanupDeletesAtMostFiveHundredExpiredReceiptsAcrossDeletedClubsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Draft, ct);
        var other = await SeedAsync(CampaignStatus.Draft, ct);
        await using var db = fixture.CreateAdminContext();
        // Keep this global maintenance cutoff earlier than other parallel tests' recovery windows.
        var now = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tied = Enumerable.Range(0, 501).Select(_ => RetentionReceipt(seed, now.AddMinutes(-1))).ToArray();
        db.PlayerCreationReceipts.AddRange(tied);
        await db.SaveChangesAsync(ct);
        var oldest = RetentionReceipt(other, now.AddMinutes(-2));
        var boundary = RetentionReceipt(seed, now);
        var liveDeletedClub = RetentionReceipt(seed, now.AddTicks(10)); // One PostgreSQL microsecond after cutoff.
        var liveOtherClub = RetentionReceipt(other, now.AddHours(1));
        db.PlayerCreationReceipts.AddRange(oldest, boundary, liveDeletedClub, liveOtherClub);
        await db.SaveChangesAsync(ct);
        db.Clubs.Remove(await db.Clubs.SingleAsync(x => x.ClubId == seed.ClubId, ct));
        await db.SaveChangesAsync(ct);
        var liveIds = new[] { liveDeletedClub.PlayerCreationReceiptId, liveOtherClub.PlayerCreationReceiptId };
        var afterFirstPass = tied.OrderBy(receipt => receipt.PlayerCreationReceiptId).Skip(499)
            .Select(receipt => receipt.PlayerCreationReceiptId).Append(boundary.PlayerCreationReceiptId).Concat(liveIds).Order().ToArray();
        var observer = new RetentionReaderInterceptor();
        var factory = new RetryingAdminDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, observer);
        await using var maintenance = await factory.CreateDbContextAsync(ct);

        await PlayerCreationReceiptCleanupService.PruneAsync(maintenance, now, ct);
        observer.ReaderExecutionCount.ShouldBe(0);
        maintenance.ChangeTracker.Entries<PlayerCreationReceiptEntity>().ShouldBeEmpty();
        (await db.PlayerCreationReceipts.Where(receipt => receipt.ClubId == seed.ClubId || receipt.ClubId == other.ClubId)
            .OrderBy(receipt => receipt.PlayerCreationReceiptId).Select(receipt => receipt.PlayerCreationReceiptId).ToArrayAsync(ct))
            .ShouldBe(afterFirstPass);
        await PlayerCreationReceiptCleanupService.PruneAsync(maintenance, now, ct);
        observer.ReaderExecutionCount.ShouldBe(0);
        maintenance.ChangeTracker.Entries<PlayerCreationReceiptEntity>().ShouldBeEmpty();
        (await db.PlayerCreationReceipts.Where(receipt => receipt.ClubId == seed.ClubId || receipt.ClubId == other.ClubId)
            .OrderBy(receipt => receipt.PlayerCreationReceiptId).Select(receipt => receipt.PlayerCreationReceiptId).ToArrayAsync(ct))
            .ShouldBe(liveIds.Order().ToArray());
    }

    private static PlayerCreationReceiptEntity RetentionReceipt(Seed seed, DateTimeOffset expiresAt) => new()
    {
        ClubId = seed.ClubId,
        ActorUserId = seed.ActorUserId,
        CreatedById = seed.ActorUserId,
        OperationId = Guid.CreateVersion7(),
        RequestSha256 = new string('A', 64),
        ResultJson = "{}",
        RecoveryExpiresAt = expiresAt
    };

    private sealed class RetentionReaderInterceptor : DbCommandInterceptor
    {
        internal int ReaderExecutionCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderExecutionCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ReaderExecutionCount++;
            return ValueTask.FromResult(result);
        }
    }

    private static async Task ChangeCampaignAsync(ICampaignLifecycleService service, long campaignId, bool close, CancellationToken ct)
    {
        if (close) { (await service.CloseAsync(campaignId, ct)).IsSuccess.ShouldBeTrue(); }
        else { (await service.OpenAsync(campaignId, new OpenCampaignInput { OperationId = Guid.CreateVersion7() }, ct)).IsSuccess.ShouldBeTrue(); }
    }

    private PlayerManagementService Service(IInterceptor? interceptor = null, TimeProvider? clock = null) => new(
        new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, interceptor ?? new NoOpInterceptor()),
        fixture.CurrentUser, NullLogger<PlayerManagementService>.Instance, clock ?? TimeProvider.System);

    private async Task WaitForLockAsync(long key, CancellationToken ct)
    {
        await using var probe = fixture.CreateAdminContext();
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(probe, key, ct);
    }

    private async Task AssertCountsAsync(Seed seed, int players, int enrollments, int receipts, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(players);
        (await db.PlayerCampaignAssignments.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(enrollments);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(receipts);
        (await db.PlayerCampaignAssignments.CountAsync(x => x.ClubId == seed.ClubId && x.DecisionRecordedAt != null, ct)).ShouldBe(0);
    }

    private void ActAs(Seed seed)
    {
        fixture.CurrentUser.UserId = seed.ActorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    private static CreatePlayerInput Input(long clubId) => new()
    {
        ClubId = clubId,
        OperationId = Guid.CreateVersion7(),
        FirstName = "Avery",
        LastName = "Recovery",
        DateOfBirth = new DateOnly(2012, 1, 1),
        GraduationYear = 2030
    };

    private async Task<Seed> SeedAsync(CampaignStatus status, CancellationToken ct)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        var db = fixture.CreateAdminContext();
        await using (db)
        {
            var user = new NovaUserEntity { FirstName = "Player", LastName = "Member" };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            var club = new ClubEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                Name = $"Recovery {Guid.CreateVersion7():N}",
                City = "Austin",
                State = "TX",
                CreatedById = user.Id
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync(ct);
            user.ClubId = club.ClubId;
            var season = new SeasonEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                ClubId = club.ClubId,
                CreatedById = user.Id,
                Name = "Recovery season",
                StartDate = new DateOnly(2026, 1, 1)
            };
            db.Seasons.Add(season);
            await db.SaveChangesAsync(ct);
            club.CurrentSeasonId = season.SeasonId;
            var campaign = new CampaignEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                ClubId = club.ClubId,
                CreatedById = user.Id,
                SeasonId = season.SeasonId,
                Name = "Original campaign",
                StartDate = new DateOnly(2026, 6, 1),
                Status = status
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync(ct);
            return new Seed(club.ClubId, user.Id, campaign.CampaignId);
        }
    }

    private sealed record Seed(long ClubId, long ActorUserId, long CampaignId);

    private sealed class CreationClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
