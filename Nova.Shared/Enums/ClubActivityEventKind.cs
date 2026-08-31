namespace Nova.Shared.Enums;

/// <summary>
/// Identifies the durable activity transitions that may appear in the club activity feed.
/// </summary>
public enum ClubActivityEventKind
{
    /// <summary>A campaign Draft was created.</summary>
    CampaignDraftCreated,
    /// <summary>A campaign Draft was deleted.</summary>
    CampaignDraftDeleted,
    /// <summary>A campaign was opened.</summary>
    CampaignOpened,
    /// <summary>A campaign was closed.</summary>
    CampaignClosed,
    /// <summary>A campaign was reopened.</summary>
    CampaignReopened,
    /// <summary>A player was assigned to a team.</summary>
    PlacementAssigned,
    /// <summary>A player changed teams.</summary>
    PlacementReassigned,
    /// <summary>A player's placement outcome changed.</summary>
    PlacementOutcomeChanged,
    /// <summary>A join request was submitted.</summary>
    JoinRequestSubmitted,
    /// <summary>A join request was cancelled.</summary>
    JoinRequestCancelled,
    /// <summary>A join request was rejected.</summary>
    JoinRequestRejected,
    /// <summary>A join request was approved.</summary>
    JoinRequestApproved,
    /// <summary>A member joined the club (the member-shaped projection of approval).</summary>
    MemberJoined,
    /// <summary>A member was promoted to club administrator.</summary>
    MemberPromoted,
    /// <summary>A member was demoted from club administrator.</summary>
    MemberDemoted,
    /// <summary>A member was removed by an administrator.</summary>
    MemberRemoved,
    /// <summary>A member left voluntarily.</summary>
    MemberLeft,
}
