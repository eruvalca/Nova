using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reads campaign-participant roster and detail data in a tenant-safe manner.
/// </summary>
public interface ICampaignParticipantQueryService
{
    /// <summary>
    /// Loads a bounded, filtered roster for a campaign.
    /// </summary>
    /// <param name="input">The roster filters, sort options, and paging request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded participant roster or a service problem.</returns>
    Task<ServiceResult<PagedResult<CampaignParticipantRosterItem>>> GetParticipantRosterAsync(
        GetCampaignParticipantRosterInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single campaign-participant detail payload.
    /// </summary>
    /// <param name="input">The campaign and assignment identifiers for the requested participant.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The participant detail payload or a service problem.</returns>
    Task<ServiceResult<CampaignParticipantDetailDto>> GetParticipantDetailAsync(
        GetCampaignParticipantDetailInput input,
        CancellationToken cancellationToken = default);
}
