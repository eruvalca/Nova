using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignPlacePanel
{
    private sealed record PageCorrectionSettlement(UpdateCampaignPlacementInput Input,
        ServiceResult<PlacementMutationSuccess> Result, string Owner, string Scope, int Generation);

    private PageCorrectionSettlement? _pageCorrectionSettlement;
    private bool _resumingPageCorrection;
    private CampaignWorkspacePlacementState? _requestedPageCorrection;
    private bool IsRequestedPageCorrection => _requestedPageCorrection is { } requested
        && string.Equals(QueryKey(State), QueryKey(requested), StringComparison.Ordinal);

    private Task RequestPageCorrectionAsync(CampaignWorkspacePlacementState state, int page)
    {
        _requestedPageCorrection = state with { Page = page };
        return OnStateChanged.InvokeAsync(_requestedPageCorrection);
    }

    /// <summary>Finishes a confirmed operation only after the corrected URL owns fresh evidence.</summary>
    private async Task ResumePageCorrectionSettlementAsync()
    {
        if (_pageCorrectionSettlement is not { } settlement || _resumingPageCorrection
            || (!IsDiscoveryChanged && !IsRequestedPageCorrection)) { return; }
        if (!OwnsOperation(settlement.Owner, settlement.Scope, settlement.Generation)) { return; }
        _resumingPageCorrection = true;
        try
        {
            for (var pass = 0; pass < StartupReconciliationPasses; pass++)
            {
                DeferWhileSaving();
                // Back/Forward can return to the applied key while a fresh correction is outstanding.
                if (IsRequestedPageCorrection) { _pendingState = State; }
                await ApplyPendingStateAsync();
                if (!OwnsOperation(settlement.Owner, settlement.Scope, settlement.Generation)) { return; }
                if (_requestedPageCorrection is not null)
                {
                    if (IsRequestedPageCorrection) { continue; }
                    return;
                }
                if (IsDiscoveryChanged || _appliedParticipantId != SelectedParticipantId)
                {
                    // A further page correction may have requested navigation whose parameters have not arrived.
                    if (_queueLoading && _pendingState is null) { return; }
                    continue;
                }
                if (_queueLoading) { return; }
                _pageCorrectionSettlement = null;
                CompleteSettlement(settlement.Input, settlement.Result);
                return;
            }

            _pageCorrectionSettlement = null;
            _keepOperationId = null;
            EnterConflict("Current placement evidence kept changing. Review the latest placement before editing.");
        }
        finally
        {
            if (OwnsOperation(settlement.Owner, settlement.Scope, settlement.Generation)) { _resumingPageCorrection = false; }
        }
    }
}
