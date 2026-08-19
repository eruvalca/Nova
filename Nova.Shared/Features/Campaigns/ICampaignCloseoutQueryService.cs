using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reads tenant-safe campaign closeout readiness and bounded recent activity.
/// </summary>
public interface ICampaignCloseoutQueryService
{
    /// <summary>
    /// Loads the authoritative closeout readiness for a campaign by composing the placement summary
    /// and the foundation closure policy verdict.
    /// </summary>
    /// <param name="input">The campaign identifier for the readiness query.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The closeout readiness or a service problem.</returns>
    Task<ServiceResult<CampaignCloseoutReadinessDto>> GetCloseoutReadinessAsync(
        GetCampaignCloseoutReadinessInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a bounded, deterministically ordered feed of recent campaign lifecycle events.
    /// </summary>
    /// <param name="input">The campaign identifier and optional bound for the activity query.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded recent activity or a service problem.</returns>
    Task<ServiceResult<CampaignActivityResult>> GetActivityAsync(
        GetCampaignActivityInput input,
        CancellationToken cancellationToken = default);
}
