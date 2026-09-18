#pragma warning disable CA1724 // The routed page shares its feature's namespace name.
#pragma warning disable CA1849, S6966 // Cancellation completes before replacing request ownership.
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.UI.Components;
using Nova.UI.Features.Players.Components;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players.Pages;

/// <summary>Owns the URL-backed directory and the manual intake board for one authenticated club.</summary>
public partial class Players(
    IPlayerService playerService,
    IPlayerManagementService playerManagementService,
    IPlayerLifecycleService playerLifecycleService,
    IPlayerDetailService playerDetailService,
    IPlayerIntakeContextService intakeContextService,
    IPlayerIntakeInterop intakeInterop,
    ITagDefinitionQueryService tagDefinitionQueryService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager,
    ILogger<Players> logger) : NovaComponentBase
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

    /// <summary>
    /// The club-and-member identity that owns route-scoped state. Capabilities are deliberately
    /// excluded: a role change is the same owner's view, so its transient state survives the refresh.
    /// </summary>
    private string CurrentOwner => $"{_userId}:{_clubId}";
    private string _searchDraft = string.Empty;
    private PlayersUrlState _urlState = new();
    private string? _locationKey;
    private string? _loadedQuery;

    /// <summary>The route path whose per-route reset was last applied.</summary>
    private string? _appliedRoutePath;

    /// <summary>The club-and-member owner the per-route reset was last applied for.</summary>
    private string? _appliedRouteOwner;
    private bool _metadataStarted;
    private PlayerFormState _createForm = PlayerFormState.CreateDefault();
    private PlayerFormState? _editForm;
    private bool _showCreateForm;
    private bool _isEditRoute;
    private bool _formLoading;
    private bool _interactive;
    private IReadOnlyList<GraduationYearBlockerItem> _graduationYearBlockers = [];
    private PlayerListItem? _archiveCandidate;
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

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The browser storage boundary only exists after interactive attachment.
            _interactive = true;
        }

        if (!_interactive || !_showCreateForm || !_canManagePlayers || _board is null)
        {
            return;
        }

        var scope = CurrentScope;
        if (string.Equals(_recoveryScope, scope, StringComparison.Ordinal))
        {
            return;
        }

        // Claim the scope before awaiting so a re-render cannot start a second read, and only once
        // the board has mounted: a read before that cannot reach its interop boundary.
        _recoveryScope = scope;
        await RestoreRecoveryAsync();
        StateHasChanged();
    }

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
        var intake = CaptureIntakeState(sameOwner);
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
            RestoreIntakeState(intake);
            if (!sameOwner)
            {
                _urlState = new();
                navigationManager.NavigateTo("/players", replace: true);
            }
        }
        StateHasChanged();
        await ReconcileLocationAsync();
    }

    /// <summary>
    /// The intake state one identity owns, carried across a same-owner refresh. A role-only change
    /// invalidates visible capabilities without settling retained or already settled work.
    /// </summary>
    /// <param name="Pending">The retained command, when one is held.</param>
    /// <param name="Recovery">The recovery state that command implies.</param>
    /// <param name="InvalidValue">The exact unreadable retained bytes, when they were inspected.</param>
    /// <param name="RetainedName">The retained command's player name.</param>
    /// <param name="Form">The board's form state.</param>
    /// <param name="Status">The page's status message.</param>
    /// <param name="Receipt">The settled receipt, when the operation has one.</param>
    /// <param name="UnreleasedOperationId">The settled operation whose record the browser still holds.</param>
    /// <param name="StorageUnavailable">Whether the browser's recovery storage was unavailable.</param>
    private sealed record IntakeState(
        CreatePlayerInput? Pending,
        PlayerCreationRecoveryState Recovery,
        string? InvalidValue,
        string? RetainedName,
        PlayerFormState Form,
        string? Status,
        PlayerCreationCompletion? Receipt,
        Guid? UnreleasedOperationId,
        bool StorageUnavailable);

    /// <summary>Captures the intake state this owner keeps across an identity refresh.</summary>
    /// <param name="sameOwner">Whether the refresh belongs to the same club and member.</param>
    /// <returns>The state to restore, with another owner's values already dropped.</returns>
    private IntakeState CaptureIntakeState(bool sameOwner)
        => new(
            sameOwner ? _pendingCreate : null,
            sameOwner ? _recoveryState : PlayerCreationRecoveryState.None,
            sameOwner ? _invalidRetainedValue : null,
            sameOwner ? _retainedPlayerName : null,
            _createForm,
            sameOwner ? _statusMessage : null,
            sameOwner ? _receipt : null,
            sameOwner ? _unreleasedOperationId : null,
            sameOwner && _storageUnavailable);

    /// <summary>
    /// Restores the captured intake state after the identity reset. A retained command keeps its bytes,
    /// and a settled receipt keeps its release retry, so neither a role-only refresh nor a document
    /// reload can present the member with a blank form over work that still needs an outcome.
    /// </summary>
    /// <param name="state">The state captured before the reset.</param>
    private void RestoreIntakeState(IntakeState state)
    {
        _pendingCreate = state.Pending;
        _recoveryState = state.Recovery;
        _invalidRetainedValue = state.InvalidValue;
        _retainedPlayerName = state.RetainedName;
        if (state.Pending is not null) { _createForm = state.Form; }
        _statusMessage = state.Status;
        _receipt = state.Receipt;
        _unreleasedOperationId = state.UnreleasedOperationId;
        _storageUnavailable = state.StorageUnavailable;
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
        _recoveryState = PlayerCreationRecoveryState.None;
        _invalidRetainedValue = null;
        _retainedPlayerName = null;
        _storageUnavailable = false;
        _unreleasedOperationId = null;
        _recoveryScope = null;
        _receipt = null;
        _fieldErrors = null;
        _intakeContext = null;
        _intakeContextUnavailable = false;
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
        var path = new Uri(navigationManager.Uri).AbsolutePath.TrimEnd('/');
        if (!IsDirectoryOrFormRoute(path))
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
        ApplyRouteState(path);
        if (!_canManagePlayers)
        {
            ClearPersistedState();
            _pageError = "You must join a club before viewing the player roster.";
            PersistStartupState();
            return;
        }
        StateHasChanged();
        await Task.WhenAll(StartRouteReads(state, path, routeVersion));
    }

    private static bool IsDirectoryOrFormRoute(string path)
        => string.Equals(path, "/players", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/players/new", StringComparison.OrdinalIgnoreCase)
            || (path.StartsWith("/players/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith("/edit", StringComparison.OrdinalIgnoreCase));

    /// <summary>Resets per-route feedback and transient mutation state at a real navigation boundary.</summary>
    private void ApplyRouteState(string path)
    {
        // Which route this is always derives from the path: the identity reset clears these flags with
        // the rest of the state, so the view must be re-derived even when nothing else is reset.
        _showCreateForm = string.Equals(path, "/players/new", StringComparison.OrdinalIgnoreCase);
        _isEditRoute = path.EndsWith("/edit", StringComparison.OrdinalIgnoreCase);

        // The transient resets belong to a real boundary: the same path applied again for the same
        // owner — an authentication refresh, or a repeated location notification — is the same view,
        // and resetting it would replace a settled receipt (with its release retry) with a blank form.
        var routeChanged = !string.Equals(_appliedRoutePath, path, StringComparison.OrdinalIgnoreCase);
        var ownerChanged = !string.Equals(_appliedRouteOwner, CurrentOwner, StringComparison.Ordinal);
        if (!routeChanged && !ownerChanged)
        {
            // A same-owner refresh re-runs this route's reads without resetting its transient state,
            // so the gates those reads need are armed here rather than at the boundary below: the
            // board must refuse input, and name the consequence as unread, until they settle.
            if (_showCreateForm)
            {
                _recoveryChecked = false;
                _recoveryScope = null;
                _intakeContextLoading = true;
            }

            return;
        }

        _appliedRoutePath = path;
        _appliedRouteOwner = CurrentOwner;
        _editForm = null;
        _receipt = null;
        _fieldErrors = null;
        _creationDuplicate = null;
        _graduationYearBlockers = [];
        _formLoading = false;
        _mutationError = null;
        // The route boundary is where the board first renders, and rendering is synchronous at this
        // boundary, so the consequence must be named as unread here rather than when the read starts.
        _intakeContextLoading = _showCreateForm;
        if (_showCreateForm)
        {
            // Every entry to the board re-reads the owner's retained command, and the board refuses
            // input until that read settles so a landed recovery cannot replace typed values.
            _recoveryScope = null;
            _recoveryChecked = false;
        }

        CancelArchive();
    }

    /// <summary>Starts the independent reads one route needs, including its form-specific evidence.</summary>
    private List<Task> StartRouteReads(PlayersUrlState state, string path, int routeVersion)
    {
        var reads = StartDirectoryReads(state);
        if (_showCreateForm)
        {
            reads.Add(LoadIntakeContextAsync());
        }
        if (_isEditRoute)
        {
            var parts = path.Split('/');
            if (parts.Length == 4 && long.TryParse(parts[2], CultureInfo.InvariantCulture, out var id) && id > 0)
            {
                reads.Add(LoadEditAsync(id, routeVersion));
            }
            else { _mutationError = "This player could not be found."; }
        }
        return reads;
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

    // Auto rendering calls server services directly as well as HTTP clients. Isolate read failures
    // here without broadening the mutation helper's settlement or recovery behavior.
    private async Task<ServiceResult<T>> ReceiveDirectoryReadAsync<T>(
        Func<Task<ServiceResult<T>>> request, string region, CancellationToken cancellationToken)
    {
        var userId = _userId;
        var clubId = _clubId;
        try { return await request(); }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested
            || exception is HttpRequestException or OperationCanceledException or DbException
            || exception.InnerException is DbException)
        {
            // Obsolete provider failures must not fault the new scope's rendering task either.
            cancellationToken.ThrowIfCancellationRequested();
            LogDirectoryReadFailed(exception, logger, region, userId, clubId);
            return ServiceProblem.ServerError("This part of the directory is unavailable. Please retry.");
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Player directory {Region} read failed for UserId={UserId}, ClubId={ClubId}.")]
    private static partial void LogDirectoryReadFailed(Exception exception, ILogger logger, string region, string? userId, long? clubId);

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
        var result = await ReceiveDirectoryReadAsync(() => playerService.GetPlayerRosterAsync(new GetPlayerRosterInput
        {
            ClubId = clubId,
            Search = _urlState.Search,
            LifecycleStatus = _urlState.View,
            GraduationYear = _urlState.GraduationYear,
            PlayerTagId = _urlState.TagId,
            Page = _urlState.Page,
            PageSize = RosterPageSize
        }, token), "roster", token);
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
        var result = await ReceiveDirectoryReadAsync(
            () => playerService.GetPlayerDirectorySummaryAsync(new GetPlayerDirectorySummaryInput { ClubId = clubId }, token), "summary", token);
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
        var result = await ReceiveDirectoryReadAsync(() => tagDefinitionQueryService.GetChoicesAsync(token), "tags", token);
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
        // Route flags belong to ApplyRouteState, which re-derives them from the path: clearing them
        // here would leave a same-owner refresh with no form to render and nothing to re-derive it.
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
        _archiveBlockers = [];
    }

    /// <summary>
    /// Archives the currently selected player after explicit user confirmation.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (!_canManagePlayers || _isMutating || _archiveCandidate is null)
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
                _statusMessage = PlayerLifecycleCopy.ArchivedResult;
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
            _ => _statusMessage = PlayerLifecycleCopy.RestoredResult,
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

}


#pragma warning restore CA1849, S6966

#pragma warning restore CA1724
