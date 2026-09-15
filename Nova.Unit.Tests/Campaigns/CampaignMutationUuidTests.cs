using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData('0', false)]
    [InlineData('7', false)]
    [InlineData('8', true)]
    [InlineData('9', true)]
    [InlineData('a', true)]
    [InlineData('b', true)]
    [InlineData('c', false)]
    [InlineData('f', false)]
    public async Task PlacementAndEvaluationRequireTheRfcUuidVariantAsync(char variant, bool valid)
    {
        ActAs(ClubAMemberId, ClubAId);
        var bytes = Guid.CreateVersion7().ToString("N").ToCharArray();
        bytes[16] = variant;
        var operationId = Guid.ParseExact(new string(bytes), "N");
        ICampaignPlacementService service = CreateService();
        var result = await service.UpdatePlacementAsync(new(ClubAAssignmentId, PlacementOutcome.NotSelected, null,
            _clubAConcurrencyToken, operationId), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBe(valid);
        if (!valid) { result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation); }

        var invoked = false;
        var executor = new EvaluationMutationExecutor(new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext), _harness.CurrentUser);
        var evaluation = await executor.ExecuteAsync<string>(new AddEvaluationNoteInput
        {
            OperationId = operationId,
            PlayerCampaignAssignmentId = ClubAAssignmentId,
            Content = "Variant boundary"
        }, (_, _, _, _) =>
        {
            invoked = true;
            return Task.FromResult(new ServiceResult<string>(ServiceProblem.ServerError("Stop before recording effects.")));
        }, TestContext.Current.CancellationToken);
        invoked.ShouldBe(valid);
        evaluation.IsProblem.ShouldBeTrue();
        await using var db = _harness.CreateAdminContext();
        (await db.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(valid ? 1 : 0);
        (await db.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await db.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(valid ? 1 : 0);
        var saved = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == ClubAAssignmentId, TestContext.Current.CancellationToken);
        saved.PlacementOutcome.ShouldBe(valid ? PlacementOutcome.NotSelected : PlacementOutcome.Undecided);
        if (!valid) { saved.ConcurrencyToken.ShouldBe(_clubAConcurrencyToken); }
    }
}
