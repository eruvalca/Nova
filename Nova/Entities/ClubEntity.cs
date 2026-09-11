#pragma warning disable CA1515 // Identity components expose these framework model and navigation types through their public constructors.
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
    public long ClubId { get; set; }
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
    /// Gets or sets the stable identifier for the logical club-creation operation. Set once per
    /// operation and reused across retry attempts so an ambiguous commit can be verified (and
    /// not replayed) by looking up the club created by this operation.
    /// </summary>
    public required Guid CreationOperationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the season that currently owns the club's active work.
    /// A null value represents the supported onboarding or recovery state where no season has
    /// yet been established.
    /// </summary>
    public long? CurrentSeasonId { get; set; }

    /// <summary>
    /// Gets or sets the season currently selected for the club.
    /// </summary>
    public SeasonEntity? CurrentSeason { get; set; }

    /// <summary>
    /// Gets or sets the Nova Users.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<NovaUserEntity> NovaUsers { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Campaigns.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<CampaignEntity> Campaigns { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Seasons.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<SeasonEntity> Seasons { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Teams.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<TeamEntity> Teams { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Players.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<PlayerEntity> Players { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Player Tags.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<PlayerTagEntity> PlayerTags { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the campaign tag applications.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<CampaignTagApplicationEntity> CampaignTagApplications { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Join Requests.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<ClubJoinRequestEntity> JoinRequests { get; set; } = [];
#pragma warning restore CA2227
    /// <summary>
    /// Gets or sets the Club Crest.
    /// </summary>
    public ClubCrestEntity? ClubCrest { get; set; }
    /// <summary>
    /// Gets or sets the Activity Events.
    /// </summary>
#pragma warning disable CA2227 // EF relationship materialization and aggregate construction use this navigation setter.
    public ICollection<ActivityEventEntity> ActivityEvents { get; set; } = [];
#pragma warning restore CA2227
}
