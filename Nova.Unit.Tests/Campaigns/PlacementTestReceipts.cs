using Nova.SharedKernel.Features.Campaigns;

namespace Nova.Unit.Tests.Campaigns;

internal static class PlacementTestReceipts
{
    internal static PlacementMutationSuccess Success(UpdateCampaignPlacementInput input, Guid token)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlacementMutationSuccess(token)
        {
            Receipt = new PlacementMutationReceipt(input.OperationId, input.PlayerCampaignAssignmentId,
                new CampaignSavedPlacementDecision(input.PlayerCampaignAssignmentId, 7, 42, 10, 1,
                    input.Outcome, input.TeamId, now, 200, "Test member", token), now, now.AddHours(24))
        };
    }
}
