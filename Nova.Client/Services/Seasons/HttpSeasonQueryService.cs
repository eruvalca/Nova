using System.Net.Http.Json;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Validation;

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
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var expectedPage = input.Page ?? GetSeasonListInput.DefaultPage;
        var expectedPageSize = input.PageSize ?? GetSeasonListInput.DefaultPageSize;
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
            page => IsValidPage(page, expectedPage, expectedPageSize),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeasonDetailResult>> GetAsync(
        GetSeasonDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var expectedCampaignPage = input.CampaignPage ?? GetSeasonListInput.DefaultPage;
        var expectedCampaignPageSize = input.CampaignPageSize ?? GetSeasonListInput.DefaultPageSize;
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
                && result.CampaignPage == expectedCampaignPage
                && result.CampaignPageSize == expectedCampaignPageSize
                && result.CampaignTotalCount >= 0
                && result.Campaigns.Count <= result.CampaignPageSize
                && result.Campaigns.All(campaign => campaign.CampaignId > 0
                    && !string.IsNullOrWhiteSpace(campaign.Name)
                    && campaign.StartDate != default
                    && (campaign.EndDate is null || campaign.EndDate >= campaign.StartDate)
                    && campaign.ParticipantCount >= 0)
                && IsCampaignOrderValid(result.Campaigns),
            cancellationToken);
    }

    /// <summary>Validates portable season-page invariants and requested paging metadata.</summary>
    /// <param name="page">The deserialized season page.</param>
    /// <param name="expectedPage">The effective page requested by the caller.</param>
    /// <param name="expectedPageSize">The effective page size requested by the caller.</param>
    /// <returns><see langword="true"/> when the page satisfies the client contract.</returns>
    private static bool IsValidPage(SeasonPageResult page, int expectedPage, int expectedPageSize)
        => page is not null
            && page.Items is not null
            && page.Page == expectedPage
            && page.PageSize == expectedPageSize
            && page.TotalCount >= 0
            && page.Items.Count <= page.PageSize
            && page.Items.All(IsValidSummary)
            && page.Items.Count(season => season.IsCurrent) <= 1
            && IsSeasonOrderValid(page.Items, page.Page);

    /// <summary>Validates portable season-summary invariants.</summary>
    private static bool IsValidSummary(SeasonSummary season)
        => season is not null
            && season.SeasonId > 0
            && !string.IsNullOrWhiteSpace(season.Name)
            && season.StartDate != default
            && (season.EndDate is null || season.EndDate >= season.StartDate)
            && season.ConcurrencyToken != Guid.Empty;

    /// <summary>Validates current-first, start-date-descending, identifier-descending page order.</summary>
    private static bool IsSeasonOrderValid(IReadOnlyList<SeasonSummary> seasons, int page)
    {
        if (page > 1 && seasons.Any(season => season.IsCurrent))
        {
            return false;
        }

        for (var index = 1; index < seasons.Count; index++)
        {
            var previous = seasons[index - 1];
            var current = seasons[index];
            if (!previous.IsCurrent && current.IsCurrent)
            {
                return false;
            }

            if (previous.IsCurrent == current.IsCurrent
                && (previous.StartDate < current.StartDate
                    || (previous.StartDate == current.StartDate
                        && previous.SeasonId < current.SeasonId)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Validates campaign start-date-descending, identifier-descending page order.</summary>
    private static bool IsCampaignOrderValid(IReadOnlyList<SeasonCampaignSummary> campaigns)
    {
        for (var index = 1; index < campaigns.Count; index++)
        {
            var previous = campaigns[index - 1];
            var current = campaigns[index];
            if (previous.StartDate < current.StartDate
                || (previous.StartDate == current.StartDate
                    && previous.CampaignId < current.CampaignId))
            {
                return false;
            }
        }

        return true;
    }
}
