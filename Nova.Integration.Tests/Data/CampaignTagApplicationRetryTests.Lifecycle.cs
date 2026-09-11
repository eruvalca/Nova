using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Features.Campaigns;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class CampaignTagApplicationRetryTests
{
    [Theory]
    [InlineData("close", ServiceProblemKind.Conflict)]
    [InlineData("archive-player", ServiceProblemKind.NotFound)]
    [InlineData("archive-tag", ServiceProblemKind.Conflict)]
    [InlineData("remove-membership", ServiceProblemKind.Forbidden)]
    public async Task CaptureRechecksCommittedLifecycleAndMembershipChangesAfterWaitingAsync(string change, ServiceProblemKind expected)
    {
        var token = TestContext.Current.CancellationToken;
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (clubId, campaignId, tagId, assignmentId, _) = await SeedTagApplicationDataAsync(actor, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = clubId;
        var service = new CampaignTagApplicationService(fixture.CreateTenantContextFactory(), fixture.CurrentUser, NullLogger<CampaignTagApplicationService>.Instance);
        await using var gate = fixture.CreateAdminContext();
        await using var transaction = await gate.Database.BeginTransactionAsync(token);
        var membership = string.Equals(change, "remove-membership", StringComparison.Ordinal);
        if (membership) { await gate.AcquireUserMembershipLockAsync(actor, token); }
        else { await gate.AcquireClubRosterLockAsync(clubId, token); }
        var input = new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignmentId, PlayerTagId = tagId };
        var pending = service.ApplyAsync(input, token);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(gate, membership ? (long.MinValue / 64) + actor : (long.MinValue / 4) + clubId, token);
        pending.IsCompleted.ShouldBeFalse();
        await CommitCompetingChangeAsync(gate, change, actor, campaignId, tagId, assignmentId, token);
        await transaction.CommitAsync(token);
        var result = await pending;
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(expected);
        EvaluationMutationRejection.IsNotCommitted(result.Problem, input.OperationId).ShouldBe(!membership);
        await using var verify = fixture.CreateAdminContext();
        (await verify.CampaignTagApplications.AnyAsync(application => application.ClubId == clubId && application.CreationOperationId == input.OperationId, token)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == clubId && receipt.OperationId == input.OperationId, token)).ShouldBe(membership ? 0 : 1);
    }

    private static async Task CommitCompetingChangeAsync(NovaAdminDbContext db, string change, long actor,
        long campaignId, long tagId, long assignmentId, CancellationToken token)
    {
        switch (change)
        {
            case "close":
                var campaign = await db.Campaigns.SingleAsync(campaign => campaign.CampaignId == campaignId, token);
                campaign.Status = CampaignStatus.Closed;
                campaign.ClosedAt = DateTimeOffset.UtcNow;
                campaign.ClosedById = actor;
                break;
            case "archive-player":
                var player = await db.PlayerCampaignAssignments.Where(assignment => assignment.PlayerCampaignAssignmentId == assignmentId).Select(assignment => assignment.Player).SingleAsync(token);
                player.LifecycleStatus = LifecycleStatus.Archived;
                player.ArchivedAt = DateTimeOffset.UtcNow;
                player.ArchivedById = actor;
                break;
            case "archive-tag":
                var tag = await db.PlayerTags.SingleAsync(tag => tag.PlayerTagId == tagId, token);
                tag.LifecycleStatus = LifecycleStatus.Archived;
                tag.ArchivedAt = DateTimeOffset.UtcNow;
                tag.ArchivedById = actor;
                break;
            case "remove-membership":
                (await db.Users.SingleAsync(user => user.Id == actor, token)).ClubId = null;
                break;
        }
        await db.SaveChangesAsync(token);
    }
}
