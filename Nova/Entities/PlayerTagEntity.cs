using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Player Tag Entity persisted in the database.
/// </summary>
public class PlayerTagEntity : ArchivableEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Player Tag Id.
    /// </summary>
    public long PlayerTagId { get; set; } = default;
    /// <summary>
    /// Gets or sets the Name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the Color.
    /// </summary>
    public required string Color { get; set; }

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;

    /// <summary>
    /// Gets or sets the campaign tag applications using this definition.
    /// </summary>
    public ICollection<CampaignTagApplicationEntity> CampaignTagApplications { get; set; } = [];
}
