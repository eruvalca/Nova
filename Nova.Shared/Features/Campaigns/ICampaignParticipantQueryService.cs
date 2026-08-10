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
    Task<ServiceResult<PagedResult<CampaignParticipantRosterItem>>> GetParticipantRosterAsync(
        GetCampaignParticipantRosterInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single campaign-participant detail payload.
    /// </summary>
    Task<ServiceResult<CampaignParticipantDetailDto>> GetParticipantDetailAsync(
        GetCampaignParticipantDetailInput input,
        CancellationToken cancellationToken = default);
}
