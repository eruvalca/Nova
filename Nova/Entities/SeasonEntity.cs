using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Season Entity persisted in the database.
/// </summary>
public class SeasonEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Season Id.
    /// </summary>
    public long SeasonId { get; set; } = default;

    /// <summary>
    /// Gets or sets the stable identifier for the logical season-creation operation, used to verify
    /// an idempotent creation transaction.
    /// </summary>
    public required Guid CreationOperationId { get; set; }

    /// <summary>
    /// Gets or sets the command path that originally created this season.
    /// </summary>
    public SeasonCreationKind CreationKind { get; set; } = SeasonCreationKind.InlineCampaign;

    /// <summary>
    /// Gets or sets the season that was current when this season was created by an advancement
    /// operation. Other creation kinds leave this value null.
    /// </summary>
    public long? CreationPreviousSeasonId { get; set; }

    /// <summary>
    /// Gets or sets the application-managed token used to detect concurrent season metadata
    /// corrections.
    /// </summary>
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the Name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the Start Date.
    /// </summary>
    public required DateOnly StartDate { get; set; }
    /// <summary>
    /// Gets or sets the End Date.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Gets or sets the Campaigns.
    /// </summary>
    public ICollection<CampaignEntity> Campaigns { get; set; } = [];

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;

}
