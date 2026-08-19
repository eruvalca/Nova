using Nova.Shared.Enums;

namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// Identifies the kind of activity shown in the club dashboard's recent-activity feed. The numeric
/// values are the fixed tie-break rank used when two events share a timestamp.
/// </summary>
public enum DashboardActivityEventKind
{
    /// <summary>
    /// An evaluation note was added to a participant.
    /// </summary>
    NoteAdded = 0,

    /// <summary>
    /// A tag was applied to a participant.
    /// </summary>
    TagApplied = 1,

    /// <summary>
    /// A participant's placement outcome was set.
    /// </summary>
    PlacementSet = 2,

    /// <summary>
    /// A campaign was closed.
    /// </summary>
    CampaignClosed = 3,

    /// <summary>
    /// A campaign was reopened.
    /// </summary>
    CampaignReopened = 4,
}

/// <summary>
/// One event in the bounded, deterministically ordered club dashboard recent-activity feed. Kind-specific
/// fields are <see langword="null"/> unless the event kind uses them.
/// </summary>
public sealed record DashboardActivityItemDto
{
    /// <summary>
    /// Gets the event kind.
    /// </summary>
    public required DashboardActivityEventKind Kind { get; init; }

    /// <summary>
    /// Gets the per-kind entity identifier used as the final ordering tie-break.
    /// </summary>
    public required long EventId { get; init; }

    /// <summary>
    /// Gets when the event occurred.
    /// </summary>
    public required DateTimeOffset EventAt { get; init; }

    /// <summary>
    /// Gets the identifier of the user who performed the action.
    /// </summary>
    public required long ActorUserId { get; init; }

    /// <summary>
    /// Gets the resolved actor display name, or "Former member" when the actor row is unavailable.
    /// </summary>
    public required string ActorDisplayName { get; init; }

    /// <summary>
    /// Gets the campaign identifier the event belongs to.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the campaign name the event belongs to.
    /// </summary>
    public required string CampaignName { get; init; }

    /// <summary>
    /// Gets the participant assignment identifier, populated for note, tag, and placement events.
    /// </summary>
    public long? PlayerCampaignAssignmentId { get; init; }

    /// <summary>
    /// Gets the participant display name, populated for note, tag, and placement events.
    /// </summary>
    public string? PlayerDisplayName { get; init; }

    /// <summary>
    /// Gets the applied tag name, populated only for tag-applied events.
    /// </summary>
    public string? TagName { get; init; }

    /// <summary>
    /// Gets the placement outcome, populated only for placement events.
    /// </summary>
    public PlacementOutcome? PlacementOutcome { get; init; }

    /// <summary>
    /// Gets the lifecycle transition type, populated only for campaign close and reopen events.
    /// </summary>
    public CampaignLifecycleEventType? LifecycleEventType { get; init; }
}

/// <summary>
/// The bounded, deterministically ordered club dashboard recent-activity feed.
/// </summary>
/// <param name="Events">The newest activity events, ordered newest-first.</param>
public sealed record DashboardActivityResult(IReadOnlyList<DashboardActivityItemDto> Events);
