using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents one append-only lifecycle transition event for a campaign.
/// </summary>
public class CampaignLifecycleEventEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the campaign lifecycle event identifier.
    /// </summary>
    public long CampaignLifecycleEventId { get; set; } = default;

    /// <summary>
    /// Gets or sets the campaign identifier whose lifecycle transitioned.
    /// </summary>
    public required long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the campaign whose lifecycle transitioned.
    /// </summary>
    public CampaignEntity Campaign { get; set; } = null!;

    /// <summary>
    /// Gets or sets the lifecycle transition type that occurred.
    /// </summary>
    public CampaignLifecycleEventType EventType { get; set; } = CampaignLifecycleEventType.Closed;

    /// <summary>
    /// Gets or sets the club identifier that owns this event.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the club that owns this event.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
