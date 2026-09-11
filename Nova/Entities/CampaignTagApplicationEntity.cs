#pragma warning disable CA1515 // Identity components expose these framework model and navigation types through their public constructors.
using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents one explicit application of a tag definition to a campaign participation.
/// </summary>
public class CampaignTagApplicationEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the campaign tag application identifier.
    /// </summary>
    public long CampaignTagApplicationId { get; set; }

    /// <summary>Gets or sets the original actor's display name captured when this trait was applied.</summary>
    public required string AuthorDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the campaign participation identifier receiving the tag.
    /// </summary>
    public required long PlayerCampaignAssignmentId { get; set; }

    /// <summary>
    /// Gets or sets the campaign participation receiving the tag.
    /// </summary>
    public PlayerCampaignAssignmentEntity PlayerCampaignAssignment { get; set; } = null!;

    /// <summary>
    /// Gets or sets the tag-definition identifier that was applied.
    /// </summary>
    public required long PlayerTagId { get; set; }

    /// <summary>
    /// Gets or sets the tag definition that was applied.
    /// </summary>
    public PlayerTagEntity PlayerTag { get; set; } = null!;

    /// <summary>
    /// Gets or sets the club identifier owning this campaign tag application.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the club owning this campaign tag application.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;

    /// <summary>
    /// Gets or sets the stable identifier used to verify an idempotent apply transaction.
    /// </summary>
    public required Guid CreationOperationId { get; set; }
}
