using System.Globalization;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the campaign placements workspace: a graduation-year and unresolved-only filter bar, the
/// authoritative outcome summary, a responsive table/card roster, and per-row outcome/team editing
/// with concurrency-safe saves and a conflict recovery flow that discards every draft and reloads.
/// </summary>
/// <param name="placementQueryService">The placement roster and summary query service.</param>
/// <param name="placementService">The placement mutation service.</param>
/// <param name="teamRosterService">The active team choices service.</param>
/// <param name="participantQueryService">The graduation-year choices service.</param>
public partial class CampaignPlacementsPanel(
    ICampaignPlacementQueryService placementQueryService,
    ICampaignPlacementService placementService,
    ITeamRosterService teamRosterService,
    ICampaignParticipantQueryService participantQueryService) : NovaComponentBase
{
    /// <summary>
    /// The fixed placement roster page size requested by this panel.
    /// </summary>
    private const int PlacementPageSize = GetCampaignPlacementRosterInput.DefaultPageSize;

    /// <summary>
    /// The fallback conflict warning shown when the server does not supply a detail message.
    /// </summary>
    private const string ConflictFallbackMessage = "The placement was changed by someone else.";

    /// <summary>
    /// The fallback save-failure message shown when the server does not supply a detail message.
    /// </summary>
    private const string SaveFailureFallbackMessage = "Failed to save the placement. Please retry.";

    /// <summary>The maximum number of active teams loaded for placement choices.</summary>
    private const int TeamChoiceLimit = 200;

    /// <summary>
    /// Gets or sets the campaign identifier from the route.
    /// </summary>
    [Parameter]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the campaign lifecycle status, controlling the read-only frozen view.
    /// </summary>
    [Parameter]
    public CampaignStatus CampaignStatus { get; set; }

    /// <summary>
    /// Gets or sets whether the current user may edit placements (Active campaign + club administrator).
    /// </summary>
    [Parameter]
    public bool CanEditPlacements { get; set; }

    /// <summary>
    /// Gets or sets the placement filter and paging state owned by the parent page.
    /// </summary>
    [Parameter]
    public CampaignWorkspacePlacementState State { get; set; } = new();

    /// <summary>
    /// Gets or sets the callback invoked when the filter or page changes, with the requested placement state.
    /// </summary>
    [Parameter]
    public EventCallback<CampaignWorkspacePlacementState> OnStateChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a conflict recovery reload must also refresh the campaign detail.
    /// </summary>
    [Parameter]
    public EventCallback OnReloadRequested { get; set; }

    /// <summary>
    /// Gets or sets the persisted placement roster used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public PagedResult<CampaignPlacementRosterItem>? PersistedRoster { get; set; }

    /// <summary>
    /// Gets or sets the persisted placement summary used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignPlacementSummaryDto? PersistedSummary { get; set; }

    /// <summary>
    /// Gets or sets the persisted active team choices used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TeamRosterItem>? PersistedTeams { get; set; }

    /// <summary>
    /// Gets or sets the persisted graduation-year choices used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<int>? PersistedGraduationYears { get; set; }

    /// <summary>
    /// Gets or sets the persisted load error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// The loaded placement roster page, or <see langword="null"/> when unavailable.
    /// </summary>
    private PagedResult<CampaignPlacementRosterItem>? _roster;

    /// <summary>
    /// The loaded authoritative placement summary, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignPlacementSummaryDto? _summary;

    /// <summary>
    /// The active teams available as bounded choice metadata.
    /// </summary>
    private IReadOnlyList<TeamRosterItem> _teamChoices = [];

    /// <summary>
    /// The distinct graduation years available as filter choices.
    /// </summary>
    private IReadOnlyList<int> _graduationYears = [];

    /// <summary>
    /// The per-row edit drafts keyed by participant assignment identifier.
    /// </summary>
    private Dictionary<long, PlacementRowDraft> _drafts = [];

    /// <summary>
    /// Indicates whether the panel data is loading.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// The current panel-level load error message.
    /// </summary>
    private string? _error;

    /// <summary>
    /// Indicates that at least one filter-choice load failed.
    /// </summary>
    private bool _choicesLoadFailed;

    /// <summary>Indicates that the authoritative summary is unavailable.</summary>
    private bool _summaryLoadFailed;

    /// <summary>Stores a navigation state received while a row is saving.</summary>
    private CampaignWorkspacePlacementState? _pendingState;

    /// <summary>
    /// The current conflict warning message, or <see langword="null"/> when no conflict is active.
    /// </summary>
    private string? _conflictMessage;

    /// <summary>
    /// Indicates that submissions are blocked until a conflict reload completes.
    /// </summary>
    private bool _conflictActive;

    /// <summary>
    /// Indicates that a conflict recovery reload is in progress.
    /// </summary>
    private bool _reloading;

    /// <summary>
    /// The panel-level save success message, or <see langword="null"/> when none is active.
    /// </summary>
    private string? _saveMessage;

    /// <summary>
    /// Indicates that the last successful save could not refresh the authoritative summary.
    /// </summary>
    private bool _saveSummaryRefreshFailed;

    /// <summary>
    /// The placement state that produced the currently loaded roster.
    /// </summary>
    private CampaignWorkspacePlacementState _appliedState = new();

    /// <summary>
    /// The last applied campaign status, used to detect a Closed transition.
    /// </summary>
    private CampaignStatus _appliedStatus;

    /// <summary>
    /// Monotonic request-sequence token used to discard stale roster responses.
    /// </summary>
    private int _requestSequence;

    /// <summary>
    /// The conflict warning region, focused when a conflict warning appears.
    /// </summary>
    private ElementReference _conflictRegion;

    /// <summary>
    /// Indicates that the conflict warning region should receive focus after the next render.
    /// </summary>
    private bool _shouldFocusConflict;

    /// <summary>
    /// Gets a value indicating whether the panel renders static read-only rows.
    /// </summary>
    private bool IsReadOnly => CampaignStatus == CampaignStatus.Closed || !CanEditPlacements;

    /// <summary>Gets whether any row currently has an in-flight save.</summary>
    private bool _savingActive => _drafts.Values.Any(draft => draft.IsSaving);

    /// <summary>Gets whether team choices were capped and may be incomplete.</summary>
    private bool TeamChoicesTruncated => _teamChoices.Count == TeamChoiceLimit;

    /// <summary>
    /// Gets a value indicating whether a placement filter is active.
    /// </summary>
    private bool HasPlacementFilter => _appliedState.GraduationYear is not null || _appliedState.UnresolvedOnly;

    /// <summary>
    /// Gets the selected graduation year as a select-binding string.
    /// </summary>
    private string GraduationYearText
        => State.GraduationYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        _appliedState = State;
        _appliedStatus = CampaignStatus;

        if (Initialized)
        {
            _roster = PersistedRoster;
            _summary = PersistedSummary;
            _teamChoices = PersistedTeams ?? [];
            _graduationYears = PersistedGraduationYears ?? [];
            _error = PersistedError;
            if (_roster is not null)
            {
                RebuildDrafts(_roster);
            }

            _isLoading = false;
            return;
        }

        _isLoading = true;
        await LoadInitialAsync();
        PersistStartupState();
        Initialized = true;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var becameClosed = CampaignStatus == CampaignStatus.Closed && _appliedStatus != CampaignStatus.Closed;
        _appliedStatus = CampaignStatus;

        if (becameClosed)
        {
            ResetAllDrafts();
            _conflictMessage = null;
            _conflictActive = false;
            _reloading = false;
        }

        if (State != _appliedState)
        {
            if (_savingActive)
            {
                _pendingState = State;
                return;
            }
            _appliedState = State;
            await LoadRosterAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusConflict)
        {
            _shouldFocusConflict = false;
            await _conflictRegion.FocusAsync();
        }
    }

    /// <summary>
    /// Loads the roster, summary, and filter choices in parallel for the initial render.
    /// </summary>
    /// <returns>A task that completes when every initial load finishes.</returns>
    private async Task LoadInitialAsync()
    {
        _error = null;
        _choicesLoadFailed = false;
        _summaryLoadFailed = false;

        var teamChoicesTask = LoadTeamChoicesAsync();
        var graduationYearsTask = LoadGraduationYearChoicesAsync();
        await Task.WhenAll(LoadRosterAsync(), LoadSummaryAsync(), teamChoicesTask, graduationYearsTask);

        _choicesLoadFailed = !await teamChoicesTask || !await graduationYearsTask;
        _isLoading = false;
    }

    /// <summary>
    /// Loads the placement roster page for the currently applied state, discarding stale responses.
    /// </summary>
    /// <returns>A task that completes when the load finishes.</returns>
    private async Task LoadRosterAsync()
    {
        _error = null;
        var requestId = ++_requestSequence;

        var input = new GetCampaignPlacementRosterInput
        {
            CampaignId = CampaignId,
            GraduationYear = _appliedState.GraduationYear,
            UnresolvedOnly = _appliedState.UnresolvedOnly ? true : null,
            Page = _appliedState.Page,
            PageSize = PlacementPageSize
        };

        var result = await placementQueryService.GetPlacementRosterAsync(input, ComponentCancellationToken);

        if (requestId != _requestSequence)
        {
            return;
        }

        result.Switch(
            roster =>
            {
                _roster = roster;
                RebuildDrafts(roster);
            },
            problem =>
            {
                _error = FirstNonBlank(problem.Detail, "Failed to load placements. Please retry.");
                _roster = null;
                _drafts = [];
            });
    }

    /// <summary>
    /// Loads the authoritative placement summary without touching row drafts.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> when the summary loads successfully.</returns>
    private async Task<bool> LoadSummaryAsync()
    {
        var result = await placementQueryService.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = CampaignId },
            ComponentCancellationToken);

        var succeeded = false;
        result.Switch(
            summary =>
            {
                _summary = summary;
                _summaryLoadFailed = false;
                _saveSummaryRefreshFailed = false;
                succeeded = true;
            },
            _ =>
            {
                _summary = null;
                _summaryLoadFailed = true;
            });

        return succeeded;
    }

    /// <summary>
    /// Loads the active team choices for the bounded team selects.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> when the load succeeded.</returns>
    private async Task<bool> LoadTeamChoicesAsync()
    {
        var result = await teamRosterService.GetRosterAsync(
            new GetTeamRosterInput { LifecycleStatus = "active", Limit = TeamChoiceLimit },
            ComponentCancellationToken);
        var succeeded = false;
        result.Switch(
            teams =>
            {
                _teamChoices = teams;
                succeeded = true;
            },
            _ => { });
        return succeeded;
    }

    /// <summary>
    /// Loads the distinct graduation-year choices for the filter bar.
    /// </summary>
    /// <returns>A task that completes with <see langword="true"/> when the load succeeded.</returns>
    private async Task<bool> LoadGraduationYearChoicesAsync()
    {
        var result = await participantQueryService.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = CampaignId },
            ComponentCancellationToken);
        var succeeded = false;
        result.Switch(
            years =>
            {
                _graduationYears = years;
                succeeded = true;
            },
            _ => { });
        return succeeded;
    }

    /// <summary>
    /// Rebuilds the row drafts from the supplied roster page, discarding any previous edits.
    /// </summary>
    /// <param name="roster">The roster page to derive drafts from.</param>
    private void RebuildDrafts(PagedResult<CampaignPlacementRosterItem> roster)
        => _drafts = roster.Items.ToDictionary(
            item => item.PlayerCampaignAssignmentId,
            item => PlacementRowDraft.FromRow(item));

    /// <summary>
    /// Resets every existing row draft back to its loaded snapshot.
    /// </summary>
    private void ResetAllDrafts()
    {
        if (_roster is null)
        {
            _drafts = [];
            return;
        }

        foreach (var item in _roster.Items)
        {
            _drafts[item.PlayerCampaignAssignmentId] = PlacementRowDraft.FromRow(item);
        }
    }

    /// <summary>
    /// Persists the startup state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedRoster = _roster;
        PersistedSummary = _summary;
        PersistedTeams = _teamChoices;
        PersistedGraduationYears = _graduationYears;
        PersistedError = _error;
    }

    /// <summary>
    /// Raises a placement filter or page change back to the parent page and resets the page to one.
    /// </summary>
    /// <param name="next">The requested placement state.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task RaiseStateChangedAsync(CampaignWorkspacePlacementState next)
        => OnStateChanged.InvokeAsync(next);

    /// <summary>
    /// Handles a graduation-year filter change.
    /// </summary>
    /// <param name="args">The select change payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnGraduationYearChangedAsync(ChangeEventArgs args)
    {
        _saveMessage = null;
        var raw = args.Value?.ToString();
        var graduationYear = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : (int?)null;
        return RaiseStateChangedAsync(State with { GraduationYear = graduationYear, Page = 1 });
    }

    /// <summary>
    /// Handles an unresolved-only filter change.
    /// </summary>
    /// <param name="args">The checkbox change payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnUnresolvedOnlyChangedAsync(ChangeEventArgs args)
    {
        _saveMessage = null;
        return RaiseStateChangedAsync(State with { UnresolvedOnly = args.Value is true, Page = 1 });
    }

    /// <summary>
    /// Clears every placement filter.
    /// </summary>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnClearFiltersAsync()
    {
        _saveMessage = null;
        return RaiseStateChangedAsync(new CampaignWorkspacePlacementState());
    }

    /// <summary>
    /// Handles a placement page change.
    /// </summary>
    /// <param name="page">The requested one-based page number.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnPageChangedAsync(int page)
    {
        _saveMessage = null;
        return RaiseStateChangedAsync(State with { Page = Math.Max(1, page) });
    }

    /// <summary>
    /// Retries the initial load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load finishes.</returns>
    private async Task RetryAsync()
    {
        _isLoading = true;
        await LoadInitialAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Gets the row draft for a roster item, falling back to a clean throwaway draft when absent.
    /// </summary>
    /// <param name="item">The roster item whose draft is requested.</param>
    /// <returns>The existing row draft, or a clean draft when none exists.</returns>
    private PlacementRowDraft DraftFor(CampaignPlacementRosterItem item)
        => _drafts.TryGetValue(item.PlayerCampaignAssignmentId, out var draft)
            ? draft
            : PlacementRowDraft.FromRow(item);

    /// <summary>
    /// Handles an outcome change for a row, clearing the draft team when the outcome leaves Assigned.
    /// </summary>
    /// <param name="item">The roster item being edited.</param>
    /// <param name="args">The select change payload.</param>
    private void OnOutcomeChanged(CampaignPlacementRosterItem item, ChangeEventArgs args)
    {
        if (IsReadOnly || _conflictActive
            || !_drafts.TryGetValue(item.PlayerCampaignAssignmentId, out var draft)
            || draft.IsSaving)
        {
            return;
        }

        draft.Outcome = int.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? (PlacementOutcome)value
            : PlacementOutcome.Undecided;

        if (draft.Outcome != PlacementOutcome.Assigned)
        {
            draft.TeamId = null;
        }

        draft.SaveStatus = null;
        draft.RowError = null;
        _saveMessage = null;
    }

    /// <summary>
    /// Handles a team change for a row.
    /// </summary>
    /// <param name="item">The roster item being edited.</param>
    /// <param name="args">The select change payload.</param>
    private void OnTeamChanged(CampaignPlacementRosterItem item, ChangeEventArgs args)
    {
        if (IsReadOnly || _conflictActive
            || !_drafts.TryGetValue(item.PlayerCampaignAssignmentId, out var draft)
            || draft.IsSaving)
        {
            return;
        }

        var raw = args.Value?.ToString();
        draft.TeamId = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamId) && teamId > 0
            ? teamId
            : null;
        draft.SaveStatus = null;
        draft.RowError = null;
        _saveMessage = null;
    }

    /// <summary>
    /// Saves a dirty row through the placement mutation service and applies the outcome.
    /// </summary>
    /// <param name="item">The roster item being saved.</param>
    /// <returns>A task that completes when the save and summary refresh finish.</returns>
    private async Task SaveRowAsync(CampaignPlacementRosterItem item)
    {
        if (IsReadOnly || _conflictActive || _reloading)
        {
            return;
        }

        if (!_drafts.TryGetValue(item.PlayerCampaignAssignmentId, out var draft))
        {
            return;
        }

        if (draft.IsSaving || !draft.IsDirty)
        {
            return;
        }

        draft.RowError = null;
        draft.SaveStatus = null;
        _saveMessage = null;
        _saveSummaryRefreshFailed = false;

        var input = draft.ToInput();
        var clientErrors = InputValidator.Validate(input);
        if (clientErrors.Count > 0)
        {
            draft.RowError = FirstValidationMessage(clientErrors);
            return;
        }

        draft.IsSaving = true;

        try
        {
            var result = await placementService.UpdatePlacementAsync(input, ComponentCancellationToken);

            var saved = false;
            result.Switch(
                success =>
                {
                    draft.CurrentToken = success.ConcurrencyToken;
                    draft.RowError = null;
                    draft.SaveStatus = "Saved";
                    ApplySavedToDraft(draft);
                    RemoveFromUnresolvedViewIfNeeded(item, draft);
                    saved = true;
                },
                problem =>
                {
                    draft.RowError = null;
                    switch (problem.Kind)
                    {
                        case ServiceProblemKind.Conflict:
                            EnterConflict(problem.Detail);
                            break;
                        case ServiceProblemKind.Validation:
                            draft.RowError = FirstValidationMessage(problem.Errors);
                            break;
                        case ServiceProblemKind.Forbidden:
                        case ServiceProblemKind.NotFound:
                            draft.RowError = FirstNonBlank(problem.Detail, "This placement can no longer be updated.");
                            break;
                        default:
                            draft.RowError = FirstNonBlank(problem.Detail, SaveFailureFallbackMessage);
                            break;
                    }
                });

            if (saved)
            {
                if (await LoadSummaryAsync())
                {
                    _saveMessage = "Placement saved.";
                }
                else
                {
                    _saveSummaryRefreshFailed = true;
                }
            }
        }
        finally
        {
            // Always release the per-row save gate, even if the mutation or summary refresh
            // throws, so the row never stays stuck in the saving state.
            draft.IsSaving = false;
            if (_pendingState is { } pendingState)
            {
                _pendingState = null;
                _appliedState = pendingState;
                await LoadRosterAsync();
            }
        }
    }

    /// <summary>
    /// Updates a draft's snapshot to reflect a successfully saved outcome and team.
    /// </summary>
    /// <param name="draft">The draft whose snapshot should adopt the saved values.</param>
    private void ApplySavedToDraft(PlacementRowDraft draft)
    {
        var savedTeam = draft.Outcome == PlacementOutcome.Assigned && draft.TeamId is { } savedTeamId
            ? _teamChoices.FirstOrDefault(team => team.TeamId == savedTeamId) is { } team
                ? new CampaignParticipantTeamSummaryDto(team.TeamId, team.Name)
                : draft.Snapshot.Team
            : null;

        draft.Snapshot = draft.Snapshot with { PlacementOutcome = draft.Outcome, Team = savedTeam };
    }

    /// <summary>
    /// Removes a saved row from the unresolved-only view when its new outcome leaves Undecided.
    /// </summary>
    /// <param name="item">The saved roster item.</param>
    /// <param name="draft">The saved row draft.</param>
    private void RemoveFromUnresolvedViewIfNeeded(CampaignPlacementRosterItem item, PlacementRowDraft draft)
    {
        if (!_appliedState.UnresolvedOnly || draft.Outcome == PlacementOutcome.Undecided || _roster is null)
        {
            return;
        }

        var remaining = _roster.Items
            .Where(row => row.PlayerCampaignAssignmentId != item.PlayerCampaignAssignmentId)
            .ToList();
        _roster = new PagedResult<CampaignPlacementRosterItem>(
            remaining,
            _roster.Page,
            _roster.PageSize,
            Math.Max(0, _roster.TotalCount - 1));
        _drafts.Remove(item.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Enters the blocked conflict state, showing the actionable warning.
    /// </summary>
    /// <param name="detail">The server conflict detail, when available.</param>
    private void EnterConflict(string? detail)
    {
        _conflictMessage = FirstNonBlank(detail, ConflictFallbackMessage);
        _conflictActive = true;
        _shouldFocusConflict = true;
    }

    /// <summary>
    /// Discards every row draft and reloads roster, summary, choices, and the campaign detail.
    /// </summary>
    /// <returns>A task that completes when the reload finishes and editing re-enables.</returns>
    private async Task ReloadAndDiscardAsync()
    {
        if (_reloading)
        {
            return;
        }

        _reloading = true;
        _conflictActive = true;
        ResetAllDrafts();

        try
        {
            await Task.WhenAll(
                LoadRosterAsync(),
                LoadSummaryAsync(),
                LoadTeamChoicesAsync(),
                LoadGraduationYearChoicesAsync());

            await OnReloadRequested.InvokeAsync();
        }
        finally
        {
            // Always clear the conflict/save gates, even if a reload or detail refresh throws,
            // so the panel never stays permanently blocked with no recovery short of a page reload.
            _conflictActive = false;
            _conflictMessage = null;
            _reloading = false;
        }
    }

    /// <summary>
    /// Builds the player-detail link for a roster row, carrying the current placements workspace URL as the return URL.
    /// </summary>
    /// <param name="item">The roster item to link from.</param>
    /// <returns>The player-detail URL with an encoded return URL.</returns>
    private string BuildPlayerLink(CampaignPlacementRosterItem item)
    {
        var returnUrl = CampaignWorkspaceUrlState.BuildPlacementsWorkspaceUrl(CampaignId, _appliedState);
        return $"/players/{item.PlayerId}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    /// <summary>
    /// Determines whether a row's currently assigned team is absent from the active team choices.
    /// </summary>
    /// <param name="draft">The row draft to inspect.</param>
    /// <returns><see langword="true"/> when the assigned team is missing from the active choices.</returns>
    private bool IsCurrentTeamMissing(PlacementRowDraft draft)
        => draft.Outcome == PlacementOutcome.Assigned
            && draft.TeamId is { } teamId
            && _teamChoices.All(team => team.TeamId != teamId);

    /// <summary>
    /// Gets a draft's selected team identifier as a select-binding string.
    /// </summary>
    /// <param name="draft">The row draft whose team identifier should be projected.</param>
    /// <returns>The selected team identifier string, or an empty string when unset.</returns>
    private static string TeamText(PlacementRowDraft draft)
        => draft.TeamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Returns the first non-blank message from the supplied candidates.
    /// </summary>
    /// <param name="candidates">The candidate messages in preference order.</param>
    /// <returns>The first non-blank candidate.</returns>
    private static string FirstNonBlank(params string?[] candidates)
        => candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;

    /// <summary>
    /// Returns the first validation message from a structured error dictionary.
    /// </summary>
    /// <param name="errors">The field-to-messages error dictionary.</param>
    /// <returns>The first non-blank validation message, or a fallback when empty.</returns>
    private static string FirstValidationMessage(IReadOnlyDictionary<string, string[]>? errors)
        => errors is { Count: > 0 }
            ? errors.SelectMany(pair => pair.Value)
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? SaveFailureFallbackMessage
            : SaveFailureFallbackMessage;

    /// <summary>
    /// Mutable per-row edit draft that snapshots the loaded row and tracks local edits.
    /// </summary>
    private sealed class PlacementRowDraft
    {
        /// <summary>
        /// Gets or sets the loaded row snapshot the draft is compared against.
        /// </summary>
        public CampaignPlacementRosterItem Snapshot { get; set; } = null!;

        /// <summary>
        /// Gets or sets the draft placement outcome.
        /// </summary>
        public PlacementOutcome Outcome { get; set; }

        /// <summary>
        /// Gets or sets the draft team identifier, meaningful only for an Assigned outcome.
        /// </summary>
        public long? TeamId { get; set; }

        /// <summary>
        /// Gets or sets whether a save is in flight for this row.
        /// </summary>
        public bool IsSaving { get; set; }

        /// <summary>
        /// Gets or sets the transient saved status shown after a successful save.
        /// </summary>
        public string? SaveStatus { get; set; }

        /// <summary>
        /// Gets or sets the transient row-level error message.
        /// </summary>
        public string? RowError { get; set; }

        /// <summary>
        /// Gets or sets the current concurrency token, replaced after a successful save.
        /// </summary>
        public Guid CurrentToken { get; set; }

        /// <summary>
        /// Gets a value indicating whether the draft differs from its snapshot.
        /// </summary>
        public bool IsDirty
            => Outcome != Snapshot.PlacementOutcome
                || EffectiveTeamId != SnapshotTeamId;

        /// <summary>
        /// Gets the team identifier the snapshot persists, or <see langword="null"/> for non-assigned outcomes.
        /// </summary>
        private long? SnapshotTeamId
            => Snapshot.PlacementOutcome == PlacementOutcome.Assigned ? Snapshot.Team?.TeamId : null;

        /// <summary>
        /// Gets the team identifier the draft currently selects, or <see langword="null"/> for non-assigned outcomes.
        /// </summary>
        private long? EffectiveTeamId
            => Outcome == PlacementOutcome.Assigned ? TeamId : null;

        /// <summary>
        /// Creates a clean draft from a loaded roster row.
        /// </summary>
        /// <param name="item">The roster row to snapshot.</param>
        /// <returns>The new clean draft.</returns>
        public static PlacementRowDraft FromRow(CampaignPlacementRosterItem item) => new()
        {
            Snapshot = item,
            Outcome = item.PlacementOutcome,
            TeamId = item.Team?.TeamId,
            CurrentToken = item.ConcurrencyToken
        };

        /// <summary>
        /// Builds the mutation input from the current draft state.
        /// </summary>
        /// <returns>The placement update input.</returns>
        public UpdateCampaignPlacementInput ToInput() => new(
            Snapshot.PlayerCampaignAssignmentId,
            Outcome,
            Outcome == PlacementOutcome.Assigned ? TeamId : null,
            CurrentToken);
    }
}
