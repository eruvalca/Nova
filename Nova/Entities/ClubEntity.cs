using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Club Entity persisted in the database.
/// </summary>
public class ClubEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public long ClubId { get; set; } = default;
    /// <summary>
    /// Gets or sets the Name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the City.
    /// </summary>
    public required string City { get; set; }
    /// <summary>
    /// Gets or sets the State.
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Gets or sets the Nova Users.
    /// </summary>
    public ICollection<NovaUserEntity> NovaUsers { get; set; } = [];
    /// <summary>
    /// Gets or sets the Campaigns.
    /// </summary>
    public ICollection<CampaignEntity> Campaigns { get; set; } = [];
    /// <summary>
    /// Gets or sets the Seasons.
    /// </summary>
    public ICollection<SeasonEntity> Seasons { get; set; } = [];
    /// <summary>
    /// Gets or sets the Teams.
    /// </summary>
    public ICollection<TeamEntity> Teams { get; set; } = [];
    /// <summary>
    /// Gets or sets the Players.
    /// </summary>
    public ICollection<PlayerEntity> Players { get; set; } = [];
    /// <summary>
    /// Gets or sets the Player Tags.
    /// </summary>
    public ICollection<PlayerTagEntity> PlayerTags { get; set; } = [];
    /// <summary>
    /// Gets or sets the Join Requests.
    /// </summary>
    public ICollection<ClubJoinRequestEntity> JoinRequests { get; set; } = [];
}
