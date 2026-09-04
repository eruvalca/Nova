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
    /// Gets or sets the caller-generated identifier for the logical campaign creation operation.
    /// </summary>
    public required Guid CreationOperationId { get; set; }

    /// <summary>
    /// Gets or sets whether campaign creation also created its season in the same transaction.
    /// </summary>
    public bool SeasonCreatedInline { get; set; }

    /// <summary>
    /// Gets or sets the caller-generated identifier for the logical opening operation.
    /// </summary>
    public Guid? OpeningOperationId { get; set; }

    /// <summary>
    /// Gets or sets when this campaign originally opened.
    /// </summary>
    public DateTimeOffset? OpenedAt { get; set; }

    /// <summary>
    /// Gets or sets the administrator who originally opened this campaign.
    /// </summary>
    public long? OpenedById { get; set; }

    /// <summary>
    /// Gets or sets the deterministic opening order within the campaign's season.
    /// </summary>
    public long? SeasonOpeningSequence { get; set; }

    /// <summary>
    /// Gets or sets the exact active-player count enrolled when the campaign opened.
    /// </summary>
    public int? InitialEnrolledPlayerCount { get; set; }

    /// <summary>
    /// Gets or sets the exact active-team count observed when the campaign opened.
    /// </summary>
    public int? InitialActiveTeamCount { get; set; }

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
    /// Gets or sets the campaign lifecycle status.
    /// </summary>
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

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
