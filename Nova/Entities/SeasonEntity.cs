using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Season Entity persisted in the database.
/// </summary>
public class SeasonEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the Season Id.
    /// </summary>
    public long SeasonId { get; set; } = default;
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

    /// <summary>
    /// Gets the Is Complete.
    /// </summary>
    public bool IsComplete => EndDate.HasValue && EndDate.Value < DateOnly.FromDateTime(DateTime.UtcNow);
}
