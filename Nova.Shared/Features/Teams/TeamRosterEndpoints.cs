using System.Text;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Defines shared routes and URL builders for the team roster query.
/// </summary>
public static class TeamRosterEndpoints
{
    /// <summary>
    /// Gets the team API group prefix.
    /// </summary>
    public const string GroupPrefix = TeamEndpoints.GroupPrefix;

    /// <summary>
    /// Gets the absolute team-roster route.
    /// </summary>
    public const string GetRoster = GroupPrefix;

    /// <summary>
    /// Gets the team-roster route relative to the team API group.
    /// </summary>
    public const string GetRosterRelative = "";

    /// <summary>
    /// Builds a team-roster URL from the accepted optional filters.
    /// </summary>
    /// <param name="search">The optional team-name search term.</param>
    /// <param name="lifecycleStatus">The optional lifecycle view.</param>
    /// <param name="graduationYear">The optional graduation year.</param>
    /// <param name="limit">The optional maximum row count, between 1 and 200.</param>
    /// <returns>The team-roster URL.</returns>
    public static string GetRosterUrl(
        string? search = null,
        string? lifecycleStatus = null,
        int? graduationYear = null,
        int? limit = null)
    {
        var url = new StringBuilder(GetRoster);
        var querySegments = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            querySegments.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        var normalizedLifecycleStatus = lifecycleStatus?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "archived" => "archived",
            _ => null
        };
        if (normalizedLifecycleStatus is not null)
        {
            querySegments.Add($"lifecycleStatus={normalizedLifecycleStatus}");
        }

        if (graduationYear is >= 2000 and <= 2100)
        {
            querySegments.Add($"graduationYear={graduationYear.Value}");
        }

        if (limit is >= 1 and <= 200)
        {
            querySegments.Add($"limit={limit.Value}");
        }

        if (querySegments.Count > 0)
        {
            _ = url.Append('?').Append(string.Join('&', querySegments));
        }

        return url.ToString();
    }
}
