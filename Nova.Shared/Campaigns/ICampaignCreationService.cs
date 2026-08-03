using Nova.Shared.Results;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Creates Active campaigns and their initial participation snapshots across server and HTTP boundaries.
/// </summary>
public interface ICampaignCreationService
{
    /// <summary>
    /// Creates an Active campaign in an existing or inline-created season and enrolls all Active players.
    /// </summary>
    /// <param name="input">The idempotent campaign and season creation request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The committed campaign aggregate or a ProblemDetails-mappable failure.</returns>
    Task<ServiceResult<CreateCampaignResult>> CreateAsync(
        CreateCampaignInput input,
        CancellationToken cancellationToken = default);
}
