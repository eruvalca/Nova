using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Applies administrator-only campaign close and reopen lifecycle transitions across the HTTP boundary.
/// </summary>
public interface ICampaignLifecycleService
{
    /// <summary>
    /// Opens a Draft campaign and enrolls the fresh active-player snapshot exactly once.
    /// </summary>
    /// <param name="campaignId">The Draft campaign identifier.</param>
    /// <param name="input">The idempotent opening input.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The immutable opening receipt or a structured service problem.</returns>
    Task<ServiceResult<OpenCampaignResult>> OpenAsync(
        long campaignId,
        OpenCampaignInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an unopened Draft campaign without removing durable club teams or its season.
    /// </summary>
    /// <param name="campaignId">The Draft campaign identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>Success or a structured service problem.</returns>
    Task<ServiceResult<Success>> DeleteDraftAsync(
        long campaignId,
        CancellationToken cancellationToken = default);

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
