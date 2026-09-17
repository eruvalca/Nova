using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

public sealed partial class PlayerManagementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("01994916-0000-4000-8000-000000000000")]
    [InlineData("ffffffff-ffff-7000-8000-000000000000")]
    [InlineData("01994916-0000-7000-0000-000000000000")]
    public async Task CreateRejectsMalformedOperationIdentityAsync(string identity)
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var input = ValidCreateInput() with { OperationId = Guid.Parse(identity) };
        var result = await CreateService().CreateAsync(input, ct);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull().Keys.ShouldBe([nameof(CreatePlayerInput.OperationId)]);
        result.Problem.Extensions.ShouldBeNull();
        await using var db = _harness.CreateAdminContext();
        (await db.Players.CountAsync(x => x.CreationOperationId == input.OperationId, ct)).ShouldBe(0);
        (await db.PlayerCreationReceipts.CountAsync(x => x.OperationId == input.OperationId, ct)).ShouldBe(0);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(59999)]
    [InlineData(60000)]
    [InlineData(60001)]
    public async Task CreateHonorsFutureOperationClockToleranceAsync(int milliseconds)
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var clock = new CreationClock(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));
        var input = ValidCreateInput() with { OperationId = Guid.CreateVersion7(clock.Now.AddMilliseconds(milliseconds)) };
        var service = CreateService(clock);
        var result = await service.CreateAsync(input, ct);
        await using var db = _harness.CreateAdminContext();
        if (milliseconds > 60000)
        {
            result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
            result.Problem.Errors.ShouldNotBeNull().Keys.ShouldBe([nameof(CreatePlayerInput.OperationId)]);
            result.Problem.Extensions.ShouldBeNull();
            (await db.Players.CountAsync(x => x.CreationOperationId == input.OperationId, ct)).ShouldBe(0);
            (await db.PlayerCreationReceipts.CountAsync(x => x.OperationId == input.OperationId, ct)).ShouldBe(0);
            clock.Now = clock.Now.AddMilliseconds(1);
            result = await service.CreateAsync(input, ct);
        }
        result.IsSuccess.ShouldBeTrue();
        result.Value.OperationId.ShouldBe(input.OperationId);
        (await db.Players.CountAsync(x => x.CreationOperationId == input.OperationId, ct)).ShouldBe(1);
        (await db.PlayerCreationReceipts.CountAsync(x => x.OperationId == input.OperationId, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task DuplicateSelectionPrefersActiveThenLowestPlayerIdentityAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var input = ValidCreateInput();
        var matching = Enumerable.Range(0, 3).Select(index => new PlayerEntity
        {
            ClubId = ClubAId,
            CreatedById = ClubAAdminId,
            CreationOperationId = Guid.CreateVersion7(),
            FirstName = input.FirstName,
            LastName = input.LastName,
            DateOfBirth = input.DateOfBirth,
            GraduationYear = input.GraduationYear,
            LifecycleStatus = index == 0 ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = index == 0 ? DateTimeOffset.UtcNow : null,
            ArchivedById = index == 0 ? ClubAAdminId : null
        }).ToArray();
        await using (var db = _harness.CreateAdminContext())
        {
            db.Players.AddRange(matching);
            await db.SaveChangesAsync(ct);
        }
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var rejected = await CreateService().CreateAsync(input, ct);
        PlayerCreationProblems.TryGetDuplicate(rejected.Problem, out var duplicate).ShouldBeTrue();
        duplicate!.PlayerId.ShouldBe(matching.Skip(1).Min(x => x.PlayerId));
        duplicate.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    [Fact]
    public async Task ExactReplayReturnsOriginalProfileAndEnrollmentAfterLaterChangesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var input = ValidCreateInput();
        var service = CreateService();
        var original = (await service.CreateAsync(input, ct)).Value;
        original.Enrollment.ShouldNotBeNull();
        original.Enrollment.CampaignId.ShouldBe(_activeCampaignId);
        (await service.UpdateAsync(ValidUpdateInput(original.Player.PlayerId), ct)).IsSuccess.ShouldBeTrue();
        await using (var db = _harness.CreateAdminContext())
        {
            db.Campaigns.Remove(await db.Campaigns.SingleAsync(x => x.CampaignId == _activeCampaignId, ct));
            await db.SaveChangesAsync(ct);
        }

        (await service.CreateAsync(input, ct)).Value.ShouldBe(original);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Players.CountAsync(x => x.CreationOperationId == input.OperationId, ct)).ShouldBe(1);
        (await verify.PlayerCreationReceipts.CountAsync(ct)).ShouldBe(1);
        (await verify.Players.SingleAsync(x => x.PlayerId == original.Player.PlayerId, ct)).FirstName.ShouldBe("Updated");
    }

    [Fact]
    public async Task CreationWithoutActiveCampaignReturnsExplicitNullEnrollmentAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _harness.CreateAdminContext())
        {
            db.Campaigns.Remove(await db.Campaigns.SingleAsync(x => x.CampaignId == _activeCampaignId, ct));
            await db.SaveChangesAsync(ct);
        }
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var input = ValidCreateInput();
        var completion = (await CreateService().CreateAsync(input, ct)).Value;
        completion.Enrollment.ShouldBeNull();
        await using var verify = _harness.CreateTenantContext();
        (await verify.PlayerCampaignAssignments.CountAsync(x => x.PlayerId == completion.Player.PlayerId, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task ReplayRejectsChangedPayloadActorAndClubWithoutDisclosingReceiptAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var input = ValidCreateInput();
        (await CreateService().CreateAsync(input, ct)).IsSuccess.ShouldBeTrue();
        var changed = await CreateService().CreateAsync(input with { FirstName = input.FirstName + " " }, ct);
        changed.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        PlayerCreationProblems.IsNotCommitted(changed.Problem, input.OperationId).ShouldBeFalse();
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        (await CreateService().CreateAsync(input, ct)).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);
        (await CreateService().CreateAsync(input, ct)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        await using var verify = _harness.CreateAdminContext();
        (await verify.PlayerCreationReceipts.CountAsync(ct)).ShouldBe(1);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateRejectionRemainsSettledAfterMatchingPlayerChangesAsync(bool archived)
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var input = ValidCreateInput();
        var original = (await CreateService().CreateAsync(input, ct)).Value;
        await using (var db = _harness.CreateAdminContext())
        {
            var player = await db.Players.SingleAsync(x => x.PlayerId == original.Player.PlayerId, ct);
            if (archived)
            {
                player.LifecycleStatus = LifecycleStatus.Archived;
                player.ArchivedAt = DateTimeOffset.UtcNow;
                player.ArchivedById = ClubAMemberId;
                await db.SaveChangesAsync(ct);
            }
        }
        var duplicateInput = input with { OperationId = Guid.CreateVersion7(), FirstName = " " + input.FirstName.ToUpperInvariant() + " " };
        var rejected = await CreateService().CreateAsync(duplicateInput, ct);
        PlayerCreationProblems.IsNotCommitted(rejected.Problem, duplicateInput.OperationId).ShouldBeTrue();
        PlayerCreationProblems.TryGetDuplicate(rejected.Problem, out var duplicate).ShouldBeTrue();
        duplicate!.PlayerId.ShouldBe(original.Player.PlayerId);
        duplicate.LifecycleStatus.ShouldBe(archived ? LifecycleStatus.Archived : LifecycleStatus.Active);
        await using (var db = _harness.CreateAdminContext())
        {
            (await db.Players.SingleAsync(x => x.PlayerId == original.Player.PlayerId, ct)).FirstName = "Renamed";
            await db.SaveChangesAsync(ct);
        }
        var replay = await CreateService().CreateAsync(duplicateInput, ct);
        PlayerCreationProblems.IsNotCommitted(replay.Problem, duplicateInput.OperationId).ShouldBeTrue();
        (await CreateService().CreateAsync(duplicateInput with { OperationId = Guid.CreateVersion7(), LastName = "Corrected" }, ct)).IsSuccess.ShouldBeTrue();
        await using var verify = _harness.CreateAdminContext();
        (await verify.Players.CountAsync(x => x.CreationOperationId == duplicateInput.OperationId, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task StaleMembershipCannotCreateEditOrRecoverAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var input = ValidCreateInput();
        var original = (await CreateService().CreateAsync(input, ct)).Value;
        await using (var db = _harness.CreateAdminContext())
        {
            (await db.Users.SingleAsync(x => x.Id == ClubAMemberId, ct)).ClubId = null;
            await db.SaveChangesAsync(ct);
        }
        (await CreateService().CreateAsync(input, ct)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        (await CreateService().CreateAsync(input with { OperationId = Guid.CreateVersion7() }, ct)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        (await CreateService().UpdateAsync(ValidUpdateInput(original.Player.PlayerId), ct)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Players.SingleAsync(x => x.PlayerId == original.Player.PlayerId, ct)).FirstName.ShouldBe(input.FirstName);
        (await verify.PlayerCreationReceipts.CountAsync(ct)).ShouldBe(1);
    }

    [Fact]
    public async Task ExpiredCreationCannotRestartAfterReceiptCleanupAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var clock = new CreationClock(DateTimeOffset.UtcNow);
        var input = ValidCreateInput() with { OperationId = Guid.CreateVersion7(clock.GetUtcNow()) };
        var service = CreateService(clock);
        var completion = (await service.CreateAsync(input, ct)).Value;
        clock.Now = completion.RecoveryExpiresAt.AddTicks(-1);
        (await service.CreateAsync(input, ct)).Value.ShouldBe(completion);
        clock.Now = completion.RecoveryExpiresAt;
        PlayerCreationProblems.IsNotCommitted((await service.CreateAsync(input, ct)).Problem, input.OperationId).ShouldBeFalse();
        await using (var db = _harness.CreateAdminContext())
        {
            // Simulate receipt removal; provider-specific bounded retention is covered against PostgreSQL.
            await db.PlayerCreationReceipts.Where(receipt => receipt.OperationId == input.OperationId).ExecuteDeleteAsync(ct);
            (await db.PlayerCreationReceipts.CountAsync(ct)).ShouldBe(0);
        }
        (await service.CreateAsync(input, ct)).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Players.CountAsync(x => x.CreationOperationId == input.OperationId, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task CreationReceiptsAreTenantFilteredAndCannotBeRewrittenAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var input = ValidCreateInput();
        (await CreateService().CreateAsync(input, ct)).IsSuccess.ShouldBeTrue();
        await using (var own = _harness.CreateTenantContext()) { (await own.PlayerCreationReceipts.CountAsync(ct)).ShouldBe(1); }
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);
        await using (var other = _harness.CreateTenantContext())
        {
            (await other.PlayerCreationReceipts.CountAsync(ct)).ShouldBe(0);
            other.PlayerCreationReceipts.Add(Receipt(ClubAId, DateTimeOffset.UtcNow.AddHours(1)));
            await Should.ThrowAsync<InvalidOperationException>(() => other.SaveChangesAsync(ct));
        }
        await using var admin = _harness.CreateAdminContext();
        (await admin.PlayerCreationReceipts.SingleAsync(ct)).ResultJson = "{}";
        await Should.ThrowAsync<InvalidOperationException>(() => admin.SaveChangesAsync(ct));
    }

    private static PlayerCreationReceiptEntity Receipt(long club, DateTimeOffset expiry) => new()
    {
        ClubId = club,
        ActorUserId = ClubAMemberId,
        CreatedById = ClubAMemberId,
        OperationId = Guid.CreateVersion7(),
        RecoveryExpiresAt = expiry,
        RequestSha256 = new string('A', 64),
        ResultJson = "{}"
    };

    private sealed class CreationClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
