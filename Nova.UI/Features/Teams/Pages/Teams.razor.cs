using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Components;
using Nova.UI.Features.Teams.Components;

namespace Nova.UI.Features.Teams.Pages;

/// <summary>
/// Renders the team roster workflow with filters and team lifecycle/profile actions.
/// </summary>
/// <param name="teamRosterService">The roster query service.</param>
/// <param name="teamManagementService">The team create/update service.</param>
/// <param name="teamLifecycleService">The team archive/restore service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for redirects.</param>
public partial class Teams(
    ITeamRosterService teamRosterService,
    ITeamManagementService teamManagementService,
    ITeamLifecycleService teamLifecycleService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// The debounce interval for search input updates.
    /// </summary>
    private const int SearchDebounceMilliseconds = 350;

    /// <summary>
    /// The loaded team roster rows, or <see langword="null"/> when unavailable.
    /// </summary>
    private IReadOnlyList<TeamRosterItem>? _roster;

    /// <summary>
    /// The current page-level error message.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// The current create/edit form mutation error message.
    /// </summary>
    private string? _formError;

    /// <summary>
    /// The current non-form lifecycle mutation error message.
    /// </summary>
    private string? _actionError;

    /// <summary>
    /// The current status message shown after successful mutations.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Indicates whether roster data is being loaded.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether a create/edit/archive/restore mutation is in progress.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// Indicates whether the current user can create/edit/archive/restore teams.
    /// </summary>
    private bool _canManageTeams;

    /// <summary>
    /// Stores the current user's club identifier from claims.
    /// </summary>
    private long? _clubId;

    /// <summary>
    /// Draft text from the search input.
    /// </summary>
    private string _searchDraft = string.Empty;

    /// <summary>
    /// Applied search term used in server queries.
    /// </summary>
    private string _searchApplied = string.Empty;

    /// <summary>
    /// The active roster lifecycle-status filter ("active" or "archived").
    /// </summary>
    private string _lifecycleStatusFilter = "active";

    /// <summary>
    /// The selected graduation-year filter.
    /// </summary>
    private int? _graduationYearFilter;

    /// <summary>
    /// The years displayed in the graduation-year dropdown.
    /// </summary>
    private IReadOnlyList<int> _availableGraduationYears = [];

    /// <summary>
    /// A stable set of seen graduation years used to avoid collapsing the year filter options.
    /// </summary>
    private readonly HashSet<int> _knownGraduationYears = [];

    /// <summary>
    /// The create-team input model.
    /// </summary>
    private TeamFormState _createForm = TeamFormState.CreateDefault();

    /// <summary>
    /// The edit-team input model when edit mode is active.
    /// </summary>
    private TeamFormState? _editForm;

    /// <summary>
    /// Indicates whether the create form is currently visible.
    /// </summary>
    private bool _showCreateForm;

    /// <summary>
    /// Structured blockers for graduation-year cutoff conflicts.
    /// </summary>
    private IReadOnlyList<TeamGraduationYearBlockerItem> _cutoffBlockers = [];

    /// <summary>
    /// The currently selected archive target.
    /// </summary>
    private TeamRosterItem? _archiveCandidate;

    /// <summary>
    /// Indicates whether the archive confirmation checkbox is checked.
    /// </summary>
    private bool _archiveConfirmed;

    /// <summary>
    /// Structured blockers returned from archive conflicts.
    /// </summary>
    private IReadOnlyList<TeamArchiveBlocker> _archiveBlockers = [];

    /// <summary>
    /// Debounce source used to cancel stale search requests.
    /// </summary>
    private CancellationTokenSource? _searchDebounceSource;

    /// <summary>
    /// Request source used to cancel stale roster loads.
    /// </summary>
    private CancellationTokenSource? _loadRosterSource;

    /// <summary>
    /// Monotonic request version used to ignore stale roster results.
    /// </summary>
    private long _loadRosterVersion;

    /// <summary>
    /// Gets or sets the persisted startup roster snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TeamRosterItem>? PersistedRoster { get; set; }

    /// <summary>
    /// Gets or sets the club identifier the persisted roster snapshot was sourced from.
    /// </summary>
    [PersistentState]
    public long? PersistedClubId { get; set; }

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
    /// Gets or sets the incoming lifecycle view query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "view")]
    private string? ViewQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming search query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "search")]
    private string? SearchQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming graduation-year query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "graduationYear")]
    private string? GraduationYearQuery { get; set; }

    /// <summary>
    /// Gets the selected graduation-year filter as a string for select binding.
    /// </summary>
    protected string GraduationYearFilterText => _graduationYearFilter?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var filtersChanged = ApplyQueryFiltersToState();
        if (filtersChanged && Initialized && _clubId is not null)
        {
            await LoadRosterAsync();
        }
    }

    protected override void OnInitialized()
        => authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        _ = ApplyQueryFiltersToState();

        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authenticationState.User;

        _canManageTeams = principal.IsInRole(Roles.ClubAdmin);
        _clubId = ReadClubIdClaim(principal);

        if (Initialized)
        {
            // Only restore the prerendered snapshot when it belongs to the still-current club.
            // On an interactive attach after a club change, reload against the new scope
            // instead of surfacing the previous club's roster.
            if (PersistedClubId is not null && PersistedClubId == _clubId)
            {
                _roster = PersistedRoster;
                _pageError = PersistedPageError;
                RefreshAvailableGraduationYears(_roster ?? []);

                _isLoading = false;
                return;
            }

            Initialized = false;
        }

        _isLoading = true;
        if (_clubId is null)
        {
            _pageError = "You must join a club before viewing the team roster.";
            PersistStartupState();
            Initialized = true;
            _isLoading = false;
            return;
        }

        await LoadRosterAsync();
        PersistStartupState();
        Initialized = true;
    }

    /// <summary>
    /// Applies normalized query-string filters to component state.
    /// </summary>
    /// <returns><see langword="true"/> when one or more filter values changed; otherwise <see langword="false"/>.</returns>
    private bool ApplyQueryFiltersToState()
    {
        var lifecycleFromQuery = NormalizeLifecycleStatus(ViewQuery);
        var searchFromQuery = (SearchQuery ?? string.Empty).Trim();
        if (searchFromQuery.Length > 200)
        {
            searchFromQuery = string.Empty;
        }
        var graduationYearFromQuery = ParseGraduationYearQuery(GraduationYearQuery);

        var hasChanged = !string.Equals(_lifecycleStatusFilter, lifecycleFromQuery, StringComparison.Ordinal)
            || !string.Equals(_searchDraft, searchFromQuery, StringComparison.Ordinal)
            || _graduationYearFilter != graduationYearFromQuery;

        _lifecycleStatusFilter = lifecycleFromQuery;
        _searchDraft = searchFromQuery;
        _searchApplied = searchFromQuery;
        _graduationYearFilter = graduationYearFromQuery;
        if (_graduationYearFilter is int year)
        {
            _knownGraduationYears.Add(year);
        }

        return hasChanged;
    }

    /// <summary>
    /// Parses and normalizes the lifecycle query value.
    /// </summary>
    /// <param name="lifecycleQuery">The incoming lifecycle query value.</param>
    /// <returns><c>archived</c> or <c>active</c>.</returns>
    private static string NormalizeLifecycleStatus(string? lifecycleQuery)
        => string.Equals(lifecycleQuery, "archived", StringComparison.OrdinalIgnoreCase)
            ? "archived"
            : "active";

    /// <summary>
    /// Parses and validates the graduation-year query value.
    /// </summary>
    /// <param name="graduationYearQuery">The raw graduation-year query value.</param>
    /// <returns>A valid graduation year, or <see langword="null"/> when invalid/out of range.</returns>
    private static int? ParseGraduationYearQuery(string? graduationYearQuery)
    {
        if (!int.TryParse(graduationYearQuery, NumberStyles.Integer, CultureInfo.InvariantCulture, out var graduationYear))
        {
            return null;
        }

        return graduationYear is >= 2000 and <= 2100
            ? graduationYear
            : null;
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
    /// Reloads the roster using the currently selected filters.
    /// </summary>
    /// <returns>A task that completes when loading and state updates are finished.</returns>
    private async Task LoadRosterAsync()
    {
        if (_clubId is null)
        {
            return;
        }

        var version = Interlocked.Increment(ref _loadRosterVersion);
        _loadRosterSource?.Cancel();
        _loadRosterSource?.Dispose();
        _loadRosterSource = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        var requestToken = _loadRosterSource.Token;

        _isLoading = true;
        _pageError = null;

        var input = new GetTeamRosterInput
        {
            Search = _searchApplied,
            LifecycleStatus = _lifecycleStatusFilter,
            GraduationYear = _graduationYearFilter
        };

        ServiceResult<IReadOnlyList<TeamRosterItem>> result;
        try
        {
            result = await teamRosterService.GetRosterAsync(input, requestToken);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (version != _loadRosterVersion || requestToken.IsCancellationRequested)
            {
                return;
            }

            _pageError = "Failed to load teams. Please retry.";
            _roster = null;
            PersistStartupState();
            _isLoading = false;
            return;
        }

        if (version != _loadRosterVersion || requestToken.IsCancellationRequested)
        {
            return;
        }

        result.Switch(
            roster =>
            {
                _roster = roster;
                RefreshAvailableGraduationYears(roster);
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _pageError = problem.Detail ?? "Failed to load teams. Please retry.";
                _roster = null;
            });

        PersistStartupState();
        _isLoading = false;
    }

    /// <summary>
    /// Persists the current startup roster/error state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedRoster = _roster;
        PersistedPageError = _pageError;
        PersistedClubId = _clubId;
    }

    /// <summary>
    /// Refreshes graduation-year filter options from known and currently loaded roster rows.
    /// </summary>
    /// <param name="items">The loaded roster rows.</param>
    private void RefreshAvailableGraduationYears(IReadOnlyList<TeamRosterItem> items)
    {
        foreach (var year in items.Select(team => team.GraduationYear))
        {
            _knownGraduationYears.Add(year);
        }

        if (_graduationYearFilter is int selectedYear)
        {
            _knownGraduationYears.Add(selectedYear);
        }

        _availableGraduationYears = _knownGraduationYears
            .OrderBy(year => year)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Synchronizes active filter values into the URL query string.
    /// </summary>
    private void SyncFiltersToUrl()
    {
        var uri = navigationManager.GetUriWithQueryParameters(
            new Dictionary<string, object?>
            {
                ["view"] = _lifecycleStatusFilter,
                ["search"] = string.IsNullOrWhiteSpace(_searchApplied) ? null : _searchApplied,
                ["graduationYear"] = _graduationYearFilter
            });

        if (!string.Equals(uri, navigationManager.Uri, StringComparison.Ordinal))
        {
            navigationManager.NavigateTo(uri, new NavigationOptions { ReplaceHistoryEntry = true });
        }
    }

    /// <summary>
    /// Reloads roster data after a user-initiated retry action.
    /// </summary>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task ReloadAsync() => await LoadRosterAsync();

    /// <summary>
    /// Applies a debounced search term update and reloads the roster.
    /// </summary>
    /// <param name="args">The input event payload.</param>
    /// <returns>A task that completes when the debounce and reload flow finishes.</returns>
    private async Task OnSearchInputChangedAsync(ChangeEventArgs args)
    {
        _searchDraft = args.Value?.ToString() ?? string.Empty;

        _searchDebounceSource?.Cancel();
        _searchDebounceSource?.Dispose();
        _searchDebounceSource = new CancellationTokenSource();
        var debounceToken = _searchDebounceSource.Token;

        try
        {
            await Task.Delay(SearchDebounceMilliseconds, debounceToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _searchApplied = _searchDraft.Trim();
        _statusMessage = null;
        _actionError = null;
        SyncFiltersToUrl();
        await LoadRosterAsync();
    }

    /// <summary>
    /// Applies a lifecycle-status filter change and reloads the roster.
    /// </summary>
    /// <param name="args">The select-change payload.</param>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task OnLifecycleStatusChangedAsync(ChangeEventArgs args)
    {
        _lifecycleStatusFilter = NormalizeLifecycleStatus(args.Value?.ToString());
        PromoteSearchDraftToApplied();
        _statusMessage = null;
        _actionError = null;
        SyncFiltersToUrl();
        await LoadRosterAsync();
    }

    /// <summary>
    /// Applies a graduation-year filter change and reloads the roster.
    /// </summary>
    /// <param name="args">The select-change payload.</param>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task OnGraduationYearChangedAsync(ChangeEventArgs args)
    {
        var raw = args.Value?.ToString();
        _graduationYearFilter = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedYear)
            ? parsedYear
            : null;

        if (_graduationYearFilter is int year)
        {
            _knownGraduationYears.Add(year);
        }

        PromoteSearchDraftToApplied();
        _statusMessage = null;
        _actionError = null;
        SyncFiltersToUrl();
        await LoadRosterAsync();
    }

    /// <summary>
    /// Applies the current search draft immediately and cancels any pending debounce.
    /// </summary>
    private void PromoteSearchDraftToApplied()
    {
        _searchDebounceSource?.Cancel();
        _searchDebounceSource?.Dispose();
        _searchDebounceSource = null;
        _searchApplied = _searchDraft.Trim();
    }

    /// <summary>
    /// Shows the create-team form and clears mutation messages.
    /// </summary>
    private void ShowCreateForm()
    {
        if (_isMutating)
        {
            return;
        }

        ClearArchiveState();
        _createForm = TeamFormState.CreateDefault();
        _showCreateForm = true;
        _editForm = null;
        _statusMessage = null;
        _formError = null;
        _actionError = null;
        _cutoffBlockers = [];
    }

    /// <summary>
    /// Starts edit mode for the selected roster row.
    /// </summary>
    /// <param name="team">The selected roster team.</param>
    private void BeginEdit(TeamRosterItem team)
    {
        if (_isMutating)
        {
            return;
        }

        ClearArchiveState();
        _showCreateForm = false;
        _statusMessage = null;
        _formError = null;
        _actionError = null;
        _cutoffBlockers = [];
        _editForm = TeamFormState.FromRosterItem(team);
    }

    /// <summary>
    /// Cancels create/edit mode and clears mutation state.
    /// </summary>
    private void CancelMutationForm()
    {
        if (_isMutating)
        {
            return;
        }

        _showCreateForm = false;
        _editForm = null;
        _formError = null;
        _cutoffBlockers = [];
    }

    /// <summary>
    /// Creates a new team and refreshes the roster.
    /// </summary>
    /// <param name="formState">The validated team form state.</param>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task CreateTeamAsync(TeamFormState formState)
    {
        _isMutating = true;
        _formError = null;
        _actionError = null;
        _cutoffBlockers = [];

        var shouldReloadRoster = false;
        try
        {
            var result = await teamManagementService.CreateAsync(formState.ToCreateInput(), ComponentCancellationToken);
            result.Switch(
                _ =>
                {
                    _showCreateForm = false;
                    _createForm = TeamFormState.CreateDefault();
                    _statusMessage = "Team created successfully.";
                },
                problem => _formError = problem.Detail ?? "Could not create team.");
            shouldReloadRoster = result.IsSuccess;
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _formError = "Could not create team. Please retry.";
            return;
        }
        finally
        {
            _isMutating = false;
        }

        if (shouldReloadRoster)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Saves edits for an existing team and refreshes the roster.
    /// </summary>
    /// <param name="formState">The validated edit form state.</param>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task UpdateTeamAsync(TeamFormState formState)
    {
        _isMutating = true;
        _formError = null;
        _actionError = null;
        _cutoffBlockers = [];

        var shouldReloadRoster = false;
        try
        {
            var result = await teamManagementService.UpdateAsync(formState.ToUpdateInput(), ComponentCancellationToken);
            result.Switch(
                _ =>
                {
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
            shouldReloadRoster = result.IsSuccess;
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _formError = "Could not update team. Please retry.";
            return;
        }
        finally
        {
            _isMutating = false;
        }

        if (shouldReloadRoster)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Sets the archive target and opens archive confirmation state.
    /// </summary>
    /// <param name="team">The selected team.</param>
    private void BeginArchive(TeamRosterItem team)
    {
        if (_isMutating)
        {
            return;
        }

        _showCreateForm = false;
        _editForm = null;
        _cutoffBlockers = [];
        _archiveCandidate = team;
        _archiveConfirmed = false;
        _archiveBlockers = [];
        _formError = null;
        _actionError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes archive confirmation state without mutating data.
    /// </summary>
    private void CancelArchive()
    {
        if (_isMutating)
        {
            return;
        }

        ClearArchiveState();
    }

    /// <summary>
    /// Clears archive confirmation state.
    /// </summary>
    private void ClearArchiveState()
    {
        _archiveCandidate = null;
        _archiveConfirmed = false;
        _archiveBlockers = [];
        _actionError = null;
    }

    /// <summary>
    /// Archives the currently selected team after explicit user confirmation.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (_archiveCandidate is null || !_archiveConfirmed || _isMutating)
        {
            return;
        }

        _isMutating = true;
        _actionError = null;
        _archiveBlockers = [];

        var shouldReloadRoster = false;
        try
        {
            var result = await teamLifecycleService.ArchiveAsync(_archiveCandidate.TeamId, ComponentCancellationToken);
            result.Switch(
                _ =>
                {
                    _statusMessage = "Team archived.";
                    ClearArchiveState();
                },
                problem =>
                {
                    _actionError = problem.Detail ?? "Could not archive team.";
                    if (problem.Kind == ServiceProblemKind.Conflict
                        && problem.TryGetArchiveBlockers(out var blockers))
                    {
                        _archiveBlockers = blockers;
                    }
                });
            shouldReloadRoster = result.IsSuccess;
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _actionError = "Could not archive team. Please retry.";
            return;
        }
        finally
        {
            _isMutating = false;
        }

        if (shouldReloadRoster)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Restores an archived team and refreshes the roster.
    /// </summary>
    /// <param name="team">The archived team to restore.</param>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task RestoreTeamAsync(TeamRosterItem team)
    {
        if (_isMutating)
        {
            return;
        }

        _isMutating = true;
        _statusMessage = null;
        _actionError = null;
        var shouldReloadRoster = false;
        try
        {
            var result = await teamLifecycleService.RestoreAsync(team.TeamId, ComponentCancellationToken);
            result.Switch(
                _ => _statusMessage = "Team restored.",
                problem => _actionError = problem.Detail ?? "Could not restore team.");
            shouldReloadRoster = result.IsSuccess;
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _actionError = "Could not restore team. Please retry.";
            return;
        }
        finally
        {
            _isMutating = false;
        }

        if (shouldReloadRoster)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Builds the team-detail URL while preserving current roster filter context as a return URL.
    /// </summary>
    /// <param name="teamId">The target team identifier.</param>
    /// <returns>A relative team-detail URL with an encoded return URL query parameter.</returns>
    private string BuildTeamDetailUrl(long teamId)
        => $"{ClubRoutes.TeamDetail(teamId)}?returnUrl={Uri.EscapeDataString(BuildCurrentRosterUrl())}";

    /// <summary>
    /// Builds the current roster URL with active filter state for use as a return URL.
    /// </summary>
    /// <returns>The roster URL with active query-string filter values.</returns>
    private string BuildCurrentRosterUrl()
    {
        var querySegments = new List<string>
        {
            $"view={Uri.EscapeDataString(_lifecycleStatusFilter)}"
        };

        if (!string.IsNullOrWhiteSpace(_searchApplied))
        {
            querySegments.Add($"search={Uri.EscapeDataString(_searchApplied)}");
        }

        if (_graduationYearFilter is not null)
        {
            querySegments.Add($"graduationYear={_graduationYearFilter.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return $"{ClubRoutes.Teams}?{string.Join("&", querySegments)}";
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = ApplyAuthenticationStateAsync(stateTask);

    private async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> stateTask)
    {
        var authState = await stateTask;
        var principal = authState.User;
        var canManageTeams = principal.IsInRole(Roles.ClubAdmin);
        var clubId = ReadClubIdClaim(principal);

        var roleChanged = canManageTeams != _canManageTeams;
        var clubChanged = clubId != _clubId;

        _canManageTeams = canManageTeams;
        _clubId = clubId;

        if (clubChanged)
        {
            ResetManagementState();
            ResetRosterState();

            if (_clubId is null)
            {
                _pageError = "You must join a club before viewing the team roster.";
                PersistStartupState();
            }
            else
            {
                // AuthenticationStateChanged is an external event, so render the loading
                // state before the reload's first await to avoid showing the old club's roster.
                _isLoading = true;
                await InvokeAsync(StateHasChanged);
                await LoadRosterAsync();
            }

            await InvokeAsync(StateHasChanged);
            return;
        }

        if (roleChanged)
        {
            if (!canManageTeams)
            {
                ResetManagementState();
            }

            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Closes every open management interaction (create/edit/archive panel and mutation
    /// feedback) without touching the loaded roster.
    /// </summary>
    private void ResetManagementState()
    {
        _showCreateForm = false;
        _editForm = null;
        _createForm = TeamFormState.CreateDefault();
        _formError = null;
        _actionError = null;
        _statusMessage = null;
        _cutoffBlockers = [];
        _archiveCandidate = null;
        _archiveConfirmed = false;
        _archiveBlockers = [];
    }

    /// <summary>
    /// Cancels any in-flight roster load and clears the currently displayed roster/error state,
    /// including club-scoped graduation-year options inherited from the previous club.
    /// </summary>
    private void ResetRosterState()
    {
        _loadRosterSource?.Cancel();
        _loadRosterSource?.Dispose();
        _loadRosterSource = null;
        _loadRosterVersion++;
        _roster = null;
        _pageError = null;
        _isLoading = false;
        _knownGraduationYears.Clear();
        _availableGraduationYears = [];
    }

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        _searchDebounceSource?.Cancel();
        _searchDebounceSource?.Dispose();
        _searchDebounceSource = null;
        _loadRosterSource?.Cancel();
        _loadRosterSource?.Dispose();
        _loadRosterSource = null;
        return ValueTask.CompletedTask;
    }
}
