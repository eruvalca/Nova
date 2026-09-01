using System.Text.Json.Serialization;
using Nova.Shared.Enums;

namespace Nova.Shared.Features.Dashboard;

/// <summary>Identifies the activity kinds exposed by the dashboard feed.</summary>
public enum DashboardActivityEventKind
{
    /// <summary>A campaign Draft was created.</summary>
    CampaignDraftCreated = 0,
    /// <summary>A campaign Draft was deleted.</summary>
    CampaignDraftDeleted = 1,
    /// <summary>A campaign was opened.</summary>
    CampaignOpened = 2,
    /// <summary>A campaign was closed.</summary>
    CampaignClosed = 3,
    /// <summary>A campaign was reopened.</summary>
    CampaignReopened = 4,
    /// <summary>A placement was assigned.</summary>
    PlacementAssigned = 5,
    /// <summary>A placement was reassigned.</summary>
    PlacementReassigned = 6,
    /// <summary>A placement outcome changed.</summary>
    PlacementOutcomeChanged = 7,
    /// <summary>A join request was submitted.</summary>
    JoinRequestSubmitted = 8,
    /// <summary>A join request was cancelled.</summary>
    JoinRequestCancelled = 9,
    /// <summary>A join request was rejected.</summary>
    JoinRequestRejected = 10,
    /// <summary>A join request was approved.</summary>
    JoinRequestApproved = 11,
    /// <summary>A member joined the club.</summary>
    MemberJoined = 12,
    /// <summary>A member was promoted.</summary>
    MemberPromoted = 13,
    /// <summary>A member was demoted.</summary>
    MemberDemoted = 14,
    /// <summary>A member was removed.</summary>
    MemberRemoved = 15,
    /// <summary>A member left voluntarily.</summary>
    MemberLeft = 16,
}

/// <summary>Identifies the structured family carried by a dashboard activity item.</summary>
public enum DashboardActivityContextFamily
{
    /// <summary>Campaign lifecycle context.</summary>
    Campaign,
    /// <summary>Placement transition context.</summary>
    Placement,
    /// <summary>Join-request context.</summary>
    JoinRequest,
    /// <summary>Membership or role context.</summary>
    Membership,
}

/// <summary>Common context for a campaign activity item.</summary>
public sealed record CampaignActivityContextDto : DashboardActivityContextDto
{
    /// <summary>The actor's stable display name.</summary>
    public required string ActorDisplayName { get; init; }
    /// <summary>The campaign identifier when the campaign still exists and is authorized.</summary>
    public long? CampaignId { get; init; }
    /// <summary>The durable campaign display name.</summary>
    public required string CampaignName { get; init; }
    /// <summary>The durable season display name.</summary>
    public string? SeasonName { get; init; }
}

/// <summary>A placement outcome and team snapshot used by placement activity.</summary>
public sealed record PlacementSnapshotDto
{
    /// <summary>The outcome at this point in the transition.</summary>
    public required PlacementOutcome Outcome { get; init; }
    /// <summary>The team identifier when it remains available.</summary>
    public long? TeamId { get; init; }
    /// <summary>The durable team display name.</summary>
    public string? TeamName { get; init; }
    /// <summary>The source campaign name for a superseded prior decision.</summary>
    public string? SourceCampaignName { get; init; }
}

/// <summary>Structured context for a placement transition.</summary>
public sealed record PlacementActivityContextDto : DashboardActivityContextDto
{
    /// <summary>The actor's stable display name.</summary>
    public required string ActorDisplayName { get; init; }
    /// <summary>The player identifier when the target remains available.</summary>
    public long? PlayerId { get; init; }
    /// <summary>The durable player display name.</summary>
    public required string PlayerDisplayName { get; init; }
    /// <summary>The assignment identifier when the target remains available.</summary>
    public long? PlayerCampaignAssignmentId { get; init; }
    /// <summary>The campaign identifier when the campaign remains available.</summary>
    public long? CampaignId { get; init; }
    /// <summary>The durable campaign display name.</summary>
    public required string CampaignName { get; init; }
    /// <summary>The previous placement state.</summary>
    public required PlacementSnapshotDto Previous { get; init; }
    /// <summary>The new placement state.</summary>
    public required PlacementSnapshotDto Current { get; init; }
}

/// <summary>Structured context for administrator-visible join-request activity.</summary>
public sealed record JoinRequestActivityContextDto : DashboardActivityContextDto
{
    /// <summary>The actor's stable display name.</summary>
    public required string ActorDisplayName { get; init; }
    /// <summary>The requester identifier when the user remains available to the administrator.</summary>
    public long? RequesterUserId { get; init; }
    /// <summary>The durable requester display name.</summary>
    public required string RequesterDisplayName { get; init; }
    /// <summary>The request identifier when the request is still actionable.</summary>
    public long? ActionableRequestId { get; init; }
}

/// <summary>Structured context for membership, role, removal, departure, and member-joined activity.</summary>
public sealed record MembershipActivityContextDto : DashboardActivityContextDto
{
    /// <summary>The member identifier when the member remains available.</summary>
    public long? MemberUserId { get; init; }
    /// <summary>The durable member display name.</summary>
    public required string MemberDisplayName { get; init; }
    /// <summary>The actor's stable display name when the event has a distinct actor.</summary>
    public string? ActorDisplayName { get; init; }
}

/// <summary>Polymorphic structured context for a dashboard activity item.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "family")]
[JsonDerivedType(typeof(CampaignActivityContextDto), "campaign")]
[JsonDerivedType(typeof(PlacementActivityContextDto), "placement")]
[JsonDerivedType(typeof(JoinRequestActivityContextDto), "joinRequest")]
[JsonDerivedType(typeof(MembershipActivityContextDto), "membership")]
public abstract record DashboardActivityContextDto;

/// <summary>One durable, role-shaped activity event in the club feed.</summary>
public sealed record DashboardActivityItemDto
{
    /// <summary>The event kind.</summary>
    public required DashboardActivityEventKind Kind { get; init; }
    /// <summary>The stable event identity and ordering key.</summary>
    public required long EventId { get; init; }
    /// <summary>When the event was committed.</summary>
    public required DateTimeOffset EventAt { get; init; }
    /// <summary>The family-specific structured event context.</summary>
    public required DashboardActivityContextDto Context { get; init; }
}

/// <summary>The fixed-size, cursor-paged club activity response.</summary>
public sealed record DashboardActivityResult(IReadOnlyList<DashboardActivityItemDto> Events)
{
    /// <summary>The number of events returned per page.</summary>
    public const int PageSize = 20;
    /// <summary>The opaque continuation token for the next older page.</summary>
    public string? NextContinuationToken { get; init; }
}
