namespace Nova.SharedKernel.Features.Clubs;

/// <summary>Defines canonical application routes for the current member's club.</summary>
public static class ClubRoutes
{
    /// <summary>The club overview route.</summary>
    public const string Overview = "/club";
    /// <summary>The club seasons directory route, readable by every approved club member.</summary>
    public const string Seasons = "/club/seasons";
    /// <summary>The club season-detail route template, with a route constraint on the season identifier.</summary>
    public const string SeasonDetailTemplate = "/club/seasons/{SeasonId:long}";
    /// <summary>The club season-advancement route, reserved for club administrators.</summary>
    public const string StartNextSeason = "/club/seasons/start-next";
    /// <summary>The club teams route.</summary>
    public const string Teams = "/club/teams";
    /// <summary>The club team-detail route template, with a route constraint on the team identifier.</summary>
    public const string TeamDetailTemplate = "/club/teams/{TeamId:long}";
    /// <summary>The club members route.</summary>
    public const string Members = "/club/members";
    /// <summary>The club requests route.</summary>
    public const string Requests = "/club/requests";
    /// <summary>The club tags route.</summary>
    public const string Tags = "/club/tags";
    /// <summary>The club crest route.</summary>
    public const string Crest = "/club/crest";
    /// <summary>The permissions-changed notice query parameter name.</summary>
    public const string PermissionsChangedNotice = "permissions-changed";
    /// <summary>The club overview route with the permissions-changed notice appended.</summary>
    public const string OverviewWithPermissionsChanged = "/club?notice=permissions-changed";

    /// <summary>
    /// Builds the club team-detail route for the given team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The team-detail URL.</returns>
    public static string TeamDetail(long teamId) => $"/club/teams/{teamId}";

    /// <summary>
    /// Builds the club season-detail route for the given season.
    /// </summary>
    /// <param name="seasonId">The season identifier.</param>
    /// <returns>The season-detail URL.</returns>
    public static string SeasonDetail(long seasonId) => $"/club/seasons/{seasonId}";

    /// <summary>
    /// Determines whether a path targets an administrator-only club route, including legacy
    /// pre-shell admin routes, so demoted admins can be recovered with the permissions-changed notice.
    /// The seasons directory is deliberately absent: every approved member reads it.
    /// </summary>
    /// <param name="path">The request path to inspect.</param>
    /// <returns><c>true</c> when the path is an administrator route; otherwise, <c>false</c>.</returns>
    public static bool IsAdministratorRoute(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = "/" + path.Trim().TrimStart('/').Split(['?', '#'], StringSplitOptions.None)[0].TrimEnd('/');
        return normalized.Equals(StartNextSeason, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Members, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Requests, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Tags, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Crest, StringComparison.OrdinalIgnoreCase)
            || IsLegacyClubAdminRoute(normalized);
    }

    /// <summary>
    /// Recognizes the legacy pre-shell admin route <c>/Clubs/{ClubId:long}/admin</c>, which still
    /// uses <see cref="Nova.SharedKernel.Security.Policies.RequireClubAdmin"/> and is linked from the
    /// dashboard, so demoted admins are recovered with the permissions-changed notice there too.
    /// </summary>
    private static bool IsLegacyClubAdminRoute(string normalized)
    {
        const string Prefix = "/clubs/";
        const string Suffix = "/admin";
        if (!normalized.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var clubId = normalized.AsSpan(Prefix.Length, normalized.Length - Prefix.Length - Suffix.Length);
        return clubId.Length > 0 && long.TryParse(clubId, System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
