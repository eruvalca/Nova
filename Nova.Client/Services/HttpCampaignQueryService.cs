using System.Net.Http.Json;
using System.Text.Json;
using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services;

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

        try
        {
            var result = await response.Content.ReadFromJsonAsync<CampaignListResult>(cancellationToken);
            return result is not null && IsValidCampaignList(result, input.Limit)
                ? result
                : ServiceProblem.ServerError("The server returned an invalid campaign list response.");
        }
        catch (JsonException)
        {
            return ServiceProblem.ServerError("The server returned an invalid campaign list response.");
        }
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

        try
        {
            var result = await response.Content.ReadFromJsonAsync<CampaignCreationSetupResult>(cancellationToken);
            return result is not null && IsValidCreationSetup(result)
                ? result
                : ServiceProblem.ServerError("The server returned an invalid campaign setup response.");
        }
        catch (JsonException)
        {
            return ServiceProblem.ServerError("The server returned an invalid campaign setup response.");
        }
    }

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

    private static bool IsValidCampaign(CampaignListItem campaign)
        => campaign.CampaignId > 0
            && !string.IsNullOrWhiteSpace(campaign.Name)
            && campaign.StartDate != default
            && (campaign.PlannedEndDate is null || campaign.PlannedEndDate >= campaign.StartDate)
            && campaign.ParticipantCount >= 0
            && campaign.UnresolvedCount >= 0
            && campaign.UnresolvedCount <= campaign.ParticipantCount
            && campaign.Status is CampaignStatus.Active or CampaignStatus.Closed;

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

    private static bool IsValidCreationSetup(CampaignCreationSetupResult result)
        => result.Seasons is not null
            && result.Seasons.All(season => season is not null)
            && result.TotalSeasonCount >= 0
            && result.Seasons.Count <= CampaignCreationSetupResult.MaxSeasonChoices
            && result.ActivePlayerCount >= 0
            && result.ActiveTeamCount >= 0
            && IsOrderedAndValidSeasons(result.Seasons);

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
