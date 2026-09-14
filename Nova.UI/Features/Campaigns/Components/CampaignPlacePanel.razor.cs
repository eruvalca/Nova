
using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the campaign Place destination as a player-first decision station: a bounded queue rail beside a
/// working sheet that holds the selected participant's authoritative evidence and decision controls.
/// </summary>
/// <remarks>
/// The surface consumes the delivered effective-placement read and the existing placement mutation only. It
/// never re-derives eligibility, never patches a row from a mutation response, and presents no second mutation
/// path: after a successful decision the queue page, the unfiltered section totals, and the selected
/// participant's evidence are all re-read authoritatively. Needs placement and the campaign-local
/// <c>Undecided</c> outcome are deliberately distinct here — zero Needs placement never stands in for the
/// explicit local outcomes a campaign needs before it can close.
/// </remarks>
/// <param name="placementQueries">The effective-placement read service.</param>
/// <param name="placementService">The placement mutation service.</param>
/// <param name="campaignPlacementQueries">The campaign-local outcome summary read used by the Closed posture.</param>
/// <param name="teamRosterService">The bounded team-choice read service.</param>
public partial class CampaignPlacePanel(
    IEffectivePlacementQueryService placementQueries,
    ICampaignPlacementService placementService,
    ICampaignPlacementQueryService campaignPlacementQueries,
    ITeamRosterService teamRosterService) : NovaComponentBase
{
    /// <summary>
    /// The documented cap passed to the team roster query for the bounded compatible-team choices.
    /// </summary>
    private const int TeamChoiceLimit = 200;

    /// <summary>
    /// The fallback conflict warning shown when the server supplies no detail message.
    /// </summary>
    private const string ConflictFallbackMessage = "This placement was changed by someone else.";

    /// <summary>
    /// The fallback save-failure message shown when the server supplies no detail message.
    /// </summary>
    private const string SaveFailureFallbackMessage = "Failed to save this placement. Please retry.";

    /// <summary>
    /// The written read-only statement a Closed campaign shows in place of every mutation control.
    /// </summary>
    private const string ClosedReadOnlyMessage = "Read-only — campaign is closed.";

    /// <summary>
    /// Gets or sets the campaign identifier from the route.
    /// </summary>
    [Parameter]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the authoritative campaign lifecycle. Place is read-only for every role once Closed.
    /// </summary>
    [Parameter]
    public CampaignStatus CampaignStatus { get; set; }

    /// <summary>
    /// Gets or sets the participant assignment linked from a campaign handoff, if any.
    /// </summary>
    [Parameter]
    public long? SelectedParticipantId { get; set; }

    /// <summary>
    /// Gets or sets the Evaluate return path carried by an Evaluate handoff.
    /// </summary>
    [Parameter]
    public string? EvaluationReturnPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the current campaign allows placement mutations.
    /// </summary>
    [Parameter]
    public bool CanEditPlacements { get; set; }

    /// <summary>
    /// Gets or sets the applied Place discovery state reflected in the workspace URL.
    /// </summary>
    [Parameter]
    public CampaignWorkspacePlacementState State { get; set; } = new();

    /// <summary>
    /// Gets or sets the graduation-year filter choices owned by the campaign workspace.
    /// </summary>
    [Parameter]
    public IReadOnlyList<int> GraduationYearChoices { get; set; } = [];

    /// <summary>
    /// Gets or sets the applied-tag filter choices owned by the campaign workspace.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TagDefinitionDto> TagChoices { get; set; } = [];

    /// <summary>
    /// Gets or sets the campaign-local team filter choices owned by the campaign workspace.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TeamRosterItem> CampaignTeamChoices { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the campaign-local team filter choices are truncated.
    /// </summary>
    [Parameter]
    public bool CampaignTeamChoicesTruncated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the shared discovery choices failed to load.
    /// </summary>
    [Parameter]
    public bool ChoicesLoadFailed { get; set; }

    /// <summary>
    /// Gets or sets the owner scope used to reject persisted state from another campaign, lifecycle, or authority.
    /// </summary>
    [Parameter]
    public string? Owner { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the Place discovery state changes.
    /// </summary>
    [Parameter]
    public EventCallback<CampaignWorkspacePlacementState> OnStateChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the selected participant changes.
    /// </summary>
    [Parameter]
    public EventCallback<long?> OnSelectionChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the panel needs an authoritative reload of the campaign itself.
    /// </summary>
    [Parameter]
    public EventCallback OnReloadRequested { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the shared discovery choices need a retry.
    /// </summary>
    [Parameter]
    public EventCallback OnRetryChoices { get; set; }

    /// <summary>
    /// Gets or sets the persisted queue snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignPlacePersistedSnapshot? PersistedQueue { get; set; }

    /// <summary>
    /// Gets or sets the persisted owner scope used to reject stale persisted state.
    /// </summary>
    [PersistentState]
    public string? PersistedOwner { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the panel has already completed its startup load.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>The applied queue page, section, and discovery state currently reflected by the loaded page.</summary>
    private CampaignWorkspacePlacementState _appliedState = new();

    /// <summary>The linked participant currently reflected by the loaded queue and evidence.</summary>
    private long? _appliedParticipantId;

    /// <summary>The lifecycle currently reflected by the loaded queue and evidence.</summary>
    private CampaignStatus _appliedStatus;

    /// <summary>The loaded queue page and its unfiltered section totals.</summary>
    private CampaignPlaceQueueData? _queue;

    /// <summary>Indicates a queue request is in flight.</summary>
    private bool _queueLoading;

    /// <summary>The queue load failure message, or <see langword="null"/> when the queue is healthy.</summary>
    private string? _queueError;

    /// <summary>Indicates the retained queue totals are no longer known to be current.</summary>
    private bool _queueStale;

    /// <summary>The monotonic queue request identifier used to discard obsolete responses.</summary>
    private int _queueRequestSequence;

    /// <summary>The selected participant's authoritative evidence.</summary>
    private CampaignPlaceQueueRow? _selected;

    /// <summary>Indicates a selected-participant evidence request is in flight.</summary>
    private bool _selectedLoading;

    /// <summary>The selected-participant read failure message, or <see langword="null"/> when healthy.</summary>
    private string? _selectedError;

    /// <summary>The monotonic selected-participant request identifier used to discard obsolete responses.</summary>
    private int _selectedRequestSequence;

    /// <summary>The bounded active teams compatible with the selected participant's graduation year.</summary>
    private IReadOnlyList<TeamRosterItem> _compatibleTeams = [];

    /// <summary>Indicates a compatible-team choices request is in flight.</summary>
    private bool _teamChoicesLoading;

    /// <summary>The compatible-team choices failure message, or <see langword="null"/> when healthy.</summary>
    private string? _teamChoicesError;

    /// <summary>The monotonic team-choices request identifier used to discard obsolete responses.</summary>
    private int _teamChoicesRequestSequence;

    /// <summary>The drafted outcome.</summary>
    private PlacementOutcome _draftOutcome = PlacementOutcome.Undecided;

    /// <summary>The drafted team identifier.</summary>
    private long? _draftTeamId;

    /// <summary>The local concurrency token the next save must present.</summary>
    private Guid? _draftToken;

    /// <summary>Indicates a placement save is in flight.</summary>
    private bool _saving;

    /// <summary>The success announcement shown after an authoritative save.</summary>
    private string? _saveMessage;

    /// <summary>The local decision error shown beside the decision controls.</summary>
    private string? _saveError;

    /// <summary>The conflict statement shown when another member saved first.</summary>
    private string? _conflictMessage;

    /// <summary>Indicates a refusal that requires an authoritative reload before further editing.</summary>
    private bool _conflictActive;

    /// <summary>Indicates the conflict statement should take focus after the next render.</summary>
    private bool _shouldFocusConflict;

    /// <summary>The conflict statement element that receives focus when a save is refused.</summary>
    private ElementReference _conflictBanner;

    /// <summary>Indicates a reconcile reload triggered by the conflict recovery is in flight.</summary>
    private bool _reconciling;

    /// <summary>
    /// Gets a value indicating whether the campaign permits placement mutations at all.
    /// </summary>
    private bool IsClosed => CampaignStatus != CampaignStatus.Active;

    /// <summary>
    /// Gets a value indicating whether the decision controls are editable.
    /// </summary>
    private bool CanRecordDecision => CanEditPlacements && !IsClosed && !_conflictActive;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var owner = EffectiveOwner;
        if (Initialized && PersistedQueue is not null && string.Equals(PersistedOwner, owner, StringComparison.Ordinal))
        {
            RestorePersistedQueue(PersistedQueue);
            return;
        }

        await LoadInitialAsync();
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_saving)
        {
            // A discovery change that arrives mid-save is deferred rather than dropped, so browser
            // back/forward during a save cannot orphan the in-flight mutation or lose the new state.
            if (!Equals(_appliedState, State))
            {
                _pendingState = State;
            }

            return;
        }

        if (!Initialized)
        {
            return;
        }

        // A lifecycle or authority change swaps the authoritative read entirely: Active and Closed are
        // different endpoints with different shapes, so neither posture's retained evidence may stand in
        // for the other, and prior authority's evidence must not survive a re-authorization.
        if (_appliedStatus != CampaignStatus
            || !string.Equals(PersistedOwner, EffectiveOwner, StringComparison.Ordinal))
        {
            await LoadInitialAsync();
            return;
        }

        if (!Equals(_appliedState, State))
        {
            _saveMessage = null;
            _saveError = null;
            await LoadQueueAsync(State);
            await RefreshSelectionAsync();
            return;
        }

        if (_appliedParticipantId != SelectedParticipantId)
        {
            await RefreshSelectionAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusConflict && _conflictBanner.Context is not null)
        {
            _shouldFocusConflict = false;

            // Move focus to the conflict statement so a keyboard user lands on the recovery affordance
            // rather than being left on a now-disabled decision control.
            try
            {
                await _conflictBanner.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // The element left the DOM between render and focus; the statement is still announced.
            }
        }
    }

    /// <summary>
    /// Gets the owner scope used to reject persisted state belonging to another campaign, lifecycle, or authority.
    /// </summary>
    private string EffectiveOwner => Owner ?? $"{CampaignId}:{CampaignStatus}";

    /// <summary>
    /// Runs the panel's first authoritative load for the current lifecycle.
    /// </summary>
    /// <returns>A task that completes when the startup load finishes.</returns>
    private async Task LoadInitialAsync()
    {
        await LoadQueueAsync(State);
        await RefreshSelectionAsync();
        PersistedOwner = EffectiveOwner;
        Initialized = true;
    }

    /// <summary>
    /// Restores the persisted queue snapshot so an interactive attach does not refetch what the prerender loaded.
    /// </summary>
    /// <param name="persisted">The persisted snapshot to restore.</param>
    private void RestorePersistedQueue(CampaignPlacePersistedSnapshot persisted)
    {
        _appliedState = State;
        _appliedParticipantId = SelectedParticipantId;
        _appliedStatus = CampaignStatus;
        _queue = new CampaignPlaceQueueData(persisted.Rows, persisted.Page, persisted.PageSize, persisted.TotalCount, persisted.Sections);
        _queueStale = persisted.Stale;
        if (persisted.SelectedParticipantId is { } selectedId)
        {
            _selected = persisted.Rows.FirstOrDefault(row => row.PlayerCampaignAssignmentId == selectedId);
            ApplyDraftFromSelection();
        }
    }

    /// <summary>
    /// Writes the currently loaded queue and evidence into the persisted snapshot.
    /// </summary>
    private void PersistQueue()
    {
        PersistedQueue = _queue is null
            ? null
            : new CampaignPlacePersistedSnapshot
            {
                Rows = [.. _queue.Rows],
                Page = _queue.Page,
                PageSize = _queue.PageSize,
                TotalCount = _queue.TotalCount,
                Sections = _queue.Sections,
                SelectedParticipantId = _selected?.PlayerCampaignAssignmentId,
                Stale = _queueStale
            };
    }

    /// <summary>
    /// Projects the current selection into the decision draft and loads its compatible team choices.
    /// </summary>
    private void ApplyDraftFromSelection()
    {
        _draftOutcome = _selected?.LocalOutcome ?? PlacementOutcome.Undecided;
        _draftTeamId = _selected?.LocalDecision?.TeamId;
        _draftToken = _selected?.ConcurrencyToken;
        _saveError = null;
    }

    /// <summary>
    /// Gets a value indicating whether the drafted values differ from the selected participant's saved decision.
    /// </summary>
    private bool IsDraftDirty => _selected is not null
        && (_draftOutcome != _selected.LocalOutcome || (_draftOutcome == PlacementOutcome.Assigned && _draftTeamId != _selected.LocalDecision?.TeamId));

    /// <summary>
    /// Gets a value indicating whether a draft filter, search, section, or sort deviates from the defaults.
    /// </summary>
    private bool HasActiveFilters => CampaignWorkspaceUrlState.HasActivePlaceFilters(_appliedState);

    /// <summary>
    /// Applies the requested Place discovery state through the workspace URL owner.
    /// </summary>
    /// <param name="next">The Place state to apply.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task ApplyStateAsync(CampaignWorkspacePlacementState next)
    {
        if (_saving)
        {
            return Task.CompletedTask;
        }

        _saveMessage = null;
        _saveError = null;
        return OnStateChanged.InvokeAsync(next);
    }

    /// <summary>
    /// Clears every Place filter, search, and sort.
    /// </summary>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnClearFiltersAsync() => ApplyStateAsync(CampaignWorkspaceUrlState.ClearPlaceFilters(_appliedState));

    /// <summary>
    /// Applies a name-or-tryout-number search term.
    /// </summary>
    /// <param name="search">The applied search term.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnSearchChangedAsync(string search)
    {
        var trimmed = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return ApplyStateAsync(_appliedState with { Search = trimmed, Page = 1 });
    }

    /// <summary>
    /// Applies the selected Place section.
    /// </summary>
    /// <param name="section">The section token, or an empty string for every section.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnSectionChangedAsync(string section)
    {
        var token = string.IsNullOrWhiteSpace(section) ? CampaignWorkspaceUrlState.AllPlacementSections : section;
        return ApplyStateAsync(_appliedState with { Eligibility = token, Page = 1 });
    }

    /// <summary>
    /// Applies a graduation-year filter toggle.
    /// </summary>
    /// <param name="change">The toggled graduation year and its new selection state.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnGraduationYearToggledAsync((int Year, bool Selected) change)
    {
        var years = change.Selected
            ? _appliedState.GraduationYears.Append(change.Year).Distinct().OrderBy(value => value).ToArray()
            : _appliedState.GraduationYears.Where(value => value != change.Year).ToArray();
        return ApplyStateAsync(_appliedState with { GraduationYears = years, Page = 1 });
    }

    /// <summary>
    /// Applies an applied-tag filter toggle.
    /// </summary>
    /// <param name="change">The toggled tag identifier and its new selection state.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnTagToggledAsync((long PlayerTagId, bool Selected) change)
    {
        var tags = change.Selected
            ? _appliedState.TagDefinitionIds.Append(change.PlayerTagId).Distinct().OrderBy(value => value).ToArray()
            : _appliedState.TagDefinitionIds.Where(value => value != change.PlayerTagId).ToArray();
        return ApplyStateAsync(_appliedState with { TagDefinitionIds = tags, Page = 1 });
    }

    /// <summary>
    /// Applies the campaign-local outcome filter.
    /// </summary>
    /// <param name="outcome">The outcome token, or an empty string for every outcome.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnOutcomeChangedAsync(string outcome)
    {
        var token = string.IsNullOrWhiteSpace(outcome) ? null : outcome;
        return ApplyStateAsync(_appliedState with { Outcome = token, Page = 1 });
    }

    /// <summary>
    /// Applies the campaign-local team filter.
    /// </summary>
    /// <param name="teamId">The team identifier, or <see langword="null"/> for every team.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnCampaignTeamChangedAsync(long? teamId)
        => ApplyStateAsync(_appliedState with { TeamId = teamId, Page = 1 });

    /// <summary>
    /// Applies a queue page change.
    /// </summary>
    /// <param name="page">The requested one-based page.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnPageChangedAsync(int page)
    {
        var target = Math.Max(1, page);
        return target == _appliedState.Page ? Task.CompletedTask : ApplyStateAsync(_appliedState with { Page = target });
    }

    /// <summary>
    /// Selects a queue row, preserving the current discovery state and page in the URL.
    /// </summary>
    /// <param name="assignmentId">The participant assignment to select.</param>
    /// <returns>A task that completes when the selection is raised.</returns>
    private Task OnSelectAsync(long assignmentId) => SelectParticipantAsync(assignmentId);

    /// <summary>
    /// Clears the current selection, returning the queue to its browsing posture.
    /// </summary>
    /// <returns>A task that completes when the selection is cleared.</returns>
    private Task OnCloseSelectionAsync() => SelectParticipantAsync(null);

    /// <summary>
    /// Builds the participant link that preserves the current Place context as a return URL.
    /// </summary>
    /// <param name="row">The row to link.</param>
    /// <returns>The player-detail URL with an encoded return URL.</returns>
    private string BuildPlayerLink(CampaignPlaceQueueRow row)
    {
        var returnUrl = CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(
            CampaignId, _appliedState, placementParticipantId: row.PlayerCampaignAssignmentId, returnToEvaluation: EvaluationReturnPath is not null);
        return $"/players/{row.PlayerId}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
