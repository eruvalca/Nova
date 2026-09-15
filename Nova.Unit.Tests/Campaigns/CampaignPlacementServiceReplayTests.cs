using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DemotedAdministratorCanRecoverAnOverrideButCannotExecuteANewOverrideAsync(bool committed)
    {
        var priorToken = await SeedPriorDecisionAsync(PlacementOutcome.Withdrawn);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        ICampaignPlacementService service = CreateService();
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.Assigned, EligibleTeamId, _clubAConcurrencyToken, Guid.CreateVersion7());
        PlacementMutationSuccess? original = null;
        if (committed)
        {
            original = (await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken)).Value;
        }
        await using (var demote = _harness.CreateAdminContext())
        {
            await demote.UserRoles.Where(role => role.UserId == ClubAAdminId).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }
        // Keep stale administrator claims: persisted authority must decide whether new effects are allowed.
        var result = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        if (committed) { result.Value.ShouldBe(original!.Value); }
        else { result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden); }
        await using var verify = _harness.CreateAdminContext();
        var prior = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == 310, TestContext.Current.CancellationToken);
        prior.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        prior.ConcurrencyToken.ShouldBe(priorToken);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(committed ? 1 : 0);
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ExactReplayReturnsOriginalDecisionAfterLaterSaveAndClosureAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignPlacementService service = CreateService();
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.Assigned, EligibleTeamId, _clubAConcurrencyToken, Guid.CreateVersion7());
        var first = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();
        var later = await service.UpdatePlacementAsync(input with
        {
            Outcome = PlacementOutcome.NotSelected,
            TeamId = null,
            ExpectedConcurrencyToken = first.Value.ConcurrencyToken,
            OperationId = Guid.CreateVersion7()
        }, TestContext.Current.CancellationToken);
        later.IsSuccess.ShouldBeTrue();
        await using (var close = _harness.CreateAdminContext())
        {
            var campaign = await close.Campaigns.SingleAsync(row => row.CampaignId == 600, TestContext.Current.CancellationToken);
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow;
            campaign.ClosedById = ClubAAdminId;
            await close.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var replay = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(first.Value);
        replay.Value.Receipt.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
        replay.Value.Receipt.Decision.TeamId.ShouldBe(EligibleTeamId);
        await using var verify = _harness.CreateAdminContext();
        var assignment = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == ClubAAssignmentId, TestContext.Current.CancellationToken);
        assignment.ConcurrencyToken.ShouldBe(later.Value.ConcurrencyToken);
        assignment.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("outcome")]
    [InlineData("team")]
    [InlineData("token")]
    [InlineData("participant")]
    public async Task ReusedOperationRejectsChangedPayloadWithoutReplacingReceiptAsync(string field)
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignPlacementService service = CreateService();
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.Assigned, EligibleTeamId, _clubAConcurrencyToken, Guid.CreateVersion7());
        var first = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();
        var changed = field switch
        {
            "outcome" => input with { Outcome = PlacementOutcome.NotSelected, TeamId = null },
            "team" => input with { TeamId = SecondEligibleTeamId },
            "token" => input with { ExpectedConcurrencyToken = first.Value.ConcurrencyToken },
            "participant" => input with { PlayerCampaignAssignmentId = ClubAReassignedAssignmentId },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var rejected = await service.UpdatePlacementAsync(changed, TestContext.Current.CancellationToken);

        rejected.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        PlacementMutationRejection.IsNotCommitted(rejected.Problem, input.OperationId).ShouldBeFalse();
        (await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken)).Value.ShouldBe(first.Value);
        await using var verify = _harness.CreateAdminContext();
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task AnotherCurrentMemberCannotRecoverActorsReceiptAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignPlacementService service = CreateService();
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.NotSelected, null, _clubAConcurrencyToken, Guid.CreateVersion7());
        (await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var rejected = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);

        rejected.IsProblem.ShouldBeTrue();
        PlacementMutationRejection.IsNotCommitted(rejected.Problem, input.OperationId).ShouldBeFalse();
        await using var verify = _harness.CreateAdminContext();
        (await verify.PlacementMutationReceipts.SingleAsync(TestContext.Current.CancellationToken)).ActorUserId.ShouldBe(ClubAMemberId);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task RemovedMemberCannotStartOrRecoverUsingStaleClaimsAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignPlacementService service = CreateService();
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.NotSelected, null, _clubAConcurrencyToken, Guid.CreateVersion7());
        var saved = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        saved.IsSuccess.ShouldBeTrue();
        await using (var remove = _harness.CreateAdminContext())
        {
            var member = await remove.Users.SingleAsync(row => row.Id == ClubAMemberId, TestContext.Current.CancellationToken);
            member.ClubId = null;
            await remove.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var replay = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        var next = await service.UpdatePlacementAsync(input with { OperationId = Guid.CreateVersion7(), ExpectedConcurrencyToken = saved.Value.ConcurrencyToken }, TestContext.Current.CancellationToken);

        replay.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        next.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        PlacementMutationRejection.IsNotCommitted(replay.Problem, input.OperationId).ShouldBeFalse();
        await using var verify = _harness.CreateAdminContext();
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task ExpiredOperationWithoutReceiptCannotExecuteDelayedCommandAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignPlacementService service = CreateService();
        var operation = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddHours(-25));
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.Assigned, EligibleTeamId, _clubAConcurrencyToken, operation);

        var expired = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);

        expired.IsProblem.ShouldBeTrue();
        PlacementMutationRejection.IsNotCommitted(expired.Problem, operation).ShouldBeFalse();
        await using var verify = _harness.CreateAdminContext();
        var assignment = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == ClubAAssignmentId, TestContext.Current.CancellationToken);
        assignment.ConcurrencyToken.ShouldBe(_clubAConcurrencyToken);
        assignment.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task DefinitiveRejectionRemainsRejectedAfterTeamBecomesCompatibleAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignPlacementService service = CreateService();
        var input = new UpdateCampaignPlacementInput(ClubAAssignmentId, PlacementOutcome.Assigned, IneligibleTeamId, _clubAConcurrencyToken, Guid.CreateVersion7());
        var rejected = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        rejected.IsProblem.ShouldBeTrue();
        PlacementMutationRejection.IsNotCommitted(rejected.Problem, input.OperationId).ShouldBeTrue();
        await using (var correct = _harness.CreateAdminContext())
        {
            var team = await correct.Teams.SingleAsync(row => row.TeamId == IneligibleTeamId, TestContext.Current.CancellationToken);
            team.GraduationYear = 2030;
            await correct.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var replay = await service.UpdatePlacementAsync(input, TestContext.Current.CancellationToken);

        replay.Problem.Kind.ShouldBe(rejected.Problem.Kind);
        PlacementMutationRejection.IsNotCommitted(replay.Problem, input.OperationId).ShouldBeTrue();
        await using var verify = _harness.CreateAdminContext();
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == ClubAAssignmentId, TestContext.Current.CancellationToken)).ConcurrencyToken.ShouldBe(_clubAConcurrencyToken);
        (await service.UpdatePlacementAsync(input with { OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }
}
