using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Teams.Components;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>Routes campaign lifecycle state and owns administrator Draft preparation and retry recovery.</summary>
public partial class CampaignEntry(
    ICampaignQueryService queries,
    ICampaignLifecycleService lifecycle,
    ICampaignMetadataService metadata,
    ITeamManagementService teams,
    AuthenticationStateProvider authentication,
    NavigationManager navigation,
    IJSRuntime js)
{
    /// <summary>Gets or sets the campaign from the URL.</summary>
    [Parameter] public long CampaignId { get; set; }
    /// <summary>Gets or sets the URL-backed opening checkpoint.</summary>
    [SupplyParameterFromQuery(Name = "review")] public string? Review { get; set; }
    /// <summary>Gets or sets the server-authorized campaign snapshot.</summary>
    [PersistentState] public CampaignDetailResult? Detail { get; set; }
    /// <summary>Gets or sets the advisory opening snapshot.</summary>
    [PersistentState] public CampaignOpeningReadinessResult? Readiness { get; set; }
    /// <summary>Gets or sets the owner of the prerender snapshot.</summary>
    [PersistentState] public string? SnapshotScope { get; set; }

    /// <summary>Gets or sets the authorized current-season setup across prerender.</summary>
    [PersistentState] public CampaignCreationSetupResult? Setup { get; set; }
    private CampaignMetadataFormState? _edit;
    private TeamFormState? _team;
    private string? _error;
    private string? _readinessError;
    private string? _mutationError;
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;
    private string? _message;
    private string _scope = "";
    private bool _admin;
    private bool _unavailable;
    private bool _sessionReady;
    private bool _storageAttempted;
    private bool _busy;
    private bool _confirmDelete;
    private bool _deletePending;
    private Guid? _openingId;
    private long _loadedId;
    private string? _loadedRoute;
    private int _version;
    private int _mutationVersion;
    private IJSObjectReference? _module;
    private ElementReference _heading;
    private ElementReference _reviewHeading;
    private bool _focusReview;
    private string? _previousReview;

    private string PlayersUrl => $"/players?returnToDraft={CampaignId}";
    private string TeamsUrl => $"/club/teams?returnToDraft={CampaignId}";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        authentication.AuthenticationStateChanged += AuthenticationChanged;
        ApplyIdentity(await authentication.GetAuthenticationStateAsync());
    }

    private void ApplyIdentity(AuthenticationState state)
    {
        var user = state.User;
        _admin = user.IsInRole(Roles.ClubAdmin);
        _scope = $"{user.FindFirst(ClaimTypes.NameIdentifier)?.Value}:{user.FindFirst(NovaClaimTypes.ClubId)?.Value}:{_admin}";
    }

    private void AuthenticationChanged(Task<AuthenticationState> stateTask) => _ = InvokeAsync(async () =>
    {
        var state = await stateTask;
        var oldScope = _scope;
        ApplyIdentity(state);
        if (oldScope == _scope)
        {
            return;
        }

        ++_version;
        ++_mutationVersion;
        Detail = null;
        Readiness = null;
        SnapshotScope = null;
        Setup = null;
        _edit = null;
        _team = null;
        _openingId = null;
        _sessionReady = false;
        _storageAttempted = false;
        _busy = false;
        _confirmDelete = false;
        _deletePending = false;
        _mutationError = null;
        _fieldErrors = null;
        _message = null;
        StateHasChanged();
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("clear", ComponentCancellationToken, oldScope); }
            catch (JSException) { /* New-scope initialization independently probes recovery storage. */ }
        }

        await ReloadAsync();
        StateHasChanged();
    });

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_previousReview != Review)
        {
            _focusReview = Review == "open";
            _previousReview = Review;
        }
        var route = new Uri(navigation.Uri).AbsolutePath;
        var routeChanged = _loadedRoute is not null && _loadedRoute != route;
        if (_loadedId == CampaignId && !routeChanged)
        {
            return;
        }

        _loadedRoute = route;
        _loadedId = CampaignId;
        ++_version;
        ++_mutationVersion;
        _sessionReady = false;
        _storageAttempted = false;
        _openingId = null;
        _edit = null;
        _team = null;
        _busy = false;
        _confirmDelete = false;
        _deletePending = false;
        if (routeChanged || Detail?.CampaignId != CampaignId || SnapshotScope != _scope)
        {
            Detail = null;
            Readiness = null;
            Setup = null;
        }
        if (Detail is null)
        {
            await ReloadAsync();
        }
        else if (Detail.Status == CampaignStatus.Draft && !_admin)
        {
            Detail = null;
            Readiness = null;
            Setup = null;
            _unavailable = true;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_storageAttempted && (Detail is not null || (_admin && _unavailable)))
        {
            _storageAttempted = true;
            var version = _version;
            try
            {
                _module ??= await js.InvokeAsync<IJSObjectReference>("import", ComponentCancellationToken,
                    "./_content/Nova.UI/Features/Campaigns/Pages/CampaignEntry.razor.js");
                var pending = await _module.InvokeAsync<string?>("read", ComponentCancellationToken, _scope, $"open:{CampaignId}");
                if (version != _version)
                {
                    return;
                }
                var deleting = await _module.InvokeAsync<bool?>("read", ComponentCancellationToken, _scope, $"delete:{CampaignId}");
                if (version != _version)
                {
                    return;
                }

                _openingId = Guid.TryParse(pending, out var id) && id != Guid.Empty ? id : _openingId;
                _deletePending = deleting == true || _deletePending;
                _sessionReady = true;
                if (_deletePending && _admin)
                {
                    await DeleteAsync();
                }
                else if (_openingId is not null && _admin)
                {
                    await RecoverOpeningAsync();
                }

                if (!_unavailable && Detail?.Status == CampaignStatus.Draft)
                {
                    await _module.InvokeVoidAsync("focus", ComponentCancellationToken, _heading);
                }
                StateHasChanged();
            }
            catch (JSException)
            {
                if (version != _version)
                {
                    return;
                }

                _error = "Recovery storage is unavailable. Enable session storage and reload before changing this campaign.";
                StateHasChanged();
            }
        }
        if (_focusReview && _sessionReady && _module is not null)
        {
            _focusReview = false;
            await _module.InvokeVoidAsync("focus", ComponentCancellationToken, _reviewHeading);
        }
    }

    private async Task ReloadAsync()
    {
        var version = ++_version;
        _error = null;
        _unavailable = false;
        try
        {
            var detail = await queries.GetCampaignDetailAsync(new GetCampaignDetailInput { CampaignId = CampaignId }, ComponentCancellationToken);
            if (version != _version)
            {
                return;
            }

            if (detail.IsProblem)
            {
                Detail = null;
                Readiness = null;
                _unavailable = detail.Problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden;
                if (!_unavailable)
                {
                    _error = detail.Problem.Detail ?? "Campaign details are unavailable.";
                }

                return;
            }
            Detail = detail.Value;
            SnapshotScope = _scope;
            if (Detail.Status == CampaignStatus.Draft && !_admin)
            {
                Detail = null;
                Readiness = null;
                _unavailable = true;
                return;
            }
            if (Detail.Status != CampaignStatus.Draft)
            {
                return;
            }

            var setup = await queries.GetCreationSetupAsync(ComponentCancellationToken);
            if (version != _version)
            {
                return;
            }

            Setup = setup.IsSuccess ? setup.Value : null;
            await RefreshReadinessAsync(version);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            if (version == _version)
            {
                _error = "Could not load the campaign. Check your connection and retry.";
            }
        }
    }

    private Task RetryAsync()
    {
        _storageAttempted = false;
        _sessionReady = false;
        return ReloadAsync();
    }

    private async Task<bool> RefreshReadinessAsync(int version)
    {
        Readiness = null;
        _readinessError = null;
        var result = await queries.GetOpeningReadinessAsync(CampaignId, ComponentCancellationToken);
        if (version != _version)
        {
            return false;
        }

        if (result.IsSuccess)
        {
            Readiness = result.Value;
            return true;
        }
        _readinessError = result.Problem.Detail ?? "Opening readiness is unavailable.";
        if (result.Problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden)
        {
            _unavailable = true;
            Detail = null;
            Readiness = null;
        }
        return false;
    }

    /// <summary>Starts a metadata correction with a fresh validation state.</summary>
    private void BeginEdit()
    {
        _fieldErrors = null;
        if (Detail is not null)
        {
            _edit = CampaignMetadataFormState.FromDetail(Detail);
        }

        _team = null;
        _mutationError = null;
    }
    /// <summary>Discards the metadata edit and its server validation messages.</summary>
    private void CancelEdit() { _edit = null; _mutationError = null; _fieldErrors = null; }
    /// <summary>Switches to team creation and discards metadata validation messages.</summary>
    private void BeginTeam() { _team = TeamFormState.CreateDefault(); _edit = null; _mutationError = null; _fieldErrors = null; }
    private void CancelTeam() { _team = null; _mutationError = null; }
    private void ConfirmDelete() => _confirmDelete = true;
    private void CancelDelete() => _confirmDelete = false;

    private Task SaveDetailsAsync(CampaignMetadataFormState model) => MutateAsync(async version =>
    {
        var result = await metadata.UpdateAsync(model.ToUpdateInput(), ComponentCancellationToken);
        if (version != _version)
        {
            return;
        }

        if (result.IsProblem) { _mutationError = Explain(result.Problem); _fieldErrors = result.Problem.Errors; return; }
        _edit = null;
        _message = "Campaign details saved.";
        await ReloadAsync();
    });

    private Task CreateTeamAsync(TeamFormState model) => MutateAsync(async version =>
    {
        var result = await teams.CreateAsync(model.ToCreateInput(), ComponentCancellationToken);
        if (version != _version)
        {
            return;
        }

        if (result.IsProblem) { _mutationError = Explain(result.Problem); return; }
        _team = null;
        _message = $"{result.Value.Name} created for your club.";
        await RefreshReadinessAsync(version);
    });

    private Task OpenAsync() => MutateAsync(async version =>
    {
        if (_openingId is not null)
        {
            return;
        }

        if (!await RefreshReadinessAsync(version) || Readiness?.CanOpen != true || version != _version)
        {
            return;
        }

        _openingId = Guid.CreateVersion7();
        await SubmitOpeningAsync(version);
    });

    private Task RecoverOpeningAsync() => MutateAsync(async version =>
    {
        // Refresh lifecycle first; the exact receipt is recovered by replay, even after a later close.
        var current = await queries.GetCampaignDetailAsync(new GetCampaignDetailInput { CampaignId = CampaignId }, ComponentCancellationToken);
        if (version != _version)
        {
            return;
        }

        if (current.IsProblem)
        {
            _mutationError = Explain(current.Problem);
            if (current.Problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden)
            {
                Detail = null;
                Readiness = null;
                Setup = null;
                _unavailable = true;
                await ClearOpeningAsync();
            }
            return;
        }
        Detail = current.Value;
        await SubmitOpeningAsync(version);
    });

    private async Task SubmitOpeningAsync(int version)
    {
        // Every submission, including recovery after a failed storage write, must first
        // retain the exact operation so an ambiguous commit can be recovered after reload.
        await _module!.InvokeVoidAsync("write", ComponentCancellationToken, _scope, $"open:{CampaignId}", _openingId.ToString());
        if (version != _version)
        {
            return;
        }

        var result = await lifecycle.OpenAsync(CampaignId, new OpenCampaignInput { OperationId = _openingId!.Value }, ComponentCancellationToken);
        if (version != _version)
        {
            return;
        }

        if (result.IsSuccess)
        {
            var receipt = result.Value;
            await _module!.InvokeVoidAsync("write", ComponentCancellationToken, _scope, $"receipt:{CampaignId}", receipt);
            if (version != _version)
            {
                return;
            }

            await ClearOpeningAsync();
            if (version != _version)
            {
                return;
            }

            navigation.NavigateTo($"/campaigns/{CampaignId}/roster");
            return;
        }
        _mutationError = Explain(result.Problem);
        if (result.Problem.Kind != ServiceProblemKind.ServerError)
        {
            if (result.Problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden)
            {
                Detail = null;
                Readiness = null;
                Setup = null;
                _unavailable = true;
            }
            await ClearOpeningAsync();
            if (version != _version)
            {
                return;
            }

            var current = await queries.GetCampaignDetailAsync(new GetCampaignDetailInput { CampaignId = CampaignId }, ComponentCancellationToken);
            if (version != _version)
            {
                return;
            }

            if (current.IsSuccess && current.Value.Status != CampaignStatus.Draft)
            {
                navigation.NavigateTo($"/campaigns/{CampaignId}/roster");
                return;
            }
            if (current.IsProblem && current.Problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden)
            {
                Detail = null;
                Readiness = null;
                _unavailable = true;
                return;
            }
            await RefreshReadinessAsync(version);
        }
    }

    private async Task ClearOpeningAsync()
    {
        var version = _version;
        await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, $"open:{CampaignId}");
        if (version == _version)
        {
            _openingId = null;
        }
    }

    private Task DeleteAsync() => MutateAsync(async version =>
    {
        _deletePending = true;
        await _module!.InvokeVoidAsync("write", ComponentCancellationToken, _scope, $"delete:{CampaignId}", true);
        if (version != _version)
        {
            return;
        }
        var result = await lifecycle.DeleteDraftAsync(CampaignId, ComponentCancellationToken);
        if (version != _version)
        {
            return;
        }

        if (result.IsSuccess)
        {
            await ClearOpeningAsync();
            if (version != _version)
            {
                return;
            }

            await ClearDeletionAsync(version);
            if (version == _version)
            {
                navigation.NavigateTo("/campaigns?deleted=true");
            }
        }
        else
        {
            _mutationError = Explain(result.Problem);
            if (result.Problem.Kind != ServiceProblemKind.ServerError)
            {
                await ClearDeletionAsync(version);
                if (version != _version)
                {
                    return;
                }
            }
            if (result.Problem.Kind is ServiceProblemKind.Conflict or ServiceProblemKind.Forbidden or ServiceProblemKind.NotFound)
            {
                await ReloadAsync();
            }
        }
    });

    private async Task ClearDeletionAsync(int version)
    {
        await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, $"delete:{CampaignId}");
        if (version == _version)
        {
            _deletePending = false;
        }
    }

    private async Task MutateAsync(Func<int, Task> operation)
    {
        if (_busy || !_sessionReady || !_admin)
        {
            return;
        }

        var version = _version;
        var mutationVersion = ++_mutationVersion;
        _busy = true;
        _mutationError = null;
        _fieldErrors = null;
        try { await operation(version); }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JSException)
        {
            if (version == _version)
            {
                _mutationError = _openingId is not null
                ? "The opening result is uncertain. Confirm the original opening result to recover safely."
                : "The result is uncertain. Refresh the campaign before retrying; your input is preserved.";
            }
        }
        finally
        {
            if (mutationVersion == _mutationVersion)
            {
                _busy = false;
            }
        }
    }

    private static string Explain(ServiceProblem problem) => problem.Errors is { Count: > 0 }
        ? string.Join(" ", problem.Errors.Values.SelectMany(messages => messages))
        : problem.Detail ?? "The action could not be completed. Refresh and try again.";

    private static string FormatDates(CampaignDetailResult detail) => detail.PlannedEndDate is { } end
        ? $"{detail.StartDate:MMM d} – {end:MMM d, yyyy}" : $"{detail.StartDate:MMM d, yyyy} · No planned end";

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
