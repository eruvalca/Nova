using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Creates administrator-only Draft campaigns across server and HTTP boundaries.
/// </summary>
public interface ICampaignCreationService
{
    /// <summary>
    /// Creates a Draft campaign in an existing or inline-created season without enrolling players.
    /// </summary>
    /// <param name="input">The idempotent campaign and season creation request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The committed campaign aggregate or a ProblemDetails-mappable failure.</returns>
    Task<ServiceResult<CreateCampaignResult>> CreateAsync(
        CreateCampaignInput input,
        CancellationToken cancellationToken = default);
}
