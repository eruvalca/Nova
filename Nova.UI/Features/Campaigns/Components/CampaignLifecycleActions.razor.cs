using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>The single close/reopen confirmation and dispatch path, also consumed by the Closed record.</summary>
/// <param name="lifecycle">The existing lifecycle mutation boundary.</param>
public partial class CampaignLifecycleActions(ICampaignLifecycleService lifecycle) : NovaComponentBase
{
    /// <summary>The current user, club, campaign and authority generation.</summary>
    [Parameter, EditorRequired] public string Owner { get; set; } = string.Empty;
    /// <summary>The current workspace-owned evidence; null removes mutation authority.</summary>
    [Parameter] public CampaignLifecycleEvidence? Evidence { get; set; }
    /// <summary>Refreshes authorized detail and readiness, returning null on required read failure.</summary>
    [Parameter, EditorRequired] public Func<Task<CampaignLifecycleEvidence?>> RefreshEvidence { get; set; } = null!;

    private CampaignLifecycleEvidence? _confirmation;
    private CampaignLifecycleEvidence? _observedEvidence;
    private string? _owner;
    private int _operation;
    private bool _busy;
    private bool _unavailable;
    private bool _focusPending;
    private string? _feedback;
    private ElementReference _heading;
    private bool Eligible => !_unavailable && Evidence is { } evidence && (evidence.Readiness.Lifecycle.CanClose || evidence.Readiness.Lifecycle.CanReopen);
    private string Action => Evidence?.Detail.Status == CampaignStatus.Closed ? "Reopen" : "Close";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!string.Equals(_owner, Owner, StringComparison.Ordinal))
        {
            _owner = Owner;
            ++_operation;
            _busy = false;
            _confirmation = null;
            _feedback = null;
            _unavailable = false;
        }
        if (!ReferenceEquals(_confirmation, Evidence))
        {
            _confirmation = null;
        }
        if (!ReferenceEquals(_observedEvidence, Evidence))
        {
            _observedEvidence = Evidence;
            if (Evidence is not null) { _unavailable = false; }
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusPending)
        {
            _focusPending = false;
            await _heading.FocusAsync(preventScroll: false);
        }
    }

    private bool Current(int operation, string owner) => operation == _operation && string.Equals(owner, Owner, StringComparison.Ordinal) && !ComponentCancellationToken.IsCancellationRequested;

    private async Task ReviewAsync()
    {
        if (_busy || !Eligible)
        {
            return;
        }
        _busy = true;
        _feedback = null;
        _confirmation = null;
        var operation = ++_operation;
        var owner = Owner;
        var (refreshed, superseded) = await RefreshAsync();
        if (!Current(operation, owner))
        {
            return;
        }
        _busy = false;
        _unavailable = refreshed is null && !superseded;
        if (refreshed is not null && !superseded
            && (refreshed.Readiness.Lifecycle.CanClose || refreshed.Readiness.Lifecycle.CanReopen))
        {
            _confirmation = refreshed;
        }
        else
        {
            _feedback = _unavailable ? "The required review could not be refreshed. Retry the read before taking an action." : "The campaign changed. Review its current readiness before taking an action.";
        }
    }

    private void Cancel()
    {
        _confirmation = null;
        _feedback = "Review canceled. No lifecycle request was sent.";
    }

    private async Task CommitAsync()
    {
        if (_busy || _confirmation is not { } confirmed || !ReferenceEquals(confirmed, Evidence) || !Eligible)
        {
            return;
        }
        _busy = true;
        _confirmation = null;
        var operation = ++_operation;
        var owner = Owner;
        var close = confirmed.Detail.Status == CampaignStatus.Active;
        var message = "The request outcome is unknown. Current state is shown separately; this request will not be replayed.";
        var succeeded = false;
        try
        {
            var result = close
                ? await lifecycle.CloseAsync(confirmed.Detail.CampaignId, ComponentCancellationToken)
                : await lifecycle.ReopenAsync(confirmed.Detail.CampaignId, ComponentCancellationToken);
            succeeded = result.IsSuccess;
            message = ResultMessage(result, close);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // A transport failure cannot establish whether the server committed.
        }
        if (!Current(operation, owner))
        {
            return;
        }
        // Evidence observed during dispatch invalidates any claim about that obsolete response.
        if (!ReferenceEquals(confirmed, Evidence))
        {
            succeeded = false;
            message = "The campaign changed while the request was pending. Review the current state.";
        }
        _feedback = message;
        StateHasChanged();
        var (refreshed, superseded) = await RefreshAsync();
        if (!Current(operation, owner))
        {
            return;
        }
        _busy = false;
        _unavailable = refreshed is null && !superseded;
        _feedback = message;
        if (_unavailable)
        {
            _feedback += " Current state is unavailable. Retry the read before taking another action.";
        }
        _focusPending = succeeded && refreshed is not null && !superseded;
    }

    private static string ResultMessage(ServiceResult<OneOf.Types.Success> result, bool close)
    {
        if (result.IsSuccess) { return close ? "Campaign closed." : "Campaign reopened."; }
        return result.Problem.Kind == ServiceProblemKind.ServerError
            ? "The request outcome is unknown. Current state is shown separately; this request will not be replayed."
            : result.Problem.Detail ?? "The campaign could not be changed. Review its current state.";
    }

    private async Task<(CampaignLifecycleEvidence? Value, bool Superseded)> RefreshAsync()
    {
        var previous = Evidence;
        CampaignLifecycleEvidence? refreshed;
        try { refreshed = await RefreshEvidence(); }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { refreshed = null; }
        // A synchronous parent refresh can return before its queued parameter render reaches us.
        // Reject replacement evidence observed during the read; Commit still requires exact identity.
        var superseded = refreshed is not null
            ? !ReferenceEquals(refreshed, Evidence) && !ReferenceEquals(previous, Evidence)
            : Evidence is not null && !ReferenceEquals(previous, Evidence);
        return (refreshed, superseded);
    }

    private async Task RetryReadAsync()
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        var operation = ++_operation;
        var owner = Owner;
        var (refreshed, superseded) = await RefreshAsync();
        if (!Current(operation, owner))
        {
            return;
        }
        _busy = false;
        _unavailable = refreshed is null && !superseded;
        _confirmation = null;
    }

    private string Restriction => Evidence?.Readiness.Lifecycle.ReopenUnavailableReason switch
    {
        CampaignReopenUnavailableReason.HistoricalSeason => "Only a campaign in the club’s current season can reopen.",
        CampaignReopenUnavailableReason.LaterCampaignOpened => "A later campaign has opened. This campaign can no longer reopen.",
        CampaignReopenUnavailableReason.AnotherActiveCampaign => "Another campaign is Active. Only one campaign can be Active in a season.",
        CampaignReopenUnavailableReason.MissingOpening => "This campaign has no authoritative opening sequence and cannot reopen.",
        _ => "Reopening is not available for the current campaign state.",
    };
}
