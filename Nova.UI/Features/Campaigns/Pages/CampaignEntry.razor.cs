using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Teams.Components;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>Routes campaign lifecycle state and owns administrator Draft preparation and retry recovery.</summary>
/// <param name="queries">The authorized campaign detail and readiness queries.</param>
/// <param name="lifecycle">The idempotent opening and Draft deletion commands.</param>
/// <param name="metadata">The campaign metadata update service.</param>
/// <param name="teams">The durable-team creation service.</param>
/// <param name="authentication">The current identity and its change notifications.</param>
/// <param name="navigation">The local route navigation service.</param>
/// <param name="js">The runtime used for recovery storage and heading focus.</param>
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
    /// <summary>Unsaved campaign metadata while its editor is open.</summary>
    private CampaignMetadataFormState? _edit;
    /// <summary>Input for durable-team creation.</summary>
    private TeamFormState? _team;
    /// <summary>Detail-loading or recovery-storage failure shown to the administrator.</summary>
    private string? _error;
    /// <summary>Failure from the latest opening-readiness refresh.</summary>
    private string? _readinessError;
    /// <summary>Explains missing current-season choices and keeps metadata editing unavailable until retry.</summary>
    private string? _setupError;
    /// <summary>Current command failure or uncertain outcome.</summary>
    private string? _mutationError;
    /// <summary>Server validation errors keyed by metadata field.</summary>
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;
    /// <summary>Successful preparation feedback retained across refreshes.</summary>
    private string? _message;
    /// <summary>User, club, and authority key owning recovery state.</summary>
    private string _scope = "";
    /// <summary>Whether the current identity may prepare and open Drafts.</summary>
    private bool _admin;
    /// <summary>Whether campaign data is missing or no longer authorized.</summary>
    private bool _unavailable;
    /// <summary>Whether scoped recovery storage permits mutation dispatch.</summary>
    private bool _sessionReady;
    /// <summary>Whether recovery initialization has already been attempted.</summary>
    private bool _storageAttempted;
    /// <summary>Whether any preparation or lifecycle mutation is running.</summary>
    private bool _busy;
    /// <summary>Identifies opening work so unrelated mutations cannot show opening progress.</summary>
    private bool _isOpening;
    /// <summary>Whether inline deletion confirmation is open.</summary>
    private bool _confirmDelete;
    /// <summary>Uncertain deletion intent retained for tombstone replay.</summary>
    private bool _deletePending;
    /// <summary>Original opening operation identifier retained for exact replay.</summary>
    private Guid? _openingId;
    /// <summary>Campaign identifier whose route parameters were last applied.</summary>
    private long _loadedId;
    /// <summary>Last route used to distinguish preparation and Roster transitions.</summary>
    private string? _loadedRoute;
    /// <summary>Request generation rejecting obsolete route or identity results.</summary>
    private int _version;
    /// <summary>Orders authentication completions across startup, notifications, and disposal.</summary>
    private int _authenticationVersion;
    /// <summary>Mutation generation protecting newer operations from older completions.</summary>
    private int _mutationVersion;
    /// <summary>Scoped recovery-storage and focus interop module.</summary>
    private IJSObjectReference? _module;
    /// <summary>Preparation heading used for initial focus.</summary>
    private ElementReference _heading;
    /// <summary>Opening-review heading used for URL-backed focus.</summary>
    private ElementReference _reviewHeading;
    /// <summary>Whether to focus review after its board has rendered.</summary>
    private bool _focusReview;
    /// <summary>Previously applied review query value.</summary>
    private string? _previousReview;

    /// <summary>Local Players correction link returning to this Draft.</summary>
    private string PlayersUrl => $"/players?returnToDraft={CampaignId}";
    /// <summary>Local Teams correction link returning to this Draft.</summary>
    private string TeamsUrl => $"{ClubRoutes.Teams}?returnToDraft={CampaignId}";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        authentication.AuthenticationStateChanged += AuthenticationChanged;
        var authenticationVersion = _authenticationVersion;
        var state = await authentication.GetAuthenticationStateAsync();
        if (authenticationVersion != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        ApplyIdentity(state);
    }

    /// <summary>Applies administrator authority and recovery ownership from claims.</summary>
    /// <param name="state">The authenticated identity.</param>
    private void ApplyIdentity(AuthenticationState state)
    {
        var user = state.User;
        _admin = user.IsInRole(Roles.ClubAdmin);
        _scope = $"{user.FindFirst(ClaimTypes.NameIdentifier)?.Value}:{user.FindFirst(NovaClaimTypes.ClubId)?.Value}:{_admin}";
    }

    /// <summary>Applies only the latest identity before replacing its recovery and campaign state.</summary>
    /// <param name="stateTask">The pending authentication notification.</param>
    private void AuthenticationChanged(Task<AuthenticationState> stateTask) => _ = InvokeAsync(async () =>
    {
        var authenticationVersion = ++_authenticationVersion;
        var state = await stateTask;
        if (authenticationVersion != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        var oldScope = _scope;
        ApplyIdentity(state);
        if (oldScope == _scope)
        {
            return;
        }

        var version = ++_version;
        ++_mutationVersion;
        Detail = null;
        Readiness = null;
        SnapshotScope = null;
        Setup = null;
        _setupError = null;
        _edit = null;
        _team = null;
        _openingId = null;
        _sessionReady = false;
        _storageAttempted = false;
        _busy = false;
        _isOpening = false;
        _confirmDelete = false;
        _deletePending = false;
        _error = null;
        _mutationError = null;
        _fieldErrors = null;
        _message = null;
        StateHasChanged();
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("clear", ComponentCancellationToken, oldScope); }
            catch (JSException) { /* New-scope initialization independently probes recovery storage. */ }
        }

        if (version != _version || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
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
        _isOpening = false;
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
                if (version != _version)
                {
                    return;
                }
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
            catch (Exception exception) when (exception is JSException or System.Text.Json.JsonException or NotSupportedException)
            {
                if (version != _version)
                {
                    return;
                }

                _sessionReady = false;
                _error = exception is JSException
                    ? "Recovery storage is unavailable. Enable session storage and reload before changing this campaign."
                    : "Stored campaign recovery data is incompatible. Opening and deletion recovery is paused, and the original markers are preserved. Restore compatible recovery data before retrying.";
                StateHasChanged();
            }
        }
        if (_focusReview && _sessionReady && _module is not null)
        {
            _focusReview = false;
            await _module.InvokeVoidAsync("focus", ComponentCancellationToken, _reviewHeading);
        }
    }

    /// <summary>Fetches authorized campaign details, setup, and readiness.</summary>
    /// <returns>The campaign refresh task.</returns>
    private async Task ReloadAsync()
    {
        var version = ++_version;
        _error = null;
        _setupError = null;
        Setup = null;
        _edit = null;
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
            if (setup.IsProblem)
            {
                _setupError = Explain(setup.Problem);
                _edit = null;
            }
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

    /// <summary>Retries loading and probes preserved recovery storage again.</summary>
    /// <returns>The retry task.</returns>
    private Task RetryAsync()
    {
        _storageAttempted = false;
        _sessionReady = false;
        return ReloadAsync();
    }

    /// <summary>Refreshes readiness only for the request that still owns the page.</summary>
    /// <param name="version">The owning route and identity generation.</param>
    /// <returns>Whether fresh readiness was applied.</returns>
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
        if (result.Problem.Kind == ServiceProblemKind.Conflict)
        {
            // Readiness can race another administrator's lifecycle command. Reconcile once;
            // a still-Draft conflict (such as season advancement) must not trigger a reload loop.
            Detail = null;
            ServiceResult<CampaignDetailResult> current;
            try
            {
                current = await queries.GetCampaignDetailAsync(new GetCampaignDetailInput { CampaignId = CampaignId }, ComponentCancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                && !ComponentCancellationToken.IsCancellationRequested)
            {
                if (version == _version)
                {
                    _error = "Could not confirm the campaign's current state. Check your connection and retry.";
                }
                return false;
            }
            if (version != _version)
            {
                return false;
            }
            if (current.IsSuccess)
            {
                Detail = current.Value;
                SnapshotScope = _scope;
                if (Detail.Status != CampaignStatus.Draft)
                {
                    _readinessError = null;
                    Setup = null;
                    _setupError = null;
                    _edit = null;
                    _team = null;
                    _confirmDelete = false;
                    navigation.NavigateTo($"/campaigns/{CampaignId}/roster");
                }
            }
            else
            {
                Detail = null;
                Setup = null;
                _unavailable = current.Problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden;
                if (!_unavailable)
                {
                    _error = current.Problem.Detail ?? "Campaign details are unavailable.";
                }
            }
            return false;
        }
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
        if (Setup is null)
        {
            return;
        }
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
    /// <summary>Discards unsaved team input and its mutation error.</summary>
    private void CancelTeam() { _team = null; _mutationError = null; }
    /// <summary>Opens inline Draft deletion confirmation.</summary>
    private void ConfirmDelete() => _confirmDelete = true;
    /// <summary>Closes confirmation without deleting the Draft.</summary>
    private void CancelDelete() => _confirmDelete = false;

    /// <summary>Saves campaign metadata and refreshes authorized preparation.</summary>
    /// <param name="model">The metadata to validate and save.</param>
    /// <returns>The guarded metadata mutation task.</returns>
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

    /// <summary>Creates a durable club team and refreshes readiness.</summary>
    /// <param name="model">The durable-team input.</param>
    /// <returns>The guarded team-creation task.</returns>
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

    /// <summary>Refreshes readiness, reconfirms a changed enrollment preview, and starts one retained opening operation.</summary>
    /// <returns>The guarded opening task.</returns>
    private Task OpenAsync() => MutateAsync(async version =>
    {
        if (_openingId is not null)
        {
            return;
        }

        var confirmedPlayerCount = Readiness?.ActivePlayerCount;
        if (!await RefreshReadinessAsync(version) || Readiness?.CanOpen != true || version != _version)
        {
            return;
        }

        if (Readiness.ActivePlayerCount != confirmedPlayerCount)
        {
            _message = "The active player count changed. Review the updated enrollment count and confirm opening again.";
            return;
        }

        _message = null;
        _openingId = Guid.CreateVersion7();
        await SubmitOpeningAsync(version);
    }, opening: true);

    /// <summary>Reconciles lifecycle state before replaying the original opening operation.</summary>
    /// <returns>The guarded receipt-recovery task.</returns>
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
    }, opening: true);

    /// <summary>Persists and submits the exact opening operation and hands off its immutable receipt.</summary>
    /// <param name="version">The owning route and identity generation.</param>
    /// <returns>The opening and reconciliation task.</returns>
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

    /// <summary>Removes the completed opening marker without clearing a newer operation.</summary>
    /// <returns>The marker cleanup task.</returns>
    private async Task ClearOpeningAsync()
    {
        var version = _version;
        await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, $"open:{CampaignId}");
        if (version == _version)
        {
            _openingId = null;
        }
    }

    /// <summary>Retains deletion intent and executes tombstone-backed Draft deletion.</summary>
    /// <returns>The guarded deletion task.</returns>
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

    /// <summary>Removes resolved deletion intent only for its owning generation.</summary>
    /// <param name="version">The generation owning deletion.</param>
    /// <returns>The deletion-marker cleanup task.</returns>
    private async Task ClearDeletionAsync(int version)
    {
        await _module!.InvokeVoidAsync("remove", ComponentCancellationToken, _scope, $"delete:{CampaignId}");
        if (version == _version)
        {
            _deletePending = false;
        }
    }

    /// <summary>Serializes authorized mutations and retains operation-specific progress and recovery feedback.</summary>
    /// <param name="operation">The command to run with its owning page generation.</param>
    /// <param name="opening">Whether the command opens a campaign or recovers an opening receipt.</param>
    /// <returns>The guarded mutation task.</returns>
    private async Task MutateAsync(Func<int, Task> operation, bool opening = false)
    {
        if (_busy || !_sessionReady || !_admin || (opening && (_confirmDelete || _deletePending)))
        {
            return;
        }

        var version = _version;
        var mutationVersion = ++_mutationVersion;
        _busy = true;
        _isOpening = opening;
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
                _isOpening = false;
            }
        }
    }

    /// <summary>Selects validation messages or problem detail for command feedback.</summary>
    /// <param name="problem">The service failure.</param>
    /// <returns>The user-facing failure description.</returns>
    private static string Explain(ServiceProblem problem) => problem.Errors is { Count: > 0 }
        ? string.Join(" ", problem.Errors.Values.SelectMany(messages => messages))
        : problem.Detail ?? "The action could not be completed. Refresh and try again.";

    /// <summary>Formats campaign start and optional planned end dates.</summary>
    /// <param name="detail">The campaign dates to display.</param>
    /// <returns>The date-range label.</returns>
    private static string FormatDates(CampaignDetailResult detail) => detail.PlannedEndDate is { } end
        ? $"{detail.StartDate:MMM d} – {end:MMM d, yyyy}" : $"{detail.StartDate:MMM d, yyyy} · No planned end";

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        ++_authenticationVersion;
        ++_version;
        authentication.AuthenticationStateChanged -= AuthenticationChanged;
        if (_module is not null)
        {
            try { await _module.DisposeAsync(); } catch (JSDisconnectedException) { }
        }
        await base.DisposeAsyncCore();
    }
}
