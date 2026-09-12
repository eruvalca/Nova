#pragma warning disable CA1055, CA1849, S6966 // Route strings are consumed by Razor attributes and NavigationManager; cancellation callbacks finish before replacing or disposing request state.
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;

namespace Nova.UI.Features.Seasons.Pages;

/// <summary>
/// Renders the club seasons directory: the current season as the lit stop of one season route,
/// then bounded, deterministically ordered past seasons with URL-backed paging.
/// </summary>
/// <param name="seasonQueryService">Reads tenant-safe season pages.</param>
/// <param name="authenticationStateProvider">Signals club and role changes that invalidate the loaded directory.</param>
/// <param name="navigationManager">Keeps the requested season page canonical in the URL and routes forbidden reads.</param>
public partial class SeasonDirectory(
    ISeasonQueryService seasonQueryService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager)
{
    /// <summary>Seasons requested per history page; the shared contract bounds this at 50.</summary>
    private const int HistoryPageSize = GetSeasonListInput.DefaultPageSize;

    /// <summary>Current season of the club in the current batch, or null when the club has none.</summary>
    private SeasonSummary? _currentSeason;

    /// <summary>
    /// Recorded season count reported by the current-season read. Zero is the first-season state,
    /// which is stated explicitly rather than inferred from an empty history list.
    /// </summary>
    private int _currentSeasonCount;

    /// <summary>Loaded history page, or null before the history region has ever loaded.</summary>
    private SeasonPageResult? _historyPage;

    /// <summary>Past seasons on the loaded page, derived from the loaded history page.</summary>
    private IReadOnlyList<SeasonSummary> _pastSeasons = [];

    /// <summary>Whether the loaded history page carried the current season, which the route shows separately.</summary>
    private bool _historyExcludedCurrent;

    /// <summary>One-based season page loaded into the history region.</summary>
    private int _page = GetSeasonListInput.DefaultPage;

    /// <summary>Error for the current-season region, or null when it loaded.</summary>
    private string? _currentError;

    /// <summary>Error for the season-history region, or null when it loaded.</summary>
    private string? _historyError;

    /// <summary>Whether the current-season region is loading.</summary>
    private bool _currentLoading;

    /// <summary>Whether the season-history region is loading.</summary>
    private bool _historyLoading;

    /// <summary>Whether the authenticated principal holds the club-administrator role.</summary>
    private bool _isClubAdmin;

    /// <summary>User, club, and role identity the loaded directory belongs to.</summary>
    private string? _identityScope;

    /// <summary>Monotonic generation for authentication changes that supersede in-flight loads.</summary>
    private int _authenticationVersion;

    /// <summary>
    /// Monotonic generation for reload batches: a slower earlier load must never overwrite results
    /// for a club or authority that a later authentication change superseded.
    /// </summary>
    private int _reloadVersion;

    /// <summary>Cancellation source for the current reload batch; starting a batch supersedes the prior one.</summary>
    private CancellationTokenSource? _reloadSource;

    /// <summary>
    /// Per-region retry sources so a retry or page change in one region never cancels the other,
    /// and a newer request in the same region supersedes the older one by cancellation.
    /// </summary>
    private CancellationTokenSource? _currentRetrySource;
    private CancellationTokenSource? _historyRetrySource;

    /// <summary>Raw season page from the URL, normalized before use.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public string? DirectoryPage { get; set; }

    /// <summary>Current season persisted across prerender/interactive hops.</summary>
    [PersistentState] public SeasonSummary? PersistedCurrentSeason { get; set; }

    /// <summary>Recorded season count persisted across prerender/interactive hops.</summary>
    [PersistentState] public int PersistedCurrentSeasonCount { get; set; }

    /// <summary>Loaded history page persisted across prerender/interactive hops.</summary>
    [PersistentState] public SeasonPageResult? PersistedHistoryPage { get; set; }

    /// <summary>Season page the persisted history payload or error describes.</summary>
    [PersistentState] public int PersistedPage { get; set; }

    /// <summary>Current-season error persisted across prerender/interactive hops.</summary>
    [PersistentState] public string? PersistedCurrentError { get; set; }

    /// <summary>Season-history error persisted across prerender/interactive hops.</summary>
    [PersistentState] public string? PersistedHistoryError { get; set; }

    /// <summary>Identity scope the persisted directory belongs to.</summary>
    [PersistentState] public string? PersistedIdentityScope { get; set; }

    /// <summary>Whether the directory completed at least one load for the persisted identity.</summary>
    [PersistentState] public bool Initialized { get; set; }

    /// <summary>Total recorded seasons reported by the loaded history page.</summary>
    protected int TotalSeasonCount => _historyPage?.TotalCount ?? 0;

    /// <summary>Last season page, always at least one so the pager never divides by zero.</summary>
    protected int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalSeasonCount / (double)HistoryPageSize));

    /// <summary>Whether the requested page lies beyond the last recorded season page.</summary>
    protected bool PageBeyondHistory => _page > TotalPages;

    /// <summary>Whether the club has recorded no season at all, which is the first-season state.</summary>
    protected bool HasNoRecordedSeason => _currentSeasonCount == 0;

    /// <summary>
    /// Label for an absent current season, stating the first-season state separately from the
    /// recovery state where recorded seasons exist without a current one.
    /// </summary>
    protected string AbsentSeasonLabel
        => HasNoRecordedSeason ? "No season has been established yet" : "No current season";

    /// <summary>Role-shaped explanation for an absent current season.</summary>
    protected string AbsentSeasonExplanation
    {
        get
        {
            if (HasNoRecordedSeason)
            {
                return _isClubAdmin
                    ? "Establish the club's first season, including when you create its first campaign."
                    : "A club administrator establishes the club's first season.";
            }

            return _isClubAdmin
                ? $"{DescribeRecordedSeasons(_currentSeasonCount)} remain and no current season is set. Start next season establishes one."
                : $"{DescribeRecordedSeasons(_currentSeasonCount)} remain and no current season is set. A club administrator establishes the next one.";
        }
    }

    /// <summary>Creates the canonical URL for the first season page.</summary>
    /// <returns>The first directory page URL.</returns>
    protected static string FirstPageUrl() => PageUrl(GetSeasonListInput.DefaultPage);

    /// <summary>Creates the canonical URL for one season page, keeping page one free of a query string.</summary>
    /// <param name="page">The one-based season page.</param>
    /// <returns>The directory URL for that page.</returns>
    protected static string PageUrl(int page) => page <= GetSeasonListInput.DefaultPage
        ? ClubRoutes.Seasons
        : $"{ClubRoutes.Seasons}?page={page.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Creates the route for one season's record.</summary>
    /// <param name="seasonId">The season identifier.</param>
    /// <returns>The season-detail URL.</returns>
    protected static string SeasonUrl(long seasonId) => ClubRoutes.SeasonDetail(seasonId);

    /// <summary>Describes a season count for the current and recovery states.</summary>
    /// <param name="count">The recorded season count.</param>
    /// <returns>A readable count phrase.</returns>
    protected static string DescribeRecordedSeasons(int count)
        => count == 1 ? "1 recorded season" : $"{count} recorded seasons";

    /// <summary>Formats a season date window using the current culture, with "onward" for an open end.</summary>
    /// <param name="start">Start of the window.</param>
    /// <param name="end">End of the window, or null when open-ended.</param>
    /// <returns>The formatted date window.</returns>
    protected static string FormatDateWindow(DateOnly start, DateOnly? end)
        => end is null
            ? $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} onward"
            : $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – {end.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";

    /// <summary>Subscribes to authentication changes so club and role changes invalidate the directory.</summary>
    protected override void OnInitialized()
        => authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var authenticationVersion = _authenticationVersion;
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (authenticationVersion != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _isClubAdmin = state.User.IsInRole(Roles.ClubAdmin);
        _identityScope = DirectoryIdentity(state);
        // SupplyParameterFromQuery properties are assigned before initialization, so the requested
        // page is available before the startup load and its persisted snapshot.
        var requestedPage = NormalizeRequestedPage();

        if (Initialized && string.Equals(PersistedIdentityScope, _identityScope, StringComparison.Ordinal))
        {
            RestorePersistedState();
            var restoredPage = _page;
            _page = requestedPage;
            SyncPageToUrl();
            // A persisted page or error for this page is already this pass's history state. Re-requesting
            // either one is the duplicate startup fetch PersistentState exists to prevent, so a recorded
            // failure for the requested page counts as initialized exactly like a successful payload.
            if (restoredPage == requestedPage && (_historyPage is not null || _historyError is not null))
            {
                return;
            }

            await ReloadHistoryPageAsync();
            PersistState();
            return;
        }

        _page = requestedPage;
        await ReloadAsync();
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var requestedPage = NormalizeRequestedPage();
        if (requestedPage == _page)
        {
            SyncPageToUrl();
            return;
        }

        _page = requestedPage;
        SyncPageToUrl();
        if (!Initialized)
        {
            // OnInitializedAsync owns the first load, including this page.
            return;
        }

        await ReloadHistoryPageAsync();
        PersistState();
    }

    /// <summary>Reloads both regions for the current identity scope as one superseding batch.</summary>
    private async Task ReloadAsync()
    {
        var (version, requestToken) = BeginReloadBatch();
        _currentLoading = _historyLoading = true;
        await Task.WhenAll(
            LoadCurrentAsync(version, requestToken),
            LoadHistoryAsync(version, requestToken));
        if (IsCurrentBatch(version, requestToken))
        {
            Initialized = true;
            PersistState();
        }
    }

    /// <summary>
    /// Loads the current season from its own bounded read, so currentness never depends on which
    /// history page is loaded.
    /// </summary>
    /// <param name="version">Batch version this load belongs to.</param>
    /// <param name="requestToken">Cancellation token for this load's request.</param>
    private async Task LoadCurrentAsync(int version, CancellationToken requestToken)
    {
        _currentLoading = true;
        _currentError = null;
        try
        {
            var result = await seasonQueryService.ListAsync(
                new GetSeasonListInput { Page = GetSeasonListInput.DefaultPage, PageSize = 1 }, requestToken);
            if (IsCurrentBatch(version, requestToken))
            {
                await ApplyResultAsync(result, page =>
                {
                    _currentSeasonCount = page.TotalCount;
                    _currentSeason = page.Items.FirstOrDefault(season => season.IsCurrent);
                }, message => _currentError = message, "The current season is unavailable.");
            }
            if (IsCurrentBatch(version, requestToken))
            {
                _currentLoading = false;
            }
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            if (IsCurrentBatch(version, requestToken))
            {
                _currentLoading = false;
                _currentError = "The current season is unavailable. Retry this section.";
            }
        }
    }

    /// <summary>Loads the requested history page for the given batch, applying results only when current.</summary>
    /// <param name="version">Batch version this load belongs to.</param>
    /// <param name="requestToken">Cancellation token for this load's request.</param>
    private async Task LoadHistoryAsync(int version, CancellationToken requestToken)
    {
        _historyLoading = true;
        _historyError = null;
        try
        {
            var result = await seasonQueryService.ListAsync(
                new GetSeasonListInput { Page = _page, PageSize = HistoryPageSize }, requestToken);
            if (IsCurrentBatch(version, requestToken))
            {
                await ApplyResultAsync(result, ApplyHistoryPage, message => _historyError = message,
                    "Season history is unavailable.");
            }
            if (IsCurrentBatch(version, requestToken))
            {
                _historyLoading = false;
            }
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            if (IsCurrentBatch(version, requestToken))
            {
                _historyLoading = false;
                _historyError = "Season history is unavailable. Retry this section.";
            }
        }
    }

    /// <summary>
    /// Stores the loaded history page and rebuilds the derived past-season rows, so the current
    /// season appears once on the route and never twice.
    /// </summary>
    /// <param name="page">The loaded history page.</param>
    private void ApplyHistoryPage(SeasonPageResult page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _historyPage = page;
        _historyExcludedCurrent = page.Items.Any(season => season.IsCurrent);
        _pastSeasons = [.. page.Items.Where(season => !season.IsCurrent)];
    }

    /// <summary>Re-runs only the current-season read with a fresh region token.</summary>
    private async Task RetryCurrentAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _currentRetrySource);
        await LoadCurrentAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }

    /// <summary>
    /// Reloads only the season-history region for the loaded page under a fresh region request, so a
    /// newer page supersedes an older one by cancellation.
    /// </summary>
    private async Task ReloadHistoryPageAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _historyRetrySource);
        await LoadHistoryAsync(version, requestToken);
    }

    /// <summary>Normalizes the requested season page, defaulting malformed or absent values to page one.</summary>
    /// <returns>The effective one-based season page.</returns>
    private int NormalizeRequestedPage()
        => int.TryParse(DirectoryPage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page > 0
            ? page
            : GetSeasonListInput.DefaultPage;

    /// <summary>Rewrites a malformed or non-canonical season page in the URL without adding a history entry.</summary>
    private void SyncPageToUrl()
    {
#pragma warning disable S1075 // This is a browser-relative URL path, not a filesystem path.
        var currentPath = "/" + navigationManager.ToBaseRelativePath(navigationManager.Uri)
            .Split(['?', '#'], StringSplitOptions.None)[0].TrimEnd('/');
#pragma warning restore S1075
        if (!string.Equals(currentPath, ClubRoutes.Seasons, StringComparison.OrdinalIgnoreCase))
        {
            // Another route owns the URL now (for example the access-denied recovery); never fight it.
            return;
        }

        var target = navigationManager.ToAbsoluteUri(PageUrl(_page)).ToString();
        if (!string.Equals(target, navigationManager.Uri, StringComparison.Ordinal))
        {
            navigationManager.NavigateTo(target, new NavigationOptions { ReplaceHistoryEntry = true });
        }
    }

    /// <summary>Routes a service result to a success or failure callback, navigating to access-denied on forbidden.</summary>
    /// <typeparam name="T">The successful result type.</typeparam>
    /// <param name="result">The service result to switch on.</param>
    /// <param name="success">Callback invoked with the value on success.</param>
    /// <param name="failure">Callback invoked with a message on failure.</param>
    /// <param name="fallback">Message used when the problem detail is empty.</param>
    private Task ApplyResultAsync<T>(ServiceResult<T> result, Action<T> success, Action<string> failure, string fallback)
    {
        result.Switch(success, problem =>
        {
            if (problem.Kind == ServiceProblemKind.Forbidden)
            {
                navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                return;
            }

            failure(string.IsNullOrWhiteSpace(problem.Detail) ? fallback : problem.Detail);
        });
        return Task.CompletedTask;
    }

    /// <summary>Persists the loaded directory and its errors for prerender/interactive hops.</summary>
    private void PersistState()
    {
        PersistedCurrentSeason = _currentSeason;
        PersistedCurrentSeasonCount = _currentSeasonCount;
        PersistedHistoryPage = _historyPage;
        PersistedPage = _page;
        PersistedCurrentError = _currentError;
        PersistedHistoryError = _historyError;
        PersistedIdentityScope = _identityScope;
    }

    /// <summary>Restores the persisted directory and rebuilds its derived rows.</summary>
    private void RestorePersistedState()
    {
        _currentSeason = PersistedCurrentSeason;
        _currentSeasonCount = PersistedCurrentSeasonCount;
        _currentError = PersistedCurrentError;
        _historyError = PersistedHistoryError;
        _page = PersistedPage > 0 ? PersistedPage : GetSeasonListInput.DefaultPage;
        if (PersistedHistoryPage is { } page)
        {
            ApplyHistoryPage(page);
        }
    }

    /// <summary>Derives the identity scope that owns the loaded directory.</summary>
    /// <param name="state">The current authentication state.</param>
    /// <returns>The user, club, and role scope.</returns>
    private static string DirectoryIdentity(AuthenticationState state) => string.Join(":",
        state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        state.User.FindFirst(NovaClaimTypes.ClubId)?.Value, state.User.IsInRole(Roles.ClubAdmin));

    /// <summary>Handles authentication-state changes by applying them on the renderer dispatcher.</summary>
    /// <param name="stateTask">Pending authentication-state resolution.</param>
    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = InvokeAsync(() => ApplyAuthenticationStateAsync(stateTask));

    /// <summary>
    /// Applies an authentication change: re-evaluates authority, and on a club or role change clears
    /// the previous scope's data before reloading so it can never repopulate the directory.
    /// </summary>
    /// <param name="stateTask">Pending authentication-state resolution.</param>
    private async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> stateTask)
    {
        var authenticationVersion = ++_authenticationVersion;
        var state = await stateTask;
        if (authenticationVersion != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        var identity = DirectoryIdentity(state);
        if (string.Equals(identity, _identityScope, StringComparison.Ordinal))
        {
            return;
        }

        _identityScope = identity;
        _isClubAdmin = state.User.IsInRole(Roles.ClubAdmin);
        _currentSeason = null;
        _currentSeasonCount = 0;
        _historyPage = null;
        _pastSeasons = [];
        _historyExcludedCurrent = false;
        _currentError = null;
        _historyError = null;
        _page = GetSeasonListInput.DefaultPage;
        var (version, requestToken) = BeginReloadBatch();
        _currentLoading = _historyLoading = true;
        SyncPageToUrl();
        // AuthenticationStateChanged is an external event, so render the cleared state
        // before the reload's first await to avoid showing the previous club's seasons.
        await InvokeAsync(StateHasChanged);
        await Task.WhenAll(
            LoadCurrentAsync(version, requestToken),
            LoadHistoryAsync(version, requestToken));
        if (IsCurrentBatch(version, requestToken))
        {
            Initialized = true;
            PersistState();
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Starts a new reload batch: increments the version, cancels the prior batch, and supersedes region requests.
    /// </summary>
    /// <returns>The batch version and its request token.</returns>
    private (int Version, CancellationToken Token) BeginReloadBatch()
    {
        var version = Interlocked.Increment(ref _reloadVersion);
        _reloadSource?.Cancel();
        _reloadSource?.Dispose();
        _reloadSource = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        SupersedeRegionRetries();
        return (version, _reloadSource.Token);
    }

    /// <summary>
    /// Cancels and disposes a region's request source, then creates a fresh one linked to the
    /// component lifetime, so a newer request in that region supersedes only an older request in
    /// the same region. Deliberately keeps the current batch version: per-region supersession is by
    /// cancellation, while the version marks a full batch superseding every region at once.
    /// </summary>
    /// <param name="source">The region's request source, replaced with a fresh one.</param>
    /// <returns>The current batch version and the fresh request token.</returns>
    private (int Version, CancellationToken Token) BeginRegionRetry(ref CancellationTokenSource? source)
    {
        var version = _reloadVersion;
        source?.Cancel();
        source?.Dispose();
        source = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        return (version, source.Token);
    }

    /// <summary>Cancels and disposes every region request source so a new batch owns all reloads.</summary>
    private void SupersedeRegionRetries()
    {
        _currentRetrySource?.Cancel();
        _currentRetrySource?.Dispose();
        _currentRetrySource = null;
        _historyRetrySource?.Cancel();
        _historyRetrySource?.Dispose();
        _historyRetrySource = null;
    }

    /// <summary>Whether the given batch is still the current one and its request is not cancelled.</summary>
    /// <param name="version">Batch version to compare.</param>
    /// <param name="requestToken">Request token to check.</param>
    /// <returns><see langword="true"/> when the response still owns the directory.</returns>
    private bool IsCurrentBatch(int version, CancellationToken requestToken)
        => version == _reloadVersion && !requestToken.IsCancellationRequested;

    /// <summary>Disposes cancellation sources and unsubscribes from authentication-state changes.</summary>
    protected override async ValueTask DisposeAsyncCore()
    {
        _reloadSource?.Cancel();
        _reloadSource?.Dispose();
        SupersedeRegionRetries();
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        await base.DisposeAsyncCore();
    }
}

#pragma warning restore CA1055
