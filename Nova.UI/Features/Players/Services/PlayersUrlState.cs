#pragma warning disable CA1054, CA1055, CA1056 // Raw query parsing must accept malformed strings; Blazor consumes relative URLs.
using System.Globalization;
using Nova.SharedKernel.Features.Players;
using Nova.UI.Common;

namespace Nova.UI.Features.Players.Services;

/// <summary>Owns the directory's normalized discovery and local correction return context.</summary>
public sealed record PlayersUrlState
{
    /// <summary>The lifecycle destination, active by default.</summary>
    public string View { get; init; } = "active";
    /// <summary>The trimmed literal search, retained even when too long so it can be corrected.</summary>
    public string Search { get; init; } = string.Empty;
    /// <summary>The optional supported graduation year.</summary>
    public int? GraduationYear { get; init; }
    /// <summary>The optional tag identifier, including saved inactive definitions.</summary>
    public long? TagId { get; init; }
    /// <summary>The one-based twenty-player page.</summary>
    public int Page { get; init; } = 1;
    /// <summary>The Draft campaign to return to after a correction.</summary>
    public long? ReturnToDraft { get; init; }
    /// <summary>The safe local correction destination, potentially nested beneath Draft.</summary>
    public string? ReturnUrl { get; init; }
    /// <summary>Whether the server can accept the retained search.</summary>
    public bool IsSearchValid => Search.Length <= GetPlayerRosterInput.MaxSearchLength;
    /// <summary>Whether discovery narrows the selected lifecycle destination.</summary>
    public bool HasFilters => Search.Length > 0 || GraduationYear is not null || TagId is not null;
    /// <summary>A normalized fingerprint for ownership of a persisted query result.</summary>
    public string QueryFingerprint => $"{View}|{Uri.EscapeDataString(Search)}|{GraduationYear}|{TagId}|{Page}";

    /// <summary>Parses raw values without allowing numeric query conversion to fail routing.</summary>
    public static PlayersUrlState Parse(string? view = null, string? search = null,
        string? graduationYear = null, string? tag = null, string? page = null,
        string? returnToDraft = null, string? returnUrl = null)
        => new()
        {
            View = string.Equals(view?.Trim(), "archived", StringComparison.OrdinalIgnoreCase) ? "archived" : "active",
            Search = search?.Trim() ?? string.Empty,
            GraduationYear = int.TryParse(graduationYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
                && year is >= 2000 and <= 2100 ? year : null,
            TagId = ParsePositiveId(tag),
            Page = int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : 1,
            ReturnToDraft = ParsePositiveId(returnToDraft),
            ReturnUrl = CorrectionReturnContext.Normalize(returnUrl)
        };

    /// <summary>Reads the directory fields from an absolute navigation URI or safe relative URL.</summary>
    public static PlayersUrlState FromUri(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var start = uri.IndexOf('?', StringComparison.Ordinal);
        var end = uri.IndexOf('#', StringComparison.Ordinal);
        var query = string.Empty;
        if (start >= 0 && (end < 0 || end > start)) { query = end < 0 ? uri[start..] : uri[start..end]; }
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : string.Empty;
            if (!values.TryAdd(key, value)) { values[key] = null; }
        }
        // Repeated values have no unambiguous meaning; normal parsing falls back defensively.
        string? Read(string key) => values.GetValueOrDefault(key);
        return Parse(Read("view"), Read("search"), Read("graduationYear"), Read("tag"), Read("page"),
            Read("returnToDraft"), Read("returnUrl"));
    }

    /// <summary>Preserves a directory return or wraps a direct campaign/Place return for the form host.</summary>
    public static PlayersUrlState FromReturnDestination(string? destination)
    {
        var local = CorrectionReturnContext.Normalize(destination);
        if (local is null) { return new(); }
        var path = local.Split(['?', '#'], StringSplitOptions.None)[0];
        return string.Equals(path, "/players", StringComparison.OrdinalIgnoreCase)
            ? FromUri(local) : new() { ReturnUrl = local };
    }

    /// <summary>Builds the directory destination with all normalized context.</summary>
    public string ToDirectoryUrl() => AppendQuery("/players");

    /// <summary>Builds a create or edit destination under the directory's shared route host.</summary>
    public string ToFormUrl(long? playerId = null)
        => AppendQuery(playerId is > 0 ? $"/players/{playerId}/edit" : "/players/new");

    /// <summary>Builds the single player-record destination with the complete directory return.</summary>
    public string ToPlayerUrl(long playerId)
        => $"/players/{playerId}?returnUrl={Uri.EscapeDataString(ToDirectoryUrl())}";

    /// <summary>Builds the Draft correction return while retaining its nested Place context.</summary>
    public string? ToDraftUrl()
    {
        if (ReturnToDraft is not { } id) { return null; }
        var suffix = ReturnUrl is { } destination ? $"?returnUrl={Uri.EscapeDataString(destination)}" : string.Empty;
        return $"/campaigns/{id}" + suffix;
    }

    private string AppendQuery(string path)
    {
        var values = new List<string>();
        if (string.Equals(View, "archived", StringComparison.Ordinal)) { values.Add("view=archived"); }
        if (Search.Length > 0) { values.Add($"search={Uri.EscapeDataString(Search)}"); }
        if (GraduationYear is { } year) { values.Add($"graduationYear={year.ToString(CultureInfo.InvariantCulture)}"); }
        if (TagId is { } tag) { values.Add($"tag={tag.ToString(CultureInfo.InvariantCulture)}"); }
        if (Page > 1) { values.Add($"page={Page.ToString(CultureInfo.InvariantCulture)}"); }
        if (ReturnToDraft is { } draft) { values.Add($"returnToDraft={draft.ToString(CultureInfo.InvariantCulture)}"); }
        if (CorrectionReturnContext.Normalize(ReturnUrl) is { } destination) { values.Add($"returnUrl={Uri.EscapeDataString(destination)}"); }
        return values.Count == 0 ? path : path + "?" + string.Join('&', values);
    }

    private static long? ParsePositiveId(string? raw)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}
#pragma warning restore CA1054, CA1055, CA1056
