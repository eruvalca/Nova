using System.Text;

namespace Nova.Shared.Features.Players;

/// <summary>
/// Defines route constants and URL builders for the player-roster endpoint.
/// </summary>
public static class GetPlayerRosterEndpoints
{
    /// <summary>
    /// The group prefix for club APIs.
    /// </summary>
    public const string GroupPrefix = "/api/clubs";

    /// <summary>
    /// The absolute route template for retrieving a club's player roster.
    /// </summary>
    public const string GetRosterTemplate = "/api/clubs/{clubId:long}/players/roster";

    /// <summary>
    /// The relative route template for retrieving a club's player roster inside the clubs group.
    /// </summary>
    public const string GetRosterRelative = "{clubId:long}/players/roster";

    /// <summary>
    /// Builds a roster URL with optional filtering, sorting, and pagination query parameters.
    /// </summary>
    /// <param name="clubId">The club identifier.</param>
    /// <param name="search">The optional search term.</param>
    /// <param name="lifecycleStatus">The optional lifecycle-status view filter (<c>active</c> or <c>archived</c>).</param>
    /// <param name="graduationYear">The optional graduation-year filter.</param>
    /// <param name="playerTagId">The optional player-tag filter.</param>
    /// <param name="sortBy">The optional sort field.</param>
    /// <param name="sortDirection">The optional sort direction.</param>
    /// <param name="page">The optional 1-based page number.</param>
    /// <param name="pageSize">The optional page size.</param>
    /// <returns>The relative roster URL.</returns>
    public static string GetRosterUrl(
        long clubId,
        string? search = null,
        string? lifecycleStatus = null,
        int? graduationYear = null,
        long? playerTagId = null,
        string? sortBy = null,
        string? sortDirection = null,
        int? page = null,
        int? pageSize = null)
    {
        var url = new StringBuilder($"/api/clubs/{clubId}/players/roster");
        var querySegments = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            querySegments.Add($"search={Uri.EscapeDataString(search)}");
        }

        var normalizedLifecycleStatus = lifecycleStatus?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "archived" => "archived",
            _ => null
        };
        if (normalizedLifecycleStatus is not null)
        {
            querySegments.Add($"lifecycleStatus={Uri.EscapeDataString(normalizedLifecycleStatus)}");
        }

        if (graduationYear is >= 2000 and <= 2100)
        {
            querySegments.Add($"graduationYear={graduationYear.Value}");
        }

        if (playerTagId is > 0)
        {
            querySegments.Add($"playerTagId={playerTagId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            querySegments.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        }

        if (!string.IsNullOrWhiteSpace(sortDirection))
        {
            querySegments.Add($"sortDirection={Uri.EscapeDataString(sortDirection)}");
        }

        if (page is > 0)
        {
            querySegments.Add($"page={page.Value}");
        }

        if (pageSize is > 0)
        {
            querySegments.Add($"pageSize={pageSize.Value}");
        }

        if (querySegments.Count > 0)
        {
            _ = url.Append('?').Append(string.Join('&', querySegments));
        }

        return url.ToString();
    }
}
