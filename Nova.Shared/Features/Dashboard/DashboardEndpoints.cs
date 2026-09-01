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
    /// Gets the bounded recent-activity route (GET).
    /// </summary>
    public const string GetActivity = $"{GroupPrefix}/activity";

    /// <summary>
    /// Gets the recent-activity route relative to the dashboard group.
    /// </summary>
    public const string GetActivityRelative = "activity";

    /// <summary>
    /// Gets the route name assigned to the bounded recent-activity query.
    /// </summary>
    public const string GetActivityRouteName = "GetClubDashboardActivity";

    /// <summary>The administrator-only attention projection route.</summary>
    public const string GetAttention = $"{GroupPrefix}/attention";

    /// <summary>The attention route relative to the dashboard group.</summary>
    public const string GetAttentionRelative = "attention";

    /// <summary>The route name assigned to the administrator attention projection.</summary>
    public const string GetAttentionRouteName = "GetClubDashboardAttention";

    /// <summary>
    /// The route prefix of the campaign workspace page (the #10 read surface), used to build the
    /// prebuilt workspace link carried by each active campaign card.
    /// </summary>
    public const string CampaignWorkspaceRoutePrefix = "/campaigns";

    /// <summary>
    /// The route of the campaign list page, used as the administrator attention fallback target when
    /// no active campaign has an unresolved placement.
    /// </summary>
    public const string CampaignListRoute = "/campaigns";

    /// <summary>
    /// Builds the bounded recent-activity URL, omitting the optional limit when it is not supplied
    /// or would not be accepted by the input contract.
    /// </summary>
    /// <param name="continuationToken">The opaque token for the next older page.</param>
    /// <returns>The recent-activity URL.</returns>
    public static string GetActivityUrl(string? continuationToken)
        => string.IsNullOrWhiteSpace(continuationToken)
            ? GetActivity
            : $"{GetActivity}?continuationToken={Uri.EscapeDataString(continuationToken)}";

    /// <summary>
    /// Builds the prebuilt workspace URL for an active campaign card, pointing at the campaign
    /// workspace page without duplicating the roster or workspace route literal.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <returns>The relative campaign workspace URL.</returns>
    public static string CampaignWorkspaceUrl(long campaignId)
        => $"{CampaignWorkspaceRoutePrefix}/{campaignId}";
}
