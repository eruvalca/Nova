using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents the Campaign Entity persisted in the database.
/// </summary>
public class CampaignEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Campaign Id.
    /// </summary>
    public long CampaignId { get; set; } = default;
    /// <summary>
    /// Gets or sets the Name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the Start Date.
    /// </summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    /// <summary>
    /// Gets or sets the End Date.
    /// </summary>
    public DateOnly? EndDate { get; set; } = null;

    /// <summary>
    /// Gets or sets whether the campaign is active or closed.
    /// </summary>
    public CampaignStatus Status { get; set; } = CampaignStatus.Active;

    /// <summary>
    /// Gets or sets when the campaign was closed.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who closed the campaign.
    /// </summary>
    public long? ClosedById { get; set; }

    /// <summary>
    /// Gets or sets the Player Assignments.
    /// </summary>
    public ICollection<PlayerCampaignAssignmentEntity> PlayerAssignments { get; set; } = [];

    /// <summary>
    /// Gets or sets the append-only lifecycle events for this campaign.
    /// </summary>
    public ICollection<CampaignLifecycleEventEntity> LifecycleEvents { get; set; } = [];

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Season Id.
    /// </summary>
    public required long SeasonId { get; set; }
    /// <summary>
    /// Gets or sets the Season.
    /// </summary>
    public SeasonEntity Season { get; set; } = null!;

    /// <summary>
    /// Gets the Is Complete.
    /// </summary>
    public bool IsComplete => Status == CampaignStatus.Closed;
}
