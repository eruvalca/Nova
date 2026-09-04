namespace Nova.Shared.Features.Clubs;

/// <summary>Defines canonical application routes for the current member's club.</summary>
public static class ClubRoutes
{
    public const string Overview = "/club";
    public const string Seasons = "/club/seasons";
    public const string Teams = "/club/teams";
    public const string TeamDetailTemplate = "/club/teams/{TeamId:long}";
    public const string Members = "/club/members";
    public const string Requests = "/club/requests";
    public const string Tags = "/club/tags";
    public const string Crest = "/club/crest";
    public const string PermissionsChangedNotice = "permissions-changed";
    public const string OverviewWithPermissionsChanged = "/club?notice=permissions-changed";

    public static string TeamDetail(long teamId) => $"/club/teams/{teamId}";

    public static bool IsAdministratorRoute(string path)
    {
        var normalized = "/" + path.Trim().TrimStart('/').Split('?', '#')[0].TrimEnd('/');
        return normalized.Equals(Seasons, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Members, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Requests, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Tags, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(Crest, StringComparison.OrdinalIgnoreCase)
            || IsLegacyClubAdminRoute(normalized);
    }

    /// <summary>
    /// Recognizes the legacy pre-shell admin route <c>/Clubs/{ClubId:long}/admin</c>, which still
    /// uses <see cref="Nova.Shared.Security.Policies.RequireClubAdmin"/> and is linked from the
    /// dashboard, so demoted admins are recovered with the permissions-changed notice there too.
    /// </summary>
    private static bool IsLegacyClubAdminRoute(string normalized)
    {
        const string prefix = "/clubs/";
        const string suffix = "/admin";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var clubId = normalized.AsSpan(prefix.Length, normalized.Length - prefix.Length - suffix.Length);
        return clubId.Length > 0 && long.TryParse(clubId, out _);
    }
}
