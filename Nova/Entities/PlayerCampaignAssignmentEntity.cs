using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents the Player Campaign Assignment Entity persisted in the database.
/// </summary>
public class PlayerCampaignAssignmentEntity : BaseEntity, ITenantOwnedEntity
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
    /// Gets or sets the campaign-scoped tryout number.
    /// </summary>
    public int? TryoutNumber { get; set; }

    /// <summary>
    /// Gets or sets the player's placement outcome for the campaign.
    /// </summary>
    public PlacementOutcome PlacementOutcome { get; set; } = PlacementOutcome.Undecided;

    /// <summary>
    /// Gets or sets the Team Id.
    /// </summary>
    public long? TeamId { get; set; } = null;
    /// <summary>
    /// Gets or sets the Team.
    /// </summary>
    public TeamEntity? Team { get; set; } = null;

    /// <summary>
    /// Gets or sets the application-managed token used to detect concurrent placement updates.
    /// </summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the evaluation notes written for this campaign participation.
    /// </summary>
    public ICollection<NoteEntity> Notes { get; set; } = [];

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
