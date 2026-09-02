using System.Net.Http.Json;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;

namespace Nova.Client.Services.Seasons;

/// <summary>Calls the server's season query endpoints over HTTP.</summary>
/// <param name="http">The application HTTP client.</param>
public sealed class HttpSeasonQueryService(HttpClient http) : ISeasonQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<SeasonPageResult>> ListAsync(
        GetSeasonListInput input,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (input.Page is int page)
        {
            query.Add($"page={page}");
        }

        if (input.PageSize is int pageSize)
        {
            query.Add($"pageSize={pageSize}");
        }

        var url = query.Count == 0
            ? SeasonEndpoints.GroupPrefix
            : $"{SeasonEndpoints.GroupPrefix}?{string.Join('&', query)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<SeasonPageResult>(
            "The server returned an invalid season page.",
            IsValidPage,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeasonDetailResult>> GetAsync(
        GetSeasonDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (input.CampaignPage is int page)
        {
            query.Add($"campaignPage={page}");
        }

        if (input.CampaignPageSize is int pageSize)
        {
            query.Add($"campaignPageSize={pageSize}");
        }

        var baseUrl = SeasonEndpoints.Detail(input.SeasonId);
        var url = query.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join('&', query)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<SeasonDetailResult>(
            "The server returned an invalid season detail.",
            result => result is not null
                && IsValidSummary(result.Season)
                && result.Season.SeasonId == input.SeasonId
                && result.Campaigns is not null
                && result.CampaignPage > 0
                && result.CampaignPageSize is > 0 and <= GetSeasonListInput.MaximumPageSize
                && result.CampaignTotalCount >= result.Campaigns.Count
                && result.Campaigns.All(campaign => campaign.CampaignId > 0
                    && !string.IsNullOrWhiteSpace(campaign.Name)
                    && campaign.StartDate != default
                    && (campaign.EndDate is null || campaign.EndDate >= campaign.StartDate)
                    && campaign.ParticipantCount >= 0),
            cancellationToken);
    }

    /// <summary>Validates portable season-page invariants.</summary>
    private static bool IsValidPage(SeasonPageResult page)
        => page is not null
            && page.Items is not null
            && page.Page > 0
            && page.PageSize is > 0 and <= GetSeasonListInput.MaximumPageSize
            && page.TotalCount >= page.Items.Count
            && page.Items.Count <= page.PageSize
            && page.Items.All(IsValidSummary)
            && page.Items.Count(season => season.IsCurrent) <= 1;

    /// <summary>Validates portable season-summary invariants.</summary>
    private static bool IsValidSummary(SeasonSummary season)
        => season is not null
            && season.SeasonId > 0
            && !string.IsNullOrWhiteSpace(season.Name)
            && season.StartDate != default
            && (season.EndDate is null || season.EndDate >= season.StartDate)
            && season.ConcurrencyToken != Guid.Empty;
}
