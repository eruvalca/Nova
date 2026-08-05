using System.Net.Http.Json;
using Nova.Shared.Results;
using Nova.Shared.Teams;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ITeamDetailService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTeamDetailService(HttpClient http) : ITeamDetailService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TeamDetailDto>> GetTeamDetailAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(TeamEndpoints.GetDetailUrl(teamId), cancellationToken);
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
                    placement.CampaignStatus == Nova.Shared.Enums.CampaignStatus.Active));

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
            && placement.CampaignStatus is Nova.Shared.Enums.CampaignStatus.Active
                or Nova.Shared.Enums.CampaignStatus.Closed
            && placement.CampaignStartDate != default
            && placement.PlayerId > 0
            && !string.IsNullOrWhiteSpace(placement.PlayerDisplayName)
            && placement.PlacementOutcome == Nova.Shared.Enums.PlacementOutcome.Assigned;

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
               return pair.First.CampaignStatus == Nova.Shared.Enums.CampaignStatus.Active;
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
}
