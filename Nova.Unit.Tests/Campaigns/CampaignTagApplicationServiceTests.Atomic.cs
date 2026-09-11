using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignTagApplicationServiceTests
{
    [Fact]
    public async Task CreateAndApplyNormalizesWhitespaceAndReusesFirstCreatedCasingAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();
        var input = new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, Label = "  Good\t ball   control  " };
        var created = await service.CreateAndApplyAsync(input, TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue();
        created.Value.AlreadyApplied.ShouldBeFalse();
        var replay = await service.CreateAndApplyAsync(input, TestContext.Current.CancellationToken);
        replay.Value.ShouldBe(created.Value);
        ActAs(ClubAOtherMemberId, ClubAId);
        var existing = await service.CreateAndApplyAsync(input with { OperationId = Guid.CreateVersion7(), Label = "GOOD BALL CONTROL" }, TestContext.Current.CancellationToken);
        existing.IsSuccess.ShouldBeTrue();
        existing.Value.AlreadyApplied.ShouldBeTrue();
        existing.Value.CampaignTagApplicationId.ShouldBe(created.Value.CampaignTagApplicationId);
        using var verify = _harness.CreateAdminContext();
        var tag = (await verify.PlayerTags.SingleAsync(tag => tag.PlayerTagId == created.Value.PlayerTagId, TestContext.Current.CancellationToken));
        tag.Name.ShouldBe("Good ball control");
        tag.NormalizedName.ShouldBe("GOOD BALL CONTROL");
        tag.Color.ShouldBe(CollaborativeTagPolicy.DefaultColor);
        (await verify.PlayerTags.CountAsync(tag => tag.NormalizedName == "GOOD BALL CONTROL", TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.CampaignTagApplications.SingleAsync(application => application.CampaignTagApplicationId == created.Value.CampaignTagApplicationId, TestContext.Current.CancellationToken)).CreatedById.ShouldBe(ClubAMemberId);
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task CreateAndApplyRejectsArchivedNameWithRestoreGuidanceWithoutEffectsAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var input = new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, Label = "  ARCHIVED " };
        var result = await CreateService().CreateAndApplyAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldNotBeNull();
        result.Problem.Detail.ShouldContain("restore");
        using var verify = _harness.CreateAdminContext();
        (await verify.PlayerTags.CountAsync(tag => tag.NormalizedName == "ARCHIVED", TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task AtActiveDefinitionCapExistingTraitsRemainApplicableAndNewNamesAreRejectedAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
            for (var index = 0; index < 98; index++)
            {
                db.PlayerTags.Add(new PlayerTagEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = $"Trait {index}",
                    NormalizedName = $"TRAIT {index}",
                    Color = CollaborativeTagPolicy.DefaultColor,
                    ClubId = ClubAId,
                    CreatedById = ClubAAdminId,
                    LifecycleStatus = LifecycleStatus.Active
                });
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();
        var existing = await service.CreateAndApplyAsync(new CreateAndApplyCampaignTagInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = ActiveAssignmentId,
            Label = "secondary active"
        }, TestContext.Current.CancellationToken);
        existing.IsSuccess.ShouldBeTrue();
        existing.Value.PlayerTagId.ShouldBe(SecondaryActiveTagId);
        var rejected = await service.CreateAndApplyAsync(new CreateAndApplyCampaignTagInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = ActiveAssignmentId,
            Label = "A new trait"
        }, TestContext.Current.CancellationToken);
        rejected.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        rejected.Problem.Detail.ShouldNotBeNull();
        rejected.Problem.Detail.ShouldContain("100");
        using var verify = _harness.CreateAdminContext();
        (await verify.PlayerTags.CountAsync(tag => tag.ClubId == ClubAId && tag.LifecycleStatus == LifecycleStatus.Active, TestContext.Current.CancellationToken)).ShouldBe(100);
        (await verify.PlayerTags.AnyAsync(tag => tag.Name == "A new trait", TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveReplayReturnsOriginalReceiptWithoutRepeatingRemovalAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var input = new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ExistingApplicationId };
        var service = CreateService();
        var removed = await service.RemoveAsync(input, TestContext.Current.CancellationToken);
        removed.IsSuccess.ShouldBeTrue();
        var replayed = await service.RemoveAsync(input, TestContext.Current.CancellationToken);
        replayed.IsSuccess.ShouldBeTrue();
        replayed.Value.ShouldBe(removed.Value);
        using var verify = _harness.CreateAdminContext();
        (await verify.CampaignTagApplications.AnyAsync(application => application.CampaignTagApplicationId == ExistingApplicationId, TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }
}
