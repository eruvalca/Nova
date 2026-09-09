using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Authoritative bounded placement reads for approved members; identifiers are tenant scoped.</summary>
public interface IEffectivePlacementQueryService
{
    /// <summary>Reads valid current-season membership, or an explicit empty no-season result. May return validation, forbidden, or not-found.</summary>
    Task<ServiceResult<CurrentSeasonRosterResult>> GetCurrentSeasonRosterAsync(GetCurrentSeasonRosterInput input, CancellationToken cancellationToken = default);
    /// <summary>Reads current-season Active campaign work. May return validation, forbidden, not-found, or lifecycle conflict.</summary>
    Task<ServiceResult<CampaignEffectivePlacementsResult>> GetCampaignEffectivePlacementsAsync(GetCampaignEffectivePlacementsInput input, CancellationToken cancellationToken = default);
    /// <summary>Reads only a Closed campaign's saved local outcomes. May return validation, forbidden, not-found, lifecycle conflict, or record-integrity conflict.</summary>
    Task<ServiceResult<ClosedCampaignRosterResult>> GetClosedCampaignRosterAsync(GetClosedCampaignRosterInput input, CancellationToken cancellationToken = default);
}
