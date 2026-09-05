using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;

namespace Nova.Extensions.Campaigns;

/// <summary>Maps campaign-local saved decisions without treating participation as placement.</summary>
internal static class PlayerCampaignAssignmentEntityExtensions
{
    extension(PlayerCampaignAssignmentEntity assignment)
    {
        /// <summary>Builds a saved-decision snapshot with the Campaign navigation already loaded.</summary>
        /// <returns>The explicit decision, or null for technical enrollment.</returns>
        public CampaignSavedPlacementDecision? ToSavedPlacementDecision()
            => assignment.PlacementOutcome == PlacementOutcome.Undecided ? null : new(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlayerId,
                assignment.CampaignId,
                assignment.Campaign.SeasonId,
                assignment.Campaign.SeasonOpeningSequence!.Value,
                assignment.PlacementOutcome,
                assignment.TeamId,
                assignment.DecisionRecordedAt,
                assignment.DecisionRecordedById,
                assignment.DecisionActorDisplayName,
                assignment.ConcurrencyToken);
    }
}
