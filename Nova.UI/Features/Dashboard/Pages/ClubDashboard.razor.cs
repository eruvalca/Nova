using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Components;

namespace Nova.UI.Features.Dashboard.Pages;

/// <summary>
/// Renders the role-aware club dashboard at <c>/</c>: active campaign cards with workspace links,
/// roster/team count cards, the administrator attention card, and the bounded recent-activity feed.
/// </summary>
/// <param name="dashboardQueryService">The dashboard read query service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class ClubDashboard(
    IDashboardQueryService dashboardQueryService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// The loaded dashboard summary, or <see langword="null"/> when unavailable.
    /// </summary>
    private ClubDashboardResult? _summary;

    /// <summary>
    /// The loaded recent-activity feed, or <see langword="null"/> when unavailable.
    /// </summary>
    private DashboardActivityResult? _activity;

    /// <summary>
    /// The current page-level error message.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// Indicates whether dashboard data is being loaded.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether the current user holds the club administrator role.
    /// </summary>
    private bool _isClubAdmin;

    /// <summary>
    /// Stores the current user's club identifier parsed from claims.
    /// </summary>
    private long? _clubId;

    /// <summary>
    /// Gets or sets the persisted startup summary snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public ClubDashboardResult? PersistedSummary { get; set; }

    /// <summary>
    /// Gets or sets the persisted startup activity snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public DashboardActivityResult? PersistedActivity { get; set; }

    /// <summary>
    /// Gets or sets the persisted startup page error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedPageError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// Gets the Bootstrap grid classes for the summary stat-card row, switching between three
    /// administrator columns and two member columns when the attention card is absent.
    /// </summary>
    protected string StatRowClass =>
        _summary?.AdminAttention is not null
            ? "row row-cols-1 row-cols-md-3 g-3 mb-4"
            : "row row-cols-1 row-cols-md-2 g-3 mb-4";

    /// <summary>
    /// Gets the administrator join-request review link target for the current club.
    /// </summary>
    protected string ReviewRequestsUrl => $"/Clubs/{_clubId}/admin";

    /// <summary>
    /// Gets the administrator placement review link target, falling back to the campaign list when no
    /// active campaign has an unresolved placement.
    /// </summary>
    protected string ReviewPlacementsUrl =>
        _summary?.AdminAttention?.FirstUnresolvedCampaignId is long campaignId
            ? DashboardEndpoints.CampaignWorkspaceUrl(campaignId)
            : DashboardEndpoints.CampaignListRoute;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authenticationState.User;

        _isClubAdmin = principal.IsInRole(Roles.ClubAdmin);
        _clubId = ReadClubIdClaim(principal);

        if (Initialized)
        {
            _summary = PersistedSummary;
            _activity = PersistedActivity;
            _pageError = PersistedPageError;
            _isLoading = false;
            return;
        }

        _isLoading = true;
        if (_clubId is null)
        {
            _pageError = "You must join a club before viewing the dashboard.";
            PersistStartupState();
            Initialized = true;
            _isLoading = false;
            return;
        }

        await LoadDashboardAsync();
        PersistStartupState();
        Initialized = true;
        _isLoading = false;
    }

    /// <summary>
    /// Loads the dashboard summary and recent activity in parallel, surfacing the first failure as a
    /// page-level error and redirecting forbidden callers to the access-denied page.
    /// </summary>
    /// <returns>A task that completes when both loads finish and state is updated.</returns>
    private async Task LoadDashboardAsync()
    {
        _pageError = null;

        var summaryTask = dashboardQueryService.GetDashboardAsync(ComponentCancellationToken);
        var activityTask = dashboardQueryService.GetActivityAsync(
            new GetDashboardActivityInput { Limit = GetDashboardActivityInput.DefaultLimit },
            ComponentCancellationToken);

        await Task.WhenAll(summaryTask, activityTask);

        var summaryResult = await summaryTask;
        var activityResult = await activityTask;

        string? error = null;
        summaryResult.Switch(
            summary => _summary = summary,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                error = ProblemMessage(problem, "Failed to load the dashboard. Please retry.");
            });

        activityResult.Switch(
            activity => _activity = activity,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                error ??= ProblemMessage(problem, "Failed to load recent activity. Please retry.");
            });

        _pageError = error;
    }

    /// <summary>
    /// Retries the dashboard load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load finishes.</returns>
    private async Task RetryAsync()
    {
        _isLoading = true;
        await LoadDashboardAsync();
        PersistStartupState();
        _isLoading = false;
    }

    /// <summary>
    /// Persists the startup summary/activity/error state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedSummary = _summary;
        PersistedActivity = _activity;
        PersistedPageError = _pageError;
    }

    /// <summary>
    /// Parses the club identifier claim from the current principal.
    /// </summary>
    /// <param name="principal">The current principal.</param>
    /// <returns>The parsed club identifier when present; otherwise <see langword="null"/>.</returns>
    private static long? ReadClubIdClaim(ClaimsPrincipal principal)
    {
        var clubIdText = principal.FindFirst(NovaClaimTypes.ClubId)?.Value;
        return long.TryParse(clubIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var clubId)
            ? clubId
            : null;
    }

    /// <summary>
    /// Resolves a human-readable message from a service problem.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <param name="fallback">The fallback message when the problem has no detail.</param>
    /// <returns>The problem detail or the fallback message.</returns>
    private static string ProblemMessage(ServiceProblem problem, string fallback)
        => string.IsNullOrWhiteSpace(problem.Detail) ? fallback : problem.Detail;
}
