#pragma warning disable CA1055 // These relative route strings are consumed by HTTP clients, Razor attributes, and NavigationManager.
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.SharedKernel.Features.Seasons;

/// <summary>Provides stable routes for season HTTP APIs.</summary>
public static class SeasonEndpoints
{
    /// <summary>The effective current-season roster route relative to the season group.</summary>
    public const string CurrentRosterRelative = "current/roster";
    /// <summary>Builds a bounded effective current-season roster request.</summary>
    public static string CurrentRosterUrl(GetCurrentSeasonRosterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return PlacementQueryUrls.WithPage($"{GroupPrefix}/{CurrentRosterRelative}", input, input.TeamId, input.GraduationYear, input.Search);
    }
    /// <summary>Gets the seasons route group prefix.</summary>
    public const string GroupPrefix = "/api/seasons";

    /// <summary>Gets the relative collection route.</summary>
    public const string CollectionRelative = "";

    /// <summary>Gets the relative season detail route.</summary>
    public const string DetailRelative = "{seasonId:long}";

    /// <summary>Gets the relative season advancement route.</summary>
    public const string StartNextRelative = "start-next";

    /// <summary>Gets the absolute season advancement route.</summary>
    public const string StartNext = $"{GroupPrefix}/{StartNextRelative}";

    /// <summary>Gets the named route for season detail.</summary>
    public const string GetDetailRouteName = "GetSeasonDetail";

    /// <summary>Builds the detail route for a season identifier.</summary>
    /// <param name="seasonId">The season identifier.</param>
    /// <returns>The season detail route.</returns>
    public static string Detail(long seasonId) => $"{GroupPrefix}/{seasonId}";

    /// <summary>Builds the season-list route with the supplied optional paging values.</summary>
    /// <param name="input">The season-list request.</param>
    /// <returns>The season-list route.</returns>
    public static string ListUrl(GetSeasonListInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = new List<string>();
        if (input.Page is >= 1 and var page)
        {
            query.Add($"page={page}");
        }

        if (input.PageSize is >= 1 and <= GetSeasonListInput.MaximumPageSize and var pageSize)
        {
            query.Add($"pageSize={pageSize}");
        }

        return query.Count == 0
            ? GroupPrefix
            : $"{GroupPrefix}?{string.Join('&', query)}";
    }

    /// <summary>Builds the season-detail route with the supplied optional campaign paging values.</summary>
    /// <param name="input">The season-detail request.</param>
    /// <returns>The season-detail route.</returns>
    public static string DetailUrl(GetSeasonDetailInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var baseUrl = Detail(input.SeasonId);
        var query = new List<string>();
        if (input.CampaignPage is >= 1 and var page)
        {
            query.Add($"campaignPage={page}");
        }

        if (input.CampaignPageSize is >= 1 and <= GetSeasonListInput.MaximumPageSize and var pageSize)
        {
            query.Add($"campaignPageSize={pageSize}");
        }

        return query.Count == 0
            ? baseUrl
            : $"{baseUrl}?{string.Join('&', query)}";
    }
}

#pragma warning restore CA1055
