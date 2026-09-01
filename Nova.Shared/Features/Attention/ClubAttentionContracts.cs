using Nova.Shared.Enums;

namespace Nova.Shared.Features.Attention;

/// <summary>
/// The administrator-only club attention projection. Each region loads independently, so one
/// region reporting <see cref="AttentionRegionStatus.Unavailable"/> never hides the other's count.
/// </summary>
public sealed record ClubAttentionResult
{
    /// <summary>
    /// Gets the pending club join requests region.
    /// </summary>
    public required PendingJoinRequestsRegion PendingJoinRequests { get; init; }

    /// <summary>
    /// Gets the campaigns needing placement region.
    /// </summary>
    public required NeedsPlacementRegion NeedsPlacement { get; init; }
}

/// <summary>
/// The pending club join requests attention region.
/// </summary>
public sealed record PendingJoinRequestsRegion
{
    /// <summary>
    /// Gets the region availability.
    /// </summary>
    public required AttentionRegionStatus Status { get; init; }

    /// <summary>
    /// Gets the count of pending join requests, meaningful only when <see cref="Status"/> is
    /// <see cref="AttentionRegionStatus.Loaded"/>.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets when the oldest pending request was submitted, meaningful only when the region loaded
    /// and the count is non-zero.
    /// </summary>
    public DateTimeOffset? OldestRequestAt { get; init; }
}

/// <summary>
/// The campaigns needing placement attention region.
/// </summary>
public sealed record NeedsPlacementRegion
{
    /// <summary>
    /// Gets the region availability.
    /// </summary>
    public required AttentionRegionStatus Status { get; init; }

    /// <summary>
    /// Gets the count of Active campaigns with placements still to be decided, meaningful only when
    /// <see cref="Status"/> is <see cref="AttentionRegionStatus.Loaded"/>.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets the identifier of the oldest campaign still needing placement decisions, meaningful only
    /// when the region loaded and the count is non-zero.
    /// </summary>
    public long? CampaignId { get; init; }

    /// <summary>
    /// Gets the display name of the oldest campaign still needing placement decisions, meaningful only
    /// when the region loaded and the count is non-zero.
    /// </summary>
    public string? CampaignName { get; init; }
}
