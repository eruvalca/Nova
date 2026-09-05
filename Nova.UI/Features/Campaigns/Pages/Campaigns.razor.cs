using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Components;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>
/// Renders the season-grouped campaign list with Active/Closed views and role-aware actions.
/// </summary>
/// <param name="campaignQueryService">The campaign list query service.</param>
/// <param name="campaignMetadataService">The campaign metadata correction service.</param>
/// <param name="seasonCommandService">The season command service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for redirects.</param>
public partial class Campaigns(
    ICampaignQueryService campaignQueryService,
    ICampaignMetadataService campaignMetadataService,
    ISeasonCommandService seasonCommandService,
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// The loaded campaign list, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignListResult? _list;

    /// <summary>
    /// The current page-level error message.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// The current status message shown after successful mutations.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Indicates whether campaign data is being loaded.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether the current user can create campaigns and correct metadata.
    /// </summary>
    private bool _canManageCampaigns;

    /// <summary>
    /// The campaign view filter ("all", "draft", "active", or "closed").
    /// </summary>
    private string _statusFilter = "all";
    /// <summary>The normalized one-based page requested from the campaign directory.</summary>
    private int _page = 1;
    /// <summary>Identifies the user, club, and administrator authority owning the loaded directory.</summary>
    private string? _identityScope;

    /// <summary>Gets or sets the raw directory page, normalized before use.</summary>
    [SupplyParameterFromQuery(Name = "page")] public string? DirectoryPage { get; set; }
    /// <summary>Gets or sets the raw deletion-feedback marker, parsed before use.</summary>
    [SupplyParameterFromQuery(Name = "deleted")] public string? Deleted { get; set; }
    /// <summary>Gets or sets the identity owning the persisted list.</summary>
    [PersistentState] public string? PersistedIdentityScope { get; set; }

    /// <summary>
    /// Version counter for edit-form selection; incremented whenever forms are closed or a new
    /// edit begins so a stale begin-edit continuation cannot reopen a superseded form.
    /// </summary>
    private int _editVersion;

    /// <summary>
    /// Version counter used to ignore stale list-load completions.
    /// </summary>
    private int _loadListVersion;

    /// <summary>Ensures only the latest authentication notification can publish identity and role state.</summary>
    private int _authenticationVersion;

    /// <summary>
    /// Cancellation source for the in-flight list load; canceled and replaced by newer loads.
    /// </summary>
    private CancellationTokenSource? _loadListSource;

    /// <summary>
    /// The campaign edit target whose season-choice load failed, enabling Retry to resume the edit.
    /// </summary>
    private CampaignListItem? _editRetryCandidate;

    /// <summary>
    /// The season group associated with <see cref="_editRetryCandidate"/>.
    /// </summary>
    private CampaignSeasonGroup? _editRetrySeason;

    /// <summary>
    /// The campaign metadata form state when a campaign correction is active.
    /// </summary>
    private CampaignMetadataFormState? _editCampaignForm;

    /// <summary>
    /// The season metadata form state when a season correction is active.
    /// </summary>
    private SeasonMetadataFormState? _editSeasonForm;

    /// <summary>
    /// The season choices loaded for the campaign edit form dropdown.
    /// </summary>
    private IReadOnlyList<CampaignSeasonChoice> _seasonChoices = [];

    /// <summary>
    /// The total number of tenant seasons before the choice bound, used to disclose truncation.
    /// </summary>
    private int _seasonChoiceTotalCount;

    /// <summary>
    /// The per-edit season list passed to the campaign metadata form; may include the edited
    /// campaign's current season when the bounded cache omits it.
    /// </summary>
    private IReadOnlyList<CampaignSeasonChoice> _editFormSeasonChoices = [];

    /// <summary>
    /// Indicates whether the active mutation ended in a lifecycle conflict (campaign closed
    /// concurrently), which offers a close-and-reload affordance.
    /// </summary>
    private bool _mutationConflict;

    /// <summary>
    /// Indicates whether a metadata correction mutation is in progress.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// The current mutation-level error message shown inside the active form.
    /// </summary>
    private string? _mutationError;

    /// <summary>
    /// Gets or sets the persisted startup campaign-list snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignListResult? PersistedList { get; set; }

    /// <summary>
    /// Gets or sets the persisted startup page error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedPageError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// Gets or sets the incoming lifecycle view query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "view")]
    private string? ViewQuery { get; set; }

    /// <summary>
    /// Gets a value indicating whether list results are truncated in the current UI payload.
    /// </summary>
    protected bool IsListTruncated => _list is not null && _list.TotalCount > LoadedCampaignCount;

    /// <summary>
    /// Gets the number of campaign rows currently rendered.
    /// </summary>
    protected int LoadedCampaignCount => _list?.Seasons.Sum(season => season.Campaigns.Count) ?? 0;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (ApplyViewQueryToState() && Initialized)
        {
            // A query-driven view change (e.g. enhanced navigation to ?view=closed) must close any
            // open correction form and view-specific feedback before loading the new view.
            CancelMutationForm();
            _statusMessage = null;
            await LoadListAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // SupplyParameterFromQuery properties are assigned before initialization, so the
        // requested view is available before the startup load and its persisted snapshot.
        authenticationStateProvider.AuthenticationStateChanged += DirectoryAuthenticationChanged;
        var authenticationVersion = _authenticationVersion;
        var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (authenticationVersion != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        _canManageCampaigns = authenticationState.User.IsInRole(Roles.ClubAdmin);
        _identityScope = DirectoryIdentity(authenticationState);
        _ = ApplyViewQueryToState();
        if (bool.TryParse(Deleted, out var wasDeleted) && wasDeleted)
        {
            _statusMessage = "Draft deleted. Your club's teams remain.";
        }

        if (Initialized && PersistedIdentityScope == _identityScope)
        {
            _list = PersistedList;
            _pageError = PersistedPageError;
            _isLoading = false;
            return;
        }

        _isLoading = true;
        await LoadListAsync();
    }

    /// <summary>
    /// Applies the normalized view query-string value to component state.
    /// </summary>
    /// <returns><see langword="true"/> when the filter value changed; otherwise <see langword="false"/>.</returns>
    private bool ApplyViewQueryToState()
    {
        var viewFromQuery = ViewQuery?.ToLowerInvariant() is "active" or "closed" or "draft" ? ViewQuery.ToLowerInvariant() : "all";
        var unsupportedDraft = viewFromQuery == "draft" && !_canManageCampaigns;
        if (unsupportedDraft)
        {
            viewFromQuery = "all";
        }

        var validPage = int.TryParse(DirectoryPage, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var requestedPage) && requestedPage > 0;
        var nextPage = unsupportedDraft || !validPage ? 1 : requestedPage;
        var hasChanged = !string.Equals(_statusFilter, viewFromQuery, StringComparison.Ordinal) || nextPage != _page;
        _page = nextPage;
        _statusFilter = viewFromQuery;
        if (unsupportedDraft || (DirectoryPage is not null && !validPage)
            || (Deleted is not null && !bool.TryParse(Deleted, out _)))
        {
            SyncViewToUrl();
        }
        return hasChanged;
    }

    /// <summary>
    /// Reloads the campaign list using the currently selected view. Newer loads cancel and
    /// supersede older in-flight loads so stale responses never overwrite fresher state.
    /// </summary>
    /// <returns>A task that completes when loading and state updates are finished.</returns>
    private async Task LoadListAsync()
    {
        var version = Interlocked.Increment(ref _loadListVersion);
        _loadListSource?.Cancel();
        _loadListSource?.Dispose();
        _loadListSource = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        var requestToken = _loadListSource.Token;

        _isLoading = true;
        _pageError = null;

        var input = new GetCampaignListInput
        {
            Status = _statusFilter == "all" ? null : _statusFilter,
            Limit = 20,
            Page = _page
        };

        ServiceResult<CampaignListResult> result;
        try
        {
            result = await campaignQueryService.GetCampaignListAsync(input, requestToken);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (version != _loadListVersion || requestToken.IsCancellationRequested)
            {
                return;
            }

            _pageError = "Failed to load campaigns. Please retry.";
            _list = null;
            PersistStartupState();
            _isLoading = false;
            return;
        }

        if (version != _loadListVersion || requestToken.IsCancellationRequested)
        {
            return;
        }

        result.Switch(
            list => _list = list,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _pageError = FirstNonBlank(problem.Detail, "Failed to load campaigns. Please retry.");
                _list = null;
            });

        PersistStartupState();
        _isLoading = false;
        if (_list is not null && _page > 1 && LoadedCampaignCount == 0)
        {
            // Counts can precede a concurrent deletion; always leave an empty non-first page.
            var lastPage = (int)Math.Ceiling(_list.TotalCount / 20d);
            navigationManager.NavigateTo(PageUrl(Math.Max(1, Math.Min(_page - 1, lastPage))), replace: true);
        }
    }

    /// <summary>
    /// Persists the current startup list/error state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedList = _list;
        PersistedPageError = _pageError;
        PersistedIdentityScope = _identityScope;
        Initialized = true;
    }

    /// <summary>
    /// Handles campaign view filter changes: closes any open correction form, clears feedback,
    /// and reloads the list.
    /// </summary>
    /// <param name="args">The change event arguments.</param>
    /// <returns>A task that completes when the reload finishes.</returns>
    private async Task OnStatusFilterChangedAsync(ChangeEventArgs args)
    {
        _statusFilter = args.Value?.ToString()?.ToLowerInvariant() is "active" or "closed" or "draft" ? args.Value.ToString()!.ToLowerInvariant() : "all";
        _page = 1;
        CancelMutationForm();
        _statusMessage = null;
        SyncViewToUrl();
        await LoadListAsync();
    }

    /// <summary>
    /// Synchronizes the active view filter into the URL query string without adding a history entry.
    /// </summary>
    private void SyncViewToUrl()
    {
        var uri = navigationManager.GetUriWithQueryParameters(
            new Dictionary<string, object?>
            {
                ["view"] = _statusFilter,
                ["page"] = _page,
                ["deleted"] = bool.TryParse(Deleted, out var deleted) ? deleted : null
            });

        if (!string.Equals(uri, navigationManager.Uri, StringComparison.Ordinal))
        {
            navigationManager.NavigateTo(uri, new NavigationOptions { ReplaceHistoryEntry = true });
        }
    }

    /// <summary>
    /// Retries the failed operation: resumes a pending campaign edit when its season-choice load
    /// failed, otherwise reloads the list.
    /// </summary>
    /// <returns>A task that completes when the retry finishes.</returns>
    private async Task RetryAsync()
    {
        if (_editRetryCandidate is not null && _editRetrySeason is not null)
        {
            var candidate = _editRetryCandidate;
            var season = _editRetrySeason;
            _editRetryCandidate = null;
            _editRetrySeason = null;
            await BeginEditCampaignAsync(candidate, season);
            return;
        }

        await LoadListAsync();
    }

    /// <summary>
    /// Opens the campaign metadata correction form for an Active campaign.
    /// </summary>
    /// <param name="campaign">The selected campaign row.</param>
    /// <param name="season">The season group containing the campaign.</param>
    /// <returns>A task that completes when season choices are loaded and the form is ready.</returns>
    private async Task BeginEditCampaignAsync(CampaignListItem campaign, CampaignSeasonGroup season)
    {
        CancelMutationForm();
        _statusMessage = null;
        var viewAtStart = _statusFilter;
        var editVersion = _editVersion;

        if (!await EnsureSeasonChoicesLoadedAsync(editVersion, viewAtStart))
        {
            // Only the latest, same-view edit selection may install failure feedback or the
            // retry target; stale continuations exit quietly.
            if (editVersion == _editVersion
                && viewAtStart == _statusFilter
                && !ComponentCancellationToken.IsCancellationRequested)
            {
                _editRetryCandidate = campaign;
                _editRetrySeason = season;
            }

            return;
        }

        // A view change or a newer edit selection while season choices loaded must not reopen
        // this form.
        if (viewAtStart != _statusFilter || editVersion != _editVersion)
        {
            return;
        }

        // The bounded setup payload may omit the campaign's current season; keep it selectable for
        // this edit only without polluting the shared cached choices (which drive the truncation
        // disclosure and later edits).
        _editFormSeasonChoices = _seasonChoices.Any(choice => choice.SeasonId == season.SeasonId)
            ? _seasonChoices
            : _seasonChoices
                .Prepend(new CampaignSeasonChoice
                {
                    SeasonId = season.SeasonId,
                    Name = season.Name,
                    StartDate = season.StartDate,
                    EndDate = season.EndDate
                })
                .ToList()
                .AsReadOnly();

        _editRetryCandidate = null;
        _editRetrySeason = null;
        _editCampaignForm = CampaignMetadataFormState.FromListItem(campaign, season.SeasonId);
    }

    /// <summary>
    /// Opens the season metadata correction form for a season group.
    /// </summary>
    /// <param name="season">The selected season group.</param>
    private void BeginEditSeason(CampaignSeasonGroup season)
    {
        CancelMutationForm();
        _statusMessage = null;
        // A stale setup-load error from a previous campaign edit must not linger above this form.
        _pageError = null;
        _editSeasonForm = SeasonMetadataFormState.FromSeasonGroup(season);
    }

    /// <summary>
    /// Loads the season choices used by the campaign edit form when not already available. Failure
    /// feedback is published only when the calling edit selection is still current and on the same
    /// view.
    /// </summary>
    /// <param name="editVersion">The edit-selection version captured by the caller.</param>
    /// <param name="viewAtStart">The view filter captured by the caller.</param>
    /// <returns><see langword="true"/> when season choices are available; otherwise <see langword="false"/>.</returns>
    private async Task<bool> EnsureSeasonChoicesLoadedAsync(int editVersion, string viewAtStart)
    {
        if (_seasonChoices.Count > 0)
        {
            return true;
        }

        bool IsCurrent() => editVersion == _editVersion
            && viewAtStart == _statusFilter
            && !ComponentCancellationToken.IsCancellationRequested;

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

            if (IsCurrent())
            {
                _pageError = "Could not reach the server. Check your connection and retry.";
            }

            return false;
        }

        var loaded = false;
        result.Switch(
            setup =>
            {
                // Only the current edit selection may publish the payload; a superseded
                // completion must not replace fresher choices cached by a newer request.
                if (IsCurrent())
                {
                    _seasonChoices = setup.CurrentSeason is null ? [] : [setup.CurrentSeason];
                    _seasonChoiceTotalCount = _seasonChoices.Count;
                    _pageError = null;
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

                if (IsCurrent())
                {
                    _pageError = FirstNonBlank(problem.Detail, "Failed to load seasons. Please retry.");
                }
            });

        return loaded;
    }

    /// <summary>
    /// Applies an Active campaign metadata correction and reloads the list on success.
    /// </summary>
    /// <param name="model">The validated campaign metadata form state.</param>
    /// <returns>A task that completes when the update request finishes.</returns>
    private async Task UpdateCampaignAsync(CampaignMetadataFormState model)
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
            await HandleMutationResultAsync(
                result,
                updated =>
                {
                    _editCampaignForm = null;
                    _statusMessage = $"Campaign \"{updated.Name}\" metadata updated.";
                });
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
    /// Applies a season metadata correction and reloads the list on success.
    /// </summary>
    /// <param name="model">The validated season metadata form state.</param>
    /// <returns>A task that completes when the update request finishes.</returns>
    private async Task UpdateSeasonAsync(SeasonMetadataFormState model)
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
            var result = await seasonCommandService.UpdateAsync(
                model.SeasonId,
                model.ToUpdateInput(),
                ComponentCancellationToken);
            await HandleMutationResultAsync(
                result,
                updated =>
                {
                    _editSeasonForm = null;
                    _statusMessage = $"Season \"{updated.Name}\" metadata updated.";
                    // Invalidate cached season choices so the next campaign edit reloads current names/dates.
                    _seasonChoices = [];
                });
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
    /// Applies shared mutation result handling: success callback, Forbidden redirect, conflict and
    /// generic error feedback, then reloads the list after success without clearing the status message.
    /// </summary>
    /// <typeparam name="T">The mutation result payload type.</typeparam>
    /// <param name="result">The service result.</param>
    /// <param name="onSuccess">The action to run on success before the list reloads.</param>
    /// <returns>A task that completes when result handling finishes.</returns>
    private async Task HandleMutationResultAsync<T>(ServiceResult<T> result, Action<T> onSuccess)
    {
        var succeeded = false;
        result.Switch(
            value =>
            {
                onSuccess(value);
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
            await LoadListAsync();
        }
    }

    /// <summary>
    /// Closes any active mutation form and clears its feedback.
    /// </summary>
    private void CancelMutationForm()
    {
        _editCampaignForm = null;
        _editSeasonForm = null;
        _mutationError = null;
        _editRetryCandidate = null;
        _editRetrySeason = null;
        _editFormSeasonChoices = [];
        _mutationConflict = false;
        _editVersion++;
    }

    /// <summary>
    /// Closes the conflicted edit form and reloads the list so it reflects the campaign's
    /// current lifecycle state.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private async Task CloseFormAndReloadAsync()
    {
        CancelMutationForm();
        await LoadListAsync();
    }

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        ++_authenticationVersion;
        authenticationStateProvider.AuthenticationStateChanged -= DirectoryAuthenticationChanged;
        _loadListSource?.Cancel();
        _loadListSource?.Dispose();
        _loadListSource = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>Builds directory pagination links retaining the current status filter.</summary>
    /// <param name="page">The one-based destination page.</param>
    /// <returns>The local campaign directory URL.</returns>
    private string PageUrl(int page) => $"/campaigns?view={_statusFilter}&page={page}";

    /// <summary>Scopes retained directory data to the authenticated user, club, and Draft visibility.</summary>
    /// <param name="state">The authentication state whose claims own the directory.</param>
    /// <returns>The identity and authority key used to invalidate stale directory state.</returns>
    private static string DirectoryIdentity(AuthenticationState state) => string.Join(":",
        state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        state.User.FindFirst(NovaClaimTypes.ClubId)?.Value, state.User.IsInRole(Roles.ClubAdmin));

    /// <summary>Applies only the newest authentication notification before replacing the scoped directory.</summary>
    /// <param name="task">The pending authentication state supplied by the provider.</param>
    private void DirectoryAuthenticationChanged(Task<AuthenticationState> task) => _ = InvokeAsync(async () =>
    {
        var authenticationVersion = ++_authenticationVersion;
        var state = await task;
        if (authenticationVersion != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        var identity = DirectoryIdentity(state);
        if (identity == _identityScope)
        {
            return;
        }

        _identityScope = identity;
        _canManageCampaigns = state.User.IsInRole(Roles.ClubAdmin);
        _ = ApplyViewQueryToState();
        _list = null;
        PersistedList = null;
        _loadListSource?.Cancel();
        CancelMutationForm();
        _statusMessage = null;
        _page = 1;
        StateHasChanged();
        SyncViewToUrl();
        await LoadListAsync();
        StateHasChanged();
    });

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
    /// Formats a season group's date range for display.
    /// </summary>
    /// <param name="season">The season group.</param>
    /// <returns>The formatted date range.</returns>
    protected static string FormatSeasonDates(CampaignSeasonGroup season)
        => season.EndDate is null
            ? $"Starts {season.StartDate:MMM d, yyyy}"
            : $"{season.StartDate:MMM d, yyyy} – {season.EndDate.Value:MMM d, yyyy}";

    /// <summary>
    /// Formats a campaign row's date range for display.
    /// </summary>
    /// <param name="campaign">The campaign row.</param>
    /// <returns>The formatted date range.</returns>
    protected static string FormatCampaignDates(CampaignListItem campaign)
        => campaign.PlannedEndDate is null
            ? $"Starts {campaign.StartDate:MMM d, yyyy}"
            : $"{campaign.StartDate:MMM d, yyyy} – {campaign.PlannedEndDate.Value:MMM d, yyyy}";

    /// <summary>
    /// Maps a campaign lifecycle status to its Bootstrap badge class.
    /// </summary>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <returns>The badge background class.</returns>
    protected static string CampaignStatusBadgeClass(CampaignStatus status) => status switch
    {
        CampaignStatus.Active => "text-bg-success",
        CampaignStatus.Closed => "text-bg-secondary",
        _ => "text-bg-secondary"
    };
}
