using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Corrects Active campaign metadata across server and HTTP boundaries.
/// </summary>
public interface ICampaignMetadataService
{
    /// <summary>
    /// Updates an Active campaign's name, season, start date, and planned end date.
    /// Returns a conflict when the campaign is Closed; does not affect enrollment.
    /// </summary>
    /// <param name="input">The metadata correction request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The updated campaign metadata or a ProblemDetails-mappable failure.</returns>
    Task<ServiceResult<UpdateCampaignMetadataResult>> UpdateAsync(
        UpdateCampaignMetadataInput input,
        CancellationToken cancellationToken = default);
}
