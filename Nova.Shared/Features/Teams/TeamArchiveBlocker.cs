namespace Nova.Shared.Features.Teams;

/// <summary>
/// Describes active-campaign placements that block team archival.
/// </summary>
public sealed record TeamArchiveBlocker
{
    /// <summary>
    /// Gets the active campaign identifier.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the active campaign display name.
    /// </summary>
    public required string CampaignName { get; init; }

    /// <summary>
    /// Gets the placement identifiers referencing the team in the campaign.
    /// </summary>
    public required IReadOnlyList<long> PlacementIds { get; init; }
}
