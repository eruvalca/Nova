using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Tags;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>
/// Renders the campaign workspace: header, tab bar, and the filterable evaluate roster region.
/// </summary>
/// <param name="campaignQueryService">The campaign detail query service.</param>
/// <param name="participantQueryService">The campaign roster query service.</param>
/// <param name="tagDefinitionQueryService">The tag-definition choices service used by roster filters.</param>
/// <param name="teamRosterService">The team choices service used by roster filters.</param>
/// <param name="campaignMetadataService">The campaign metadata correction service used by the edit-metadata flow.</param>
/// <param name="authenticationStateProvider">The authentication state provider used for role derivation.</param>
/// <param name="navigationManager">The navigation manager used for URL history and redirects.</param>
/// <param name="jsRuntime">The JavaScript runtime used to import the collocated workspace module.</param>
public partial class CampaignWorkspace(
    ICampaignQueryService campaignQueryService,
    ICampaignParticipantQueryService participantQueryService,
    ITagDefinitionQueryService tagDefinitionQueryService,
    ITeamRosterService teamRosterService,
    ICampaignMetadataService campaignMetadataService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager,
    IJSRuntime jsRuntime) : NovaComponentBase
{
    private bool IsRosterLanding => new Uri(navigationManager.Uri).AbsolutePath.EndsWith("/roster", StringComparison.OrdinalIgnoreCase);
    private string? _openingReceiptMessage;
    private bool _receiptChecked;
    private ElementReference _rosterHeading;

    private string BuildRosterUrl(CampaignWorkspaceRosterState state, string tab, long? participantId = null)
    {
        var url = CampaignWorkspaceUrlState.BuildWorkspaceUrl(CampaignId, state, tab, participantId);
        return IsRosterLanding
            ? url.Replace($"/campaigns/{CampaignId}?", $"/campaigns/{CampaignId}/roster?", StringComparison.Ordinal)
            : url;
    }
    /// <summary>
    /// The debounce interval for search input updates.
    /// </summary>
    private const int SearchDebounceMilliseconds = 350;

    /// <summary>
    /// The fixed roster page size requested by this UI.
    /// </summary>
    private const int RosterPageSize = GetCampaignParticipantRosterInput.DefaultPageSize;

    /// <summary>
    /// The name of the evaluate workspace tab.
    /// </summary>
    private const string EvaluateTabName = CampaignWorkspaceUrlState.EvaluateTab;

    /// <summary>
    /// The name of the placements workspace tab.
    /// </summary>
    private const string PlacementsTabName = CampaignWorkspaceUrlState.PlacementsTab;

    /// <summary>
    /// The name of the overview workspace tab.
    /// </summary>
    private const string OverviewTabName = CampaignWorkspaceUrlState.OverviewTab;

    /// <summary>
    /// The name of the closeout workspace tab.
    /// </summary>
    private const string CloseoutTabName = CampaignWorkspaceUrlState.CloseoutTab;

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

    /// <summary>Gets or sets the authorized detail already loaded by the lifecycle router.</summary>
    [Parameter]
    public CampaignDetailResult? InitialDetail { get; set; }

    /// <summary>Gets or sets the user, club, and role scope that owns the initial detail.</summary>
    [Parameter]
    public string? InitialDetailScope { get; set; }

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
    /// Gets or sets the incoming placements graduation-year query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementGraduationYear")]
    private int? PlacementGraduationYearQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming placements unresolved-only query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "unresolvedOnly")]
    private bool? UnresolvedOnlyQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming placements page-number query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementPage")]
    private int? PlacementPageQuery { get; set; }

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
    /// The active workspace tab.
    /// </summary>
    private string _activeTab = EvaluateTabName;

    /// <summary>
    /// Indicates whether the current user holds the club administrator role.
    /// </summary>
    private bool _isClubAdmin;

    /// <summary>
    /// The applied placement filter and paging state reflected in the workspace URL.
    /// </summary>
    private CampaignWorkspacePlacementState _placementState = new();

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
    /// The campaign metadata form state when a metadata correction is active.
    /// </summary>
    private CampaignMetadataFormState? _editCampaignForm;

    /// <summary>
    /// The season choices loaded for the metadata edit form dropdown.
    /// </summary>
    private IReadOnlyList<CampaignSeasonChoice> _seasonChoices = [];

    /// <summary>
    /// The total number of tenant seasons before the choice bound, used to disclose truncation.
    /// </summary>
    private int _seasonChoiceTotalCount;

    /// <summary>
    /// The per-edit season list passed to the metadata form; may include the edited campaign's
    /// current season when the bounded cache omits it.
    /// </summary>
    private IReadOnlyList<CampaignSeasonChoice> _editFormSeasonChoices = [];

    /// <summary>
    /// Indicates whether a metadata correction mutation is in progress.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// The current mutation-level error message shown inside the metadata form.
    /// </summary>
    private string? _mutationError;

    /// <summary>
    /// Indicates whether the active metadata mutation ended in a lifecycle conflict, which offers a
    /// close-and-reload affordance.
    /// </summary>
    private bool _mutationConflict;

    /// <summary>
    /// The current status message shown after successful mutations.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Version counter for edit-form selection, incremented whenever the form closes so a stale
    /// begin-edit continuation cannot reopen a superseded form.
    /// </summary>
    private int _editVersion;

    /// <summary>
    /// The open participant assignment identifier, or <see langword="null"/> when the drawer is closed.
    /// </summary>
    private long? _selectedParticipantId;

    /// <summary>
    /// The pending cross-page participant move to resolve after the matching roster load completes.
    /// </summary>
    private PendingBoundaryMove? _pendingBoundaryMove;

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
    /// Gets a value indicating whether the current user may edit placements for the loaded campaign.
    /// </summary>
    private bool _canEditPlacements
        => _detail is not null
            && _detail.Status == CampaignStatus.Active
            && _isClubAdmin;

    /// <summary>
    /// Gets the roster item matching the open participant, or <see langword="null"/> when the drawer is closed or the item is not on the loaded page.
    /// </summary>
    private CampaignParticipantRosterItem? SelectedRosterItem
        => _selectedParticipantId is null
            ? null
            : _roster?.Items.FirstOrDefault(item => item.PlayerCampaignAssignmentId == _selectedParticipantId.Value);

    /// <summary>
    /// Gets the 1-based position of the open participant within the filtered roster sequence,
    /// or <see langword="null"/> when the drawer is closed or the participant is off the loaded page.
    /// </summary>
    private int? SelectedParticipantPosition
    {
        get
        {
            if (_roster is null || _selectedParticipantId is null)
            {
                return null;
            }

            for (var index = 0; index < _roster.Items.Count; index++)
            {
                if (_roster.Items[index].PlayerCampaignAssignmentId == _selectedParticipantId.Value)
                {
                    return ((_roster.Page - 1) * _roster.PageSize) + index + 1;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the total number of participants in the filtered roster sequence.
    /// </summary>
    private int ParticipantSequenceCount => _roster?.TotalCount ?? 0;

    /// <summary>
    /// Gets a value indicating whether the open participant has a predecessor in the roster sequence.
    /// </summary>
    private bool HasPreviousParticipant => SelectedParticipantPosition is > 1;

    /// <summary>
    /// Gets a value indicating whether the open participant has a successor in the roster sequence.
    /// </summary>
    private bool HasNextParticipant
        => SelectedParticipantPosition is { } position
            && _roster is not null
            && position < _roster.TotalCount;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Re-derive the active tab on every parameter set. In-app tab clicks perform a client-side,
        // query-only navigation that reuses this component instance and re-supplies TabQuery, so a
        // one-shot guard would leave the rendered view stuck on the initially loaded tab.
        // The focused Roster route always owns the roster panel, regardless of workspace tab input.
        _activeTab = IsRosterLanding ? EvaluateTabName : CampaignWorkspaceUrlState.NormalizeTab(TabQuery);

        // The placements state is independent of the roster state; parse it on every parameter
        // set so the placements panel receives the URL-backed filters regardless of roster state.
        var placement = CampaignWorkspaceUrlState.ParsePlacement(
            PlacementGraduationYearQuery,
            UnresolvedOnlyQuery,
            PlacementPageQuery);
        if (placement != _placementState)
        {
            _placementState = placement;
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

        // A pending boundary move is only valid while the URL state it was issued against stays
        // in place. Clear it the moment a participant or roster-query divergence is observed — a
        // close, Back/Forward, a different selection, or a filter/page change — so a transient
        // close-then-Back round trip cannot resurrect a move the close was supposed to cancel.
        // A failed load for the unchanged query keeps the move pending so Retry can complete it.
        if (_pendingBoundaryMove is { } pending
            && (pending.InitiatingParticipantId != participant
                || !string.Equals(incomingQueryString, pending.ExpectedQueryString, StringComparison.Ordinal)))
        {
            _pendingBoundaryMove = null;
        }

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

        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        _isClubAdmin = authenticationState.User.IsInRole(Roles.ClubAdmin);

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
        var scope = $"{authenticationState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}:{authenticationState.User.FindFirst(NovaClaimTypes.ClubId)?.Value}:{_isClubAdmin}";
        if (InitialDetail is { Status: CampaignStatus.Active or CampaignStatus.Closed } initial
            && initial.CampaignId == CampaignId && InitialDetailScope == scope)
        {
            _detail = initial;
            _isLoading = false;
            await LoadChoicesAsync();
            await LoadRosterAsync();
        }
        else
        {
            await LoadDetailAsync();
        }
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

        if (detailLoaded && _detail?.Status == CampaignStatus.Draft)
        {
            navigationManager.NavigateTo($"/campaigns/{CampaignId}", replace: true);
            return;
        }
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

        var loaded = false;
        result.Switch(
            roster =>
            {
                _roster = roster;
                loaded = true;
            },
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

        TryResolvePendingBoundaryMove(loaded);
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

        var targetUrl = BuildRosterUrl(next, _activeTab, _selectedParticipantId);

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
            navigationManager.NavigateTo(BuildRosterUrl(_filters, EvaluateTabName, _selectedParticipantId));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects the placements tab, pushing the placements workspace URL.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task SelectPlacementsTabAsync()
    {
        if (!string.Equals(TabQuery, PlacementsTabName, StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo(CampaignWorkspaceUrlState.BuildPlacementsWorkspaceUrl(CampaignId, _placementState));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects the overview tab, pushing the overview workspace URL.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task SelectOverviewTabAsync()
    {
        if (!string.Equals(TabQuery, OverviewTabName, StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo(CampaignWorkspaceUrlState.BuildOverviewWorkspaceUrl(CampaignId));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Selects the closeout tab, pushing the closeout workspace URL.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task SelectCloseoutTabAsync()
    {
        if (!string.Equals(TabQuery, CloseoutTabName, StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo(CampaignWorkspaceUrlState.BuildCloseoutWorkspaceUrl(CampaignId));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens the closeout tab from the overview panel.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnOpenCloseoutAsync() => SelectCloseoutTabAsync();

    /// <summary>
    /// Navigates to the placements tab, optionally filtered to unresolved placements, in response to
    /// a closeout blocker drill-down.
    /// </summary>
    /// <param name="unresolvedOnly">Whether the target placements URL should filter to unresolved placements.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnReviewUnresolvedAsync(bool unresolvedOnly)
    {
        var url = unresolvedOnly
            ? CampaignWorkspaceUrlState.BuildReviewUnresolvedUrl(CampaignId)
            : CampaignWorkspaceUrlState.BuildPlacementsWorkspaceUrl(CampaignId, new CampaignWorkspacePlacementState());
        navigationManager.NavigateTo(url);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cancels the closeout view and returns to the evaluate tab, preserving the current roster state.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnCancelCloseoutAsync()
    {
        if (!string.Equals(TabQuery, EvaluateTabName, StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo(BuildRosterUrl(_filters, EvaluateTabName, _selectedParticipantId));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a placement filter or page change raised by the placements panel and pushes the
    /// matching placements workspace URL.
    /// </summary>
    /// <param name="next">The placement state to apply.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnPlacementStateChangedAsync(CampaignWorkspacePlacementState next)
    {
        _placementState = next;

        var targetUrl = CampaignWorkspaceUrlState.BuildPlacementsWorkspaceUrl(CampaignId, next);
        var currentPathAndQuery = new Uri(navigationManager.Uri).PathAndQuery;
        if (!string.Equals(targetUrl, currentPathAndQuery, StringComparison.Ordinal))
        {
            navigationManager.NavigateTo(targetUrl);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reloads the campaign detail after the placements panel requests a full recovery reload
    /// (for example, after a save conflict or a detected Closed transition).
    /// </summary>
    /// <returns>A task that completes when the detail reload is finished.</returns>
    private async Task OnCampaignReloadRequestedAsync()
    {
        await LoadDetailAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Opens the inline campaign metadata correction form, loading bounded season choices first.
    /// </summary>
    /// <returns>A task that completes when the form is ready or the season choices fail.</returns>
    private async Task BeginEditMetadataAsync()
    {
        if (_detail is null)
        {
            return;
        }

        CancelMutationForm();
        _statusMessage = null;
        var editVersion = _editVersion;

        if (!await EnsureSeasonChoicesLoadedAsync(editVersion))
        {
            return;
        }

        if (editVersion != _editVersion)
        {
            return;
        }

        _editFormSeasonChoices = EnsureCurrentSeasonSelectable(_seasonChoices);
        _editCampaignForm = CampaignMetadataFormState.FromDetail(_detail);
    }

    /// <summary>
    /// Loads the season choices used by the metadata edit form when not already available. Failure
    /// feedback is published only when the calling edit selection is still current.
    /// </summary>
    /// <param name="editVersion">The edit-selection version captured by the caller.</param>
    /// <returns><see langword="true"/> when season choices are available; otherwise <see langword="false"/>.</returns>
    private async Task<bool> EnsureSeasonChoicesLoadedAsync(int editVersion)
    {
        if (_seasonChoices.Count > 0)
        {
            return true;
        }

        ServiceResult<CampaignCreationSetupResult> result;
        try
        {
            result = await campaignQueryService.GetCreationSetupAsync(ComponentCancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ComponentCancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (editVersion == _editVersion)
            {
                _mutationError = "Could not reach the server. Check your connection and retry.";
            }

            return false;
        }

        var loaded = false;
        result.Switch(
            setup =>
            {
                if (editVersion == _editVersion)
                {
                    _seasonChoices = setup.CurrentSeason is null ? [] : [setup.CurrentSeason];
                    _seasonChoiceTotalCount = _seasonChoices.Count;
                }

                loaded = true;
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                if (editVersion == _editVersion)
                {
                    _mutationError = FirstNonBlank(problem.Detail, "Failed to load seasons. Please retry.");
                }
            });

        return loaded;
    }

    /// <summary>
    /// Ensures the edited campaign's current season remains selectable even when the bounded setup
    /// payload omits it, without polluting the shared cached choices.
    /// </summary>
    /// <param name="choices">The cached bounded season choices.</param>
    /// <returns>The choices to pass to the metadata form.</returns>
    private IReadOnlyList<CampaignSeasonChoice> EnsureCurrentSeasonSelectable(IReadOnlyList<CampaignSeasonChoice> choices)
    {
        if (_detail is null || choices.Any(choice => choice.SeasonId == _detail.SeasonId))
        {
            return choices;
        }

        // The detail payload carries no season start/end dates, so the campaign start date is used
        // as the display fallback for this rare omitted-season case.
        return choices
            .Prepend(new CampaignSeasonChoice
            {
                SeasonId = _detail.SeasonId,
                Name = _detail.SeasonName,
                StartDate = _detail.StartDate,
                EndDate = null
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Applies an Active campaign metadata correction and reloads the detail on success, preserving
    /// the status message across the refresh.
    /// </summary>
    /// <param name="model">The validated campaign metadata form state.</param>
    /// <returns>A task that completes when the update request and follow-up reload finish.</returns>
    private async Task UpdateMetadataAsync(CampaignMetadataFormState model)
    {
        if (_isMutating)
        {
            return;
        }

        _isMutating = true;
        _mutationError = null;
        _mutationConflict = false;

        try
        {
            var result = await campaignMetadataService.UpdateAsync(model.ToUpdateInput(), ComponentCancellationToken);
            var succeeded = false;
            result.Switch(
                updated =>
                {
                    _editCampaignForm = null;
                    _statusMessage = $"Campaign \"{updated.Name}\" metadata updated.";
                    succeeded = true;
                },
                problem =>
                {
                    if (problem.Kind == ServiceProblemKind.Forbidden)
                    {
                        navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                        return;
                    }

                    if (problem.Kind == ServiceProblemKind.Conflict)
                    {
                        _mutationError = FirstNonBlank(problem.Detail, "This campaign is Closed. Reopen the campaign before editing its metadata.");
                        _mutationConflict = true;
                        return;
                    }

                    _mutationError = FirstNonBlank(problem.Detail, FlattenValidationErrors(problem), "Failed to save changes. Please retry.");
                });

            if (succeeded)
            {
                await LoadDetailAsync();
                PersistStartupState();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }

            _mutationError = "Could not reach the server. Check your connection and retry.";
        }
        finally
        {
            _isMutating = false;
        }
    }

    /// <summary>
    /// Closes the conflicted edit form and reloads the detail so it reflects the campaign's current
    /// lifecycle state.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private async Task CloseFormAndReloadAsync()
    {
        CancelMutationForm();
        _statusMessage = null;
        await LoadDetailAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Closes the metadata correction form and clears its feedback.
    /// </summary>
    private void CancelMutationForm()
    {
        _editCampaignForm = null;
        _mutationError = null;
        _mutationConflict = false;
        _editFormSeasonChoices = [];
        _editVersion++;
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
            BuildRosterUrl(_filters, _activeTab, _selectedParticipantId));
    }

    /// <summary>
    /// Moves the open participant to the previous participant in the roster sequence, crossing pages when needed.
    /// </summary>
    /// <returns>A task that completes when the move navigation is initiated.</returns>
    private Task OnPreviousParticipantAsync() => MoveParticipantAsync(-1);

    /// <summary>
    /// Moves the open participant to the next participant in the roster sequence, crossing pages when needed.
    /// </summary>
    /// <returns>A task that completes when the move navigation is initiated.</returns>
    private Task OnNextParticipantAsync() => MoveParticipantAsync(1);

    /// <summary>
    /// Moves the open participant by an offset within the loaded roster page, or schedules a
    /// cross-page move when the offset runs past the current page boundary.
    /// </summary>
    /// <param name="offset">The move offset: <c>-1</c> for previous, <c>1</c> for next.</param>
    /// <returns>A task that completes when the move navigation is initiated.</returns>
    private async Task MoveParticipantAsync(int offset)
    {
        if (_roster is null || _selectedParticipantId is null || _pendingBoundaryMove is not null)
        {
            return;
        }

        var items = _roster.Items;
        var currentIndex = -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].PlayerCampaignAssignmentId == _selectedParticipantId.Value)
            {
                currentIndex = index;
                break;
            }
        }

        if (currentIndex < 0)
        {
            // The open participant is off the loaded page; sequence moves are unavailable.
            return;
        }

        var targetIndex = currentIndex + offset;
        if (targetIndex >= 0 && targetIndex < items.Count)
        {
            // Within-page move: only the participant parameter changes; roster and scroll stay untouched.
            await CaptureRosterScrollAsync();
            _selectedParticipantId = items[targetIndex].PlayerCampaignAssignmentId;
            navigationManager.NavigateTo(
                BuildRosterUrl(_filters, _activeTab, _selectedParticipantId));
            return;
        }

        var nextPage = _filters.Page + offset;
        if (nextPage < 1)
        {
            return;
        }

        // Cross-page move: push the page change, remember the target edge together with the exact
        // roster query and initiating participant, and correct the participant selection in place
        // once that exact page finishes loading.
        var nextState = _filters with { Page = nextPage };
        _pendingBoundaryMove = new PendingBoundaryMove(
            offset < 0 ? BoundaryIntent.Last : BoundaryIntent.First,
            CampaignWorkspaceUrlState.BuildQueryString(nextState),
            _selectedParticipantId.Value);
        await ApplyFiltersAndNavigateAsync(nextState);
    }

    /// <summary>
    /// Resolves or clears a pending cross-page move after a roster load finishes. The move only
    /// resolves when the load succeeded for the exact query the move was issued against and the
    /// initiating participant is still selected. Divergence is normally cleared immediately by
    /// <see cref="OnParametersSet"/>, so this check is defense-in-depth for responses racing a
    /// navigation; a failed load keeps the move pending so Retry can still complete it.
    /// </summary>
    /// <param name="loaded">Whether the finished roster load succeeded.</param>
    private void TryResolvePendingBoundaryMove(bool loaded)
    {
        if (_pendingBoundaryMove is null)
        {
            return;
        }

        if (_selectedParticipantId != _pendingBoundaryMove.InitiatingParticipantId
            || !string.Equals(_appliedQueryString, _pendingBoundaryMove.ExpectedQueryString, StringComparison.Ordinal))
        {
            _pendingBoundaryMove = null;
            return;
        }

        if (!loaded)
        {
            return;
        }

        var move = _pendingBoundaryMove;
        _pendingBoundaryMove = null;

        if (_roster is null || _roster.Items.Count == 0)
        {
            // The new page came back empty (concurrent roster drift); leave the drawer off-page.
            return;
        }

        var target = move.Edge == BoundaryIntent.First ? _roster.Items[0] : _roster.Items[^1];
        _selectedParticipantId = target.PlayerCampaignAssignmentId;
        navigationManager.NavigateTo(
            BuildRosterUrl(_filters, _activeTab, _selectedParticipantId),
            new NavigationOptions { ReplaceHistoryEntry = true });
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
            BuildRosterUrl(_filters, _activeTab));
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
            // A prior loaded render may have installed the keydown suppression scoped to the
            // region element this render just removed. Detach it so a stale listener can't
            // keep the dead element (and its document-level handler) alive until a retry.
            if (_moduleTask.IsValueCreated)
            {
                var rosterModule = await _moduleTask.Value;
                await rosterModule.InvokeVoidAsync("detachRosterActivationSuppression");
            }

            return;
        }

        var module = await _moduleTask.Value;

        // The region element is recreated across loading/error/loaded renders, so re-attach the
        if (IsRosterLanding && !_receiptChecked)
        {
            _receiptChecked = true;
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            var scope = $"{state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}:{state.User.FindFirst(NovaClaimTypes.ClubId)?.Value}:{state.User.IsInRole(Roles.ClubAdmin)}";
            try
            {
                var receipt = await module.InvokeAsync<OpenCampaignResult?>("readOpeningReceipt", ComponentCancellationToken, scope, CampaignId, _rosterHeading);
                ComponentCancellationToken.ThrowIfCancellationRequested();
                if (receipt is not null && receipt.CampaignId == CampaignId && receipt.EnrolledPlayerCount > 0 && receipt.OperationId != Guid.Empty)
                {
                    _openingReceiptMessage = $"Campaign opened and enrolled {receipt.EnrolledPlayerCount} players.";
                    StateHasChanged();
                    await module.InvokeVoidAsync("acknowledgeOpeningReceipt", ComponentCancellationToken, scope, CampaignId, receipt.OperationId);
                }
            }
            catch (JSException) { /* The roster remains usable if optional success feedback cannot be restored. */ }
        }

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

    /// <summary>
    /// Flattens field-level validation messages when the problem carries no detail text.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <returns>The joined field messages, or <see langword="null"/> when the problem has no errors.</returns>
    private static string? FlattenValidationErrors(ServiceProblem problem)
        => problem.Errors is { Count: > 0 }
            ? string.Join(" ", problem.Errors.SelectMany(pair => pair.Value))
            : null;

    /// <summary>
    /// A pending cross-page participant move: the target edge, the exact roster query the move was
    /// issued against, and the initiating participant selection.
    /// </summary>
    private sealed record PendingBoundaryMove(
        BoundaryIntent Edge,
        string ExpectedQueryString,
        long InitiatingParticipantId);

    /// <summary>
    /// The pending cross-page move direction to resolve after the next roster load.
    /// </summary>
    private enum BoundaryIntent
    {
        /// <summary>
        /// No cross-page move is pending.
        /// </summary>
        None,

        /// <summary>
        /// Select the first participant of the newly loaded page.
        /// </summary>
        First,

        /// <summary>
        /// Select the last participant of the newly loaded page.
        /// </summary>
        Last
    }
}
