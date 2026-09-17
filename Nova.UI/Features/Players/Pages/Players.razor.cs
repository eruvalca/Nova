#pragma warning disable CA1724 // The routed page shares its feature's namespace name.
#pragma warning disable CA1849, S6966 // Cancellation completes before replacing request ownership.
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.UI.Components;
using Nova.UI.Features.Players.Components;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players.Pages;

/// <summary>Owns the URL-backed directory and existing manual forms for one authenticated club.</summary>
public partial class Players(
    IPlayerService playerService,
    IPlayerManagementService playerManagementService,
    IPlayerLifecycleService playerLifecycleService,
    IPlayerDetailService playerDetailService,
    ITagDefinitionQueryService tagDefinitionQueryService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager) : NovaComponentBase
{
    private const int SearchDebounceMilliseconds = 350;
    private const int RosterPageSize = GetPlayerRosterInput.DefaultPageSize;
    private PagedResult<PlayerListItem>? _roster;
    private PlayerDirectorySummary? _summary;
    private IReadOnlyList<TagDefinitionDto> _availableTags = [];
    private IReadOnlyList<int> AvailableGraduationYears => _summary?.GraduationYears ?? [];
    private string? _pageError;
    private string? _summaryError;
    private string? _tagsError;
    private string? _mutationError;
    private string? _statusMessage;
    private bool _isLoading;
    private bool _summaryLoading;
    private bool _tagsLoading;
    private bool _isMutating;
    private bool _canManagePlayers;
    private bool _isClubAdmin;
    private CreatePlayerInput? _pendingCreate;
    private string? _pendingCreationError;
    private PlayerCreationDuplicate? _creationDuplicate;
    private long? _clubId;
    private string? _userId;
    private int _identityVersion;
    private int _rosterVersion;
    private int _summaryVersion;
    private int _tagsVersion;
    private int _routeVersion;
    private int _authenticationVersion;
    private bool _identityApplied;
    private string CurrentScope => $"{_userId}:{_clubId}:{_isClubAdmin}";
    private string _searchDraft = string.Empty;
    private PlayersUrlState _urlState = new();
    private string? _locationKey;
    private string? _loadedQuery;
    private bool _metadataStarted;
    private PlayerFormState _createForm = PlayerFormState.CreateDefault();
    private PlayerFormState? _editForm;
    private bool _showCreateForm;
    private bool _isEditRoute;
    private bool _formLoading;
    private IReadOnlyList<GraduationYearBlockerItem> _graduationYearBlockers = [];
    private PlayerListItem? _archiveCandidate;
    private bool _archiveConfirmed;
    private IReadOnlyList<PlayerArchiveBlocker> _archiveBlockers = [];
    private CancellationTokenSource? _searchDebounceSource;
    private CancellationTokenSource? _identitySource;
    private CancellationTokenSource? _rosterSource;
    private CancellationTokenSource? _formSource;

    /// <summary>The optional edit route's player identifier.</summary>
    [Parameter] public long? PlayerId { get; set; }
    /// <summary>The raw local correction return supplied by the router.</summary>
    [SupplyParameterFromQuery(Name = "returnUrl")] public string? CorrectionReturn { get; set; }
    /// <summary>The raw Draft correction campaign supplied by the router.</summary>
    [SupplyParameterFromQuery(Name = "returnToDraft")] public string? ReturnToDraft { get; set; }
    // Raw strings prevent the router from rejecting malformed optional numeric values.
    [SupplyParameterFromQuery(Name = "view")] private string? ViewQuery { get; set; }
    [SupplyParameterFromQuery(Name = "search")] private string? SearchQuery { get; set; }
    [SupplyParameterFromQuery(Name = "graduationYear")] private string? GraduationYearQuery { get; set; }
    [SupplyParameterFromQuery(Name = "tag")] private string? TagQuery { get; set; }
    [SupplyParameterFromQuery(Name = "page")] private string? PageQuery { get; set; }

    /// <summary>The roster snapshot across prerender and interactive attach.</summary>
    [PersistentState] public PagedResult<PlayerListItem>? PersistedRoster { get; set; }
    /// <summary>The summary snapshot across prerender and interactive attach.</summary>
    [PersistentState] public PlayerDirectorySummary? PersistedSummary { get; set; }
    /// <summary>The complete bounded active-tag choices across attach.</summary>
    [PersistentState] public IReadOnlyList<TagDefinitionDto>? PersistedTags { get; set; }
    /// <summary>The startup roster error.</summary>
    [PersistentState] public string? PersistedPageError { get; set; }
    /// <summary>The startup summary error.</summary>
    [PersistentState] public string? PersistedSummaryError { get; set; }
    /// <summary>The startup tag-choice error.</summary>
    [PersistentState] public string? PersistedTagsError { get; set; }
    /// <summary>Whether startup reads settled.</summary>
    [PersistentState] public bool Initialized { get; set; }
    /// <summary>The actor, club and authority owning the snapshot.</summary>
    [PersistentState] public string? SnapshotScope { get; set; }
    /// <summary>The normalized query owning the roster snapshot.</summary>
    [PersistentState] public string? SnapshotQuery { get; set; }

    private string GraduationYearFilterText => _urlState.GraduationYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private string PlayerTagFilterText => _urlState.TagId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    private bool IsFormRoute => _showCreateForm || _isEditRoute;
    private bool HasSavedTag => _urlState.TagId is { } id && !_availableTags.Any(tag => tag.PlayerTagId == id);
    private long PageCount => _roster is null ? 1 : Math.Max(1, ((long)_roster.TotalCount + RosterPageSize - 1) / RosterPageSize);
    private string? SearchError => _urlState.IsSearchValid ? null : $"Search must be {GetPlayerRosterInput.MaxSearchLength} characters or fewer.";
    private string? CorrectionDestination => _urlState.ReturnUrl;
    private long? DraftReturnId => _urlState.ReturnToDraft;
    private string DraftCorrectionDestination(long draftId) => (_urlState with { ReturnToDraft = draftId }).ToDraftUrl()!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        navigationManager.LocationChanged += OnLocationChanged;
        var version = _authenticationVersion;
        var authentication = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (version == _authenticationVersion && !ComponentCancellationToken.IsCancellationRequested)
        {
            await ApplyIdentityAsync(authentication.User);
        }
    }

    /// <inheritdoc />
    protected override Task OnParametersSetAsync() => ReconcileLocationAsync();

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
        => _ = InvokeAsync(async () => { await ReconcileLocationAsync(); StateHasChanged(); });

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
        => _ = InvokeAsync(async () =>
        {
            var version = ++_authenticationVersion;
            var state = await task;
            if (version == _authenticationVersion && !ComponentCancellationToken.IsCancellationRequested)
            {
                await ApplyIdentityAsync(state.User);
                StateHasChanged();
            }
        });

    private async Task ApplyIdentityAsync(ClaimsPrincipal principal)
    {
        var club = ReadClubIdClaim(principal);
        var user = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var admin = principal.IsInRole(Roles.ClubAdmin);
        var member = principal.Identity?.IsAuthenticated == true && club is > 0 && !string.IsNullOrEmpty(user);
        if (_identityApplied && club == _clubId && string.Equals(user, _userId, StringComparison.Ordinal)
            && admin == _isClubAdmin && member == _canManagePlayers) { return; }
        var previousIdentity = _identityApplied;
        var sameOwner = previousIdentity && club == _clubId && string.Equals(user, _userId, StringComparison.Ordinal);
        var pending = sameOwner ? _pendingCreate : null;
        var pendingError = sameOwner ? _pendingCreationError : null;
        var form = _createForm;
        var status = sameOwner ? _statusMessage : null;
        ++_identityVersion;
        CancelSource(ref _identitySource);
        _identitySource = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        _clubId = club;
        _userId = user;
        _isClubAdmin = admin;
        _canManagePlayers = member;
        _identityApplied = true;
        if (previousIdentity)
        {
            ResetIdentityState();
            _pendingCreate = pending;
            _pendingCreationError = pendingError;
            if (pending is not null) { _createForm = form; }
            _statusMessage = status;
            if (!sameOwner)
            {
                _urlState = new();
                navigationManager.NavigateTo("/players", replace: true);
            }
        }
        StateHasChanged();
        await ReconcileLocationAsync();
    }

    private void ResetIdentityState()
    {
        ++_rosterVersion;
        ++_summaryVersion;
        ++_tagsVersion;
        ++_routeVersion;
        CancelSource(ref _searchDebounceSource);
        CancelSource(ref _rosterSource);
        CancelSource(ref _formSource);
        _roster = null;
        _summary = null;
        _availableTags = [];
        _pageError = _summaryError = _tagsError = _mutationError = _statusMessage = null;
        _searchDraft = string.Empty;
        _pendingCreate = null;
        _pendingCreationError = null;
        _creationDuplicate = null;
        _createForm = PlayerFormState.CreateDefault();
        ClearMutationForm();
        CancelArchive();
        _isMutating = _isLoading = _summaryLoading = _tagsLoading = _formLoading = false;
        _locationKey = _loadedQuery = null;
        _metadataStarted = false;
        ClearPersistedState();
    }

    private void ClearPersistedState()
    {
        PersistedRoster = null;
        PersistedSummary = null;
        PersistedTags = null;
        PersistedPageError = PersistedSummaryError = PersistedTagsError = null;
        SnapshotScope = SnapshotQuery = null;
        Initialized = false;
    }

    private async Task ReconcileLocationAsync()
    {
        if (!_identityApplied || ComponentCancellationToken.IsCancellationRequested) { return; }
        var uri = new Uri(navigationManager.Uri);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!string.Equals(path, "/players", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, "/players/new", StringComparison.OrdinalIgnoreCase)
            && !(path.StartsWith("/players/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/edit", StringComparison.OrdinalIgnoreCase)))
        {
            // A mounted bUnit host has no Router; real navigation disposes this page on other routes.
            return;
        }
        var state = PlayersUrlState.FromUri(navigationManager.Uri);
        var key = path + state.ToDirectoryUrl();
        if (string.Equals(_locationKey, key, StringComparison.Ordinal)) { return; }
        _locationKey = key;
        var routeVersion = ++_routeVersion;
        CancelSource(ref _searchDebounceSource);
        CancelSource(ref _formSource);
        _urlState = state;
        _searchDraft = state.Search;
        _showCreateForm = string.Equals(path, "/players/new", StringComparison.OrdinalIgnoreCase);
        _isEditRoute = path.EndsWith("/edit", StringComparison.OrdinalIgnoreCase);
        _editForm = null;
        _mutationError = _showCreateForm && _pendingCreate is not null ? _pendingCreationError : null;
        _creationDuplicate = null;
        _graduationYearBlockers = [];
        _formLoading = false;
        CancelArchive();

        if (!_canManagePlayers)
        {
            ClearPersistedState();
            _pageError = "You must join a club before viewing the player roster.";
            PersistStartupState();
            return;
        }
        var reads = StartDirectoryReads(state);
        if (_isEditRoute)
        {
            var parts = path.Split('/');
            if (parts.Length == 4 && long.TryParse(parts[2], CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                reads.Add(LoadEditAsync(id, routeVersion));
            }
            else { _mutationError = "This player could not be found."; }
        }
        StateHasChanged();
        await Task.WhenAll(reads);
    }

    private List<Task> StartDirectoryReads(PlayersUrlState state)
    {
        var reads = new List<Task>();
        if (!_metadataStarted)
        {
            _metadataStarted = true;
            if (Initialized && string.Equals(SnapshotScope, CurrentScope, StringComparison.Ordinal)
                && string.Equals(SnapshotQuery, state.QueryFingerprint, StringComparison.Ordinal))
            {
                _roster = PersistedRoster;
                _summary = PersistedSummary;
                _availableTags = PersistedTags ?? [];
                _pageError = PersistedPageError;
                _summaryError = PersistedSummaryError;
                _tagsError = PersistedTagsError;
                _loadedQuery = state.QueryFingerprint;
            }
            else
            {
                ClearPersistedState();
                reads.Add(LoadSummaryAsync());
                reads.Add(LoadTagsAsync());
            }
        }
        if (!string.Equals(_loadedQuery, state.QueryFingerprint, StringComparison.Ordinal))
        {
            reads.Add(LoadRosterAsync());
        }
        return reads;
    }

    private static async Task<ServiceResult<T>> ReceiveAsync<T>(Task<ServiceResult<T>> request)
    {
        try { return await request; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return ServiceProblem.ServerError("The request did not return a result. Retry to check the latest state.");
        }
    }

    private static long? ReadClubIdClaim(ClaimsPrincipal principal)
        => long.TryParse(principal.FindFirst(NovaClaimTypes.ClubId)?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    private async Task LoadRosterAsync()
    {
        if (!_canManagePlayers || _clubId is not { } clubId) { return; }
        var version = ++_rosterVersion;
        CancelSource(ref _rosterSource);
        _rosterSource = CancellationTokenSource.CreateLinkedTokenSource(_identitySource!.Token);
        var token = _rosterSource.Token;
        _roster = null;
        _pageError = null;
        _loadedQuery = _urlState.QueryFingerprint;
        if (!_urlState.IsSearchValid) { _isLoading = false; PersistStartupState(); return; }
        _isLoading = true;
        var result = await ReceiveAsync(playerService.GetPlayerRosterAsync(new GetPlayerRosterInput
        {
            ClubId = clubId,
            Search = _urlState.Search,
            LifecycleStatus = _urlState.View,
            GraduationYear = _urlState.GraduationYear,
            PlayerTagId = _urlState.TagId,
            Page = _urlState.Page,
            PageSize = RosterPageSize
        }, token));
        if (version != _rosterVersion || token.IsCancellationRequested) { return; }
        result.Switch(roster => _roster = roster, problem =>
        {
            _pageError = problem.Detail ?? "Players are unavailable. Please retry.";
            if (problem.Kind == ServiceProblemKind.Forbidden) { navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true); }
        });
        _isLoading = false;
        PersistStartupState();
    }

    private async Task LoadSummaryAsync()
    {
        if (!_canManagePlayers || _clubId is not { } clubId) { return; }
        var version = ++_summaryVersion;
        var identity = _identityVersion;
        var token = _identitySource!.Token;
        _summaryLoading = true;
        _summaryError = null;
        var result = await ReceiveAsync(playerService.GetPlayerDirectorySummaryAsync(new GetPlayerDirectorySummaryInput { ClubId = clubId }, token));
        if (identity != _identityVersion || version != _summaryVersion || token.IsCancellationRequested) { return; }
        result.Switch(summary => _summary = summary, problem =>
        {
            _summary = null;
            _summaryError = problem.Detail ?? "Club totals and graduation years are unavailable.";
        });
        _summaryLoading = false;
        PersistStartupState();
    }

    private async Task LoadTagsAsync()
    {
        if (!_canManagePlayers) { return; }
        var version = ++_tagsVersion;
        var identity = _identityVersion;
        var token = _identitySource!.Token;
        _tagsLoading = true;
        _tagsError = null;
        var result = await ReceiveAsync(tagDefinitionQueryService.GetChoicesAsync(token));
        if (identity != _identityVersion || version != _tagsVersion || token.IsCancellationRequested) { return; }
        result.Switch(tags => _availableTags = tags, problem =>
        {
            _availableTags = [];
            _tagsError = problem.Detail ?? "Tag choices are unavailable.";
        });
        _tagsLoading = false;
        PersistStartupState();
    }

    private void PersistStartupState()
    {
        PersistedRoster = _roster;
        PersistedSummary = _summary;
        PersistedTags = _availableTags;
        PersistedPageError = _pageError;
        PersistedSummaryError = _summaryError;
        PersistedTagsError = _tagsError;
        SnapshotScope = CurrentScope;
        SnapshotQuery = _urlState.QueryFingerprint;
        Initialized = !_isLoading && !_summaryLoading && !_tagsLoading;
        StateHasChanged();
    }

    private async Task RefreshDirectoryAsync() => await Task.WhenAll(LoadRosterAsync(), LoadSummaryAsync());
    private Task ReloadAsync() => LoadRosterAsync();

    private async Task OnSearchInputChangedAsync(ChangeEventArgs args)
    {
        _searchDraft = args.Value?.ToString() ?? string.Empty;
        CancelSource(ref _searchDebounceSource);
        _searchDebounceSource = CancellationTokenSource.CreateLinkedTokenSource(_identitySource!.Token);
        var token = _searchDebounceSource.Token;
        try { await Task.Delay(SearchDebounceMilliseconds, token); }
        catch (OperationCanceledException) { return; }
        if (!token.IsCancellationRequested)
        {
            NavigateDirectory(_urlState with { Search = _searchDraft.Trim(), Page = 1 }, replace: true);
        }
    }

    private void ApplyDiscovery() => NavigateDirectory(_urlState with { Search = _searchDraft.Trim(), Page = 1 });
    private void OnGraduationYearChanged(ChangeEventArgs args)
        => NavigateDirectory(_urlState with
        {
            GraduationYear = PlayersUrlState.Parse(graduationYear: args.Value?.ToString()).GraduationYear,
            Search = _searchDraft.Trim(),
            Page = 1
        });
    private void OnTagFilterChanged(ChangeEventArgs args)
        => NavigateDirectory(_urlState with
        {
            TagId = PlayersUrlState.Parse(tag: args.Value?.ToString()).TagId,
            Search = _searchDraft.Trim(),
            Page = 1
        });
    private void NavigateDirectory(PlayersUrlState state, bool replace = false)
    {
        CancelSource(ref _searchDebounceSource);
        navigationManager.NavigateTo(state.ToDirectoryUrl(), replace: replace);
    }

    private void CancelMutationForm() => navigationManager.NavigateTo(_urlState.ToDirectoryUrl());
    private void ClearMutationForm()
    {
        _showCreateForm = _isEditRoute = false;
        _editForm = null;
        _mutationError = null;
        _creationDuplicate = null;
        _graduationYearBlockers = [];
    }

    private Task RetryEditAsync()
    {
        var parts = new Uri(navigationManager.Uri).AbsolutePath.Split('/');
        return parts.Length == 4 && long.TryParse(parts[2], CultureInfo.InvariantCulture, out var id)
            ? LoadEditAsync(id, ++_routeVersion) : Task.CompletedTask;
    }

    private string EmptyHeading => (_urlState, _summary) switch
    {
        ({ Page: > 1 }, _) => "This page is unavailable",
        ({ HasFilters: true }, _) => "No matching players",
        (_, { ActiveCount: 0, ArchivedCount: 0 }) => "Your club has no players yet",
        ({ View: "active" }, { ActiveCount: 0, ArchivedCount: > 0 }) => "All players are archived",
        ({ View: "archived" }, _) => "No archived players",
        _ => "No active players"
    };

    private string EmptyDescription => (_urlState, _summary) switch
    {
        ({ Page: > 1 }, _) => "Players may have changed since this link was saved. Start from the first page.",
        ({ HasFilters: true }, _) => "Try another search or clear the filters.",
        (_, { ActiveCount: 0, ArchivedCount: 0 }) => "Add a player to start your club directory.",
        ({ View: "active" }, { ActiveCount: 0, ArchivedCount: > 0 }) => "Open Archived to find and restore a player.",
        (_, null) => "There are no results in this view. Club totals are unavailable.",
        _ => "There are no players in this view."
    };

    private string ClubCountLabel => (_summaryLoading, _summary) switch
    {
        (true, _) => "Loading club totals…",
        (_, null) => "Club totals unavailable",
        _ => "Club players"
    };

    private string ResultSummary => (_isLoading, _roster) switch
    {
        (true, _) => "Loading players…",
        (_, null) => "Results unavailable",
        (_, { } roster) => $"{roster.TotalCount} matching players · 20 per page"
    };

    private async Task LoadEditAsync(long playerId, int version)
    {
        CancelSource(ref _formSource);
        _formSource = CancellationTokenSource.CreateLinkedTokenSource(_identitySource!.Token);
        var token = _formSource.Token;
        _formLoading = true;
        _mutationError = null;
        var result = await ReceiveAsync(playerDetailService.GetPlayerDetailAsync(playerId, token));
        if (version != _routeVersion || token.IsCancellationRequested) { return; }
        result.Switch(detail => _editForm = PlayerFormState.FromDetail(detail),
            problem => _mutationError = problem.Detail ?? "Could not load player details for editing.");
        _formLoading = false;
        StateHasChanged();
    }

    /// <summary>
    /// Creates a new player and refreshes the roster.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task CreatePlayerAsync()
    {
        if (!_canManagePlayers || _isMutating)
        {
            return;
        }
        var version = _identityVersion;
        _isMutating = true;
        _mutationError = null;
        _graduationYearBlockers = [];

        if (_clubId is not long clubId) { _isMutating = false; return; }
        _pendingCreate ??= _createForm.ToCreateInput(Guid.CreateVersion7(), clubId);
        var command = _pendingCreate;
        _creationDuplicate = null;
        var result = await ReceiveAsync(playerManagementService.CreateAsync(command, _identitySource!.Token));
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        var ownsForm = _showCreateForm && _pendingCreate?.OperationId == command.OperationId;
        result.Switch(
            completion =>
            {
                _pendingCreate = null;
                _pendingCreationError = null;
                if (ownsForm) { _showCreateForm = false; }
                _createForm = PlayerFormState.CreateDefault();
                _statusMessage = completion.Enrollment is { } enrollment
                    ? $"Player created successfully. Enrolled in {enrollment.CampaignName}."
                    : "Player created successfully. Ready for the next campaign opening.";
            },
            problem =>
            {
                var error = problem.Detail ?? "Could not create player.";
                if (PlayerCreationProblems.IsNotCommitted(problem, command.OperationId))
                {
                    _pendingCreate = null;
                    _pendingCreationError = null;
                    if (ownsForm) { PlayerCreationProblems.TryGetDuplicate(problem, out _creationDuplicate); }
                }
                else
                {
                    error += PlayerCreationProblems.IsExpired(problem)
                        ? " The original addition is still retained."
                        : " The original addition is still retained; retry it unchanged to recover its result.";
                    _pendingCreationError = error;
                }
                if (ownsForm) { _mutationError = error; }
            });

        _isMutating = false;
        if (result.IsSuccess)
        {
            if (ownsForm) { CancelMutationForm(); }
            await RefreshDirectoryAsync();
        }
    }

    /// <summary>
    /// Saves edits for an existing player and refreshes the roster.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task UpdatePlayerAsync()
    {
        if (!_canManagePlayers || _isMutating || _editForm is null)
        {
            return;
        }

        _isMutating = true;
        _mutationError = null;
        _graduationYearBlockers = [];

        var version = _identityVersion;
        var route = _routeVersion;
        var result = await ReceiveAsync(playerManagementService.UpdateAsync(_editForm.ToUpdateInput(), _identitySource!.Token));
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (route != _routeVersion)
        {
            _isMutating = false;
            if (result.IsSuccess) { await RefreshDirectoryAsync(); }
            return;
        }
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
            if (route == _routeVersion) { CancelMutationForm(); }
            await RefreshDirectoryAsync();
        }
    }

    /// <summary>
    /// Sets the archive target and opens archive confirmation state.
    /// </summary>
    /// <param name="player">The selected player.</param>
    private void BeginArchive(PlayerListItem player)
    {
        if (!_canManagePlayers || _isMutating)
        {
            return;
        }
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
        if (!_canManagePlayers || _isMutating || _archiveCandidate is null || !_archiveConfirmed)
        {
            return;
        }

        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var version = _identityVersion;
        var route = _routeVersion;
        var result = await ReceiveAsync(playerLifecycleService.ArchiveAsync(_archiveCandidate.PlayerId, _identitySource!.Token));
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (route != _routeVersion)
        {
            _isMutating = false;
            if (result.IsSuccess) { await RefreshDirectoryAsync(); }
            return;
        }
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
            await RefreshDirectoryAsync();
        }
    }

    /// <summary>
    /// Restores an archived player and refreshes the roster.
    /// </summary>
    /// <param name="player">The archived player to restore.</param>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task RestorePlayerAsync(PlayerListItem player)
    {
        if (!_canManagePlayers || _isMutating)
        {
            return;
        }
        var version = _identityVersion;
        var route = _routeVersion;
        _isMutating = true;
        _mutationError = null;

        var result = await ReceiveAsync(playerLifecycleService.RestoreAsync(player.PlayerId, _identitySource!.Token));
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (route != _routeVersion)
        {
            _isMutating = false;
            if (result.IsSuccess) { await RefreshDirectoryAsync(); }
            return;
        }
        result.Switch(
            _ => _statusMessage = "Player restored. Missed campaign enrollment is not backfilled automatically.",
            problem => _mutationError = problem.Detail ?? "Could not restore player.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await RefreshDirectoryAsync();
        }
    }

    private string BuildPlayerDetailUrl(long playerId) => _urlState.ToPlayerUrl(playerId);
    private string BuildCurrentRosterUrl() => _urlState.ToDirectoryUrl();

    /// <summary>
    /// Builds the inline CSS style string for one roster tag pill.
    /// </summary>
    /// <param name="tag">The tag to style.</param>
    /// <returns>An inline CSS style string.</returns>
    private static string BuildTagStyle(PlayerRosterTagItem tag)
        => PlayerTagStyle.BuildBadgeStyle(tag.Color);

    /// <summary>
    /// Extracts structured graduation-year blockers from a conflict error payload.
    /// </summary>
    /// <param name="errors">The service-problem errors dictionary.</param>
    /// <returns>A parsed list of blocker items, or an empty list when unavailable.</returns>
#pragma warning disable CA1859 // The helper returns both an empty array and a read-only list; the interface describes both results.
    private static IReadOnlyList<GraduationYearBlockerItem> ExtractGraduationYearBlockers(
#pragma warning restore CA1859
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

        var closeBracketIndex = key.IndexOf(']', StringComparison.Ordinal);
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

    private static void CancelSource(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        ++_identityVersion;
        ++_rosterVersion;
        ++_summaryVersion;
        ++_tagsVersion;
        ++_routeVersion;
        ++_authenticationVersion;
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        navigationManager.LocationChanged -= OnLocationChanged;
        CancelSource(ref _searchDebounceSource);
        CancelSource(ref _rosterSource);
        CancelSource(ref _formSource);
        CancelSource(ref _identitySource);
        return base.DisposeAsyncCore();
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


#pragma warning restore CA1849, S6966

#pragma warning restore CA1724
