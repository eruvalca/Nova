using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class CampaignTagApplicationRetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicCreateAndApplyRetriesFreshContextsAndPersistsExactlyOneCompleteEffectAsync(bool lostAcknowledgment)
    {
        var actorUserId = (BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, assignmentId, _) = await SeedTagApplicationDataAsync(actorUserId, suffix);
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        var beforeCommit = new FailFirstTransactionCommitInterceptor();
        var afterCommit = new FailFirstCommittedTransactionInterceptor();
        IInterceptor interceptor = lostAcknowledgment ? afterCommit : beforeCommit;
        var factory = new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, interceptor);
        var service = new CampaignTagApplicationService(factory, fixture.CurrentUser, NullLogger<CampaignTagApplicationService>.Instance);
        var input = new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignmentId, Label = $"  Good   control {suffix}  " };

        var result = await service.CreateAndApplyAsync(input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (lostAcknowledgment ? afterCommit.FailureCount : beforeCommit.FailureCount).ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThanOrEqualTo(3);
        var replay = await service.CreateAndApplyAsync(input, TestContext.Current.CancellationToken);
        replay.Value.ShouldBe(result.Value);
        await using var verify = fixture.CreateAdminContext();
        var tags = await verify.PlayerTags.Where(tag => tag.ClubId == clubId && tag.CreationOperationId == input.OperationId).ToListAsync(TestContext.Current.CancellationToken);
        tags.ShouldHaveSingleItem().Name.ShouldBe($"Good control {suffix}");
        var applications = await verify.CampaignTagApplications.Where(application => application.ClubId == clubId && application.CreationOperationId == input.OperationId).ToListAsync(TestContext.Current.CancellationToken);
        applications.ShouldHaveSingleItem().PlayerTagId.ShouldBe(tags[0].PlayerTagId);
        applications[0].CreatedById.ShouldBe(actorUserId);
        (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == clubId && receipt.OperationId == input.OperationId, TestContext.Current.CancellationToken)).ShouldBe(1);
    }
}

