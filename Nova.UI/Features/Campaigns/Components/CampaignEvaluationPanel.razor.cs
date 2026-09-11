using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Finds a participant deliberately, then captures shared evidence without advancing automatically.</summary>
/// <param name="participants">The independent identity query.</param>
/// <param name="placements">The Active effective and Closed local discovery reads.</param>
/// <param name="evidence">The bounded evidence regions.</param>
/// <param name="notes">The versioned note mutation boundary.</param>
/// <param name="tags">The atomic trait mutation boundary.</param>
/// <param name="navigation">Native route and history integration.</param>
/// <param name="js">Tab storage, navigation protection, focus and scroll.</param>
public partial class CampaignEvaluationPanel(ICampaignParticipantQueryService participants, IEffectivePlacementQueryService placements,
    ICampaignEvaluationQueryService evidence, ICampaignEvaluationNoteService notes, ICampaignTagApplicationService tags,
    NavigationManager navigation, IJSRuntime js) : NovaComponentBase
{
    /// <summary>The authorized campaign identity.</summary>
    [Parameter] public long CampaignId { get; set; }
    /// <summary>The independently refreshed campaign lifecycle.</summary>
    [Parameter] public CampaignStatus Status { get; set; }
    /// <summary>The authenticated user and club scope supplied by CampaignEntry.</summary>
    [Parameter] public string? AuthorityScope { get; set; }
    /// <summary>The stable user/club storage scope, independent of changes in administrator role.</summary>
    [Parameter] public string? CaptureScope { get; set; }
    /// <summary>The evaluation-specific URL state.</summary>
    [Parameter, EditorRequired] public CampaignWorkspaceEvaluationState State { get; set; } = new();
    /// <summary>The untouched Roster discovery context.</summary>
    [Parameter, EditorRequired] public CampaignWorkspaceRosterState RosterState { get; set; } = new();
    /// <summary>The Roster participant to restore independently of evaluation selection.</summary>
    [Parameter] public long? RosterParticipantId { get; set; }
    /// <summary>Requests an authoritative campaign refresh after lifecycle evidence changes.</summary>
    [Parameter] public EventCallback OnLifecycleChanged { get; set; }

    private ElementReference _root;
    private ElementReference _finder;
    private IJSObjectReference? _module;
    private DotNetObjectReference<CampaignEvaluationPanel>? _navigationReceiver;
    private Task<IJSObjectReference>? _moduleLoad;
    private readonly string _lease = Guid.NewGuid().ToString("N");
    private string Owner => $"{AuthorityScope}:{CampaignId}:{State.ParticipantId}";
    private string FinderOwner => $"{AuthorityScope}:{CampaignId}:{State.Search}:{State.Page}:{Status}";
    private string? _loadedParticipantOwner;
    private string? _loadedFinderOwner;
    private string? _attachedOwner;
    private string? _attachedFinderOwner;
    private CampaignStatus? _loadedStatus;
    private string _search = string.Empty;
    private int _searchSequence;
    private int _identitySequence;
    private int _finderSequence;
    private CancellationTokenSource? _finderCancellation;
    private bool _finderLoading;
    private string? _finderError;
    private int? _total;
    private List<EvaluationFinderRow> _results = [];
    private CampaignParticipantDetailDto? _identity;
    private bool _identityLoading;
    private string? _identityError;
    private bool _focusSheet;
    private bool _focusFinder;
    private string? _leaveTarget;
    private string? _leaveHistoryKey;
    private long _departureSequence;
    private long? _departureInFlight;
    private bool _historyExpanded;
    private string? _statusMessage;
    private bool Owns(string owner) => string.Equals(owner, Owner, StringComparison.Ordinal) && !ComponentCancellationToken.IsCancellationRequested;
    private bool EvidenceWritable => Status == CampaignStatus.Active && !_identityLoading && _identityError is null
        && _identity is { CampaignStatus: CampaignStatus.Active };
    private bool CanAddNote => EvidenceWritable && _identity!.Capabilities.CanAddNote;
    private bool CanApplyTag => EvidenceWritable && _identity!.Capabilities.CanApplyTag;
    private bool CanPlacePlayer => EvidenceWritable && _identity!.Capabilities.CanEditPlacement;
    private int SelectedIndex => _results.FindIndex(row => row.Id == State.ParticipantId);
    private bool HasDraft => _draft.Length > 0 || _traitSearch.Length > 0
        || (_editingNoteId is not null && !string.Equals(_editContent, _editOriginal, StringComparison.Ordinal));
    private bool Protected => HasDraft || _pending is not null;
    private string LookupUrl(CampaignWorkspaceEvaluationState state) => CampaignWorkspaceUrlState.BuildEvaluationLookupUrl(CampaignId, state, RosterState, RosterParticipantId);
    private string PlayerUrl(long id) => LookupUrl(State with { ParticipantId = id });
    private string PlacePlayerUrl => CampaignWorkspaceUrlState.WithEvaluationContext(
        CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, new(), RosterState, RosterParticipantId), State)
        + $"&placementParticipant={State.ParticipantId}&returnToEvaluation=true";

    private IEnumerable<KeyValuePair<string, string>> RosterQueryFields
    {
        get
        {
            foreach (var queryField in CampaignWorkspaceUrlState.BuildQueryString(RosterState).Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = queryField.Split('=', 2);
                yield return new(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]));
            }
            if (RosterParticipantId is { } id)
            {
                yield return new("participant", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (State.RosterLanding)
            {
                yield return new("rosterLanding", "true");
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var ownerChanged = !string.Equals(_loadedParticipantOwner, Owner, StringComparison.Ordinal);
        var lifecycleChanged = _loadedStatus != Status;
        if (ownerChanged || !string.Equals(_loadedFinderOwner, FinderOwner, StringComparison.Ordinal))
        {
            ++_searchSequence;
        }
        if (ownerChanged)
        {
            ResetParticipantOwner();
        }
        if (lifecycleChanged) { ++_departureSequence; CancelDeleteNote(); }

        var restoredEvidence = (ownerChanged || lifecycleChanged) && RestoreEvidence();
        _loadedStatus = Status;
        var tasks = new List<Task>();
        if (!string.Equals(_loadedFinderOwner, FinderOwner, StringComparison.Ordinal))
        {
            _loadedFinderOwner = FinderOwner;
            _search = State.Search ?? string.Empty;
            if (string.Equals(PersistedFinderScope, FinderOwner, StringComparison.Ordinal) && PersistedFinderRows is not null)
            {
                _results = PersistedFinderRows.ToList();
                _total = PersistedFinderCount;
                _finderLoading = false;
            }
            else
            {
                tasks.Add(LoadFinderAsync());
            }
        }
        if ((ownerChanged || lifecycleChanged) && State.ParticipantId is not null)
        {
            if (!restoredEvidence || PersistedIdentity is null)
            {
                tasks.Add(LoadIdentityAsync());
            }

            if (!restoredEvidence || PersistedNotes is null)
            {
                tasks.Add(LoadNotesAsync(false));
            }

            if (!restoredEvidence || PersistedApplications is null)
            {
                tasks.Add(LoadApplicationsAsync(false));
            }

            if (!restoredEvidence || PersistedChoices is null)
            {
                tasks.Add(LoadChoicesAsync());
            }
        }
        await Task.WhenAll(tasks);
    }

    private void ResetParticipantOwner()
    {
        _loadedParticipantOwner = Owner;
        _identity = null;
        _identityError = null;
        _identityLoading = _notesLoading = _applicationsLoading = _choicesLoading = false;
        _notes = [];
        _applications = [];
        _choices = [];
        _notesNext = null;
        _applicationsNext = null;
        _notesError = _applicationsError = _choicesError = null;
        _removeApplicationId = null;
        _traitPicker = false;
        ResetCapture();
        _attachedOwner = null;
        _focusSheet = State.ParticipantId is not null;
        _focusFinder = State.ParticipantId is null;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var owner = Owner;
        if (!await EnsureEvaluationAttachmentAsync() || !Owns(owner))
        {
            return;
        }
        try
        {
            if (_focusSheet && _identity is not null)
            {
                var focused = await _module!.InvokeAsync<bool>("focusSheet", _root, owner, _lease);
                if (Owns(owner) && focused)
                {
                    _focusSheet = false;
                }
            }
            else if (_focusFinder)
            {
                _focusFinder = false;
                await _module!.InvokeVoidAsync("restoreFinder", _root, FinderOwner, _finder);
            }
        }
        catch (JSException)
        {
            // Optional focus/scroll failure does not invalidate installed navigation protection.
            if (Owns(owner)) { _focusSheet = _focusFinder = false; }
        }
    }

    private async Task LoadIdentityAsync()
    {
        if (State.ParticipantId is not { } id)
        {
            return;
        }

        var owner = Owner;
        var sequence = ++_identitySequence;
        _identityLoading = true;
        try
        {
            var result = await participants.GetParticipantDetailAsync(new() { CampaignId = CampaignId, PlayerCampaignAssignmentId = id }, ComponentCancellationToken);
            if (!Owns(owner) || sequence != _identitySequence)
            {
                return;
            }

            result.Switch(value => { _identity = value; PersistedIdentity = value; _identityError = null; }, problem => _identityError = problem.Detail ?? "Player identity could not be loaded. Retry before capturing evidence.");
            if (result.IsSuccess && result.Value.CampaignStatus != Status)
            {
                await OnLifecycleChanged.InvokeAsync();
            }
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is HttpRequestException or JSException or OperationCanceledException)
        {
            if (Owns(owner) && sequence == _identitySequence)
            {
                _identityError = "Player identity could not be loaded. Retry before capturing evidence.";
            }
        }
        finally
        {
            if (Owns(owner) && sequence == _identitySequence)
            {
                _identityLoading = false;
            }
        }
    }

    private async Task LoadFinderAsync()
    {
        var sequence = ++_finderSequence;
        var owner = FinderOwner;
        var previous = _finderCancellation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ComponentCancellationToken);
        _finderCancellation = cancellation;
        if (previous is not null)
        {
            await previous.CancelAsync();
        }
        if (!OwnsFinder(owner, sequence))
        {
            if (ReferenceEquals(_finderCancellation, cancellation)) { _finderCancellation = null; }
            return;
        }
        _results = [];
        _total = null;
        _finderError = null;
        if (string.IsNullOrWhiteSpace(State.Search)) { _finderCancellation = null; _finderLoading = false; return; }
        _finderLoading = true;
        try
        {
            var result = await QueryFinderAsync(cancellation.Token);
            if (!OwnsFinder(owner, sequence))
            {
                return;
            }
            result.Switch(value => { _total = value.Total; _results = value.Rows; PersistedFinderCount = value.Total; PersistedFinderRows = value.Rows; PersistedFinderScope = FinderOwner; },
                problem => _finderError = problem.Detail ?? "Player search could not be loaded.");
            if (result.IsProblem && result.Problem.Kind == ServiceProblemKind.Conflict)
            {
                await OnLifecycleChanged.InvokeAsync();
            }
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException)
        {
            if (OwnsFinder(owner, sequence))
            {
                _finderError = "Player search could not be loaded. Retry this search.";
            }
        }
        finally
        {
            if (ReferenceEquals(_finderCancellation, cancellation)) { _finderCancellation = null; }
            if (OwnsFinder(owner, sequence))
            {
                _finderLoading = false;
            }
        }
    }

    private async Task<ServiceResult<FinderPage>> QueryFinderAsync(CancellationToken cancellationToken)
    {
        if (Status == CampaignStatus.Active)
        {
            var result = await placements.GetCampaignEffectivePlacementsAsync(new()
            {
                CampaignId = CampaignId,
                Search = State.Search,
                Page = State.Page,
                PageSize = 20,
                SortBy = "searchRelevance"
            }, cancellationToken);
            return result.Match<ServiceResult<FinderPage>>(value => new FinderPage(value.Participants.TotalCount,
                value.Participants.Items.Select(row => new EvaluationFinderRow(row.PlayerCampaignAssignmentId,
                    $"{row.FirstName} {row.LastName}", row.GraduationYear, row.TryoutNumber,
                    FinderPlacement(row.LocalDecision?.Outcome ?? PlacementOutcome.Undecided, row.LocalTeam))).ToList()), problem => problem);
        }
        var closed = await placements.GetClosedCampaignRosterAsync(new()
        {
            CampaignId = CampaignId,
            Search = State.Search,
            Page = State.Page,
            PageSize = 20,
            SortBy = "searchRelevance"
        }, cancellationToken);
        return closed.Match<ServiceResult<FinderPage>>(value => new FinderPage(value.Participants.TotalCount,
            value.Participants.Items.Select(row => new EvaluationFinderRow(row.PlayerCampaignAssignmentId,
                $"{row.FirstName} {row.LastName}", row.GraduationYear, row.TryoutNumber,
                FinderPlacement(row.Source.Decision.Outcome, row.Source.Team))).ToList()), problem => problem);
    }

    private static string FinderPlacement(PlacementOutcome outcome, CampaignParticipantTeamSummaryDto? team)
        => team is null ? CampaignRosterDisplay.OutcomeLabel(outcome) : $"{CampaignRosterDisplay.OutcomeLabel(outcome)} · {team.TeamName}";

    private sealed record FinderPage(int Total, List<EvaluationFinderRow> Rows);
    private bool OwnsFinder(string owner, int sequence) => sequence == _finderSequence
        && string.Equals(owner, FinderOwner, StringComparison.Ordinal) && !ComponentCancellationToken.IsCancellationRequested;

    private async Task SearchChangedAsync(ChangeEventArgs args)
    {
        _search = args.Value?.ToString() ?? string.Empty;
        var sequence = ++_searchSequence;
        try
        {
            await Task.Delay(350, ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        if (sequence == _searchSequence && !ComponentCancellationToken.IsCancellationRequested)
        {
            ApplySearch();
        }
    }

    private void ApplySearch()
    {
        ++_searchSequence;
        var target = LookupUrl(State with { Search = _search.Trim(), Page = 1, ParticipantId = null });
        if (string.Equals(navigation.Uri, navigation.ToAbsoluteUri(target).AbsoluteUri, StringComparison.Ordinal))
        {
            return;
        }
        navigation.NavigateTo(target);
    }

    private Task GuardNavigationAsync(LocationChangingContext context)
    {
        if (!Protected)
        {
            return Task.CompletedTask;
        }

        context.PreventNavigation();
        ++_departureSequence;
        _leaveTarget = context.TargetLocation;
        _leaveHistoryKey = null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Shows the owned departure prompt after native enhanced navigation was held in the browser.</summary>
    [JSInvokable]
    public Task ProtectNativeNavigationAsync(string owner, string lease, string target, string? historyKey)
    {
        if (Owns(owner) && string.Equals(lease, _lease, StringComparison.Ordinal))
        {
            ++_departureSequence;
            _leaveTarget = target;
            _leaveHistoryKey = historyKey;
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    private async Task DiscardAndLeaveAsync()
    {
        if (_pending is not null || !_storageReady || _leaveTarget is null || _departureInFlight is not null)
        {
            return;
        }

        var owner = Owner;
        var target = _leaveTarget;
        var historyKey = _leaveHistoryKey;
        var request = _departureSequence;
        _departureInFlight = request;
        try
        {
            _draft = string.Empty;
            _traitSearch = string.Empty;
            _editingNoteId = null;
            _editContent = _editOriginal = string.Empty;
            if (!await PersistCaptureAsync() || !Owns(owner) || Protected
                || !string.Equals(target, _leaveTarget, StringComparison.Ordinal) || request != _departureSequence)
            {
                return;
            }

            await ExecuteDepartureAsync(owner, request, target, historyKey);
        }
        finally
        {
            if (_departureInFlight == request)
            {
                _departureInFlight = null;
            }
        }
    }

    private async Task ExecuteDepartureAsync(string owner, long request, string target, string? historyKey)
    {
        bool current() => Owns(owner) && request == _departureSequence && _leaveTarget is not null;
        var departed = false;
        try
        {
            if (_module is null)
            {
                return;
            }

            await _module.InvokeVoidAsync("releaseNavigation", _root, owner, _lease);
            if (!current() || Protected)
            {
                return;
            }

            var resumed = true;
            if (historyKey is { } key)
            {
                resumed = await _module.InvokeAsync<bool>("resumeHistory", _root, owner, _lease, key);
            }
            else
            {
                navigation.NavigateTo(target);
            }

            departed = resumed;
            if (current() && !Protected)
            {
                if (resumed)
                {
                    _leaveTarget = null;
                }
                else
                {
                    _captureError = "Navigation was interrupted. Keep working or try leaving again.";
                }
            }
        }
        catch (JSException)
        {
            if (current())
            {
                _captureError = "Navigation could not continue. Keep working or try leaving again.";
            }
        }
        finally
        {
            if (!departed && _departureInFlight == request && _module is not null)
            {
                try { await _module.InvokeVoidAsync("cancelNavigation", _root, owner, _lease); }
                catch (JSException)
                {
                    if (current())
                    {
                        _captureError = "Navigation protection could not be restored. Reload before continuing.";
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        _navigationReceiver?.Dispose();
        _finderCancellation?.Dispose();
        _finderCancellation = null;
        if (_moduleLoad is null)
        {
            return;
        }

        try { var module = await _moduleLoad; await module.InvokeVoidAsync("detach", _root, _lease); await module.DisposeAsync(); }
        catch (JSDisconnectedException) { /* The circuit has already released browser resources. */ }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { /* Browser teardown cancelled cleanup. */ }
    }


}
