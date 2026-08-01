using System.Globalization;
using System.Threading;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Teams;
using Nova.UI.Components;
using Nova.UI.Features.Teams.Components;

namespace Nova.UI.Features.Teams.Pages;

/// <summary>
/// Displays one team's permanent profile, lifecycle state, active placement impacts, and expandable
/// placement-history grouped by campaign. Administrators can launch edit, archive, and restore
/// interactions inline; evaluators have read-only access.
/// </summary>
/// <param name="teamDetailService">The team-detail query service.</param>
/// <param name="teamManagementService">The team create/update service.</param>
/// <param name="teamLifecycleService">The team archive/restore service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class TeamDetail(
    ITeamDetailService teamDetailService,
    ITeamManagementService teamManagementService,
    ITeamLifecycleService teamLifecycleService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets or sets the target team identifier from the route.
    /// </summary>
    [Parameter]
    public long TeamId { get; set; }

    /// <summary>
    /// Gets or sets the optional return URL query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// The loaded team detail payload.
    /// </summary>
    private TeamDetailDto? _detail;

    /// <summary>
    /// The page-level error message shown when loading fails.
    /// </summary>
    private string? _error;

    /// <summary>
    /// The mutation-level error message shown when an admin action fails.
    /// </summary>
    private string? _mutationError;

    /// <summary>
    /// The form-level error message forwarded into the edit form.
    /// </summary>
    private string? _formError;

    /// <summary>
    /// The success status message shown after a successful mutation.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Indicates whether the detail is currently loading.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether the team was not found.
    /// </summary>
    private bool _isNotFound;

    /// <summary>
    /// Indicates whether a mutation is in progress.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// Indicates whether the current user can manage (edit/archive/restore) teams.
    /// </summary>
    private bool _canManageTeams;

    /// <summary>
    /// Indicates whether the edit form is currently visible.
    /// </summary>
    private bool _showEditForm;

    /// <summary>
    /// The edit-mode form state when edit is active.
    /// </summary>
    private TeamFormState? _editForm;

    /// <summary>
    /// Structured graduation-year cutoff blockers returned from an update conflict.
    /// </summary>
    private IReadOnlyList<TeamGraduationYearBlockerItem> _cutoffBlockers = [];

    /// <summary>
    /// Indicates whether the archive confirmation panel is open.
    /// </summary>
    private bool _showArchiveConfirm;

    /// <summary>
    /// Indicates whether the archive confirmation checkbox is checked.
    /// </summary>
    private bool _archiveConfirmed;

    /// <summary>
    /// Structured archive blockers returned from a failed archive attempt.
    /// </summary>
    private IReadOnlyList<TeamArchiveBlocker> _archiveBlockers = [];

    /// <summary>
    /// The normalized return URL used by the back link.
    /// </summary>
    private string? _returnUrl;

    /// <summary>
    /// The <see cref="TeamId"/> value from the most recent parameter-driven load, used to detect
    /// navigation to a different team without a full component teardown.
    /// </summary>
    private long _lastLoadedTeamId;

    /// <summary>
    /// The <see cref="ReturnUrl"/> value from the most recent parameter-driven load.
    /// </summary>
    private string? _lastReturnUrl;

    /// <summary>
    /// Scoped cancellation token source for the current team's in-flight tasks; cancelled and replaced
    /// when enhanced navigation changes <see cref="TeamId"/> so stale results cannot overwrite new-team state.
    /// </summary>
    private CancellationTokenSource _teamScopedCts = new();

    /// <summary>
    /// Monotonically increasing generation counter incremented at the start of every <see cref="LoadDetailAsync"/>
    /// call. Any completion that sees a mismatched generation discards its result without touching component state.
    /// </summary>
    private int _loadDetailVersion;

    /// <summary>
    /// Gets or sets the persisted startup detail payload used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public TeamDetailDto? PersistedDetail { get; set; }

    /// <summary>
    /// Gets or sets the persisted startup page-error message used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedError { get; set; }

    /// <summary>
    /// Gets or sets the persisted startup not-found flag used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public bool PersistedIsNotFound { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        _teamScopedCts = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);

        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        _canManageTeams = principal.IsInRole(Roles.Admin) || principal.IsInRole(Roles.ClubAdmin);

        _lastLoadedTeamId = TeamId;
        _lastReturnUrl = ReturnUrl;
        _returnUrl = NormalizeReturnUrl(ReturnUrl);

        if (Initialized)
        {
            _detail = PersistedDetail;
            _error = PersistedError;
            _isNotFound = PersistedIsNotFound;
            _isLoading = false;
            return;
        }

        await LoadDetailAsync();
        PersistStartupState();
        Initialized = true;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (TeamId == _lastLoadedTeamId && ReturnUrl == _lastReturnUrl)
        {
            return;
        }

        _lastLoadedTeamId = TeamId;
        _lastReturnUrl = ReturnUrl;
        _returnUrl = NormalizeReturnUrl(ReturnUrl);
        ResetTeamScopedState();
        await LoadDetailAsync();
    }

    /// <summary>
    /// Gets the Bootstrap badge CSS class for the current lifecycle status.
    /// </summary>
    protected string LifecycleBadgeClass => _detail?.LifecycleStatus switch
    {
        LifecycleStatus.Archived => "badge text-bg-secondary",
        _ => "badge text-bg-success"
    };

    /// <summary>
    /// Gets the Bootstrap badge CSS class for a given campaign status.
    /// </summary>
    /// <param name="status">The campaign status.</param>
    /// <returns>A Bootstrap badge class string.</returns>
    protected static string CampaignStatusBadgeClass(CampaignStatus status) => status switch
    {
        CampaignStatus.Active => "text-bg-success",
        CampaignStatus.Closed => "text-bg-secondary",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// Groups a flat list of placement-impact rows by campaign, ordered newest first.
    /// </summary>
    /// <param name="placements">The flat list of placement-impact rows from the detail payload.</param>
    /// <returns>An ordered list of campaign groups with their ordered player rows.</returns>
    public static IReadOnlyList<TeamPlacementCampaignGroup> GroupPlacementsByCampaign(
        IReadOnlyList<TeamPlacementImpactDto> placements)
        => [.. placements
            .GroupBy(p => new { p.CampaignId, p.CampaignName, p.CampaignStatus, p.CampaignStartDate })
            .OrderByDescending(g => g.Key.CampaignStartDate)
            .ThenByDescending(g => g.Key.CampaignId)
            .Select(g => new TeamPlacementCampaignGroup(
                g.Key.CampaignId,
                g.Key.CampaignName,
                g.Key.CampaignStatus,
                g.Key.CampaignStartDate,
                [.. g.OrderBy(p => p.PlayerDisplayName, StringComparer.CurrentCulture).ThenBy(p => p.PlayerId)]))];

    /// <summary>
    /// Loads or reloads the team detail payload from the service.
    /// </summary>
    /// <returns>A task that completes when loading and state updates are finished.</returns>
    private async Task LoadDetailAsync()
    {
        var teamToken = _teamScopedCts.Token;
        var version = Interlocked.Increment(ref _loadDetailVersion);
        _isLoading = true;
        _error = null;
        _isNotFound = false;

        ServiceResult<TeamDetailDto> result;
        try
        {
            result = await teamDetailService.GetTeamDetailAsync(TeamId, teamToken);
        }
        catch (OperationCanceledException) when (teamToken.IsCancellationRequested
            || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            if (version != _loadDetailVersion || teamToken.IsCancellationRequested)
            {
                return;
            }

            _error = "Failed to load team details. Please retry.";
            _isLoading = false;
            return;
        }

        if (version != _loadDetailVersion || teamToken.IsCancellationRequested)
        {
            return;
        }

        result.Switch(
            detail => _detail = detail,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                if (problem.Kind == ServiceProblemKind.NotFound)
                {
                    _isNotFound = true;
                    _detail = null;
                    return;
                }

                _error = problem.Detail ?? "Could not load team details.";
            });
        _isLoading = false;
    }

    /// <summary>
    /// Reloads detail data after a user-initiated retry.
    /// </summary>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task RetryLoadAsync() => await LoadDetailAsync();

    /// <summary>
    /// Opens the edit form populated from the currently loaded detail.
    /// </summary>
    private void BeginEdit()
    {
        if (_detail is null)
        {
            return;
        }

        _showArchiveConfirm = false;
        _formError = null;
        _mutationError = null;
        _cutoffBlockers = [];
        _editForm = TeamFormState.FromDetailDto(_detail);
        _showEditForm = true;
    }

    /// <summary>
    /// Saves edits for the team and refreshes detail.
    /// </summary>
    /// <param name="state">The submitted form state.</param>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task UpdateTeamAsync(TeamFormState state)
    {
        var teamToken = _teamScopedCts.Token;
        _isMutating = true;
        _formError = null;
        _cutoffBlockers = [];

        var success = false;
        try
        {
            var result = await teamManagementService.UpdateAsync(state.ToUpdateInput(), teamToken);
            result.Switch(
                _ =>
                {
                    _showEditForm = false;
                    _editForm = null;
                    _statusMessage = "Team updated successfully.";
                    success = true;
                },
                problem =>
                {
                    _formError = problem.Detail ?? "Could not update team.";
                    if (problem.Kind == ServiceProblemKind.Conflict
                        && problem.TryGetGraduationYearBlockers(out var blockers))
                    {
                        _cutoffBlockers = blockers;
                    }
                });
        }
        catch (OperationCanceledException) when (teamToken.IsCancellationRequested
            || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _formError = "Failed to save team. Please retry.";
        }
        finally
        {
            _isMutating = false;
        }

        if (success)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Cancels edit mode and clears form state.
    /// </summary>
    private void CancelEdit()
    {
        _showEditForm = false;
        _editForm = null;
        _formError = null;
        _cutoffBlockers = [];
    }

    /// <summary>
    /// Opens the archive confirmation panel.
    /// </summary>
    private void BeginArchive()
    {
        _showEditForm = false;
        _showArchiveConfirm = true;
        _archiveConfirmed = false;
        _archiveBlockers = [];
        _mutationError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes archive confirmation without mutating data.
    /// </summary>
    private void CancelArchive()
    {
        _showArchiveConfirm = false;
        _archiveConfirmed = false;
        _archiveBlockers = [];
    }

    /// <summary>
    /// Archives the team after explicit user confirmation, then refreshes detail.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (!_archiveConfirmed)
        {
            return;
        }

        var teamToken = _teamScopedCts.Token;
        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var success = false;
        try
        {
            var result = await teamLifecycleService.ArchiveAsync(TeamId, teamToken);
            result.Switch(
                _ =>
                {
                    _statusMessage = "Team archived.";
                    CancelArchive();
                    success = true;
                },
                problem =>
                {
                    _mutationError = problem.Detail ?? "Could not archive team.";
                    if (problem.Kind == ServiceProblemKind.Conflict
                        && problem.TryGetArchiveBlockers(out var blockers))
                    {
                        _archiveBlockers = blockers;
                    }
                });
        }
        catch (OperationCanceledException) when (teamToken.IsCancellationRequested
            || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _mutationError = "Failed to archive team. Please retry.";
        }
        finally
        {
            _isMutating = false;
        }

        if (success)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Restores an archived team, then refreshes detail.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task RestoreTeamAsync()
    {
        var teamToken = _teamScopedCts.Token;
        _isMutating = true;
        _mutationError = null;

        var success = false;
        try
        {
            var result = await teamLifecycleService.RestoreAsync(TeamId, teamToken);
            result.Switch(
                _ =>
                {
                    _statusMessage = "Team restored.";
                    success = true;
                },
                problem => _mutationError = problem.Detail ?? "Could not restore team.");
        }
        catch (OperationCanceledException) when (teamToken.IsCancellationRequested
            || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _mutationError = "Failed to restore team. Please retry.";
        }
        finally
        {
            _isMutating = false;
        }

        if (success)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Persists the current startup detail/error/not-found state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedDetail = _detail;
        PersistedError = _error;
        PersistedIsNotFound = _isNotFound;
    }

    /// <summary>
    /// Resets all team-scoped interaction state before loading a new team via enhanced navigation,
    /// preventing a previously open edit form or archive panel from acting on the newly navigated team.
    /// </summary>
    private void ResetTeamScopedState()
    {
        _teamScopedCts.Cancel();
        _teamScopedCts.Dispose();
        _teamScopedCts = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);

        _detail = null;
        _error = null;
        _isNotFound = false;
        _showEditForm = false;
        _editForm = null;
        _formError = null;
        _cutoffBlockers = [];
        _showArchiveConfirm = false;
        _archiveConfirmed = false;
        _archiveBlockers = [];
        _mutationError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Normalizes the inbound return URL to a safe local path within this application.
    /// </summary>
    /// <param name="returnUrl">The incoming return URL query value.</param>
    /// <returns>A safe local path for the teams back link, defaulting to <c>/teams</c>.</returns>
    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/teams";
        }

        var candidate = returnUrl.Trim();
        if (!Uri.IsWellFormedUriString(candidate, UriKind.Relative)
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith('\\'))
        {
            return "/teams";
        }

        return candidate.StartsWith('/') ? candidate : $"/{candidate}";
    }
}

/// <summary>
/// Represents a campaign group of team placement rows for the placement-history section.
/// </summary>
/// <param name="CampaignId">The campaign identifier.</param>
/// <param name="CampaignName">The campaign display name.</param>
/// <param name="CampaignStatus">The campaign lifecycle status.</param>
/// <param name="CampaignStartDate">The campaign start date.</param>
/// <param name="Placements">The ordered list of placement rows for this campaign.</param>
public sealed record TeamPlacementCampaignGroup(
    long CampaignId,
    string CampaignName,
    CampaignStatus CampaignStatus,
    DateOnly CampaignStartDate,
    IReadOnlyList<TeamPlacementImpactDto> Placements);
