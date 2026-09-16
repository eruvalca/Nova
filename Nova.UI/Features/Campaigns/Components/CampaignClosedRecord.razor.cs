using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Reads a bounded final record, participant history and independently retryable lifecycle activity.</summary>
/// <param name="queries">Campaign-local final roster reads.</param>
/// <param name="contextQueries">Existing selected-participant decision history.</param>
/// <param name="closeoutQueries">Existing bounded lifecycle activity.</param>
public partial class CampaignClosedRecord(IEffectivePlacementQueryService queries,
    IPlacementContextQueryService contextQueries, ICampaignCloseoutQueryService closeoutQueries)
{
    /// <summary>The authorized Closed campaign.</summary>
    [Parameter] public long CampaignId { get; set; }
    /// <summary>The workspace's user, club, campaign and authority generation.</summary>
    [Parameter] public string Owner { get; set; } = string.Empty;
    /// <summary>Changes when the workspace refreshes authoritative detail.</summary>
    [Parameter] public long RefreshGeneration { get; set; }
    /// <summary>Independent Close discovery and selection from the URL.</summary>
    [Parameter] public CampaignWorkspaceCloseState State { get; set; } = new();
    /// <summary>Builds native Close navigation while preserving sibling workspace context.</summary>
    [Parameter, EditorRequired] public Func<CampaignWorkspaceCloseState, string> BuildCloseUrl { get; set; } = null!;
    /// <summary>Builds the existing read-only evaluation handoff.</summary>
    [Parameter] public Func<long, string>? BuildEvaluationUrl { get; set; }
    /// <summary>Applies interactive discovery through the same URL state as native forms.</summary>
    [Parameter] public EventCallback<CampaignWorkspaceCloseState> OnStateChanged { get; set; }
    /// <summary>Reconciles authoritative campaign lifecycle or access after a failed read.</summary>
    [Parameter] public EventCallback OnLifecycleChanged { get; set; }

    /// <summary>The complete record response restored across prerender and interactive attachment.</summary>
    [PersistentState] public ClosedCampaignRosterResult? Record { get; set; }
    /// <summary>Ownership of the completed record read, including an error.</summary>
    [PersistentState] public string? RecordKey { get; set; }
    /// <summary>The independently retryable record failure.</summary>
    [PersistentState] public string? RecordError { get; set; }
    /// <summary>The exact selected participant, independent of roster paging.</summary>
    [PersistentState] public ClosedCampaignRosterItem? SelectedParticipant { get; set; }
    /// <summary>The bounded selected history response.</summary>
    [PersistentState] public PlacementContextResult? History { get; set; }
    /// <summary>Ownership of completed selected history, including its cursor and any error.</summary>
    [PersistentState] public string? HistoryKey { get; set; }
    /// <summary>The independently retryable history failure.</summary>
    [PersistentState] public string? HistoryError { get; set; }
    /// <summary>The bounded lifecycle activity response.</summary>
    [PersistentState] public CampaignActivityResult? Activity { get; set; }
    /// <summary>Ownership of completed activity, including an error.</summary>
    [PersistentState] public string? ActivityKey { get; set; }
    /// <summary>The independently retryable activity failure.</summary>
    [PersistentState] public string? ActivityError { get; set; }

    private string ScopeKey => $"{Owner}:{CampaignId}:{RefreshGeneration}";
    private string CurrentRecordKey => $"{ScopeKey}:{State.Search}:{State.Outcome}:{State.Page}";
    private string CurrentHistoryKey => $"{ScopeKey}:{State.ParticipantId}:{State.BeforeEventId}";
    private string? _recordRequestKey;
    private string? _historyRequestKey;
    private string? _activityRequestKey;
    private string? _reconciliationOwner;
    private readonly HashSet<string> _reconciledRegions = new(StringComparer.Ordinal);
    private int _recordRequest;
    private int _historyRequest;
    private int _activityRequest;
    private bool _loading;
    private bool _historyLoading;
    private bool _activityLoading;
    private string _search = string.Empty;
    private string _outcome = string.Empty;
    private ElementReference _historyHeading;
    private bool _focusHistory;

    private int LastAvailablePage => Record is null ? 1 : Math.Max(1, (int)Math.Ceiling(Record.Participants.TotalCount / (double)PlacementPageInput.DefaultPageSize));

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var authority = $"{Owner}:{CampaignId}";
        if (!string.Equals(authority, _reconciliationOwner, StringComparison.Ordinal))
        {
            _reconciliationOwner = authority;
            _reconciledRegions.Clear();
        }
        var loads = new List<Task>();
        if (!string.Equals(_recordRequestKey, CurrentRecordKey, StringComparison.Ordinal))
        {
            _recordRequestKey = CurrentRecordKey;
            _search = State.Search ?? string.Empty;
            _outcome = State.Outcome ?? string.Empty;
            if (!string.Equals(RecordKey, CurrentRecordKey, StringComparison.Ordinal)) { loads.Add(LoadRecordAsync()); }
        }
        if (!string.Equals(_historyRequestKey, CurrentHistoryKey, StringComparison.Ordinal))
        {
            _focusHistory = _historyRequestKey is not null && State.ParticipantId is not null;
            _historyRequestKey = CurrentHistoryKey;
            if (!string.Equals(HistoryKey, CurrentHistoryKey, StringComparison.Ordinal)) { loads.Add(LoadHistoryAsync()); }
        }
        if (!string.Equals(_activityRequestKey, ScopeKey, StringComparison.Ordinal))
        {
            _activityRequestKey = ScopeKey;
            if (!string.Equals(ActivityKey, ScopeKey, StringComparison.Ordinal)) { loads.Add(LoadActivityAsync()); }
        }
        await Task.WhenAll(loads);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusHistory && !_historyLoading && _historyHeading.Context is not null)
        {
            _focusHistory = false;
            try { await _historyHeading.FocusAsync(preventScroll: false); }
            catch (JSDisconnectedException) { /* The detached browser can no longer receive focus. */ }
            catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { /* Navigation disposed this owner. */ }
        }
    }

    private async Task LoadRecordAsync()
    {
        var key = CurrentRecordKey;
        var request = ++_recordRequest;
        _loading = true;
        Record = null;
        RecordKey = null;
        RecordError = null;
        var result = await ReadAsync(() => queries.GetClosedCampaignRosterAsync(new()
        {
            CampaignId = CampaignId,
            Search = State.Search,
            LocalOutcome = State.Outcome,
            Page = State.Page,
            PageSize = PlacementPageInput.DefaultPageSize,
            SortBy = "closeout",
            SortDirection = "asc",
        }, ComponentCancellationToken));
        if (!Owns(request, _recordRequest, key, CurrentRecordKey)) { return; }
        _loading = false;
        RecordKey = key;
        if (result.IsSuccess) { Record = result.Value; _reconciledRegions.Remove(nameof(Record)); }
        else
        {
            RecordError = result.Problem.Kind == ServiceProblemKind.Conflict
                ? "The final record could not be verified. Refresh or retry before relying on these outcomes."
                : "The final campaign record could not be loaded. Retry this region.";
            await ReconcileAsync(result.Problem, nameof(Record));
        }
    }

    private async Task LoadHistoryAsync()
    {
        var key = CurrentHistoryKey;
        var request = ++_historyRequest;
        SelectedParticipant = null;
        History = null;
        HistoryKey = null;
        HistoryError = null;
        _historyLoading = false;
        if (State.ParticipantId is not { } participant) { HistoryKey = key; _reconciledRegions.Remove(nameof(History)); return; }
        _historyLoading = true;
        var selected = await ReadAsync(() => queries.GetClosedCampaignRosterAsync(new()
        { CampaignId = CampaignId, ParticipantId = participant, Page = 1, PageSize = 1 }, ComponentCancellationToken));
        if (!Owns(request, _historyRequest, key, CurrentHistoryKey)) { return; }
        if (!selected.IsSuccess || selected.Value.Participants.Items.Count != 1)
        {
            _historyLoading = false;
            HistoryKey = key;
            HistoryError = "This participant's campaign record is unavailable. Retry or select another participant.";
            if (!selected.IsSuccess) { await ReconcileAsync(selected.Problem, nameof(History)); }
            return;
        }
        var result = await ReadAsync(() => contextQueries.GetContextAsync(new()
        { CampaignId = CampaignId, PlayerCampaignAssignmentId = participant, BeforeEventId = State.BeforeEventId, RequireClosed = true }, ComponentCancellationToken));
        if (!Owns(request, _historyRequest, key, CurrentHistoryKey)) { return; }
        _historyLoading = false;
        HistoryKey = key;
        if (result.IsSuccess) { SelectedParticipant = selected.Value.Participants.Items[0]; History = result.Value; _reconciledRegions.Remove(nameof(History)); }
        else
        {
            HistoryError = "Participant history could not be loaded. Retry this history page.";
            await ReconcileAsync(result.Problem, nameof(History));
        }
    }

    private async Task LoadActivityAsync()
    {
        var key = ScopeKey;
        var request = ++_activityRequest;
        _activityLoading = true;
        Activity = null;
        ActivityKey = null;
        ActivityError = null;
        var result = await ReadAsync(() => closeoutQueries.GetActivityAsync(new()
        { CampaignId = CampaignId, Limit = GetCampaignActivityInput.MaxEventCount }, ComponentCancellationToken));
        if (!Owns(request, _activityRequest, key, ScopeKey)) { return; }
        _activityLoading = false;
        ActivityKey = key;
        if (result.IsSuccess) { Activity = result.Value; _reconciledRegions.Remove(nameof(Activity)); }
        else { ActivityError = "Lifecycle activity could not be loaded. Retry this region."; await ReconcileAsync(result.Problem, nameof(Activity)); }
    }

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<ServiceResult<T>>> read)
    {
        try { return await read(); }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        { return ServiceProblem.ServerError("The request could not be completed."); }
    }

    private Task ReconcileAsync(ServiceProblem problem, string region)
    {
        var changed = problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden
            || problem.Kind == ServiceProblemKind.Conflict && problem.Errors?.ContainsKey(ClosedCampaignRecordErrors.Integrity) != true;
        // A refresh generation alone is not new authority. A persistent region denial must settle,
        // even when sibling reads succeed; explicit retry or that region's recovery permits another check.
        return changed && _reconciledRegions.Add(region) ? OnLifecycleChanged.InvokeAsync() : Task.CompletedTask;
    }

    private Task RetryRecordAsync() { _reconciledRegions.Remove(nameof(Record)); return LoadRecordAsync(); }
    private Task RetryHistoryAsync() { _reconciledRegions.Remove(nameof(History)); return LoadHistoryAsync(); }
    private Task RetryActivityAsync() { _reconciledRegions.Remove(nameof(Activity)); return LoadActivityAsync(); }

    private bool Owns(int request, int current, string key, string currentKey)
        => request == current && string.Equals(key, currentKey, StringComparison.Ordinal) && !ComponentCancellationToken.IsCancellationRequested;

    private IEnumerable<KeyValuePair<string, string>> NativeQueryFields
    {
        get
        {
            var url = BuildCloseUrl(new());
            var start = url.IndexOf('?', StringComparison.Ordinal);
            if (start < 0) { yield break; }
            foreach (var queryField in url[(start + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = queryField.Split('=', 2);
                var name = Uri.UnescapeDataString(pair[0]);
                if (name is "tab" or "closeSearch" or "closePage" or "closeBlocker" or "closeOutcome" or "closeParticipant" or "closeBeforeEventId") { continue; }
                yield return new(name, pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty);
            }
        }
    }

    private Task SearchAsync() => OnStateChanged.InvokeAsync(new()
    { Search = CampaignWorkspaceCloseState.NormalizeSearch(_search), Outcome = CampaignWorkspaceCloseState.NormalizeOutcome(_outcome) });
    private string PageUrl(int page) => BuildCloseUrl(State with { Page = page, ParticipantId = null, BeforeEventId = null, Blocker = null });
    private string ParticipantUrl(long participant) => BuildCloseUrl(State with { ParticipantId = participant, BeforeEventId = null, Blocker = null }) + "#closed-history-heading";
    private string HistoryUrl(long? cursor) => BuildCloseUrl(State with { BeforeEventId = cursor }) + "#closed-history-heading";
    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("g", CultureInfo.CurrentCulture) + " UTC";
    private static string OutcomeLabel(PlacementOutcome outcome) => outcome switch
    { PlacementOutcome.Assigned => "Assigned", PlacementOutcome.NotSelected => "Not selected", PlacementOutcome.Withdrawn => "Withdrawn", _ => "Incomplete decision" };
    private static string GroupLabel(ClosedCampaignRosterItem row)
        => row.Source.Team?.TeamName ?? OutcomeLabel(row.Source.Decision.Outcome);
    private static string HistoryDescription(PlacementHistoryItem item)
        => (item.PreviousOutcome is { } previous ? $"{OutcomeLabel(previous)}{TeamSuffix(item.PreviousTeamName)} → " : "Recorded ")
            + OutcomeLabel(item.Outcome) + TeamSuffix(item.TeamName);
    private static string TeamSuffix(string? team) => team is null ? string.Empty : $" · {team}";
}
