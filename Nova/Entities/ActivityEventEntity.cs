using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents one append-only activity event recorded for a club. The row carries a kind, an
/// actor and subject display-name snapshot, a stored visibility flag, and a family-shaped payload
/// so the feed remains readable after the referenced entities change or are removed.
/// </summary>
public class ActivityEventEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the activity event identifier (the monotonic ordering key used by the feed).
    /// </summary>
    public long ActivityEventId { get; set; } = default;

    /// <summary>
    /// Gets or sets the club that owns this event.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the club that owns this event.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;

    /// <summary>
    /// Gets or sets the event kind.
    /// </summary>
    public ActivityEventKind EventKind { get; set; } = default;

    /// <summary>
    /// Gets or sets whether the event is visible only to club administrators.
    /// </summary>
    public bool IsAdminOnly { get; set; } = false;

    /// <summary>
    /// Gets or sets the nullable campaign identifier the event belongs to (a loose query key used by
    /// the campaign-local activity surface; there is intentionally no FK).
    /// </summary>
    public long? CampaignId { get; set; } = null;

    /// <summary>
    /// Gets or sets the identifier of the user who performed the action (a loose snapshot key).
    /// </summary>
    public long ActorUserId { get; set; } = default;

    /// <summary>
    /// Gets or sets the actor display-name snapshot.
    /// </summary>
    public required string ActorDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the family-shaped structured JSON payload.
    /// </summary>
    public required string PayloadJson { get; set; }
}
