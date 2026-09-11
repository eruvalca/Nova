using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.Unit.Tests.Account;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignTagApplicationServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ApplicationCreationSnapshotsActorAndDuplicateAndMembershipChangesPreserveItAsync(bool createDefinition, bool deleteAuthor)
    {
        var token = TestContext.Current.CancellationToken;
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();
        var created = createDefinition
            ? await service.CreateAndApplyAsync(new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, Label = "Strong vision" }, token)
            : await service.ApplyAsync(new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = SecondaryActiveTagId }, token);
        created.IsSuccess.ShouldBeTrue();
        using (var db = _harness.CreateAdminContext())
        {
            var application = await db.CampaignTagApplications.SingleAsync(row => row.CampaignTagApplicationId == created.Value.CampaignTagApplicationId, token);
            application.AuthorDisplayName.ShouldBe("Member A");
            var author = await db.Users.SingleAsync(user => user.Id == ClubAMemberId, token);
            author.FirstName = "Changed";
            author.LastName = "Name";
            if (deleteAuthor) { db.Users.Remove(author); }
            else { author.ClubId = ClubBId; }
            await db.SaveChangesAsync(token);
        }
        ActAs(ClubAOtherMemberId, ClubAId);
        var duplicate = createDefinition
            ? await service.CreateAndApplyAsync(new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, Label = "STRONG VISION" }, token)
            : await service.ApplyAsync(new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = created.Value.PlayerTagId }, token);
        duplicate.IsSuccess.ShouldBeTrue();
        duplicate.Value.AlreadyApplied.ShouldBeTrue();
        duplicate.Value.CampaignTagApplicationId.ShouldBe(created.Value.CampaignTagApplicationId);
        using var metadata = _harness.CreateAdminContext();
        var saved = await metadata.CampaignTagApplications.SingleAsync(row => row.CampaignTagApplicationId == created.Value.CampaignTagApplicationId, token);
        saved.CreatedById.ShouldBe(ClubAMemberId);
        saved.AuthorDisplayName.ShouldBe("Member A");
        var campaignId = await metadata.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == ActiveAssignmentId).Select(row => row.CampaignId).SingleAsync(token);
        var queries = new CampaignEvaluationQueryService(new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()), _harness.CurrentUser);
        var history = await queries.GetApplicationsAsync(new GetEvaluationHistoryInput { CampaignId = campaignId, PlayerCampaignAssignmentId = ActiveAssignmentId }, token);
        history.IsSuccess.ShouldBeTrue();
        var projected = history.Value.Items.Single(row => row.CampaignTagApplicationId == created.Value.CampaignTagApplicationId);
        projected.ActorDisplayName.ShouldBe("Member A");
        projected.CanRemove.ShouldBeFalse();
    }
}
