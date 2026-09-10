using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Uses a loaded Active row or a bounded exact read for directly linked participant context.</summary>
/// <param name="queries">The authoritative placement reads.</param>
public partial class CampaignParticipantPlacementContext(IEffectivePlacementQueryService queries)
{
    /// <summary>The owning campaign.</summary>
    [Parameter] public long CampaignId { get; set; }
    /// <summary>The exact selected assignment, which may be outside the loaded page.</summary>
    [Parameter] public long ParticipantId { get; set; }
    /// <summary>The authoritative campaign lifecycle.</summary>
    [Parameter] public CampaignStatus Status { get; set; }
    /// <summary>The already loaded Active row, avoiding an extra read for page selections.</summary>
    [Parameter] public CampaignEffectivePlacementItem? WorkingRow { get; set; }
    /// <summary>The authority owning this context.</summary>
    [Parameter] public string? Owner { get; set; }
    /// <summary>Requests lifecycle reconciliation through the authorized campaign detail.</summary>
    [Parameter] public EventCallback OnLifecycleChanged { get; set; }

    private string? _key;
    private CampaignEffectivePlacementItem? _appliedRow;
    private CampaignEffectivePlacementItem? _working;
    private ClosedCampaignRosterItem? _closed;
    private bool _loading;
    private bool _error;
    private int _request;

    /// <summary>Whether the regional startup read finished.</summary>
    [PersistentState] public bool Initialized { get; set; }
    /// <summary>The authority, campaign, participant and lifecycle owning persisted context.</summary>
    [PersistentState] public string? PersistedOwner { get; set; }
    /// <summary>The Active participant's authoritative placement evidence.</summary>
    [PersistentState] public CampaignEffectivePlacementItem? PersistedWorking { get; set; }
    /// <summary>The immutable Closed participant evidence.</summary>
    [PersistentState] public ClosedCampaignRosterItem? PersistedClosed { get; set; }
    /// <summary>The regional failure retained until retry.</summary>
    [PersistentState] public bool PersistedError { get; set; }
    private string LocalOutcomeLabel => (_working?.LocalDecision ?? _closed?.Source.Decision) is { } decision
        ? CampaignRosterDisplay.OutcomeLabel(decision.Outcome) : "No campaign decision";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var key = $"{Owner}:{CampaignId}:{ParticipantId}:{Status}";
        if (_key is null && Initialized && string.Equals(PersistedOwner, key, StringComparison.Ordinal))
        {
            _key = key;
            _appliedRow = WorkingRow;
            _working = Status == CampaignStatus.Active ? WorkingRow ?? PersistedWorking : null;
            _closed = PersistedClosed;
            _error = PersistedError;
            return;
        }
        if (!string.Equals(key, _key, StringComparison.Ordinal) || !ReferenceEquals(_appliedRow, WorkingRow))
        {
            _key = key;
            _appliedRow = WorkingRow;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var request = ++_request;
        ResetRegion();
        if (Status == CampaignStatus.Active && WorkingRow?.PlayerCampaignAssignmentId == ParticipantId)
        {
            _working = WorkingRow;
            PersistRegion();
            return;
        }
        _loading = true;
        try
        {
            if (Status == CampaignStatus.Closed)
            {
                var result = await queries.GetClosedCampaignRosterAsync(new()
                { CampaignId = CampaignId, ParticipantId = ParticipantId, PageSize = 1 }, ComponentCancellationToken);
                if (!Owns(request))
                {
                    return;
                }
                _closed = result.IsSuccess ? result.Value.Participants.Items.SingleOrDefault() : null;
                _error = _closed is null;
                if (result.IsProblem && result.Problem.Kind == Nova.SharedKernel.Results.ServiceProblemKind.Conflict)
                {
                    await OnLifecycleChanged.InvokeAsync();
                }
            }
            else
            {
                var result = await queries.GetCampaignEffectivePlacementsAsync(new()
                { CampaignId = CampaignId, ParticipantId = ParticipantId, PageSize = 1 }, ComponentCancellationToken);
                if (!Owns(request))
                {
                    return;
                }
                _working = result.IsSuccess ? result.Value.Participants.Items.SingleOrDefault() : null;
                _error = _working is null;
                if (result.IsProblem && result.Problem.Kind == Nova.SharedKernel.Results.ServiceProblemKind.Conflict)
                {
                    await OnLifecycleChanged.InvokeAsync();
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !ComponentCancellationToken.IsCancellationRequested)
        {
            if (Owns(request))
            {
                _error = true;
            }
        }
        finally
        {
            if (Owns(request))
            {
                _loading = false;
                PersistRegion();
            }
        }
    }

    private bool Owns(int request) => request == _request && !ComponentCancellationToken.IsCancellationRequested;

    private void ResetRegion()
    {
        _working = null;
        _closed = null;
        _error = false;
        _loading = false;
    }

    private void PersistRegion()
    {
        Initialized = true;
        PersistedOwner = _key;
        PersistedWorking = _working;
        PersistedClosed = _closed;
        PersistedError = _error;
    }
}
