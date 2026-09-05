using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>
/// Renders the club overview: identity, current season, and active campaign per club,
/// with per-region retries and reload guards for club-claim changes.
/// </summary>
/// <param name="identityQueryService">Resolves the current club identity for the authenticated user.</param>
/// <param name="seasonQueryService">Lists seasons to find the current one.</param>
/// <param name="campaignQueryService">Lists campaigns to find active work.</param>
/// <param name="authenticationStateProvider">Signals club/role changes that trigger reloads.</param>
/// <param name="navigationManager">Navigates away on forbidden responses.</param>
public partial class ClubOverview(
    IClubIdentityQueryService identityQueryService,
    ISeasonQueryService seasonQueryService,
    ICampaignQueryService campaignQueryService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager)
{
    /// <summary>Identity of the club rendered by the current batch.</summary>
    private ClubIdentityResult? _identity;

    /// <summary>Current season of the club rendered by the current batch.</summary>
    private SeasonSummary? _currentSeason;

    /// <summary>Active campaigns rendered by the current batch.</summary>
    private CampaignListResult? _campaigns;

    /// <summary>Error for the identity section, or null when it loaded.</summary>
    private string? _identityError;

    /// <summary>Error for the season section, or null when it loaded.</summary>
    private string? _seasonError;

    /// <summary>Error for the campaign section, or null when it loaded.</summary>
    private string? _campaignError;

    /// <summary>Announcement text for the most recently finished section.</summary>
    private string? _announcement;

    /// <summary>Whether the identity section is actively loading.</summary>
    private bool _identityLoading;

    /// <summary>Whether the season section is actively loading.</summary>
    private bool _seasonLoading;

    /// <summary>Whether the campaign section is actively loading.</summary>
    private bool _campaignLoading;

    /// <summary>Whether the authenticated user is a club administrator.</summary>
    private bool _isClubAdmin;

    /// <summary>Club identifier claim of the authenticated user.</summary>
    private string? _clubIdText;

    /// <summary>
    /// Monotonic generation for reload batches: a slower earlier load must never overwrite
    /// results for a club that a later authentication change superseded.
    /// </summary>
    private int _reloadVersion;

    /// <summary>Cancellation source for the current reload batch; starting a new batch supersedes the prior one.</summary>
    private CancellationTokenSource? _reloadSource;

    /// <summary>
    /// Per-region cancellation sources so a retry of one section never cancels a concurrent
    /// retry or reload of another section, and so a second retry of the same section supersedes the first.
    /// </summary>
    private CancellationTokenSource? _identityRetrySource;
    private CancellationTokenSource? _seasonRetrySource;
    private CancellationTokenSource? _campaignRetrySource;

    /// <summary>Notice from the query string, shown as a dismissal announcement.</summary>
    [SupplyParameterFromQuery(Name = "notice")]
    public string? Notice { get; set; }

    /// <summary>Identity persisted across prerender/interactive hops, or null when uninitialized.</summary>
    [PersistentState] public ClubIdentityResult? PersistedIdentity { get; set; }

    /// <summary>Club identifier claim that the persisted state belongs to.</summary>
    [PersistentState] public string? PersistedClubId { get; set; }

    /// <summary>Season persisted across prerender/interactive hops, or null when uninitialized.</summary>
    [PersistentState] public SeasonSummary? PersistedSeason { get; set; }

    /// <summary>Campaigns persisted across prerender/interactive hops, or null when uninitialized.</summary>
    [PersistentState] public CampaignListResult? PersistedCampaigns { get; set; }

    /// <summary>Identity error persisted across prerender/interactive hops, or null.</summary>
    [PersistentState] public string? PersistedIdentityError { get; set; }

    /// <summary>Season error persisted across prerender/interactive hops, or null.</summary>
    [PersistentState] public string? PersistedSeasonError { get; set; }

    /// <summary>Campaign error persisted across prerender/interactive hops, or null.</summary>
    [PersistentState] public string? PersistedCampaignError { get; set; }

    /// <summary>Whether the overview has completed at least one load for the persisted club.</summary>
    [PersistentState] public bool Initialized { get; set; }

    /// <summary>Initials derived from the club name, or "Club" before identity loads.</summary>
    protected string ClubInitials => _identity is null
        ? "Club"
        : string.Concat(_identity.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));

    /// <summary>Selected active campaign and its season, or null when there is no active campaign.</summary>
    protected (CampaignListItem Campaign, string SeasonName)? ActiveCampaign
    {
        get
        {
            var season = _campaigns?.Seasons.FirstOrDefault(group => group.Campaigns.Count > 0);
            return season is null ? null : (season.Campaigns[0], season.Name);
        }
    }

    /// <summary>Subscribes to authentication-state changes so club/role changes trigger reloads.</summary>
    protected override void OnInitialized()
        => authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

    /// <summary>Resolves the initial auth state and loads each section for the current club.</summary>
    protected override async Task OnInitializedAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        _isClubAdmin = state.User.IsInRole(Roles.ClubAdmin);
        _clubIdText = state.User.FindFirst(NovaClaimTypes.ClubId)?.Value;
        if (Initialized)
        {
            RestorePersistedState();
            if (PersistedClubId is not null && PersistedClubId == _clubIdText)
            {
                return;
            }
        }

        var (version, requestToken) = BeginReloadBatch();
        _identityLoading = _seasonLoading = _campaignLoading = true;
        await Task.WhenAll(
            LoadIdentityAsync(version, requestToken),
            LoadSeasonAsync(version, requestToken),
            LoadCampaignAsync(version, requestToken));
        if (IsCurrentBatch(version, requestToken))
        {
            Initialized = true;
            PersistState();
        }
    }

    /// <summary>
    /// Loads the club identity for the given batch, applying results only when current.
    /// </summary>
    /// <param name="version">Batch version this load belongs to.</param>
    /// <param name="requestToken">Cancellation token for this load's request.</param>
    private async Task LoadIdentityAsync(int version, CancellationToken requestToken)
    {
        _identityLoading = true;
        _identityError = null;
        try
        {
            var result = await identityQueryService.GetCurrentAsync(requestToken);
            if (IsCurrentBatch(version, requestToken))
            {
                await ApplyResultAsync(result,
                    value => _identity = value, message => _identityError = message, "Club identity is unavailable.");
            }
            if (IsCurrentBatch(version, requestToken))
            {
                _identityLoading = false;
                _announcement = _identityError ?? "Club identity loaded.";
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
                _identityLoading = false;
                _identityError = "Club identity is unavailable. Retry this section.";
                _announcement = _identityError;
            }
        }
    }

    /// <summary>
    /// Loads the current season for the given batch, applying results only when current.
    /// </summary>
    /// <param name="version">Batch version this load belongs to.</param>
    /// <param name="requestToken">Cancellation token for this load's request.</param>
    private async Task LoadSeasonAsync(int version, CancellationToken requestToken)
    {
        _seasonLoading = true;
        _seasonError = null;
        try
        {
            var result = await seasonQueryService.ListAsync(new GetSeasonListInput { Page = 1, PageSize = 1 }, requestToken);
            if (IsCurrentBatch(version, requestToken))
            {
                await ApplyResultAsync(result,
                    value => _currentSeason = value.Items.FirstOrDefault(item => item.IsCurrent),
                    message => _seasonError = message, "Current season is unavailable.");
            }
            if (IsCurrentBatch(version, requestToken))
            {
                _seasonLoading = false;
                _announcement = _seasonError ?? "Current season loaded.";
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
                _seasonLoading = false;
                _seasonError = "Current season is unavailable. Retry this section.";
                _announcement = _seasonError;
            }
        }
    }

    /// <summary>
    /// Loads active campaigns for the given batch, applying results only when current.
    /// </summary>
    /// <param name="version">Batch version this load belongs to.</param>
    /// <param name="requestToken">Cancellation token for this load's request.</param>
    private async Task LoadCampaignAsync(int version, CancellationToken requestToken)
    {
        _campaignLoading = true;
        _campaignError = null;
        try
        {
            var result = await campaignQueryService.GetCampaignListAsync(
                new GetCampaignListInput { Status = "active", Limit = 1 }, requestToken);
            if (IsCurrentBatch(version, requestToken))
            {
                await ApplyResultAsync(result, value => _campaigns = value,
                    message => _campaignError = message, "Active work is unavailable.");
            }
            if (IsCurrentBatch(version, requestToken))
            {
                _campaignLoading = false;
                _announcement = _campaignError ?? "Active work loaded.";
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
                _campaignLoading = false;
                _campaignError = "Active work is unavailable. Retry this section.";
                _announcement = _campaignError;
            }
        }
    }

    /// <summary>
    /// Routes a service result to the success or failure callback, navigating to access-denied on forbidden.
    /// </summary>
    /// <param name="result">Service result to switch on.</param>
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

    /// <summary>Re-runs only the identity load with a fresh region token and persists on success.</summary>
    private async Task RetryIdentityAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _identityRetrySource);
        await LoadIdentityAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }

    /// <summary>Re-runs only the season load with a fresh region token and persists on success.</summary>
    private async Task RetrySeasonAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _seasonRetrySource);
        await LoadSeasonAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }

    /// <summary>Re-runs only the campaign load with a fresh region token and persists on success.</summary>
    private async Task RetryCampaignAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _campaignRetrySource);
        await LoadCampaignAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }

    /// <summary>Formats a date window using the current culture, with "onward" for an open end.</summary>
    /// <param name="start">Start of the window.</param>
    /// <param name="end">End of the window, or null when open-ended.</param>
    protected static string FormatDateWindow(DateOnly start, DateOnly? end)
        => end is null
            ? $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} onward"
            : $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – {end.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";

    /// <summary>Persists the current club's loaded state and errors for prerender/interactive hops.</summary>
    private void PersistState()
    {
        PersistedClubId = _clubIdText;
        PersistedIdentity = _identity;
        PersistedSeason = _currentSeason;
        PersistedCampaigns = _campaigns;
        PersistedIdentityError = _identityError;
        PersistedSeasonError = _seasonError;
        PersistedCampaignError = _campaignError;
    }

    /// <summary>Restores the persisted club state, skipping when it belongs to a different club.</summary>
    private void RestorePersistedState()
    {
        _identity = PersistedIdentity;
        _currentSeason = PersistedSeason;
        _campaigns = PersistedCampaigns;
        _identityError = PersistedIdentityError;
        _seasonError = PersistedSeasonError;
        _campaignError = PersistedCampaignError;
    }

    /// <summary>Handles authentication-state changes by applying them on the renderer dispatcher.</summary>
    /// <param name="stateTask">Pending authentication-state resolution.</param>
    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = InvokeAsync(() => ApplyAuthenticationStateAsync(stateTask));

    /// <summary>
    /// Applies an authentication-state change: updates role/club claims and, on club change,
    /// reloads every section and renders the loading state before the first await.
    /// </summary>
    /// <param name="stateTask">Pending authentication-state resolution.</param>
    private async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> stateTask)
    {
        var state = await stateTask;
        var isClubAdmin = state.User.IsInRole(Roles.ClubAdmin);
        var clubIdText = state.User.FindFirst(NovaClaimTypes.ClubId)?.Value;
        var clubChanged = clubIdText != _clubIdText;
        _isClubAdmin = isClubAdmin;
        _clubIdText = clubIdText;
        if (clubChanged)
        {
            var (version, requestToken) = BeginReloadBatch();
            _identityLoading = _seasonLoading = _campaignLoading = true;
            // AuthenticationStateChanged is an external event, so render the loading state
            // before the reload's first await to avoid showing the previous club's data.
            await InvokeAsync(StateHasChanged);
            await Task.WhenAll(
                LoadIdentityAsync(version, requestToken),
                LoadSeasonAsync(version, requestToken),
                LoadCampaignAsync(version, requestToken));
            if (IsCurrentBatch(version, requestToken))
            {
                PersistState();
            }
        }
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Starts a new reload batch: increments the version, cancels the prior batch, and supersedes region retries.
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
    /// Cancels and disposes a region's own retry source, then creates a fresh one linked to the
    /// component lifetime, so a retry of that region supersedes only an earlier retry of the
    /// same region — never a concurrent retry of another region. Deliberately keeps the current
    /// batch version: per-region supersession is by cancellation, while the version marks a full
    /// batch (initial load or authentication change) superseding every region at once.
    /// </summary>
    /// <param name="source">The region's retry source, replaced with a fresh one.</param>
    /// <returns>The current batch version and the fresh request token.</returns>
    private (int Version, CancellationToken Token) BeginRegionRetry(ref CancellationTokenSource? source)
    {
        var version = _reloadVersion;
        source?.Cancel();
        source?.Dispose();
        source = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        return (version, source.Token);
    }

    /// <summary>Cancels and disposes every region retry source so a new batch owns all reloads.</summary>
    private void SupersedeRegionRetries()
    {
        _identityRetrySource?.Cancel();
        _identityRetrySource?.Dispose();
        _identityRetrySource = null;
        _seasonRetrySource?.Cancel();
        _seasonRetrySource?.Dispose();
        _seasonRetrySource = null;
        _campaignRetrySource?.Cancel();
        _campaignRetrySource?.Dispose();
        _campaignRetrySource = null;
    }

    /// <summary>Whether the given batch is still the current one and its request is not cancelled.</summary>
    /// <param name="version">Batch version to compare.</param>
    /// <param name="requestToken">Request token to check.</param>
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
