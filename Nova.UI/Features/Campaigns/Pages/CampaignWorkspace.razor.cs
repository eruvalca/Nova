using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Tags;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>
/// Renders the campaign workspace: header, tab bar, and the filterable evaluate roster region.
/// </summary>
/// <param name="campaignQueryService">The campaign detail query service.</param>
/// <param name="participantQueryService">The campaign roster query service.</param>
/// <param name="tagDefinitionQueryService">The tag-definition choices service used by roster filters.</param>
/// <param name="teamRosterService">The team choices service used by roster filters.</param>
/// <param name="navigationManager">The navigation manager used for URL history and redirects.</param>
/// <param name="jsRuntime">The JavaScript runtime used to import the collocated workspace module.</param>
public partial class CampaignWorkspace(
    ICampaignQueryService campaignQueryService,
    ICampaignParticipantQueryService participantQueryService,
    ITagDefinitionQueryService tagDefinitionQueryService,
    ITeamRosterService teamRosterService,
    NavigationManager navigationManager,
    IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>
    /// The debounce interval for search input updates.
    /// </summary>
    private const int SearchDebounceMilliseconds = 350;

    /// <summary>
    /// The fixed roster page size requested by this UI.
    /// </summary>
    private const int RosterPageSize = GetCampaignParticipantRosterInput.DefaultPageSize;

    /// <summary>
    /// The name of the evaluate tab, the only functional workspace section in this phase.
    /// </summary>
    private const string EvaluateTabName = CampaignWorkspaceUrlState.EvaluateTab;

    /// <summary>
    /// The scrollable roster results region used for scroll anchoring and keyboard activation suppression.
    /// </summary>
    private ElementReference _rosterScrollRegion;

    /// <summary>
    /// The tag-definition choices service used by roster filters.
    /// </summary>
    private readonly ITagDefinitionQueryService _tagDefinitionQueryService = tagDefinitionQueryService;

    /// <summary>
    /// The team choices service used by roster filters.
    /// </summary>
    private readonly ITeamRosterService _teamRosterService = teamRosterService;

    /// <summary>
    /// The lazily imported collocated workspace module used for scroll and keyboard interactions.
    /// </summary>
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime
        .InvokeAsync<IJSObjectReference>(
            "import", "./_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js")
        .AsTask());

    /// <summary>
    /// Gets or sets the campaign identifier from the route.
    /// </summary>
    [Parameter]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the incoming tab query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "tab")]
    private string? TabQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming search query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "search")]
    private string? SearchQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming graduation-years query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "graduationYears")]
    private string? GraduationYearsQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming tag-identifiers query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "tagIds")]
    private string? TagIdsQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming outcome query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "outcome")]
    private string? OutcomeQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming team-identifier query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "teamId")]
    private long? TeamIdQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming sort-field query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "sortBy")]
    private string? SortByQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming sort-direction query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "sortDirection")]
    private string? SortDirectionQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming page query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "page")]
    private int? PageQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming participant query parameter; present means the drawer is open.
    /// </summary>
    [SupplyParameterFromQuery(Name = "participant")]
    private string? ParticipantQuery { get; set; }

    /// <summary>
    /// Gets or sets the persisted campaign detail used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignDetailResult? PersistedDetail { get; set; }

    /// <summary>
    /// Gets or sets the persisted page error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedPageError { get; set; }

    /// <summary>
    /// Gets or sets the persisted not-found flag used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public bool PersistedNotFound { get; set; }

    /// <summary>
    /// Gets or sets the persisted roster snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public PagedResult<CampaignParticipantRosterItem>? PersistedRoster { get; set; }

    /// <summary>
    /// Gets or sets the persisted roster error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedRosterError { get; set; }

    /// <summary>
    /// Gets or sets the persisted graduation-year choices used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<int>? PersistedGraduationYears { get; set; }

    /// <summary>
    /// Gets or sets the persisted tag choices used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TagDefinitionDto>? PersistedTags { get; set; }

    /// <summary>
    /// Gets or sets the persisted team choices used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TeamRosterItem>? PersistedTeams { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// The loaded campaign detail, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignDetailResult? _detail;

    /// <summary>
    /// The current page-level error message.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// Indicates whether the requested campaign was not found.
    /// </summary>
    private bool _notFound;

    /// <summary>
    /// Indicates whether campaign detail is being loaded.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// The loaded roster snapshot, or <see langword="null"/> when unavailable.
    /// </summary>
    private PagedResult<CampaignParticipantRosterItem>? _roster;

    /// <summary>
    /// The current roster-level error message.
    /// </summary>
    private string? _rosterError;

    /// <summary>
    /// Indicates whether the roster is being loaded.
    /// </summary>
    private bool _rosterLoading;

    /// <summary>
    /// The active workspace tab; only the evaluate tab is functional in this phase.
    /// </summary>
    private string _activeTab = EvaluateTabName;

    /// <summary>
    /// Indicates whether the tab query parameter has been applied to component state.
    /// </summary>
    private bool _tabQueryApplied;

    /// <summary>
    /// The applied roster filter, sort, and paging state.
    /// </summary>
    private CampaignWorkspaceRosterState _filters = new();

    /// <summary>
    /// The canonical query string of the last applied roster state, used to detect URL changes.
    /// </summary>
    private string _appliedQueryString = string.Empty;

    /// <summary>
    /// Indicates whether the initial query-parameter pass has been applied to component state.
    /// </summary>
    private bool _filtersInitialized;

    /// <summary>
    /// Indicates that a roster reload is pending after the next parameter set.
    /// </summary>
    private bool _reloadRosterPending;

    /// <summary>
    /// Draft text from the search input.
    /// </summary>
    private string _searchDraft = string.Empty;

    /// <summary>
    /// Debounce source used to cancel stale search updates.
    /// </summary>
    private CancellationTokenSource? _searchDebounceSource;

    /// <summary>
    /// Monotonic request-sequence token used to discard stale roster responses.
    /// </summary>
    private int _requestSequence;

    /// <summary>
    /// The graduation years displayed in the filter bar.
    /// </summary>
    private IReadOnlyList<int> _availableGraduationYears = [];

    /// <summary>
    /// The tag definitions displayed in the filter bar.
    /// </summary>
    private IReadOnlyList<TagDefinitionDto> _availableTags = [];

    /// <summary>
    /// The active teams displayed in the filter bar.
    /// </summary>
    private IReadOnlyList<TeamRosterItem> _availableTeams = [];

    /// <summary>
    /// Indicates that at least one filter-choice load failed, showing the choices retry affordance.
    /// </summary>
    private bool _choicesLoadFailed;

    /// <summary>
    /// The open participant assignment identifier, or <see langword="null"/> when the drawer is closed.
    /// </summary>
    private long? _selectedParticipantId;

    /// <summary>
    /// The roster region scroll offset captured before a drawer open/close navigation, restored after render.
    /// </summary>
    private double? _pendingScrollRestore;

    /// <summary>
    /// Indicates that the roster region should scroll to its top after the next render.
    /// </summary>
    private bool _scrollToRosterTop;

    /// <summary>
    /// Gets a value indicating whether any roster filter is active.
    /// </summary>
    private bool HasActiveFilters => CampaignWorkspaceUrlState.HasActiveFilters(_filters);

    /// <summary>
    /// Gets the roster item matching the open participant, or <see langword="null"/> when the drawer is closed or the item is not on the loaded page.
    /// </summary>
    private CampaignParticipantRosterItem? SelectedRosterItem
        => _selectedParticipantId is null
            ? null
            : _roster?.Items.FirstOrDefault(item => item.PlayerCampaignAssignmentId == _selectedParticipantId.Value);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!_tabQueryApplied)
        {
            _tabQueryApplied = true;
            // Only the evaluate tab is functional; any other tab value falls back to it.
            _activeTab = EvaluateTabName;
        }

        // Participant selection lives outside the roster state so opening/closing the drawer
        // never triggers a roster reload.
        var participant = CampaignWorkspaceUrlState.ParseParticipant(ParticipantQuery);
        if (participant != _selectedParticipantId)
        {
            _selectedParticipantId = participant;
        }

        var incoming = CampaignWorkspaceUrlState.Parse(
            SearchQuery,
            GraduationYearsQuery,
            TagIdsQuery,
            OutcomeQuery,
            TeamIdQuery,
            SortByQuery,
            SortDirectionQuery,
            PageQuery);
        var incomingQueryString = CampaignWorkspaceUrlState.BuildQueryString(incoming);

        if (string.Equals(incomingQueryString, _appliedQueryString, StringComparison.Ordinal))
        {
            return;
        }

        _filters = incoming;
        _searchDraft = incoming.Search ?? string.Empty;
        _appliedQueryString = incomingQueryString;
        _reloadRosterPending = true;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_reloadRosterPending && _detail is not null)
        {
            _reloadRosterPending = false;
            await LoadRosterAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        ApplyInitialQueryState();

        if (Initialized)
        {
            _detail = PersistedDetail;
            _pageError = PersistedPageError;
            _notFound = PersistedNotFound;
            _roster = PersistedRoster;
            _rosterError = PersistedRosterError;
            _availableGraduationYears = PersistedGraduationYears ?? [];
            _availableTags = PersistedTags ?? [];
            _availableTeams = PersistedTeams ?? [];
            _isLoading = false;
            return;
        }

        _isLoading = true;
        await LoadDetailAsync();
        PersistStartupState();
        Initialized = true;
    }

    /// <summary>
    /// Applies the URL query parameters to the roster state before the first data loads.
    /// </summary>
    /// <remarks>
    /// <see cref="ComponentBase.OnInitializedAsync"/> runs before
    /// <see cref="ComponentBase.OnParametersSet"/>, so the initial query pass must happen here to
    /// ensure the first roster load honors incoming URL filters, sort, and paging state.
    /// </remarks>
    private void ApplyInitialQueryState()
    {
        if (_filtersInitialized)
        {
            return;
        }

        _filtersInitialized = true;
        var incoming = CampaignWorkspaceUrlState.Parse(
            SearchQuery,
            GraduationYearsQuery,
            TagIdsQuery,
            OutcomeQuery,
            TeamIdQuery,
            SortByQuery,
            SortDirectionQuery,
            PageQuery);
        _filters = incoming;
        _searchDraft = incoming.Search ?? string.Empty;
        _appliedQueryString = CampaignWorkspaceUrlState.BuildQueryString(incoming);
        _selectedParticipantId = CampaignWorkspaceUrlState.ParseParticipant(ParticipantQuery);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        _searchDebounceSource?.Cancel();
        _searchDebounceSource?.Dispose();
        _searchDebounceSource = null;

        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.InvokeVoidAsync("detachRosterActivationSuppression");
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; the browser already destroyed the page with it.
            }
        }
    }

    /// <summary>
    /// Loads campaign detail and, on success, the filter choices and initial roster page.
    /// </summary>
    /// <returns>A task that completes when all loads are finished.</returns>
    private async Task LoadDetailAsync()
    {
        _pageError = null;
        _notFound = false;

        var result = await campaignQueryService.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = CampaignId },
            ComponentCancellationToken);

        var detailLoaded = false;
        result.Switch(
            detail =>
            {
                _detail = detail;
                detailLoaded = true;
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                if (problem.Kind == ServiceProblemKind.NotFound)
                {
                    _notFound = true;
                    return;
                }

                _pageError = FirstNonBlank(problem.Detail, "Failed to load campaign. Please retry.");
            });

        _isLoading = false;

        if (detailLoaded)
        {
            await LoadChoicesAsync();
            await LoadRosterAsync();
        }
    }

    /// <summary>
    /// Loads the filter choices: roster graduation years, active tag definitions, and active teams.
    /// </summary>
    /// <returns>A task that completes when all choice loads are finished.</returns>
    private async Task LoadChoicesAsync()
    {
        var outcomes = await Task.WhenAll(
            LoadGraduationYearChoicesAsync(),
            LoadTagChoicesAsync(),
            LoadTeamChoicesAsync());
        _choicesLoadFailed = outcomes.Any(succeeded => !succeeded);
    }

    /// <summary>
    /// Loads the distinct graduation years present in the campaign roster.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> when the load succeeded.</returns>
    private async Task<bool> LoadGraduationYearChoicesAsync()
    {
        var result = await participantQueryService.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = CampaignId },
            ComponentCancellationToken);
        var succeeded = false;
        result.Switch(
            years => { _availableGraduationYears = years; succeeded = true; },
            _ => { });
        return succeeded;
    }

    /// <summary>
    /// Loads the active tag-definition choices for the filter bar.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> when the load succeeded.</returns>
    private async Task<bool> LoadTagChoicesAsync()
    {
        var result = await _tagDefinitionQueryService.GetChoicesAsync(ComponentCancellationToken);
        var succeeded = false;
        result.Switch(
            tags => { _availableTags = tags; succeeded = true; },
            _ => { });
        return succeeded;
    }

    /// <summary>
    /// Loads the active team choices for the filter bar.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> when the load succeeded.</returns>
    private async Task<bool> LoadTeamChoicesAsync()
    {
        var result = await _teamRosterService.GetRosterAsync(
            new GetTeamRosterInput { LifecycleStatus = "active" },
            ComponentCancellationToken);
        var succeeded = false;
        result.Switch(
            teams => { _availableTeams = teams; succeeded = true; },
            _ => { });
        return succeeded;
    }

    /// <summary>
    /// Loads the roster page for the currently applied state, discarding stale responses.
    /// </summary>
    /// <returns>A task that completes when the load is finished.</returns>
    private async Task LoadRosterAsync()
    {
        _rosterError = null;
        _rosterLoading = true;
        var requestId = ++_requestSequence;

        var input = new GetCampaignParticipantRosterInput
        {
            CampaignId = CampaignId,
            Search = _filters.Search,
            GraduationYears = _filters.GraduationYears.Count > 0 ? [.. _filters.GraduationYears] : null,
            TagDefinitionIds = _filters.TagDefinitionIds.Count > 0 ? [.. _filters.TagDefinitionIds] : null,
            Outcome = _filters.Outcome,
            TeamId = _filters.TeamId,
            SortBy = _filters.SortBy,
            SortDirection = _filters.SortDirection,
            Page = _filters.Page,
            PageSize = RosterPageSize
        };

        var result = await participantQueryService.GetParticipantRosterAsync(input, ComponentCancellationToken);

        if (requestId != _requestSequence)
        {
            return;
        }

        result.Switch(
            roster => _roster = roster,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _rosterError = FirstNonBlank(problem.Detail, "Failed to load the roster. Please retry.");
                _roster = null;
            });

        _rosterLoading = false;
    }

    /// <summary>
    /// Applies a new roster state, pushes the matching workspace URL, and schedules a reload.
    /// </summary>
    /// <param name="next">The roster state to apply.</param>
    /// <returns>A task that completes when the navigation or reload is initiated.</returns>
    private async Task ApplyFiltersAndNavigateAsync(CampaignWorkspaceRosterState next)
    {
        _filters = next;
        _searchDraft = next.Search ?? string.Empty;
        _appliedQueryString = CampaignWorkspaceUrlState.BuildQueryString(next);
        _reloadRosterPending = true;

        var targetUrl = CampaignWorkspaceUrlState.BuildWorkspaceUrl(CampaignId, next, _activeTab, _selectedParticipantId);
        var currentPathAndQuery = new Uri(navigationManager.Uri).PathAndQuery;

        if (string.Equals(targetUrl, currentPathAndQuery, StringComparison.Ordinal))
        {
            _reloadRosterPending = false;
            await LoadRosterAsync();
            return;
        }

        _scrollToRosterTop = true;
        navigationManager.NavigateTo(targetUrl);
    }

    /// <summary>
    /// Applies a debounced search term update and reloads the roster.
    /// </summary>
    /// <param name="draft">The raw search draft text.</param>
    /// <returns>A task that completes when the debounce and reload flow finishes.</returns>
    private async Task OnSearchInputChangedAsync(string draft)
    {
        _searchDraft = draft;

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

        var appliedSearch = string.IsNullOrWhiteSpace(draft) ? null : draft.Trim();
        if (!string.Equals(appliedSearch, _filters.Search, StringComparison.Ordinal))
        {
            await ApplyFiltersAndNavigateAsync(_filters with { Search = appliedSearch, Page = 1 });
        }
    }

    /// <summary>
    /// Applies a graduation-year toggle and reloads the roster.
    /// </summary>
    /// <param name="change">The toggled year and its new selected state.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnGraduationYearToggledAsync((int Year, bool Selected) change)
    {
        var years = _filters.GraduationYears.ToList();
        if (change.Selected && !years.Contains(change.Year))
        {
            years.Add(change.Year);
        }
        else if (!change.Selected)
        {
            years.Remove(change.Year);
        }

        return ApplyFiltersAndNavigateAsync(_filters with { GraduationYears = years.AsReadOnly(), Page = 1 });
    }

    /// <summary>
    /// Applies a tag toggle and reloads the roster.
    /// </summary>
    /// <param name="change">The toggled tag identifier and its new selected state.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnTagToggledAsync((long PlayerTagId, bool Selected) change)
    {
        var tagIds = _filters.TagDefinitionIds.ToList();
        if (change.Selected && !tagIds.Contains(change.PlayerTagId))
        {
            tagIds.Add(change.PlayerTagId);
        }
        else if (!change.Selected)
        {
            tagIds.Remove(change.PlayerTagId);
        }

        return ApplyFiltersAndNavigateAsync(_filters with { TagDefinitionIds = tagIds.AsReadOnly(), Page = 1 });
    }

    /// <summary>
    /// Applies an outcome filter change and reloads the roster.
    /// </summary>
    /// <param name="outcome">The selected outcome token, or an empty string when cleared.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnOutcomeChangedAsync(string outcome)
        => ApplyFiltersAndNavigateAsync(_filters with { Outcome = string.IsNullOrEmpty(outcome) ? null : outcome, Page = 1 });

    /// <summary>
    /// Applies a team filter change and reloads the roster.
    /// </summary>
    /// <param name="teamId">The selected team identifier, or <see langword="null"/> when cleared.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnTeamChangedAsync(long? teamId)
        => ApplyFiltersAndNavigateAsync(_filters with { TeamId = teamId, Page = 1 });

    /// <summary>
    /// Applies a sort change, toggling direction when the same column is clicked.
    /// </summary>
    /// <param name="sortBy">The clicked sort field token.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnSortChangedAsync(string sortBy)
    {
        var nextDirection = string.Equals(_filters.SortBy, sortBy, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_filters.SortDirection, "asc", StringComparison.OrdinalIgnoreCase)
            ? "desc"
            : "asc";

        return ApplyFiltersAndNavigateAsync(_filters with { SortBy = sortBy, SortDirection = nextDirection, Page = 1 });
    }

    /// <summary>
    /// Applies a page change and reloads the roster.
    /// </summary>
    /// <param name="page">The requested page number.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnPageChangedAsync(int page)
        => ApplyFiltersAndNavigateAsync(_filters with { Page = Math.Max(1, page) });

    /// <summary>
    /// Clears all roster filters and reloads the roster.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnClearFiltersAsync()
    {
        _searchDebounceSource?.Cancel();
        return ApplyFiltersAndNavigateAsync(CampaignWorkspaceUrlState.ClearFilters(_filters));
    }

    /// <summary>
    /// Retries the detail load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load is finished.</returns>
    private async Task RetryAsync()
    {
        _isLoading = true;
        await LoadDetailAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Retries the roster load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load is finished.</returns>
    private async Task RetryRosterAsync()
    {
        await LoadRosterAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Retries the filter-choice loads after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried choice loads are finished.</returns>
    private async Task RetryChoicesAsync()
    {
        _choicesLoadFailed = false;
        await LoadChoicesAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Selects the evaluate tab, pushing the <c>tab</c> query parameter when absent.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task SelectEvaluateTabAsync()
    {
        if (!string.Equals(TabQuery, EvaluateTabName, StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo(CampaignWorkspaceUrlState.BuildWorkspaceUrl(CampaignId, _filters, EvaluateTabName, _selectedParticipantId));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens the participant drawer for the clicked roster row, pushing <c>participant</c> onto the workspace URL.
    /// </summary>
    /// <param name="item">The clicked roster item.</param>
    /// <returns>A task that completes when the scroll anchor is captured and navigation is initiated.</returns>
    private async Task OnParticipantSelectedAsync(CampaignParticipantRosterItem item)
    {
        if (_selectedParticipantId == item.PlayerCampaignAssignmentId)
        {
            return;
        }

        await CaptureRosterScrollAsync();
        _selectedParticipantId = item.PlayerCampaignAssignmentId;
        navigationManager.NavigateTo(
            CampaignWorkspaceUrlState.BuildWorkspaceUrl(CampaignId, _filters, _activeTab, _selectedParticipantId));
    }

    /// <summary>
    /// Closes the participant drawer, removing <c>participant</c> from the workspace URL while preserving roster state.
    /// </summary>
    /// <returns>A task that completes when the scroll anchor is captured and navigation is initiated.</returns>
    private async Task OnCloseParticipantAsync()
    {
        await CaptureRosterScrollAsync();
        _selectedParticipantId = null;
        navigationManager.NavigateTo(
            CampaignWorkspaceUrlState.BuildWorkspaceUrl(CampaignId, _filters, _activeTab));
    }

    /// <summary>
    /// Captures the roster region scroll offset before a drawer open/close navigation.
    /// </summary>
    /// <returns>A task that completes when the offset is captured.</returns>
    private async Task CaptureRosterScrollAsync()
    {
        var module = await _moduleTask.Value;
        _pendingScrollRestore = await module.InvokeAsync<double?>("captureScroll", _rosterScrollRegion);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The roster region is only in the DOM when a loaded roster is rendered; keep the
        // pending scroll work until then so filter changes still scroll after a loading pass.
        if (_rosterLoading || _roster is null)
        {
            return;
        }

        var module = await _moduleTask.Value;

        // The region element is recreated across loading/error/loaded renders, so re-attach the
        // keydown suppression on every pass that renders the loaded roster. The module replaces
        // any existing listener, keeping exactly one active. An empty roster never renders the
        // region, so detach instead — the unset ElementReference serializes as a plain object
        // whose contains() check would throw on every keydown.
        if (_roster.TotalCount > 0)
        {
            await module.InvokeVoidAsync("attachRosterActivationSuppression", _rosterScrollRegion);
        }
        else
        {
            await module.InvokeVoidAsync("detachRosterActivationSuppression");
        }

        if (_scrollToRosterTop)
        {
            _scrollToRosterTop = false;
            await module.InvokeVoidAsync("scrollToTop", _rosterScrollRegion);
        }

        if (_pendingScrollRestore is not null)
        {
            var restoreTop = _pendingScrollRestore.Value;
            _pendingScrollRestore = null;
            await module.InvokeVoidAsync("restoreScroll", _rosterScrollRegion, restoreTop);
        }
    }

    /// <summary>
    /// Persists the current startup state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedDetail = _detail;
        PersistedPageError = _pageError;
        PersistedNotFound = _notFound;
        PersistedRoster = _roster;
        PersistedRosterError = _rosterError;
        PersistedGraduationYears = _availableGraduationYears;
        PersistedTags = _availableTags;
        PersistedTeams = _availableTeams;
    }

    /// <summary>
    /// Formats a campaign's date range for display.
    /// </summary>
    /// <param name="detail">The campaign detail payload.</param>
    /// <returns>The formatted date range.</returns>
    protected static string FormatCampaignDates(CampaignDetailResult detail)
        => detail.PlannedEndDate is null
            ? $"Starts {detail.StartDate:MMM d, yyyy}"
            : $"{detail.StartDate:MMM d, yyyy} – {detail.PlannedEndDate.Value:MMM d, yyyy}";

    /// <summary>
    /// Maps a campaign lifecycle status to its Bootstrap badge class.
    /// </summary>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <returns>The badge background class.</returns>
    protected static string CampaignStatusBadgeClass(CampaignStatus status) => status switch
    {
        CampaignStatus.Active => "text-bg-success",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// Returns the first non-blank message from the supplied candidates.
    /// </summary>
    /// <param name="candidates">The candidate messages in preference order.</param>
    /// <returns>The first non-blank candidate.</returns>
    private static string FirstNonBlank(params string?[] candidates)
        => candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;
}
