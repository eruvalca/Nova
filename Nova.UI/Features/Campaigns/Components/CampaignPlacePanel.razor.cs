
#pragma warning disable CA1849, S6966 // Cancellation callbacks finish before replacing or disposing request state; yielding here changes ownership ordering.
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
    /// The number of times the first authoritative load may repeat while the caller keeps changing the
    /// lifecycle, authority scope, discovery state, or participant underneath it.
    /// </summary>
    private const int StartupReconciliationPasses = 4;

    /// <summary>
    /// The fallback conflict warning shown when the server supplies no detail message.
    /// </summary>
    private const string ConflictFallbackMessage = "This placement was changed by someone else.";

    /// <summary>
    /// The fallback save-failure message shown when the server supplies no detail message.
    /// </summary>
    private const string SaveFailureFallbackMessage = "Failed to save this placement. Please retry.";

    /// <summary>
    /// The pause after the last keystroke before the search is applied, matching the Roster destination.
    /// </summary>
    private const int SearchDebounceMilliseconds = 350;

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
    /// Gets or sets the workspace-owned campaign-local team search, so the shared filter's team search
    /// actually narrows the bounded choice list it promises to narrow.
    /// </summary>
    [Parameter]
    public string? CampaignTeamSearch { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the campaign-local team search changes.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnCampaignTeamSearchChanged { get; set; }

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
    /// Gets or sets the workspace-composed Place URL for a selected participant, so queue rows are real
    /// destinations and a player-detail round trip preserves the Roster and evaluation context.
    /// </summary>
    [Parameter]
    public Func<long?, string>? ComposePlaceUrl { get; set; }

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

    /// <summary>
    /// Whether the rendered participant rows came from a read that did not answer, so they are not
    /// authoritative for the state the controls now show.
    /// </summary>
    private bool _queueRowsStale;

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

    /// <summary>
    /// The canonical query string of the applied discovery state. The state record holds interface-typed
    /// collections, which compare by reference, so record equality cannot tell an unchanged filter from a
    /// re-parsed one; the serialized form can.
    /// </summary>
    private string _appliedQueryString = string.Empty;

    /// <summary>The bounded active teams compatible with the selected participant's graduation year.</summary>
    private IReadOnlyList<TeamRosterItem> _compatibleTeams = [];

    /// <summary>Indicates a compatible-team choices request is in flight.</summary>
    private bool _teamChoicesLoading;

    /// <summary>The compatible-team choices failure message, or <see langword="null"/> when healthy.</summary>
    private string? _teamChoicesError;

    /// <summary>The monotonic team-choices request identifier used to discard obsolete responses.</summary>
    private int _teamChoicesRequestSequence;

    /// <summary>The applied compatible-team search, or an empty string when unfiltered.</summary>
    private string _teamChoicesSearch = string.Empty;

    /// <summary>Indicates the compatible-team choices hit the documented cap and need a narrower search.</summary>
    private bool _teamChoicesTruncated;

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

    /// <summary>The search text the member is currently typing, owned here so a round trip cannot rewrite it.</summary>
    private string _searchDraft = string.Empty;

    /// <summary>The in-flight search debounce, cancelled by the next keystroke and on disposal.</summary>
    private CancellationTokenSource? _searchDebounce;

    /// <summary>The conflict statement element that receives focus when a save is refused.</summary>
    private ElementReference _conflictBanner;

    /// <summary>The queue region heading, focused when a selection returns to the queue.</summary>
    private ElementReference _queueHeading;

    /// <summary>The sheet region heading, focused when a participant is selected.</summary>
    private ElementReference _sheetHeading;

    /// <summary>
    /// The focus destination requested by the most recent selection transition: <see langword="true"/> for the
    /// sheet, <see langword="false"/> for the queue, <see langword="null"/> when none is pending.
    /// </summary>
    private bool? _stageFocusTarget;

    /// <summary>Indicates a reconcile reload triggered by the conflict recovery is in flight.</summary>
    private bool _reconciling;

    /// <summary>
    /// Gets a value indicating whether the campaign permits placement mutations at all.
    /// </summary>
    private bool IsClosed => CampaignStatus != CampaignStatus.Active;

    /// <summary>
    /// Gets a value indicating whether the selected participant can still receive a decision in this
    /// campaign. Archived players and local withdrawals cannot be edited; unavailable prior withdrawals
    /// require the separate administrator supersession workflow, which this slice does not expose.
    /// </summary>
    private bool IsDecisionAllowed => _selected is { } selected
        && selected.PlayerLifecycleStatus is null or LifecycleStatus.Active
        && selected.Eligibility != EffectivePlacementEligibility.Unavailable
        && selected.LocalOutcome != PlacementOutcome.Withdrawn;

    /// <summary>
    /// Gets a value indicating whether the decision controls are editable.
    /// </summary>
    private bool CanRecordDecision => CanEditPlacements && !IsClosed && !_conflictActive && IsDecisionAllowed;

    /// <summary>
    /// Gets the written reason a selected participant cannot receive a decision here.
    /// </summary>
    private string DecisionUnavailableReason => _selected switch
    {
        { PlayerLifecycleStatus: not null and not LifecycleStatus.Active } =>
            "This player is archived, so no placement decision can be recorded.",
        { LocalOutcome: PlacementOutcome.Withdrawn } =>
            "This player is withdrawn for the season. Only a superseding decision in a later active campaign can change it.",
        _ => "This player is withdrawn for the season. Administrator recovery of a prior-campaign withdrawal is not available here."
    };

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
        // A pending search debounce is owned by the URL. If the canonical state changes for any other
        // reason - Back/Forward, a section or filter change, a lifecycle switch - the draft must not fire
        // afterwards and overwrite the newer state with a stale search.
        if (IsDiscoveryChanged)
        {
            _searchDebounce?.Cancel();
            _searchDebounce?.Dispose();
            _searchDebounce = null;
            _searchDraft = _appliedState.Search ?? string.Empty;
        }

        if (_saving)
        {
            // A change that arrives mid-save is deferred rather than dropped, so browser back/forward during a
            // save cannot orphan the in-flight mutation, lose the new state, or leave the previous posture's
            // evidence rendered behind a save that is still settling.
            DeferWhileSaving();
            return;
        }

        if (!Initialized)
        {
            return;
        }

        // A lifecycle or authority change swaps the authoritative read entirely: Active and Closed are
        // different endpoints with different shapes, so neither posture's retained evidence may stand in
        // for the other, and prior authority's evidence must not survive a re-authorization.
        if (IsPostureChanged)
        {
            ClearPostureEvidence();
            await LoadInitialAsync();
            return;
        }

        if (IsDiscoveryChanged)
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
        // On viewports where the queue and the sheet are staged, a selection transition hides the stage the
        // member was focused in, so focus has to move to the stage that replaced it.
        if (!firstRender && _stageFocusTarget is { } focusSheet)
        {
            _stageFocusTarget = null;
            var target = focusSheet ? _sheetHeading : _queueHeading;
            if (target.Context is not null)
            {
                try
                {
                    await target.FocusAsync();
                }
                catch (InvalidOperationException)
                {
                    // The region left the DOM between render and focus; its heading is still announced.
                }
            }
        }

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

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = null;
        return base.DisposeAsyncCore();
    }

    /// <summary>
    /// Gets the owner scope used to reject persisted state belonging to another campaign, lifecycle, or authority.
    /// </summary>
    /// <remarks>
    /// The lifecycle is appended here rather than left to the host. Active and Closed read different endpoints
    /// with different shapes, so a snapshot the host keyed without the lifecycle could restore one posture's
    /// rows as the other's and then stamp them as current without refetching.
    /// </remarks>
    private string EffectiveOwner => string.IsNullOrWhiteSpace(Owner)
        ? $"{CampaignId}:{CampaignStatus}"
        : $"{Owner}:{CampaignStatus}";

    /// <summary>
    /// Appends the written state of a saved-team entry the narrowed choices did not offer.
    /// </summary>
    /// <param name="team">The rendered choice.</param>
    /// <returns>The suffix, or <see langword="null"/> when the choice needs none.</returns>
    /// <remarks>
    /// The words come from the authoritative correction reason, so a disabled entry never claims a state the
    /// read did not report.
    /// </remarks>
    private string? TeamChoiceSuffix(CampaignPlaceTeamChoice team) => team.Unavailable
        ? _selected?.CorrectionReason switch
        {
            PlacementCorrectionReason.TeamArchived => " (archived)",
            PlacementCorrectionReason.TeamIncompatible => " (not compatible with this year)",
            PlacementCorrectionReason.TeamUnavailable => " (not in this club)",
            _ => " (no longer available)"
        }
        : null;

    /// <summary>
    /// Gets a value indicating whether the incoming discovery state differs from the applied one.
    /// </summary>
    private bool IsDiscoveryChanged
        => !string.Equals(_appliedQueryString, QueryKey(State), StringComparison.Ordinal);

    /// <summary>
    /// Produces the canonical comparison key for a Place discovery state.
    /// </summary>
    /// <param name="state">The state to key.</param>
    /// <returns>The canonical query string.</returns>
    private static string QueryKey(CampaignWorkspacePlacementState state)
        => CampaignWorkspaceUrlState.BuildPlacementQueryString(state);

    /// <summary>
    /// Defers the parameter changes that arrived while a save holds the panel, dropping the evidence a
    /// lifecycle or authority change replaces.
    /// </summary>
    /// <remarks>
    /// Deferring is what keeps the URL, the rows, and the in-flight mutation consistent: the discovery state is
    /// applied by every settlement path, and a posture boundary is reconciled in full once the save settles.
    /// </remarks>
    private void DeferWhileSaving()
    {
        if (IsDiscoveryChanged)
        {
            _pendingState = State;
        }

        if (IsPostureChanged)
        {
            _pendingPosture = true;
            ClearPostureEvidence();
        }
    }

    /// <summary>
    /// Drops the evidence that belongs to the lifecycle or authority scope being left, and invalidates the
    /// reads already in flight for it.
    /// </summary>
    /// <remarks>
    /// The replacement read is asynchronous, so without this the posture being left keeps rendering its rows,
    /// sheet, and team choices until the new requests answer - and a response already on the wire for the old
    /// posture could still land and be adopted as current. Advancing the request sequences is what makes those
    /// responses obsolete.
    /// </remarks>
    private void ClearPostureEvidence()
    {
        _queue = null;
        _queueError = null;
        _queueStale = false;
        _queueRowsStale = false;
        _queueLoading = true;
        _selected = null;
        _selectedError = null;
        _compatibleTeams = [];
        _teamChoicesError = null;
        _teamChoicesLoading = false;

        ++_queueRequestSequence;
        ++_selectedRequestSequence;
        ++_teamChoicesRequestSequence;
    }

    /// <summary>
    /// Runs the panel's first authoritative load for the current lifecycle.
    /// </summary>
    /// <returns>A task that completes when the startup load finishes.</returns>
    /// <remarks>
    /// A parameter set that arrives while a load is in flight is skipped by the initialization guard in
    /// <see cref="OnParametersSetAsync"/>, so each pass re-reads every input the caller can change - lifecycle,
    /// authority scope, discovery state, and participant - and repeats while any of them moved. The bound
    /// keeps a caller that never settles from spinning the renderer; the next parameter set would still
    /// reconcile it through the ordinary path.
    /// </remarks>
    private async Task LoadInitialAsync()
    {
        for (var pass = 0; pass < StartupReconciliationPasses; pass++)
        {
            var owner = EffectiveOwner;
            var state = State;

            await LoadQueueAsync(state);
            await RefreshSelectionAsync();
            PersistedOwner = owner;
            Initialized = true;

            // The snapshot may only be published under the parameters the caller is supplying now: anything
            // that moved while the reads were in flight is adopted by another pass.
            if (_appliedStatus == CampaignStatus
                && _appliedParticipantId == SelectedParticipantId
                && string.Equals(owner, EffectiveOwner, StringComparison.Ordinal)
                && string.Equals(QueryKey(state), QueryKey(State), StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Restores the persisted queue snapshot so an interactive attach does not refetch what the prerender loaded.
    /// </summary>
    /// <param name="persisted">The persisted snapshot to restore.</param>
    private void RestorePersistedQueue(CampaignPlacePersistedSnapshot persisted)
    {
        _appliedState = State;
        _appliedQueryString = QueryKey(State);
        _searchDraft = State.Search ?? string.Empty;

        // The applied participant is deliberately left unset. The snapshot carries rows but not compatible
        // team choices, and an off-page selection is not in those rows at all, so marking the selection as
        // already applied would make the parameter pass skip the reconciliation that rebuilds it.
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
    /// Gets the written message for an empty queue page. The default section asks only for Needs placement,
    /// so an empty page does not mean the campaign has no participants.
    /// </summary>
    private string EmptyQueueMessage
    {
        get
        {
            if (HasActiveFilters)
            {
                return "No participants match the current filters.";
            }

            if (!IsClosedContext(CampaignStatus)
                && string.Equals(
                    CampaignWorkspaceUrlState.ResolvePlacementEligibility(_appliedState),
                    "NeedsPlacement",
                    StringComparison.Ordinal))
            {
                return "No participant needs placement in this campaign.";
            }

            return "No participants in this campaign yet.";
        }
    }

    /// <summary>
    /// Gets the written notice for a queue whose evidence is not authoritative, naming every region affected.
    /// </summary>
    private string QueueStaleNotice => _queueRowsStale
        ? "These participants and totals may be out of date."
        : TotalsNotice;

    /// <summary>
    /// Gets the written notice for totals that are stale, or that could not be read at all.
    /// </summary>
    private string TotalsNotice => _queue is { Sections.Count: 0 }
        ? "The campaign-wide totals could not be loaded."
        : "These totals may be out of date.";

    /// <summary>
    /// Builds the canonical Place URL that selects a participant, so a queue row is a normal local link
    /// rather than only an event handler.
    /// </summary>
    /// <param name="assignmentId">The participant assignment to select.</param>
    /// <returns>The relative Place workspace URL for that selection.</returns>
    private string SelectUrl(long assignmentId)
        => ComposePlaceUrl is { } compose
            ? compose(assignmentId)
            : CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, _appliedState, placementParticipantId: assignmentId);

    /// <summary>
    /// Builds the canonical Place URL with nothing selected, so the way back to the queue is a real link.
    /// </summary>
    private string ClearSelectionUrl
        => ComposePlaceUrl is { } compose
            ? compose(null)
            : CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, _appliedState);

    /// <summary>
    /// Applies the requested Place discovery state through the workspace URL owner.
    /// </summary>
    /// <param name="next">The Place state to apply.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    /// <remarks>
    /// A change raised mid-save is handed to the URL rather than dropped: the workspace supplies the new
    /// state, <see cref="OnParametersSetAsync"/> defers it while the save is in flight, and every settlement
    /// path applies it. Discarding the callback instead would lose the member's choice while the controls
    /// kept showing it.
    /// </remarks>
    private Task ApplyStateAsync(CampaignWorkspacePlacementState next)
    {
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
    /// <param name="search">The search term currently typed by the member.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnSearchChangedAsync(string search)
    {
        var trimmed = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return ApplyStateAsync(_appliedState with { Search = trimmed, Page = 1 });
    }

    /// <summary>
    /// Accepts a keystroke from the shared search field and raises the applied search once typing settles.
    /// </summary>
    /// <remarks>
    /// The field is bound to this panel's own draft rather than to the applied value, because binding it to
    /// the applied value lets a completed round trip rewrite the input mid-word. Debouncing keeps every
    /// keystroke from costing a navigation, an authoritative read, and a browser history entry.
    /// </remarks>
    /// <param name="search">The search text currently typed by the member.</param>
    /// <returns>A task that completes when the debounce settles.</returns>
    private async Task OnSearchInputAsync(string search)
    {
        _searchDraft = search;
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        var source = _searchDebounce;
        try
        {
            await Task.Delay(SearchDebounceMilliseconds, source.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke, or the component departing, superseded this debounce.
            return;
        }

        if (source.IsCancellationRequested)
        {
            return;
        }

        await OnSearchChangedAsync(search);
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
    /// Builds the participant link that preserves the current Place context as a return URL.
    /// </summary>
    /// <param name="row">The row to link.</param>
    /// <returns>The player-detail URL with an encoded return URL.</returns>
    private string BuildPlayerLink(CampaignPlaceQueueRow row)
    {
        // The workspace owns the surrounding URL context, so it composes the return URL when it can; the
        // local fallback still carries the Place state and selection for a standalone render.
        var returnUrl = ComposePlaceUrl is { } compose
            ? compose(row.PlayerCampaignAssignmentId)
            : CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(
                CampaignId, _appliedState, placementParticipantId: row.PlayerCampaignAssignmentId, returnToEvaluation: EvaluationReturnPath is not null);
        return $"/players/{row.PlayerId}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }
}
