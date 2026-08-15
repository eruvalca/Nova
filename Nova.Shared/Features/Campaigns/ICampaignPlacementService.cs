using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Updates one campaign participant's placement outcome across the HTTP boundary.
/// </summary>
public interface ICampaignPlacementService
{
    /// <summary>
    /// Updates one campaign participant's placement outcome and optional team.
    /// </summary>
    /// <param name="input">The requested placement values and expected concurrency token.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The new concurrency token or a structured service problem.</returns>
    Task<ServiceResult<PlacementMutationSuccess>> UpdatePlacementAsync(
        UpdateCampaignPlacementInput input,
        CancellationToken cancellationToken = default);
}
