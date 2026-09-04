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

        _identityLoading = _seasonLoading = _campaignLoading = true;
        await Task.WhenAll(LoadIdentityAsync(), LoadSeasonAsync(), LoadCampaignAsync());
        Initialized = true;
        PersistState();
    }

    private async Task LoadIdentityAsync()
    {
        _identityLoading = true;
        _identityError = null;
        try
        {
            await ApplyResultAsync(await identityQueryService.GetCurrentAsync(ComponentCancellationToken),
                value => _identity = value, message => _identityError = message, "Club identity is unavailable.");
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            _identityError = "Club identity is unavailable. Retry this section.";
        }
        _identityLoading = false;
        _announcement = _identityError ?? "Club identity loaded.";
    }

    private async Task LoadSeasonAsync()
    {
        _seasonLoading = true;
        _seasonError = null;
        try
        {
            var result = await seasonQueryService.ListAsync(new GetSeasonListInput { Page = 1, PageSize = 1 }, ComponentCancellationToken);
            await ApplyResultAsync(result,
                value => _currentSeason = value.Items.FirstOrDefault(item => item.IsCurrent),
                message => _seasonError = message, "Current season is unavailable.");
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            _seasonError = "Current season is unavailable. Retry this section.";
        }
        _seasonLoading = false;
        _announcement = _seasonError ?? "Current season loaded.";
    }

    private async Task LoadCampaignAsync()
    {
        _campaignLoading = true;
        _campaignError = null;
        try
        {
            var result = await campaignQueryService.GetCampaignListAsync(
                new GetCampaignListInput { Status = "active", Limit = 1 }, ComponentCancellationToken);
            await ApplyResultAsync(result, value => _campaigns = value,
                message => _campaignError = message, "Active work is unavailable.");
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            _campaignError = "Active work is unavailable. Retry this section.";
        }
        _campaignLoading = false;
        _announcement = _campaignError ?? "Active work loaded.";
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

    private async Task RetryIdentityAsync() { await LoadIdentityAsync(); PersistState(); }
    private async Task RetrySeasonAsync() { await LoadSeasonAsync(); PersistState(); }
    private async Task RetryCampaignAsync() { await LoadCampaignAsync(); PersistState(); }

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
            _identityLoading = _seasonLoading = _campaignLoading = true;
            await Task.WhenAll(LoadIdentityAsync(), LoadSeasonAsync(), LoadCampaignAsync());
            PersistState();
        }
        await InvokeAsync(StateHasChanged);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        await base.DisposeAsyncCore();
    }
}
