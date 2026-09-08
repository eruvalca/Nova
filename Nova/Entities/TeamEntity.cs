#pragma warning disable CA1515 // Identity components expose these framework model and navigation types through their public constructors.
using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Team Entity persisted in the database.
/// </summary>
public class TeamEntity : ArchivableEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Team Id.
    /// </summary>
    public long TeamId { get; set; }
    /// <summary>
    /// Gets or sets the Name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the Graduation Year.
    /// </summary>
    public required int GraduationYear { get; set; }

    /// <summary>
    /// Gets or sets the stable identifier used to verify an idempotent team-creation transaction.
    /// </summary>
    public required Guid CreationOperationId { get; set; }

    /// <summary>
    /// Gets or sets the Player Assignments.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<PlayerCampaignAssignmentEntity> PlayerAssignments { get; set; } = [];
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
