using Nova.Shared.Enums;

namespace Nova.Features.ClubActivity;

/// <summary>Immutable placement facts used to classify one activity transition.</summary>
public sealed record PlacementActivityState(
    PlacementOutcome Outcome,
    long? TeamId,
    string? TeamName,
    string? SourceCampaignName);

/// <summary>Pure classification rules for durable club activity evidence.</summary>
internal static class ClubActivityEventPolicy
{
    /// <summary>Classifies the meaningful placement transition, or null for a no-op.</summary>
    public static ClubActivityEventKind? ClassifyPlacement(
        PlacementActivityState previous,
        PlacementActivityState current)
    {
        if (previous == current)
        {
            return null;
        }

        if (current.Outcome == PlacementOutcome.Assigned
            && previous.Outcome == PlacementOutcome.Undecided)
        {
            return ClubActivityEventKind.PlacementAssigned;
        }

        if (previous.Outcome == PlacementOutcome.Assigned
            && current.Outcome == PlacementOutcome.Assigned
            && previous.TeamId != current.TeamId)
        {
            return ClubActivityEventKind.PlacementReassigned;
        }

        return ClubActivityEventKind.PlacementOutcomeChanged;
    }

    /// <summary>Returns the server-enforced audience for an event kind.</summary>
    public static ClubActivityAudience AudienceFor(ClubActivityEventKind kind)
        => kind switch
        {
            ClubActivityEventKind.CampaignDraftCreated
                or ClubActivityEventKind.CampaignDraftDeleted
                or ClubActivityEventKind.JoinRequestSubmitted
                or ClubActivityEventKind.JoinRequestCancelled
                or ClubActivityEventKind.JoinRequestRejected
                => ClubActivityAudience.Administrators,
            _ => ClubActivityAudience.AllMembers
        };
}
