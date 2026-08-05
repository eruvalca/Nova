using Nova.Shared.Enums;

namespace Nova.Shared.Teams;

/// <summary>
/// Represents a team's permanent profile, lifecycle state, and placement context.
/// </summary>
/// <param name="TeamId">The team identifier.</param>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="Name">The team's display name.</param>
/// <param name="GraduationYear">The minimum eligible player graduation year.</param>
/// <param name="LifecycleStatus">The team lifecycle state.</param>
/// <param name="ActivePlacementImpacts">Active-campaign placements currently assigned to the team.</param>
/// <param name="PlacementHistory">All historical and active placements assigned to the team.</param>
public sealed record TeamDetailDto(
    long TeamId,
    long ClubId,
    string Name,
    int GraduationYear,
    LifecycleStatus LifecycleStatus,
    IReadOnlyList<TeamPlacementImpactDto> ActivePlacementImpacts,
    IReadOnlyList<TeamPlacementImpactDto> PlacementHistory)
{
    /// <summary>
    /// Gets the maximum number of placement-history rows returned by the detail contract.
    /// </summary>
    public const int MaxPlacementHistoryItems = 100;

    /// <summary>
    /// Gets the total number of active placements assigned to the team.
    /// </summary>
    public int ActivePlacementImpactTotalCount { get; init; }

    /// <summary>
    /// Gets the total number of placements assigned to the team across all campaigns.
    /// </summary>
    public int PlacementHistoryTotalCount { get; init; }

    /// <summary>
    /// Gets whether the returned placement projections were truncated at the contract bound.
    /// </summary>
    public bool IsPlacementHistoryTruncated { get; init; }
}

/// <summary>
/// Represents the bounded campaign and player context for one team placement.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The placement assignment identifier.</param>
/// <param name="CampaignId">The campaign identifier.</param>
/// <param name="CampaignName">The campaign display name.</param>
/// <param name="CampaignStatus">The campaign lifecycle status.</param>
/// <param name="CampaignStartDate">The campaign start date.</param>
/// <param name="PlayerId">The assigned player identifier.</param>
/// <param name="PlayerDisplayName">The assigned player's display name.</param>
/// <param name="PlayerGraduationYear">The assigned player's graduation year.</param>
/// <param name="TryoutNumber">The campaign-scoped tryout number, when assigned.</param>
/// <param name="PlacementOutcome">The placement outcome.</param>
public sealed record TeamPlacementImpactDto(
    long PlayerCampaignAssignmentId,
    long CampaignId,
    string CampaignName,
    CampaignStatus CampaignStatus,
    DateOnly CampaignStartDate,
    long PlayerId,
    string PlayerDisplayName,
    int PlayerGraduationYear,
    int? TryoutNumber,
    PlacementOutcome PlacementOutcome);
