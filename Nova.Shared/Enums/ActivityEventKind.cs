namespace Nova.Shared.Enums;

/// <summary>
/// Identifies every kind of activity event that can be durably recorded for a club. Family and
/// visibility are pure functions of the kind (see <c>Nova.Features.Activity.ActivityEventPolicy</c>),
/// so future foundations (#178 and #179) can add kinds without a schema migration.
/// </summary>
public enum ActivityEventKind
{
    /// <summary>
    /// A campaign draft was created.
    /// </summary>
    CampaignDraftCreated = 0,

    /// <summary>
    /// A campaign draft was deleted.
    /// </summary>
    CampaignDraftDeleted = 1,

    /// <summary>
    /// A campaign was opened (transitioned to Active).
    /// </summary>
    CampaignOpened = 2,

    /// <summary>
    /// A campaign was closed.
    /// </summary>
    CampaignClosed = 3,

    /// <summary>
    /// A closed campaign was reopened.
    /// </summary>
    CampaignReopened = 4,

    /// <summary>
    /// A participant was placed on a team in a campaign.
    /// </summary>
    PlacementAssigned = 5,

    /// <summary>
    /// A participant was marked not selected for a campaign.
    /// </summary>
    PlacementNotSelected = 6,

    /// <summary>
    /// A participant's placement was withdrawn from a campaign.
    /// </summary>
    PlacementWithdrawn = 7,

    /// <summary>
    /// A participant's team assignment changed within the same campaign.
    /// </summary>
    PlacementReassigned = 8,

    /// <summary>
    /// A participant's placement outcome was replaced (for example, Withdrawn reverted to Assigned).
    /// </summary>
    PlacementOutcomeReplaced = 9,

    /// <summary>
    /// An earlier placement decision was superseded by an explicit decision from a later campaign.
    /// </summary>
    PlacementSuperseded = 10,

    /// <summary>
    /// A user submitted a join request to the club.
    /// </summary>
    JoinRequestSubmitted = 11,

    /// <summary>
    /// A user cancelled their pending join request.
    /// </summary>
    JoinRequestCancelled = 12,

    /// <summary>
    /// A pending join request was rejected by a club administrator.
    /// </summary>
    JoinRequestRejected = 13,

    /// <summary>
    /// A user joined the club (an approved join request).
    /// </summary>
    MemberJoined = 14,

    /// <summary>
    /// A member was removed from the club.
    /// </summary>
    MemberRemoved = 15,

    /// <summary>
    /// A member left the club.
    /// </summary>
    MemberLeft = 16,

    /// <summary>
    /// A member was promoted to an administrator role.
    /// </summary>
    MemberPromoted = 17,

    /// <summary>
    /// A member was demoted from an administrator role.
    /// </summary>
    MemberDemoted = 18,
}
