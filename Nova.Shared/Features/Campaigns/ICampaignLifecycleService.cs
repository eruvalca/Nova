using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Applies administrator-only campaign close and reopen lifecycle transitions across the HTTP boundary.
/// </summary>
public interface ICampaignLifecycleService
{
    /// <summary>
    /// Closes a campaign when every participant has a final outcome, every assigned placement remains
    /// eligible, and no assigned team is archived.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to close.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<Success>> CloseAsync(
        long campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens a closed campaign and records the transition in its append-only lifecycle history.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to reopen.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<Success>> ReopenAsync(
        long campaignId,
        CancellationToken cancellationToken = default);
}
