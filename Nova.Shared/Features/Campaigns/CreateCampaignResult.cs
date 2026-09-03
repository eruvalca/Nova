using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reports the campaign and season metadata committed by a campaign creation request.
/// </summary>
/// <param name="OperationId">The caller-generated identifier for the logical creation request.</param>
/// <param name="CampaignId">The created campaign identifier.</param>
/// <param name="CampaignName">The created campaign name.</param>
/// <param name="CampaignStartDate">The created campaign start date.</param>
/// <param name="CampaignPlannedEndDate">The optional planned campaign end date.</param>
/// <param name="Status">The created campaign lifecycle status.</param>
/// <param name="SeasonId">The selected or created season identifier.</param>
/// <param name="SeasonName">The selected or created season name.</param>
/// <param name="SeasonStartDate">The selected or created season start date.</param>
/// <param name="SeasonEndDate">The optional selected or created season end date.</param>
/// <param name="SeasonCreatedInline">Whether the request created the season atomically.</param>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(
    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCampaignResult(
    Guid OperationId,
    long CampaignId,
    string CampaignName,
    DateOnly CampaignStartDate,
    DateOnly? CampaignPlannedEndDate,
    CampaignStatus Status,
    long SeasonId,
    string SeasonName,
    DateOnly SeasonStartDate,
    DateOnly? SeasonEndDate,
    bool SeasonCreatedInline);
