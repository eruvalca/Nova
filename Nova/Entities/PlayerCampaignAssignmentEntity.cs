using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Player Campaign Assignment Entity persisted in the database.
/// </summary>
public class PlayerCampaignAssignmentEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the Player Campaign Assignment Id.
    /// </summary>
    public long PlayerCampaignAssignmentId { get; set; } = default;

    /// <summary>
    /// Gets or sets the Player Id.
    /// </summary>
    public required long PlayerId { get; set; }
    /// <summary>
    /// Gets or sets the Player.
    /// </summary>
    public PlayerEntity Player { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Campaign Id.
    /// </summary>
    public required long CampaignId { get; set; }
    /// <summary>
    /// Gets or sets the Campaign.
    /// </summary>
    public CampaignEntity Campaign { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Team Id.
    /// </summary>
    public long? TeamId { get; set; } = null;
    /// <summary>
    /// Gets or sets the Team.
    /// </summary>
    public TeamEntity? Team { get; set; } = null;

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
