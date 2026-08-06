using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Provides tenant-safe read access to campaign lists and creation setup data.
/// </summary>
public interface ICampaignQueryService
{
    /// <summary>
    /// Retrieves a bounded, season-grouped campaign list.
    /// </summary>
    /// <param name="input">The optional status filter and row limit.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The campaign list or a service problem.</returns>
    Task<ServiceResult<CampaignListResult>> GetCampaignListAsync(
        GetCampaignListInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves current season choices and Active roster counts for campaign creation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The setup data or a service problem.</returns>
    Task<ServiceResult<CampaignCreationSetupResult>> GetCreationSetupAsync(
        CancellationToken cancellationToken = default);
}
