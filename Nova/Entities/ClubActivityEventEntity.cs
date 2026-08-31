using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Stores one immutable, tenant-scoped activity transition with enough display context to survive
/// later actor, subject, campaign, or team changes.
/// </summary>
public class ClubActivityEventEntity : BaseEntity, ITenantOwnedEntity, IAppendOnlyEntity
{
    /// <summary>Gets or sets the stable activity event identifier.</summary>
    public long ClubActivityEventId { get; set; }
    /// <summary>Gets or sets the owning club identifier.</summary>
    public required long ClubId { get; set; }
    /// <summary>Gets or sets the owning club.</summary>
    public ClubEntity Club { get; set; } = null!;
    /// <summary>Gets or sets the durable event kind.</summary>
    public required ClubActivityEventKind EventKind { get; set; }
    /// <summary>Gets or sets the minimum audience for the event.</summary>
    public required ClubActivityAudience Audience { get; set; }
    /// <summary>Gets or sets the actor's stable display snapshot.</summary>
    public required string ActorDisplayName { get; set; }
    /// <summary>Gets or sets the optional subject user identifier.</summary>
    public long? SubjectUserId { get; set; }
    /// <summary>Gets or sets the optional subject display snapshot.</summary>
    public string? SubjectDisplayName { get; set; }
    /// <summary>Gets or sets the optional join request identifier.</summary>
    public long? JoinRequestId { get; set; }
    /// <summary>Gets or sets the optional campaign identifier.</summary>
    public long? CampaignId { get; set; }
    /// <summary>Gets or sets the campaign display snapshot.</summary>
    public string? CampaignName { get; set; }
    /// <summary>Gets or sets the season display snapshot.</summary>
    public string? SeasonName { get; set; }
    /// <summary>Gets or sets the optional assignment identifier.</summary>
    public long? PlayerCampaignAssignmentId { get; set; }
    /// <summary>Gets or sets the optional player identifier.</summary>
    public long? PlayerId { get; set; }
    /// <summary>Gets or sets the player display snapshot.</summary>
    public string? PlayerDisplayName { get; set; }
    /// <summary>Gets or sets the previous placement outcome.</summary>
    public PlacementOutcome? PreviousPlacementOutcome { get; set; }
    /// <summary>Gets or sets the previous team identifier.</summary>
    public long? PreviousTeamId { get; set; }
    /// <summary>Gets or sets the previous team display snapshot.</summary>
    public string? PreviousTeamName { get; set; }
    /// <summary>Gets or sets the previous source campaign display snapshot.</summary>
    public string? PreviousSourceCampaignName { get; set; }
    /// <summary>Gets or sets the current placement outcome.</summary>
    public PlacementOutcome? CurrentPlacementOutcome { get; set; }
    /// <summary>Gets or sets the current team identifier.</summary>
    public long? CurrentTeamId { get; set; }
    /// <summary>Gets or sets the current team display snapshot.</summary>
    public string? CurrentTeamName { get; set; }
    /// <summary>Gets or sets the current source campaign display snapshot.</summary>
    public string? CurrentSourceCampaignName { get; set; }
}
