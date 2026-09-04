using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Pages;

public partial class ClubOverview(
    IClubIdentityQueryService identityQueryService,
    ISeasonQueryService seasonQueryService,
    ICampaignQueryService campaignQueryService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager)
{
    private ClubIdentityResult? _identity;
    private SeasonSummary? _currentSeason;
    private CampaignListResult? _campaigns;
    private string? _identityError;
    private string? _seasonError;
    private string? _campaignError;
    private string? _announcement;
    private bool _identityLoading;
    private bool _seasonLoading;
    private bool _campaignLoading;
    private bool _isClubAdmin;
    private string? _clubIdText;

    // Monotonic generation for reload batches: a slower earlier load must never overwrite
    // results for a club that a later authentication change superseded.
    private int _reloadVersion;

    // Cancellation source for the current reload batch; starting a new batch supersedes the prior one.
    private CancellationTokenSource? _reloadSource;

    // Per-region cancellation sources so a retry of one section never cancels a concurrent
    // retry or reload of another section, and so a second retry of the same section supersedes the first.
    private CancellationTokenSource? _identityRetrySource;
    private CancellationTokenSource? _seasonRetrySource;
    private CancellationTokenSource? _campaignRetrySource;

    [SupplyParameterFromQuery(Name = "notice")]
    public string? Notice { get; set; }

    [PersistentState] public ClubIdentityResult? PersistedIdentity { get; set; }
    [PersistentState] public string? PersistedClubId { get; set; }
    [PersistentState] public SeasonSummary? PersistedSeason { get; set; }
    [PersistentState] public CampaignListResult? PersistedCampaigns { get; set; }
    [PersistentState] public string? PersistedIdentityError { get; set; }
    [PersistentState] public string? PersistedSeasonError { get; set; }
    [PersistentState] public string? PersistedCampaignError { get; set; }
    [PersistentState] public bool Initialized { get; set; }

    protected string ClubInitials => _identity is null
        ? "Club"
        : string.Concat(_identity.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));

    protected (CampaignListItem Campaign, string SeasonName)? ActiveCampaign
    {
        get
        {
            var season = _campaigns?.Seasons.FirstOrDefault(group => group.Campaigns.Count > 0);
            return season is null ? null : (season.Campaigns[0], season.Name);
        }
    }

    protected override void OnInitialized()
        => authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

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

    private async Task RetryIdentityAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _identityRetrySource);
        await LoadIdentityAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }
    private async Task RetrySeasonAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _seasonRetrySource);
        await LoadSeasonAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }
    private async Task RetryCampaignAsync()
    {
        var (version, requestToken) = BeginRegionRetry(ref _campaignRetrySource);
        await LoadCampaignAsync(version, requestToken);
        if (IsCurrentBatch(version, requestToken))
        {
            PersistState();
        }
    }

    protected static string FormatDateWindow(DateOnly start, DateOnly? end)
        => end is null
            ? $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} onward"
            : $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – {end.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";

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

    private void RestorePersistedState()
    {
        _identity = PersistedIdentity;
        _currentSeason = PersistedSeason;
        _campaigns = PersistedCampaigns;
        _identityError = PersistedIdentityError;
        _seasonError = PersistedSeasonError;
        _campaignError = PersistedCampaignError;
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = ApplyAuthenticationStateAsync(stateTask);

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

    private (int Version, CancellationToken Token) BeginReloadBatch()
    {
        var version = Interlocked.Increment(ref _reloadVersion);
        _reloadSource?.Cancel();
        _reloadSource?.Dispose();
        _reloadSource = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        SupersedeRegionRetries();
        return (version, _reloadSource.Token);
    }

    // Cancels and disposes a region's own retry source, then creates a fresh one linked to the
    // component lifetime, so a retry of that region supersedes only an earlier retry of the
    // same region — never a concurrent retry of another region. Deliberately keeps the current
    // batch version: per-region supersession is by cancellation, while the version marks a full
    // batch (initial load or authentication change) superseding every region at once.
    private (int Version, CancellationToken Token) BeginRegionRetry(ref CancellationTokenSource? source)
    {
        var version = _reloadVersion;
        source?.Cancel();
        source?.Dispose();
        source = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        return (version, source.Token);
    }

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

    private bool IsCurrentBatch(int version, CancellationToken requestToken)
        => version == _reloadVersion && !requestToken.IsCancellationRequested;

    protected override async ValueTask DisposeAsyncCore()
    {
        _reloadSource?.Cancel();
        _reloadSource?.Dispose();
        SupersedeRegionRetries();
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        await base.DisposeAsyncCore();
    }
}
