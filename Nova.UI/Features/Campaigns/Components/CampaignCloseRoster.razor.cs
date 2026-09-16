using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Loads one independently retryable, campaign-local Close roster page.</summary>
/// <param name="queries">The delivered effective placement read.</param>
public partial class CampaignCloseRoster(IEffectivePlacementQueryService queries) : NovaComponentBase
{
    /// <summary>The campaign under review.</summary>
    [Parameter] public long CampaignId { get; set; }
    /// <summary>The workspace authority and refresh generation.</summary>
    [Parameter] public string Owner { get; set; } = string.Empty;
    /// <summary>The URL-owned discovery state.</summary>
    [Parameter] public CampaignWorkspaceCloseState State { get; set; } = new();
    /// <summary>Builds discovery anchors.</summary>
    [Parameter, EditorRequired] public Func<CampaignWorkspaceCloseState, string> BuildCloseUrl { get; set; } = null!;
    /// <summary>Builds the existing participant correction destination.</summary>
    [Parameter, EditorRequired] public Func<long, string> BuildParticipantUrl { get; set; } = null!;
    /// <summary>Publishes search or blocker changes.</summary>
    [Parameter] public EventCallback<CampaignWorkspaceCloseState> OnStateChanged { get; set; }
    /// <summary>Refreshes authorized detail after a conflicting roster lifecycle.</summary>
    [Parameter] public EventCallback OnLifecycleChanged { get; set; }

    /// <summary>Whether this region has a startup result, including a retryable failure.</summary>
    [PersistentState] public bool Initialized { get; set; }
    /// <summary>The authority, campaign, refresh generation and discovery owning the startup result.</summary>
    [PersistentState] public string? PersistedKey { get; set; }
    /// <summary>The bounded page restored at interactive attachment.</summary>
    [PersistentState] public PagedResult<CampaignEffectivePlacementItem>? PersistedPage { get; set; }
    /// <summary>The independently retryable startup failure.</summary>
    [PersistentState] public string? PersistedError { get; set; }

    private string? _key;
    private int _request;
    private bool _loading;
    private string? _error;
    private string _search = string.Empty;
    private PagedResult<CampaignEffectivePlacementItem>? _page;
    private string RequestKey => $"{Owner}:{CampaignId}:{State}";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (!string.Equals(_key, RequestKey, StringComparison.Ordinal))
        {
            _key = RequestKey;
            _search = State.Search ?? string.Empty;
            if (Initialized && string.Equals(PersistedKey, _key, StringComparison.Ordinal))
            {
                _page = PersistedPage;
                _error = PersistedError;
                _loading = false;
                return;
            }
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var request = ++_request;
        var key = RequestKey;
        _loading = true;
        _error = null;
        _page = null;
        try
        {
            var result = await queries.GetCampaignEffectivePlacementsAsync(new()
            {
                CampaignId = CampaignId,
                Search = State.Search,
                CloseoutBlocker = State.Blocker,
                Page = State.Page,
                PageSize = 50,
                SortBy = "closeout",
                SortDirection = "asc",
            }, ComponentCancellationToken);
            if (!Current(request, key))
            {
                return;
            }
            if (result.IsSuccess && result.Value.Campaign.Status == CampaignStatus.Active)
            {
                _page = result.Value.Participants;
            }
            else
            {
                _error = "The roster could not be loaded. Retry this region.";
                if (result.IsSuccess || result.Problem.Kind == ServiceProblemKind.Conflict)
                {
                    await OnLifecycleChanged.InvokeAsync();
                }
            }
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (Current(request, key))
            {
                _error = "The roster could not be loaded. Retry this region.";
            }
        }
        finally
        {
            if (Current(request, key))
            {
                _loading = false;
                PersistedKey = key;
                PersistedPage = _page;
                PersistedError = _error;
                Initialized = true;
            }
        }
    }

    private bool Current(int request, string key) => request == _request && string.Equals(key, RequestKey, StringComparison.Ordinal) && !ComponentCancellationToken.IsCancellationRequested;
    private Task SearchAsync() => OnStateChanged.InvokeAsync(State with { Search = string.IsNullOrWhiteSpace(_search) ? null : _search.Trim(), Page = 1 });
    private Task BlockerChangedAsync(ChangeEventArgs args) => OnStateChanged.InvokeAsync(State with { Blocker = string.IsNullOrEmpty(args.Value?.ToString()) ? null : args.Value.ToString(), Page = 1 });
    private static string Outcome(CampaignEffectivePlacementItem row) => OutcomeLabel(row.LocalDecision?.Outcome);
    private static string OutcomeLabel(PlacementOutcome? outcome) => outcome switch
    {
        PlacementOutcome.Assigned => "Assigned",
        PlacementOutcome.NotSelected => "Not selected",
        PlacementOutcome.Withdrawn => "Withdrawn",
        _ => "No campaign decision",
    };
    private static (PlacementOutcome? Outcome, long? TeamId) GroupKey(CampaignEffectivePlacementItem row) => (row.LocalDecision?.Outcome, row.LocalTeam?.TeamId);
    private static string GroupLabel(CampaignEffectivePlacementItem row) => row.LocalDecision?.Outcome == PlacementOutcome.Assigned ? row.LocalTeam?.TeamName ?? "Unavailable team" : Outcome(row);
}
