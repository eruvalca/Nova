namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// Defines the shared route constants for the club dashboard read endpoints so the server and
/// WebAssembly client agree on routes, plus the workspace-link helper that carries the campaign
/// workspace route into dashboard cards without duplicating the route literal.
/// </summary>
public static class DashboardEndpoints
{
    /// <summary>
    /// The group prefix for club dashboard endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/dashboard";

    /// <summary>
    /// Gets the club dashboard summary route (GET).
    /// </summary>
    public const string GetSummary = GroupPrefix;

    /// <summary>
    /// Gets the dashboard-summary route relative to the dashboard group (empty maps GET to the group root).
    /// </summary>
    public const string GetSummaryRelative = "";

    /// <summary>
    /// Gets the route name assigned to the club dashboard summary query.
    /// </summary>
    public const string GetSummaryRouteName = "GetClubDashboard";

    /// <summary>
    /// The route prefix of the campaign workspace page (the #10 read surface), used to build the
    /// prebuilt workspace link carried by each active campaign card.
    /// </summary>
    public const string CampaignWorkspaceRoutePrefix = "/campaigns";

    /// <summary>
    /// Builds the prebuilt workspace URL for an active campaign card, pointing at the campaign
    /// workspace page without duplicating the roster or workspace route literal.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <returns>The relative campaign workspace URL.</returns>
    public static string CampaignWorkspaceUrl(long campaignId)
        => $"{CampaignWorkspaceRoutePrefix}/{campaignId}";
}
