using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Features.Players;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Components;
using Nova.UI.Features.Players.Components;

namespace Nova.UI.Features.Players.Pages;

/// <summary>
/// Renders the player roster workflow with filters and player lifecycle/profile actions.
/// </summary>
/// <param name="playerService">The roster query service.</param>
/// <param name="playerManagementService">The player create/update service.</param>
/// <param name="playerLifecycleService">The player archive/restore service.</param>
/// <param name="playerDetailService">The player detail query service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for redirects and links.</param>
public partial class Players(
    IPlayerService playerService,
    IPlayerManagementService playerManagementService,
    IPlayerLifecycleService playerLifecycleService,
    IPlayerDetailService playerDetailService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// The debounce interval for search input updates.
    /// </summary>
    private const int SearchDebounceMilliseconds = 350;

    /// <summary>
    /// The loaded roster page, or <see langword="null"/> when unavailable.
    /// </summary>
    private PagedResult<PlayerListItem>? _roster;

    /// <summary>
    /// The current page-level error message.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// The current mutation-level error message.
    /// </summary>
    private string? _mutationError;

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
    /// Indicates whether the current user can create/edit/archive/restore players.
    /// </summary>
    private bool _canManagePlayers;

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
    /// The selected player-tag filter.
    /// </summary>
    private long? _playerTagFilter;

    /// <summary>
    /// The years displayed in the graduation-year dropdown.
    /// </summary>
    private IReadOnlyList<int> _availableGraduationYears = [];

    /// <summary>
    /// The tags displayed in the tag filter dropdown.
    /// </summary>
    private IReadOnlyList<PlayerRosterTagItem> _availableTags = [];

    /// <summary>
    /// The create-player input model.
    /// </summary>
    private PlayerFormState _createForm = PlayerFormState.CreateDefault();

    /// <summary>
    /// The edit-player input model when edit mode is active.
    /// </summary>
    private PlayerFormState? _editForm;

    /// <summary>
    /// Indicates whether the create form is currently visible.
    /// </summary>
    private bool _showCreateForm;

    /// <summary>
    /// Structured blockers for graduation-year conflicts.
    /// </summary>
    private IReadOnlyList<GraduationYearBlockerItem> _graduationYearBlockers = [];

    /// <summary>
    /// The currently selected archive target.
    /// </summary>
    private PlayerListItem? _archiveCandidate;

    /// <summary>
    /// Indicates whether the archive confirmation checkbox is checked.
    /// </summary>
    private bool _archiveConfirmed;

    /// <summary>
    /// Structured blockers returned from archive conflicts.
    /// </summary>
    private IReadOnlyList<PlayerArchiveBlocker> _archiveBlockers = [];

    /// <summary>
    /// Debounce source used to cancel stale search requests.
    /// </summary>
    private CancellationTokenSource? _searchDebounceSource;

    /// <summary>
    /// Indicates whether query-string filters have been applied to component state.
    /// </summary>
    private bool _queryFiltersApplied;

    /// <summary>
    /// Gets or sets the persisted startup roster snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public PagedResult<PlayerListItem>? PersistedRoster { get; set; }

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
    private int? GraduationYearQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming tag query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "tag")]
    private long? TagQuery { get; set; }

    /// <summary>
    /// Gets the selected graduation-year filter as a string for select binding.
    /// </summary>
    protected string _graduationYearFilterText => _graduationYearFilter?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Gets the selected tag filter as a string for select binding.
    /// </summary>
    protected string _playerTagFilterText => _playerTagFilter?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (_queryFiltersApplied)
        {
            return;
        }

        _queryFiltersApplied = true;
        _lifecycleStatusFilter = string.Equals(ViewQuery, "archived", StringComparison.OrdinalIgnoreCase)
            ? "archived"
            : "active";
        _searchDraft = SearchQuery ?? string.Empty;
        _searchApplied = _searchDraft;
        _graduationYearFilter = GraduationYearQuery;
        _playerTagFilter = TagQuery;
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authenticationState.User;

        _canManagePlayers = principal.IsInRole(Roles.Admin) || principal.IsInRole(Roles.ClubAdmin);
        _clubId = ReadClubIdClaim(principal);

        if (Initialized)
        {
            _roster = PersistedRoster;
            _pageError = PersistedPageError;
            _isLoading = false;
            return;
        }

        _isLoading = true;
        if (_clubId is null)
        {
            _pageError = "You must join a club before viewing the player roster.";
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

        _isLoading = true;
        _pageError = null;
        _statusMessage = null;

        var input = new GetPlayerRosterInput
        {
            ClubId = _clubId.Value,
            Search = _searchApplied,
            LifecycleStatus = _lifecycleStatusFilter,
            GraduationYear = _graduationYearFilter,
            PlayerTagId = _playerTagFilter
        };

        var result = await playerService.GetPlayerRosterAsync(input, ComponentCancellationToken);
        result.Switch(
            roster =>
            {
                _roster = roster;
                RefreshAvailableFilters(roster.Items);
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _pageError = problem.Detail ?? "Failed to load players. Please retry.";
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
    }

    /// <summary>
    /// Refreshes graduation-year and tag filter options from the currently loaded roster rows.
    /// </summary>
    /// <param name="items">The loaded roster rows.</param>
    private void RefreshAvailableFilters(IReadOnlyList<PlayerListItem> items)
    {
        _availableGraduationYears = items
            .Select(player => player.GraduationYear)
            .Distinct()
            .OrderBy(year => year)
            .ToList()
            .AsReadOnly();

        _availableTags = items
            .SelectMany(player => player.CurrentTags)
            .GroupBy(tag => new { tag.PlayerTagId, tag.Name, tag.Color })
            .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
            .ThenBy(group => group.Key.PlayerTagId)
            .Select(group => new PlayerRosterTagItem(group.Key.PlayerTagId, group.Key.Name, group.Key.Color))
            .ToList()
            .AsReadOnly();
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

        _searchApplied = _searchDraft;
        await LoadRosterAsync();
    }

    /// <summary>
    /// Applies a lifecycle-status filter change and reloads the roster.
    /// </summary>
    /// <param name="args">The select-change payload.</param>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task OnLifecycleStatusChangedAsync(ChangeEventArgs args)
    {
        _lifecycleStatusFilter = args.Value?.ToString() ?? "active";
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
        await LoadRosterAsync();
    }

    /// <summary>
    /// Applies a tag filter change and reloads the roster.
    /// </summary>
    /// <param name="args">The select-change payload.</param>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task OnTagFilterChangedAsync(ChangeEventArgs args)
    {
        var raw = args.Value?.ToString();
        _playerTagFilter = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTagId)
            ? parsedTagId
            : null;
        await LoadRosterAsync();
    }

    /// <summary>
    /// Shows the create-player form and clears mutation messages.
    /// </summary>
    private void ShowCreateForm()
    {
        _showCreateForm = true;
        _editForm = null;
        _mutationError = null;
        _graduationYearBlockers = [];
    }

    /// <summary>
    /// Cancels create/edit mode and clears mutation state.
    /// </summary>
    private void CancelMutationForm()
    {
        _showCreateForm = false;
        _editForm = null;
        _mutationError = null;
        _graduationYearBlockers = [];
    }

    /// <summary>
    /// Opens edit mode by loading complete player profile data.
    /// </summary>
    /// <param name="player">The selected roster player.</param>
    /// <returns>A task that completes when the edit model is populated.</returns>
    private async Task BeginEditAsync(PlayerListItem player)
    {
        _showCreateForm = false;
        _mutationError = null;
        _graduationYearBlockers = [];
        _isMutating = true;

        var result = await playerDetailService.GetPlayerDetailAsync(player.PlayerId, ComponentCancellationToken);
        result.Switch(
            detail =>
            {
                _editForm = PlayerFormState.FromDetail(detail);
            },
            problem => _mutationError = problem.Detail ?? "Could not load player details for editing.");

        _isMutating = false;
    }

    /// <summary>
    /// Creates a new player and refreshes the roster.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task CreatePlayerAsync()
    {
        _isMutating = true;
        _mutationError = null;
        _graduationYearBlockers = [];

        var result = await playerManagementService.CreateAsync(_createForm.ToCreateInput(), ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _showCreateForm = false;
                _createForm = PlayerFormState.CreateDefault();
                _statusMessage = "Player created successfully.";
            },
            problem => _mutationError = problem.Detail ?? "Could not create player.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Saves edits for an existing player and refreshes the roster.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task UpdatePlayerAsync()
    {
        if (_editForm is null)
        {
            return;
        }

        _isMutating = true;
        _mutationError = null;
        _graduationYearBlockers = [];

        var result = await playerManagementService.UpdateAsync(_editForm.ToUpdateInput(), ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _editForm = null;
                _statusMessage = "Player updated successfully.";
            },
            problem =>
            {
                _mutationError = problem.Detail ?? "Could not update player.";
                if (problem.Kind == ServiceProblemKind.Conflict)
                {
                    _graduationYearBlockers = ExtractGraduationYearBlockers(problem.Errors);
                }
            });

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Sets the archive target and opens archive confirmation state.
    /// </summary>
    /// <param name="player">The selected player.</param>
    private void BeginArchive(PlayerListItem player)
    {
        _archiveCandidate = player;
        _archiveConfirmed = false;
        _archiveBlockers = [];
        _mutationError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes archive confirmation state without mutating data.
    /// </summary>
    private void CancelArchive()
    {
        _archiveCandidate = null;
        _archiveConfirmed = false;
        _archiveBlockers = [];
    }

    /// <summary>
    /// Archives the currently selected player after explicit user confirmation.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (_archiveCandidate is null || !_archiveConfirmed)
        {
            return;
        }

        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var result = await playerLifecycleService.ArchiveAsync(_archiveCandidate.PlayerId, ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _statusMessage = "Player archived.";
                CancelArchive();
            },
            problem =>
            {
                _mutationError = problem.Detail ?? "Could not archive player.";
                if (problem.Kind == ServiceProblemKind.Conflict
                    && problem.TryGetArchiveBlockers(out var blockers))
                {
                    _archiveBlockers = blockers;
                }
            });

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Restores an archived player and refreshes the roster.
    /// </summary>
    /// <param name="player">The archived player to restore.</param>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task RestorePlayerAsync(PlayerListItem player)
    {
        _isMutating = true;
        _mutationError = null;

        var result = await playerLifecycleService.RestoreAsync(player.PlayerId, ComponentCancellationToken);
        result.Switch(
            _ => _statusMessage = "Player restored. Missed campaign enrollment is not backfilled automatically.",
            problem => _mutationError = problem.Detail ?? "Could not restore player.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Builds the player-detail URL while preserving current roster filter context.
    /// </summary>
    /// <param name="playerId">The target player identifier.</param>
    /// <returns>A relative player-detail URL.</returns>
    private string BuildPlayerDetailUrl(long playerId)
        => $"/players/{playerId}?returnUrl={Uri.EscapeDataString(BuildCurrentRosterUrl())}";

    /// <summary>
    /// Builds the current roster URL with active filter state.
    /// </summary>
    /// <returns>The roster URL with query-string filter values.</returns>
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

        if (_playerTagFilter is not null)
        {
            querySegments.Add($"tag={_playerTagFilter.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return $"/players?{string.Join('&', querySegments)}";
    }

    /// <summary>
    /// Builds the inline CSS style string for one roster tag pill.
    /// </summary>
    /// <param name="tag">The tag to style.</param>
    /// <returns>An inline CSS style string.</returns>
    private static string BuildTagStyle(PlayerRosterTagItem tag)
        => $"background-color: {tag.Color}; color: #ffffff;";

    /// <summary>
    /// Extracts structured graduation-year blockers from a conflict error payload.
    /// </summary>
    /// <param name="errors">The service-problem errors dictionary.</param>
    /// <returns>A parsed list of blocker items, or an empty list when unavailable.</returns>
    private static IReadOnlyList<GraduationYearBlockerItem> ExtractGraduationYearBlockers(
        IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return [];
        }

        var blockers = new Dictionary<int, GraduationYearBlockerBuilder>();
        foreach (var (key, values) in errors)
        {
            if (values.Length == 0 || !TryParseBlockerKey(key, out var index, out var fieldName))
            {
                continue;
            }

            if (!blockers.TryGetValue(index, out var builder))
            {
                builder = new GraduationYearBlockerBuilder();
                blockers[index] = builder;
            }

            var value = values[0];
            switch (fieldName)
            {
                case "assignmentId":
                    builder.PlayerCampaignAssignmentId = TryParseLong(value);
                    break;
                case "campaignId":
                    builder.CampaignId = TryParseLong(value);
                    break;
                case "teamId":
                    builder.TeamId = TryParseLong(value);
                    break;
                case "teamGraduationYear":
                    builder.TeamGraduationYear = TryParseInt(value);
                    break;
            }
        }

        return blockers
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .Where(builder =>
                builder.PlayerCampaignAssignmentId is not null
                && builder.CampaignId is not null
                && builder.TeamId is not null
                && builder.TeamGraduationYear is not null)
            .Select(builder => new GraduationYearBlockerItem
            {
                PlayerCampaignAssignmentId = builder.PlayerCampaignAssignmentId!.Value,
                CampaignId = builder.CampaignId!.Value,
                TeamId = builder.TeamId!.Value,
                TeamGraduationYear = builder.TeamGraduationYear!.Value
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Parses one blocker payload key in the format <c>blockers[{index}].{field}</c>.
    /// </summary>
    /// <param name="key">The input key.</param>
    /// <param name="index">The parsed blocker index.</param>
    /// <param name="fieldName">The parsed field name.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    private static bool TryParseBlockerKey(string key, out int index, out string fieldName)
    {
        index = default;
        fieldName = string.Empty;

        if (!key.StartsWith("blockers[", StringComparison.Ordinal))
        {
            return false;
        }

        var closeBracketIndex = key.IndexOf(']');
        var dotIndex = key.IndexOf('.', closeBracketIndex + 1);
        if (closeBracketIndex <= "blockers[".Length || dotIndex < 0)
        {
            return false;
        }

        var indexText = key["blockers[".Length..closeBracketIndex];
        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        fieldName = key[(dotIndex + 1)..];
        return fieldName.Length > 0;
    }

    /// <summary>
    /// Parses a long using invariant culture.
    /// </summary>
    /// <param name="value">The incoming number text.</param>
    /// <returns>The parsed long value, or <see langword="null"/> when parsing fails.</returns>
    private static long? TryParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Parses an int using invariant culture.
    /// </summary>
    /// <param name="value">The incoming number text.</param>
    /// <returns>The parsed int value, or <see langword="null"/> when parsing fails.</returns>
    private static int? TryParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        _searchDebounceSource?.Cancel();
        _searchDebounceSource?.Dispose();
        _searchDebounceSource = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Stores one partially parsed graduation-year blocker row.
    /// </summary>
    private sealed class GraduationYearBlockerBuilder
    {
        /// <summary>
        /// Gets or sets the participation identifier.
        /// </summary>
        public long? PlayerCampaignAssignmentId { get; set; }

        /// <summary>
        /// Gets or sets the campaign identifier.
        /// </summary>
        public long? CampaignId { get; set; }

        /// <summary>
        /// Gets or sets the team identifier.
        /// </summary>
        public long? TeamId { get; set; }

        /// <summary>
        /// Gets or sets the team graduation-year requirement.
        /// </summary>
        public int? TeamGraduationYear { get; set; }
    }
}
