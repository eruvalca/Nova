using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// One append-only lifecycle event in a campaign's bounded activity feed.
/// </summary>
/// <param name="CampaignLifecycleEventId">The lifecycle event identifier.</param>
/// <param name="EventType">The recorded lifecycle transition type.</param>
/// <param name="CreatedAt">When the lifecycle transition was recorded.</param>
/// <param name="ActorUserId">The user identifier of the actor who performed the transition.</param>
/// <param name="ActorDisplayName">The resolved actor display name, or empty when the actor row is unavailable.</param>
public sealed record CampaignActivityItemDto(
    long CampaignLifecycleEventId,
    CampaignLifecycleEventType EventType,
    DateTimeOffset CreatedAt,
    long ActorUserId,
    string ActorDisplayName);

/// <summary>
/// Bounded, deterministically ordered recent lifecycle activity for one campaign.
/// </summary>
/// <param name="Events">The newest activity events, ordered newest-first.</param>
public sealed record CampaignActivityResult(
    IReadOnlyList<CampaignActivityItemDto> Events);
