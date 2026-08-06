using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Contains the updated campaign metadata returned after a successful correction.
/// </summary>
/// <param name="CampaignId">The campaign identifier.</param>
/// <param name="Name">The corrected campaign display name.</param>
/// <param name="StartDate">The corrected campaign start date.</param>
/// <param name="PlannedEndDate">The corrected optional planned end date.</param>
/// <param name="Status">The current campaign lifecycle status.</param>
/// <param name="SeasonId">The season the campaign belongs to.</param>
/// <param name="SeasonName">The name of the campaign's season.</param>
public sealed record UpdateCampaignMetadataResult(
    long CampaignId,
    string Name,
    DateOnly StartDate,
    DateOnly? PlannedEndDate,
    CampaignStatus Status,
    long SeasonId,
    string SeasonName);
