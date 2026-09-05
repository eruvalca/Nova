using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Campaigns.Components;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>Creates a saved Draft with tab-scoped input and exact-request recovery.</summary>
public partial class NewCampaign(
    ICampaignQueryService campaignQueryService,
    ICampaignCreationService campaignCreationService,
    NavigationManager navigationManager,
    AuthenticationStateProvider authentication,
    IJSRuntime js)
{
    private CampaignCreationSetupResult? _setup;
    private string? _pageError;
    private string? _formError;
    private bool _isLoading;
    private bool _isSubmitting;
    private bool _sessionReady;
    private bool _storageAttempted;
    private CampaignCreateFormState _createForm = CampaignCreateFormState.CreateDefault();
    private CreateCampaignInput? _pending;
    private IJSObjectReference? _module;
    private string _scope = "";
    private int _version;
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;

    /// <summary>Gets or sets the persisted setup.</summary>
    [PersistentState] public CampaignCreationSetupResult? PersistedSetup { get; set; }
    /// <summary>Gets or sets the persisted startup error.</summary>
    [PersistentState] public string? PersistedPageError { get; set; }
    /// <summary>Gets or sets the persisted snapshot's identity.</summary>
    [PersistentState] public string? SnapshotScope { get; set; }
    /// <summary>Gets or sets whether prerender initialization completed.</summary>
    [PersistentState] public bool Initialized { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        authentication.AuthenticationStateChanged += AuthenticationChanged;
        _scope = Identity(await authentication.GetAuthenticationStateAsync());
        if (Initialized && SnapshotScope == _scope)
        {
            _setup = PersistedSetup;
            _pageError = PersistedPageError;
            return;
        }
        await LoadSetupAsync();
        Initialized = true;
    }

    private static string Identity(AuthenticationState state) => $"{state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value}:{state.User.FindFirst(NovaClaimTypes.ClubId)?.Value}:{state.User.IsInRole(Roles.ClubAdmin)}";

    private void AuthenticationChanged(Task<AuthenticationState> task) => _ = InvokeAsync(async () =>
    {
        var next = Identity(await task);
        if (next == _scope)
        {
            return;
        }

        var old = _scope;
        ++_version;
        _scope = next;
        _setup = null;
        PersistedSetup = null;
        _pending = null;
        _createForm = CampaignCreateFormState.CreateDefault();
        _sessionReady = false;
        _storageAttempted = false;
        _isSubmitting = false;
        StateHasChanged();
        if (_module is not null)
        {
            await _module!.InvokeVoidAsync("clear", ComponentCancellationToken, old);
        }

        await LoadSetupAsync();
        StateHasChanged();
    });

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_storageAttempted || _setup is null)
        {
            return;
        }

        _storageAttempted = true;
        var version = _version;
        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", ComponentCancellationToken,
                "./_content/Nova.UI/Features/Campaigns/Pages/NewCampaign.razor.js");
            var saved = await _module.InvokeAsync<CampaignCreateFormState?>("read", ComponentCancellationToken, _scope, "create-form");
            var pending = await _module.InvokeAsync<CreateCampaignInput?>("read", ComponentCancellationToken, _scope, "create-pending");
            if (version != _version)
            {
                return;
            }

            if (saved is not null)
            {
                _createForm = saved;
            }
            else
            {
                _createForm.ExistingSeasonId = _setup.CurrentSeason?.SeasonId;
                _createForm.UseInlineSeason = _setup.CurrentSeason is null;
                _createForm.InlineSeasonStartDate = _createForm.StartDate;
            }
            if (_createForm.OperationId == Guid.Empty)
            {
                _createForm.OperationId = Guid.CreateVersion7();
            }

            _createForm = _createForm.Clone();
            _pending = pending;
            _sessionReady = true;
            StateHasChanged();
        }
        catch (JSException)
        {
            if (version == _version)
            {
                _pageError = "Recovery storage is unavailable. Enable session storage and reload before creating a Draft.";
                StateHasChanged();
            }
        }
    }

    private async Task LoadSetupAsync()
    {
        var version = _version;
        _isLoading = true;
        _pageError = null;
        try
        {
            var result = await campaignQueryService.GetCreationSetupAsync(ComponentCancellationToken);
            if (version != _version)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _setup = result.Value;
            }
            else
            {
                _setup = null;
                _pageError = result.Problem.Detail ?? "Campaign setup is unavailable.";
                if (result.Problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                }
            }
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            if (version == _version)
            {
                _pageError = "Could not load campaign setup. Check your connection and retry.";
            }
        }
        finally
        {
            if (version == _version)
            {
                _isLoading = false;
                PersistedSetup = _setup;
                PersistedPageError = _pageError;
                SnapshotScope = _scope;
            }
        }
    }

    private Task ReloadAsync() => LoadSetupAsync();

    private async Task SaveInputAsync(CampaignCreateFormState model)
    {
        if (!_sessionReady || _pending is not null)
        {
            return;
        }

        _createForm = model;
        _fieldErrors = null;
        try { await _module!.InvokeVoidAsync("write", ComponentCancellationToken, _scope, "create-form", model); }
        catch (JSException)
        {
            _sessionReady = false;
            _formError = "Your input could not be saved for recovery. Reload before submitting.";
        }
    }

    private async Task CreateCampaignAsync(CampaignCreateFormState model)
    {
        if (_isSubmitting || !_sessionReady)
        {
            return;
        }

        var version = _version;
        _isSubmitting = true;
        _formError = null;
        _fieldErrors = null;
        try
        {
            if (_pending is null)
            {
                _createForm = model;
                if (_createForm.OperationId == Guid.Empty)
                {
                    _createForm.OperationId = Guid.CreateVersion7();
                }

                _pending = _createForm.ToCreateInput();
                await _module!.InvokeVoidAsync("write", ComponentCancellationToken, _scope, "create-form", _createForm);
                if (version != _version)
                {
                    return;
                }

                await _module!.InvokeVoidAsync("write", ComponentCancellationToken, _scope, "create-pending", _pending);
            }
            if (version != _version)
            {
                return;
            }

            var result = await campaignCreationService.CreateAsync(_pending, ComponentCancellationToken);
            if (version != _version)
            {
                return;
            }

            if (result.IsSuccess)
            {
                await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, "create-pending");
                if (version != _version)
                {
                    return;
                }

                await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, "create-form");
                if (version != _version)
                {
                    return;
                }

                navigationManager.NavigateTo($"/campaigns/{result.Value.CampaignId}");
                return;
            }
            _formError = result.Problem.Detail ?? "The Draft could not be saved.";
            _fieldErrors = result.Problem.Errors;
            if (result.Problem.Kind != ServiceProblemKind.ServerError)
            {
                await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, "create-pending");
                if (version != _version)
                {
                    return;
                }

                _pending = null;
                _createForm.OperationId = Guid.CreateVersion7();
                _createForm = _createForm.Clone();
            }
            if (result.Problem.Kind == ServiceProblemKind.Forbidden)
            {
                navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
            }
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JSException)
        {
            if (version == _version)
            {
                _formError = "The creation result is uncertain. Confirm the original request before editing or submitting another Draft.";
            }
        }
        finally
        {
            if (version == _version)
            {
                _isSubmitting = false;
            }
        }
    }

    private Task RecoverAsync() => CreateCampaignAsync(_createForm);
    private void Cancel() => navigationManager.NavigateTo("/campaigns");

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        ++_version;
        authentication.AuthenticationStateChanged -= AuthenticationChanged;
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch (JSDisconnectedException) { }
        }
        await base.DisposeAsyncCore();
    }
}
