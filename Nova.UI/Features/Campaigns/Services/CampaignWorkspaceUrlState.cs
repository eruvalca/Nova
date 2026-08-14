using System.Globalization;

namespace Nova.UI.Features.Campaigns.Services;

/// <summary>
/// Represents the roster filter, sort, and paging state reflected in the campaign workspace URL.
/// </summary>
public sealed record CampaignWorkspaceRosterState
{
    /// <summary>
    /// Gets the applied name-or-tryout-number search term, or <see langword="null"/> when unfiltered.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Gets the selected graduation years, or an empty list when unfiltered.
    /// </summary>
    public IReadOnlyList<int> GraduationYears { get; init; } = [];

    /// <summary>
    /// Gets the selected tag-definition identifiers, or an empty list when unfiltered.
    /// </summary>
    public IReadOnlyList<long> TagDefinitionIds { get; init; } = [];

    /// <summary>
    /// Gets the applied placement-outcome token (<c>undecided</c>, <c>assigned</c>, <c>notselected</c>, or <c>withdrawn</c>), or <see langword="null"/> when unfiltered.
    /// </summary>
    public string? Outcome { get; init; }

    /// <summary>
    /// Gets the applied team identifier, or <see langword="null"/> when unfiltered.
    /// </summary>
    public long? TeamId { get; init; }

    /// <summary>
    /// Gets the applied sort field token, or <see langword="null"/> when the server default applies.
    /// </summary>
    public string? SortBy { get; init; }

    /// <summary>
    /// Gets the applied sort direction token (<c>asc</c> or <c>desc</c>).
    /// </summary>
    public string? SortDirection { get; init; }

    /// <summary>
    /// Gets the one-based roster page number.
    /// </summary>
    public int Page { get; init; } = 1;
}

/// <summary>
/// Provides pure, defensive URL round-tripping for the campaign workspace roster.
/// </summary>
public static class CampaignWorkspaceUrlState
{
    /// <summary>
    /// The only functional workspace tab token in this phase.
    /// </summary>
    public const string EvaluateTab = "evaluate";

    /// <summary>
    /// The contract-supported placement-outcome tokens, in canonical lowercase form.
    /// </summary>
    private static readonly string[] ValidOutcomes = ["undecided", "assigned", "notselected", "withdrawn"];

    /// <summary>
    /// The contract-supported sort-field tokens, in canonical camel-case form.
    /// </summary>
    private static readonly string[] ValidSortFields = ["displayName", "graduationYear", "tryoutNumber", "outcome", "teamName"];

    /// <summary>
    /// The contract-supported sort-direction tokens.
    /// </summary>
    private static readonly string[] ValidDirections = ["asc", "desc"];

    /// <summary>
    /// Parses raw query-parameter values into a defensive roster state, falling back to defaults for invalid values.
    /// </summary>
    /// <param name="search">The raw search query value.</param>
    /// <param name="graduationYears">The raw comma-separated graduation-years query value.</param>
    /// <param name="tagDefinitionIds">The raw comma-separated tag-identifier query value.</param>
    /// <param name="outcome">The raw outcome query value.</param>
    /// <param name="teamId">The raw team-identifier query value.</param>
    /// <param name="sortBy">The raw sort-field query value.</param>
    /// <param name="sortDirection">The raw sort-direction query value.</param>
    /// <param name="page">The raw page-number query value.</param>
    /// <returns>A defensive roster state built from the supplied values.</returns>
    public static CampaignWorkspaceRosterState Parse(
        string? search,
        string? graduationYears,
        string? tagDefinitionIds,
        string? outcome,
        long? teamId,
        string? sortBy,
        string? sortDirection,
        int? page)
        => new()
        {
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            GraduationYears = ParsePositiveInts(graduationYears),
            TagDefinitionIds = ParsePositiveLongs(tagDefinitionIds),
            Outcome = NormalizeToken(outcome, ValidOutcomes),
            TeamId = teamId is > 0 ? teamId : null,
            SortBy = NormalizeToken(sortBy, ValidSortFields),
            SortDirection = NormalizeToken(sortDirection, ValidDirections),
            Page = page is >= 1 ? page.Value : 1
        };

    /// <summary>
    /// Builds the canonical query string for the supplied state, omitting default values.
    /// </summary>
    /// <param name="state">The roster state to serialize.</param>
    /// <returns>The canonical query string without a leading question mark.</returns>
    public static string BuildQueryString(CampaignWorkspaceRosterState state)
    {
        var parts = new List<string>(8);

        if (state.Search is not null)
        {
            parts.Add($"search={Uri.EscapeDataString(state.Search)}");
        }

        if (state.GraduationYears.Count > 0)
        {
            parts.Add($"graduationYears={string.Join(",", state.GraduationYears.OrderBy(year => year))}");
        }

        if (state.TagDefinitionIds.Count > 0)
        {
            parts.Add($"tagIds={string.Join(",", state.TagDefinitionIds.OrderBy(id => id))}");
        }

        if (state.Outcome is not null)
        {
            parts.Add($"outcome={state.Outcome}");
        }

        if (state.TeamId is not null)
        {
            parts.Add($"teamId={state.TeamId}");
        }

        if (state.SortBy is not null && state.SortDirection is not null)
        {
            parts.Add($"sortBy={state.SortBy}");
            parts.Add($"sortDirection={state.SortDirection}");
        }

        if (state.Page > 1)
        {
            parts.Add($"page={state.Page}");
        }

        return string.Join("&", parts);
    }

