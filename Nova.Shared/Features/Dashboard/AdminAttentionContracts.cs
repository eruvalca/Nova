namespace Nova.Shared.Features.Dashboard;

/// <summary>Describes whether an administrator attention projection was available.</summary>
public enum AttentionProjectionState
{
    /// <summary>The projection completed successfully.</summary>
    Available,
    /// <summary>The projection failed and must not be interpreted as zero.</summary>
    Unavailable,
}

/// <summary>Administrator attention for pending club join requests.</summary>
public sealed record PendingJoinRequestAttentionDto
{
    /// <summary>Whether the projection is available.</summary>
    public required AttentionProjectionState State { get; init; }
    /// <summary>The pending request count when available.</summary>
    public int? Count { get; init; }
    /// <summary>The oldest pending request timestamp when available.</summary>
    public DateTimeOffset? OldestSubmittedAt { get; init; }
}

/// <summary>Administrator attention for players needing a placement decision.</summary>
public sealed record NeedsPlacementAttentionDto
{
    /// <summary>Whether the projection is available.</summary>
    public required AttentionProjectionState State { get; init; }
    /// <summary>The authoritative count when available.</summary>
    public int? Count { get; init; }
    /// <summary>The Active campaign containing the work, when there is exactly one.</summary>
    public long? CampaignId { get; init; }
    /// <summary>The durable Active campaign name.</summary>
    public string? CampaignName { get; init; }
}

/// <summary>Independent administrator attention projections returned by the dashboard shell.</summary>
public sealed record AdminAttentionResult
{
    /// <summary>The pending join-request projection.</summary>
    public required PendingJoinRequestAttentionDto PendingJoinRequests { get; init; }
    /// <summary>The Needs placement projection.</summary>
    public required NeedsPlacementAttentionDto NeedsPlacement { get; init; }
}
