using System.Text.Json.Serialization;
using Nova.Shared.Enums;

namespace Nova.Shared.Features.Dashboard;

/// <summary>Identifies the activity kinds exposed by the dashboard feed.</summary>
public enum DashboardActivityEventKind
{
    /// <summary>Legacy note event; new club activity never emits this kind.</summary>
    NoteAdded = 0,
    /// <summary>Legacy tag event; new club activity never emits this kind.</summary>
    TagApplied = 1,
    /// <summary>Legacy placement event.</summary>
    PlacementSet = 2,
    /// <summary>A campaign was closed.</summary>
    CampaignClosed = 3,
    /// <summary>A campaign was reopened.</summary>
    CampaignReopened = 4,
    /// <summary>A campaign was opened.</summary>
    CampaignOpened = 5,
    /// <summary>A placement was assigned.</summary>
    PlacementAssigned = 6,
    /// <summary>A placement was reassigned.</summary>
    PlacementReassigned = 7,
    /// <summary>A placement outcome changed.</summary>
    PlacementOutcomeChanged = 8,
    /// <summary>A join request was submitted.</summary>
    JoinRequestSubmitted = 9,
    /// <summary>A join request was cancelled.</summary>
    JoinRequestCancelled = 10,
    /// <summary>A join request was rejected.</summary>
    JoinRequestRejected = 11,
    /// <summary>A join request was approved.</summary>
    JoinRequestApproved = 12,
    /// <summary>A member joined the club.</summary>
    MemberJoined = 13,
    /// <summary>A member was promoted.</summary>
    MemberPromoted = 14,
    /// <summary>A member was demoted.</summary>
    MemberDemoted = 15,
    /// <summary>A member was removed.</summary>
    MemberRemoved = 16,
    /// <summary>A member left voluntarily.</summary>
    MemberLeft = 17,
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
    public DashboardActivityContextDto? Context { get; init; }

    // Legacy fields remain deserializable for existing campaign/evaluation components while the
    // dashboard migrates to Context. New server responses populate Context and not these fields.
    /// <summary>Legacy actor identifier.</summary>
    public long ActorUserId { get; init; }
    /// <summary>Legacy actor display name.</summary>
    public string? ActorDisplayName { get; init; }
    /// <summary>Legacy campaign identifier.</summary>
    public long CampaignId { get; init; }
    /// <summary>Legacy campaign display name.</summary>
    public string? CampaignName { get; init; }
    /// <summary>Legacy assignment identifier.</summary>
    public long? PlayerCampaignAssignmentId { get; init; }
    /// <summary>Legacy player display name.</summary>
    public string? PlayerDisplayName { get; init; }
    /// <summary>Legacy tag display name.</summary>
    public string? TagName { get; init; }
    /// <summary>Legacy placement outcome.</summary>
    public PlacementOutcome? PlacementOutcome { get; init; }
    /// <summary>Legacy lifecycle event type.</summary>
    public CampaignLifecycleEventType? LifecycleEventType { get; init; }
}

/// <summary>The fixed-size, cursor-paged club activity response.</summary>
public sealed record DashboardActivityResult(IReadOnlyList<DashboardActivityItemDto> Events)
{
    /// <summary>The number of events returned per page.</summary>
    public const int PageSize = 20;
    /// <summary>The visible events in newest-first order.</summary>
    /// <summary>The opaque continuation token for the next older page.</summary>
    public string? NextContinuationToken { get; init; }
}
