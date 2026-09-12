using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Authoritative bounded placement reads for approved members; tenant-scoped identity and rows share one snapshot per response.</summary>
public interface IEffectivePlacementQueryService
{
    /// <summary>Reads valid current-season membership, or an explicit empty no-season result. May return validation, forbidden, or not-found.</summary>
    Task<ServiceResult<CurrentSeasonRosterResult>> GetCurrentSeasonRosterAsync(GetCurrentSeasonRosterInput input, CancellationToken cancellationToken = default);
    /// <summary>Reads current-season Active campaign work. May return validation, forbidden, not-found, or lifecycle conflict.</summary>
    Task<ServiceResult<CampaignEffectivePlacementsResult>> GetCampaignEffectivePlacementsAsync(GetCampaignEffectivePlacementsInput input, CancellationToken cancellationToken = default);
    /// <summary>Reads only a Closed campaign's saved local outcomes. May return validation, forbidden, not-found, lifecycle conflict, or record-integrity conflict.</summary>
    Task<ServiceResult<ClosedCampaignRosterResult>> GetClosedCampaignRosterAsync(GetClosedCampaignRosterInput input, CancellationToken cancellationToken = default);
    /// <summary>
    /// Exports every participant of a Closed campaign as one CSV file read in a single snapshot.
    /// May return validation, forbidden, not-found, lifecycle conflict, record-integrity conflict,
    /// or an export-bound conflict when the campaign exceeds the permitted row count.
    /// </summary>
    Task<ServiceResult<ClosedCampaignRosterExport>> ExportClosedCampaignRosterAsync(GetClosedCampaignRosterExportInput input, CancellationToken cancellationToken = default);
}
