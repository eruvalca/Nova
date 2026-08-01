using System.Globalization;
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

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        _canManageTeams = principal.IsInRole(Roles.Admin) || principal.IsInRole(Roles.ClubAdmin);

        _returnUrl = NormalizeReturnUrl(ReturnUrl);
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
        _isLoading = true;
        _error = null;
        _isNotFound = false;

        var result = await teamDetailService.GetTeamDetailAsync(TeamId, ComponentCancellationToken);
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
        _isMutating = true;
        _formError = null;
        _cutoffBlockers = [];

        var result = await teamManagementService.UpdateAsync(state.ToUpdateInput(), ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _showEditForm = false;
                _editForm = null;
                _statusMessage = "Team updated successfully.";
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

        _isMutating = false;
        if (result.IsSuccess)
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

        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var result = await teamLifecycleService.ArchiveAsync(TeamId, ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _statusMessage = "Team archived.";
                CancelArchive();
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

        _isMutating = false;
        if (result.IsSuccess)
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
        _isMutating = true;
        _mutationError = null;

        var result = await teamLifecycleService.RestoreAsync(TeamId, ComponentCancellationToken);
        result.Switch(
            _ => _statusMessage = "Team restored.",
            problem => _mutationError = problem.Detail ?? "Could not restore team.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadDetailAsync();
        }
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
            || candidate.StartsWith("\\\\", StringComparison.Ordinal))
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