    /// <summary>
    /// Parses the raw <c>participant</c> query value into a positive assignment identifier.
    /// </summary>
    /// <param name="raw">The raw participant query value.</param>
    /// <returns>The assignment identifier, or <see langword="null"/> when absent or invalid.</returns>
    public static long? ParseParticipant(string? raw)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;

    /// <summary>
    /// Builds the full workspace URL for the supplied state, always carrying the active tab token
    /// and the selected participant when one is open.
    /// </summary>
    /// <param name="campaignId">The campaign identifier from the route.</param>
    /// <param name="state">The roster state to serialize.</param>
    /// <param name="tab">The active tab token.</param>
    /// <param name="participantId">The open participant assignment identifier, or <see langword="null"/> when the drawer is closed.</param>
    /// <returns>The relative workspace URL.</returns>
    public static string BuildWorkspaceUrl(
        long campaignId,
        CampaignWorkspaceRosterState state,
        string tab = EvaluateTab,
        long? participantId = null)
    {
        var parts = new List<string>(10);
        var query = BuildQueryString(state);
        if (!string.IsNullOrEmpty(query))
        {
            parts.Add(query);
        }

        parts.Add($"tab={tab}");

        if (participantId is not null)
        {
            parts.Add($"participant={participantId}");
        }

        return $"/campaigns/{campaignId}?{string.Join("&", parts)}";
    }

    /// <summary>
    /// Determines whether any roster filter (search, years, tags, outcome, or team) is active.
    /// </summary>
    /// <param name="state">The roster state to inspect.</param>
    /// <returns><see langword="true"/> when at least one filter is active; otherwise <see langword="false"/>.</returns>
    public static bool HasActiveFilters(CampaignWorkspaceRosterState state)
        => state.Search is not null
            || state.GraduationYears.Count > 0
            || state.TagDefinitionIds.Count > 0
            || state.Outcome is not null
            || state.TeamId is not null;

    /// <summary>
    /// Returns a copy of the supplied state with all filters cleared and the page reset.
    /// </summary>
    /// <param name="state">The roster state to clear.</param>
    /// <returns>The cleared state.</returns>
    public static CampaignWorkspaceRosterState ClearFilters(CampaignWorkspaceRosterState state)
        => state with { Search = null, GraduationYears = [], TagDefinitionIds = [], Outcome = null, TeamId = null, Page = 1 };

    /// <summary>
    /// Computes the total page count for a bounded roster result.
    /// </summary>
    /// <param name="totalCount">The total matching row count.</param>
    /// <param name="pageSize">The fixed page size.</param>
    /// <returns>The one-based total page count, never less than one.</returns>
    public static int CalculatePageCount(int totalCount, int pageSize)
    {
        if (totalCount <= 0 || pageSize <= 0)
        {
            return 1;
        }

        return (int)Math.Ceiling((double)totalCount / pageSize);
    }

    /// <summary>
    /// Parses a comma-separated list of positive, de-duplicated integers, dropping invalid entries.
    /// </summary>
    /// <param name="raw">The raw comma-separated value.</param>
    /// <returns>The parsed values in first-seen order.</returns>
    private static IReadOnlyList<int> ParsePositiveInts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parsed = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value > 0
                && !parsed.Contains(value))
            {
                parsed.Add(value);
            }
        }

        return parsed.AsReadOnly();
    }

    /// <summary>
    /// Parses a comma-separated list of positive, de-duplicated long identifiers, dropping invalid entries.
    /// </summary>
    /// <param name="raw">The raw comma-separated value.</param>
    /// <returns>The parsed identifiers in first-seen order.</returns>
    private static IReadOnlyList<long> ParsePositiveLongs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var parsed = new List<long>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value > 0
                && !parsed.Contains(value))
            {
                parsed.Add(value);
            }
        }

        return parsed.AsReadOnly();
    }

    /// <summary>
    /// Normalizes a raw token against the supplied valid tokens, case-insensitively.
    /// </summary>
    /// <param name="raw">The raw token.</param>
    /// <param name="validTokens">The canonical valid tokens.</param>
    /// <returns>The matching canonical token, or <see langword="null"/> when invalid.</returns>
    private static string? NormalizeToken(string? raw, string[] validTokens)
        => validTokens.FirstOrDefault(token => string.Equals(token, raw, StringComparison.OrdinalIgnoreCase));
}
