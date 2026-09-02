using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignQueryService(HttpClient http) : ICampaignQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CampaignListResult>> GetCampaignListAsync(
        GetCampaignListInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignListUrl(input.Status, input.Limit),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignListResult>(
            "The server returned an invalid campaign list response.",
            result => IsValidCampaignList(result, input.Limit),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignDetailResult>> GetCampaignDetailAsync(
        GetCampaignDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignDetailUrl(input.CampaignId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignDetailResult>(
            "The server returned an invalid campaign detail response.",
            IsValidCampaignDetail,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignCreationSetupResult>> GetCreationSetupAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(CampaignEndpoints.GetCreationSetup, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignCreationSetupResult>(
            "The server returned an invalid campaign setup response.",
            IsValidCreationSetup,
            cancellationToken);
    }

    /// <summary>
    /// Validates the structural invariants of a campaign-detail success payload.
    /// </summary>
    /// <param name="result">The deserialized campaign-detail payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidCampaignDetail(CampaignDetailResult result)
        => result.CampaignId > 0
            && !string.IsNullOrWhiteSpace(result.Name)
            && result.StartDate != default
            && (result.PlannedEndDate is null || result.PlannedEndDate >= result.StartDate)
            && result.ParticipantCount >= 0
            && result.SeasonId > 0
            && !string.IsNullOrWhiteSpace(result.SeasonName)
            && result.Status is CampaignStatus.Active or CampaignStatus.Closed
            && HasValidClosureFields(result);

    /// <summary>
    /// Validates that the closure fields are consistent with the campaign lifecycle status: a Closed
    /// campaign must carry a closure timestamp, closer identifier, and non-empty closer display name,
    /// while an Active campaign must carry none of them.
    /// </summary>
    /// <param name="result">The campaign-detail payload.</param>
    /// <returns><see langword="true"/> when the closure fields match the status.</returns>
    private static bool HasValidClosureFields(CampaignDetailResult result)
        => result.Status == CampaignStatus.Closed
            ? result.ClosedAt is not null
                && result.ClosedByUserId is > 0
                && !string.IsNullOrWhiteSpace(result.ClosedByDisplayName)
            : result.ClosedAt is null
                && result.ClosedByUserId is null;

    /// <summary>
    /// Validates the structural and ordering invariants of a campaign-list success payload.
    /// </summary>
    /// <param name="result">The deserialized campaign-list payload.</param>
    /// <param name="requestedLimit">The optional bound requested by the caller.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidCampaignList(CampaignListResult result, int? requestedLimit)
    {
        if (result.Seasons is null
            || result.Seasons.Any(season => season is null
                || season.Campaigns is null
                || season.Campaigns.Any(campaign => campaign is null)))
        {
            return false;
        }

        var rows = result.Seasons.SelectMany(season => season.Campaigns).ToList();
        var limit = requestedLimit ?? GetCampaignListInput.DefaultLimit;
        // Count and bounded rows are separate reads, so a concurrent mutation may make
        // the total briefly lag the returned rows; validate bounds without rejecting that state.
        if (rows.Count > limit || result.TotalCount < 0)
        {
            return false;
        }

        DateOnly? previousSeasonStart = null;
        long? previousSeasonId = null;
        foreach (var season in result.Seasons)
        {
            if (season.SeasonId <= 0
                || string.IsNullOrWhiteSpace(season.Name)
                || season.StartDate == default
                || season.EndDate < season.StartDate
                || (previousSeasonStart is not null
                    && (season.StartDate > previousSeasonStart
                        || (season.StartDate == previousSeasonStart && season.SeasonId >= previousSeasonId))))
            {
                return false;
            }

            previousSeasonStart = season.StartDate;
            previousSeasonId = season.SeasonId;
            CampaignListItem? previous = null;
            foreach (var campaign in season.Campaigns)
            {
                if (!IsValidCampaign(campaign)
                    || (previous is not null && CompareCampaign(previous, campaign) > 0))
                {
                    return false;
                }

                previous = campaign;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates one campaign row from a successful response.
    /// </summary>
    /// <param name="campaign">The campaign row.</param>
    /// <returns><see langword="true"/> when all row invariants hold.</returns>
    private static bool IsValidCampaign(CampaignListItem campaign)
        => campaign.CampaignId > 0
            && !string.IsNullOrWhiteSpace(campaign.Name)
            && campaign.StartDate != default
            && (campaign.PlannedEndDate is null || campaign.PlannedEndDate >= campaign.StartDate)
            && campaign.ParticipantCount >= 0
            && campaign.UnresolvedCount >= 0
            && campaign.UnresolvedCount <= campaign.ParticipantCount
            && campaign.Status is CampaignStatus.Active or CampaignStatus.Closed;

    /// <summary>
    /// Compares adjacent campaign rows using the portable response-order keys.
    /// </summary>
    /// <param name="left">The preceding row.</param>
    /// <param name="right">The following row.</param>
    /// <returns>A positive value when the rows violate the required order.</returns>
    private static int CompareCampaign(CampaignListItem left, CampaignListItem right)
    {
        var status = left.Status.CompareTo(right.Status);
        if (status != 0)
        {
            return status;
        }

        var start = right.StartDate.CompareTo(left.StartDate);
        if (start != 0)
        {
            return start;
        }

        var leftHasEnd = left.PlannedEndDate.HasValue;
        var rightHasEnd = right.PlannedEndDate.HasValue;
        var endPresence = rightHasEnd.CompareTo(leftHasEnd);
        if (endPresence != 0)
        {
            return endPresence;
        }

        var end = right.PlannedEndDate.GetValueOrDefault().CompareTo(left.PlannedEndDate.GetValueOrDefault());
        if (end != 0)
        {
            return end;
        }

        // The server's database collation determines ordering for different names.
        // Only apply the portable ID tie-breaker when names are equal.
        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            ? right.CampaignId.CompareTo(left.CampaignId)
            : 0;
    }

    /// <summary>
    /// Validates the structural, count, bound, and ordering invariants of setup data.
    /// </summary>
    /// <param name="result">The deserialized setup payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidCreationSetup(CampaignCreationSetupResult result)
        => result is not null
            && result.ActivePlayerCount >= 0
            && result.ActiveTeamCount >= 0
            && (result.CurrentSeason is null
                || (result.CurrentSeason.SeasonId > 0
                    && !string.IsNullOrWhiteSpace(result.CurrentSeason.Name)
                    && result.CurrentSeason.StartDate != default
                    && (result.CurrentSeason.EndDate is null
                        || result.CurrentSeason.EndDate >= result.CurrentSeason.StartDate)));

    /// <summary>
    /// Validates season choices and their descending start-date and identifier order.
    /// </summary>
    /// <param name="seasons">The season choices to validate.</param>
    /// <returns><see langword="true"/> when all choices and ordering keys are valid.</returns>
    private static bool IsOrderedAndValidSeasons(IReadOnlyList<CampaignSeasonChoice> seasons)
    {
        DateOnly? previousStart = null;
        long? previousId = null;
        foreach (var season in seasons)
        {
            if (season.SeasonId <= 0
                || string.IsNullOrWhiteSpace(season.Name)
                || season.StartDate == default
                || (season.EndDate is not null && season.EndDate < season.StartDate)
                || (previousStart is not null
                    && (season.StartDate > previousStart
                        || (season.StartDate == previousStart && season.SeasonId >= previousId))))
            {
                return false;
            }

            previousStart = season.StartDate;
            previousId = season.SeasonId;
        }

        return true;
    }
}
