using Nova.Entities.Base;

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
    /// Gets or sets the Player Assignments.
    /// </summary>
    public ICollection<PlayerCampaignAssignmentEntity> PlayerAssignments { get; set; } = [];

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
    public bool IsComplete => EndDate.HasValue;
}
