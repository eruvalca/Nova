using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using OneOf;

namespace Nova.Features.Campaigns;

internal readonly record struct CampaignMayReopen;
internal readonly record struct CampaignReopenBlocked(CampaignReopenUnavailableReason Reason, string Detail);

/// <summary>Shared lifecycle decision for the member-visible preview and the locked reopen command.</summary>
internal static class CampaignReopenPolicy
{
    internal static OneOf<CampaignMayReopen, CampaignReopenBlocked> Evaluate(CampaignStatus status,
        long seasonId, long? currentSeasonId, long? openingSequence, long? latestOpeningSequence, bool anotherActiveCampaign)
    {
        if (status != CampaignStatus.Closed)
        {
            return new CampaignReopenBlocked(CampaignReopenUnavailableReason.NotClosed, status == CampaignStatus.Active ? "The campaign is already active." : "Only a closed campaign can be reopened.");
        }
        if (seasonId != currentSeasonId)
        {
            return new CampaignReopenBlocked(CampaignReopenUnavailableReason.HistoricalSeason, "Only a campaign in the club's current season can be reopened.");
        }
        if (openingSequence is null)
        {
            return new CampaignReopenBlocked(CampaignReopenUnavailableReason.MissingOpening, "The campaign has no authoritative opening sequence and cannot be reopened.");
        }
        if (openingSequence != latestOpeningSequence)
        {
            return new CampaignReopenBlocked(CampaignReopenUnavailableReason.LaterCampaignOpened, "Only the most recently opened campaign in the current season can be reopened.");
        }
        return anotherActiveCampaign
            ? new CampaignReopenBlocked(CampaignReopenUnavailableReason.AnotherActiveCampaign, "Another campaign is already active for this club.")
            : new CampaignMayReopen();
    }
}
