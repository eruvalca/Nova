using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reads tenant-safe campaign placement roster and summary data.
/// </summary>
public interface ICampaignPlacementQueryService
{
    /// <summary>
    /// Loads a bounded, deterministically ordered placement roster for a campaign.
    /// </summary>
    /// <param name="input">The roster filters and paging request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded placement roster page or a service problem.</returns>
    Task<ServiceResult<PagedResult<CampaignPlacementRosterItem>>> GetPlacementRosterAsync(
        GetCampaignPlacementRosterInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the authoritative whole-campaign placement outcome summary, independent of paging and filters.
    /// </summary>
    /// <param name="input">The campaign identifier for the summary.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The whole-campaign outcome summary or a service problem.</returns>
    Task<ServiceResult<CampaignPlacementSummaryDto>> GetPlacementSummaryAsync(
        GetCampaignPlacementSummaryInput input,
        CancellationToken cancellationToken = default);
}
