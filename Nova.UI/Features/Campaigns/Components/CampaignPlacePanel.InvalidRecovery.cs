using Microsoft.JSInterop;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignPlacePanel
{
    private string? _invalidPending;

    private async Task DiscardInvalidRecoveryAsync()
    {
        if (_invalidPending is not { } invalid || _storageModule is null || _saving) { return; }
        var owner = EffectiveOwner;
        var scope = StorageScope;
        var generation = ++_operationGeneration;
        _phase = PlacementPhase.Reconciling;
        try
        {
            await OnReloadRequested.InvokeAsync();
            if (!OwnsOperation(owner, scope, generation)) { return; }
            await LoadQueueAsync(State);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            await RefreshSelectionAsync(force: true);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            if (!AuthoritativeEvidenceFresh || _queueLoading || _selectedLoading)
            {
                _saveError = "Current placement could not be refreshed. Invalid recovery data is retained; retry before making another decision.";
                return;
            }
            var discarded = await _storageModule.InvokeAsync<bool>("discardInvalidPending", ComponentCancellationToken, scope, invalid);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            _invalidPending = null;
            await ApplyPendingStateAsync();
            if (!OwnsOperation(owner, scope, generation)) { return; }
            var fresh = AuthoritativeEvidenceFresh && !_queueLoading && !_selectedLoading;
            _phase = fresh ? PlacementPhase.Editing : PlacementPhase.ConflictReview;
            _saveError = discarded
                ? "Invalid recovery data discarded. The earlier save result remains unknown."
                : "Recovery data changed and was retained. Review its current state before continuing.";
            if (!fresh) { EnterConflict("Current placement changed during recovery. Review the latest placement before editing."); }
            await RetryStorageAsync();
            if (OwnsOperation(owner, scope, generation) && _phase == PlacementPhase.Editing) { _stageFocusTarget = SelectedParticipantId is not null; }
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
            && exception is JSException or InvalidOperationException)
        {
            if (OwnsOperation(owner, scope, generation)) { _saveError = "Recovery data could not be discarded. Retry storage before continuing."; }
        }
        finally
        {
            if (OwnsOperation(owner, scope, generation) && _phase == PlacementPhase.Reconciling) { _phase = PlacementPhase.ConflictReview; }
        }
    }
}
