using Nova.SharedKernel.Enums;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Defines displayable saved transitions for both server projection and HTTP validation.</summary>
public static class PlacementHistoryValidation
{
    /// <summary>Rejects incomplete or contradictory evidence without inferring missing history.</summary>
    public static bool IsValid(PlacementHistoryItem? item) => item is { EventId: > 0, CampaignId: > 0 }
        && !string.IsNullOrWhiteSpace(item.CampaignName) && !string.IsNullOrWhiteSpace(item.ActorDisplayName)
        && item.OccurredAt > DateTimeOffset.UnixEpoch && Enum.IsDefined(item.Outcome)
        && item.Outcome != PlacementOutcome.Undecided
        && (item.Outcome == PlacementOutcome.Assigned ? !string.IsNullOrWhiteSpace(item.TeamName) : item.TeamName is null)
        && (item.PreviousOutcome is null || Enum.IsDefined(item.PreviousOutcome.Value));
}
