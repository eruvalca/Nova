using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;

namespace Nova.Client.Services.Teams;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ITeamDetailService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpTeamDetailService(HttpClient http) : ITeamDetailService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TeamDetailDto>> GetTeamDetailAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri(TeamEndpoints.GetDetailUrl(teamId), UriKind.RelativeOrAbsolute), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TeamDetailDto>(
            "The server returned an invalid team detail response.",
            detail => IsValidDetail(detail, teamId),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a team-detail payload.
    /// </summary>
    /// <param name="detail">The team detail to validate.</param>
    /// <param name="expectedTeamId">The team identifier requested by the caller.</param>
    /// <returns><see langword="true"/> when the detail is structurally valid.</returns>
    /// <remarks>
    /// Placement totals and bounded rows are separate reads and may briefly disagree.
    /// </remarks>
    private static bool IsValidDetail(TeamDetailDto detail, long expectedTeamId)
        => detail is not null
            && detail.TeamId > 0
            && detail.TeamId == expectedTeamId
            && detail.ClubId > 0
            && !string.IsNullOrWhiteSpace(detail.Name)
            && detail.GraduationYear is >= 2000 and <= 2100
            && detail.LifecycleStatus is Nova.SharedKernel.Enums.LifecycleStatus.Active
                or Nova.SharedKernel.Enums.LifecycleStatus.Archived
            && detail.ActivePlacementImpacts is not null
            && detail.PlacementHistory is not null
            && detail.ActivePlacementImpactTotalCount >= 0
            && detail.PlacementHistoryTotalCount >= 0
            && detail.ActivePlacementImpacts.Count <= TeamDetailDto.MaxPlacementHistoryItems
            && detail.PlacementHistory.Count <= TeamDetailDto.MaxPlacementHistoryItems
            && detail.IsPlacementHistoryTruncated
                == (detail.PlacementHistoryTotalCount > TeamDetailDto.MaxPlacementHistoryItems)
            && detail.PlacementHistory.All(IsValidPlacement)
            && IsPlacementHistoryOrdered(detail.PlacementHistory)
            && detail.ActivePlacementImpacts.SequenceEqual(
                detail.PlacementHistory.Where(placement =>
                    placement.CampaignStatus == Nova.SharedKernel.Enums.CampaignStatus.Active));

    /// <summary>
    /// Validates the portable invariants of a team-placement row.
    /// </summary>
    /// <param name="placement">The placement row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidPlacement(TeamPlacementImpactDto placement)
        => placement is not null
            && placement.PlayerCampaignAssignmentId > 0
            && placement.CampaignId > 0
            && !string.IsNullOrWhiteSpace(placement.CampaignName)
            && placement.CampaignStatus is Nova.SharedKernel.Enums.CampaignStatus.Active
                or Nova.SharedKernel.Enums.CampaignStatus.Draft
                or Nova.SharedKernel.Enums.CampaignStatus.Closed
            && placement.CampaignStartDate != default
            && placement.PlayerId > 0
            && !string.IsNullOrWhiteSpace(placement.PlayerDisplayName)
            && placement.PlayerGraduationYear is >= 2000 and <= 2100
            && placement.PlacementOutcome == Nova.SharedKernel.Enums.PlacementOutcome.Assigned;

    /// <summary>
    /// Validates the portable leading keys and identifier tie-breaker of placement-history ordering.
    /// </summary>
    /// <param name="placements">The bounded placement-history rows.</param>
    /// <returns><see langword="true"/> when adjacent rows retain the contracted portable order.</returns>
    private static bool IsPlacementHistoryOrdered(IReadOnlyList<TeamPlacementImpactDto> placements)
        => placements.Zip(placements.Skip(1)).All(pair =>
        {
            if (pair.First.CampaignStatus != pair.Second.CampaignStatus)
            {
                return GetLifecycleSortRank(pair.First.CampaignStatus)
                    < GetLifecycleSortRank(pair.Second.CampaignStatus);
            }

            if (pair.First.CampaignStartDate != pair.Second.CampaignStartDate)
            {
                return pair.First.CampaignStartDate > pair.Second.CampaignStartDate;
            }

            if (pair.First.CampaignId != pair.Second.CampaignId)
            {
                return pair.First.CampaignId > pair.Second.CampaignId;
            }

            return !string.Equals(
                   pair.First.PlayerDisplayName,
                   pair.Second.PlayerDisplayName,
                   StringComparison.Ordinal)
               || pair.First.PlayerId < pair.Second.PlayerId;
        });

    /// <summary>
    /// Gets the portable lifecycle ordering used by the server: Active, Draft, then Closed.
    /// </summary>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <returns>The lifecycle sort rank.</returns>
    private static int GetLifecycleSortRank(Nova.SharedKernel.Enums.CampaignStatus status)
        => status switch
        {
            Nova.SharedKernel.Enums.CampaignStatus.Active => 0,
            Nova.SharedKernel.Enums.CampaignStatus.Draft => 1,
            Nova.SharedKernel.Enums.CampaignStatus.Closed => 2,
            _ => int.MaxValue
        };
}
