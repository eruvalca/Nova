#pragma warning disable CA1515 // Identity components expose these framework model and navigation types through their public constructors.
using Nova.Entities.Base;
using Nova.SharedKernel.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents the Player Campaign Assignment Entity persisted in the database.
/// </summary>
public class PlayerCampaignAssignmentEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Player Campaign Assignment Id.
    /// </summary>
    public long PlayerCampaignAssignmentId { get; set; }

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

    /// <summary>Gets or sets when the latest explicit decision was recorded, independently of enrollment.</summary>
    public DateTimeOffset? DecisionRecordedAt { get; set; }

    /// <summary>Gets or sets the deciding member's identifier, retained without an account foreign key.</summary>
    public long? DecisionRecordedById { get; set; }

    /// <summary>Gets or sets the deciding member's display-name snapshot.</summary>
    public string? DecisionActorDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the Team Id.
    /// </summary>
    public long? TeamId { get; set; }
    /// <summary>
    /// Gets or sets the Team.
    /// </summary>
    public TeamEntity? Team { get; set; }

    /// <summary>
    /// Gets or sets the campaign tag applications for this participation.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<CampaignTagApplicationEntity> CampaignTagApplications { get; set; } = [];
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the application-managed token used to detect concurrent placement updates.
    /// </summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the evaluation notes written for this campaign participation.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<NoteEntity> Notes { get; set; } = [];
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
