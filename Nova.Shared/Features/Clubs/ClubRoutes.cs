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
            || normalized.Equals(Crest, StringComparison.OrdinalIgnoreCase);
    }
}
