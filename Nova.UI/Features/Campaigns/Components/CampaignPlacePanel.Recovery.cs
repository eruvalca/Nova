using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>A tab's valid pending command or the exact invalid bytes awaiting an explicit discard.</summary>
public sealed record PlacementRecoveryRead(UpdateCampaignPlacementInput? Pending, string? InvalidValue);

public partial class CampaignPlacePanel
{
    private IJSObjectReference? _storageModule;
    private Task<IJSObjectReference>? _storageModuleLoad;
    private string? _storageOwner;
    private string? _attachingStorage;
    private bool _storageReady;
    private int _storageGeneration;
    private string StorageScope => $"{RecoveryScope ?? Owner}:{CampaignId}";
    private string RecoveryOwner => $"{EffectiveOwner}:{StorageScope}";

    private async Task AttachRecoveryAsync()
    {
        var owner = RecoveryOwner;
        if (string.Equals(_storageOwner, owner, StringComparison.Ordinal) || string.Equals(_attachingStorage, owner, StringComparison.Ordinal)) { return; }
        var generation = ++_storageGeneration;
        _attachingStorage = owner;
        _storageReady = false;
        var scope = StorageScope;
        try
        {
            var module = await (_storageModuleLoad ??= js.InvokeAsync<IJSObjectReference>("import",
                "./_content/Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js").AsTask());
            if (!OwnsRecovery(owner, generation)) { return; }
            _storageModule = module;
            var stored = await module.InvokeAsync<PlacementRecoveryRead>("readRecovery", ComponentCancellationToken, scope)
                ?? throw new InvalidOperationException("Placement recovery storage returned no state.");
            if (!OwnsRecovery(owner, generation)) { return; }
            _invalidPending = stored.InvalidValue;
            _pendingCommand = stored.Pending;
            if (_invalidPending is not null)
            {
                _phase = PlacementPhase.ConflictReview;
                _saveError = "Stored placement recovery data is invalid and cannot be replayed. Its earlier save result remains unknown.";
                return;
            }
            if (_pendingCommand is not null)
            {
                _phase = PlacementPhase.OutcomeUnknown;
                _saveError = "A placement save still needs confirmation. Recover the original save before recording another.";
            }
            _storageReady = true;
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
            && exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            if (OwnsRecovery(owner, generation))
            {
                _saveError = "Placement recovery storage is unavailable. Retry storage before saving.";
                if (_storageModuleLoad?.IsFaulted == true) { _storageModuleLoad = null; }
            }
        }
        finally
        {
            if (OwnsRecovery(owner, generation))
            {
                _storageOwner = owner;
                _attachingStorage = null;
                StateHasChanged();
            }
        }
    }

    private bool OwnsRecovery(string owner, int generation) => generation == _storageGeneration
        && !ComponentCancellationToken.IsCancellationRequested && string.Equals(owner, RecoveryOwner, StringComparison.Ordinal);

    private async Task RetryStorageAsync()
    {
        _storageOwner = null;
        await AttachRecoveryAsync();
    }

    private Task RecoverSaveAsync() => _pendingCommand is { } pending ? DispatchAsync(pending) : Task.CompletedTask;

    private async Task ReviewExpiredSaveAsync()
    {
        if (!_recoveryExpired || _pendingCommand is not { } input || _storageModule is null || _saving) { return; }
        if (SelectedParticipantId != input.PlayerCampaignAssignmentId)
        {
            navigation.NavigateTo(ComposePlaceUrl?.Invoke(input.PlayerCampaignAssignmentId)
                ?? Nova.UI.Features.Campaigns.Services.CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, State,
                    placementParticipantId: input.PlayerCampaignAssignmentId));
            return;
        }
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
            if (!OwnsOperation(owner, scope, generation) || !AuthoritativeEvidenceFresh
                || SelectedParticipantId != input.PlayerCampaignAssignmentId
                || _selected?.PlayerCampaignAssignmentId != input.PlayerCampaignAssignmentId) { return; }
            await _storageModule.InvokeVoidAsync("clearPending", ComponentCancellationToken, scope, input.OperationId);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            _pendingCommand = null;
            _recoveryExpired = false;
            await ApplyPendingStateAsync();
            if (!OwnsOperation(owner, scope, generation)) { return; }
            var fresh = AuthoritativeEvidenceFresh && !_queueLoading && !_selectedLoading;
            _phase = fresh ? PlacementPhase.Editing : PlacementPhase.ConflictReview;
            _saveError = "The earlier save's result remains unknown; any new decision requires a deliberate save.";
            if (!fresh) { EnterConflict("Current placement changed during recovery. Review the latest placement before editing."); }
        }
        catch (JSException)
        {
            if (OwnsOperation(owner, scope, generation)) { _saveError = "Recovery storage could not be cleared. Retry reviewing current placement."; }
        }
        finally
        {
            if (OwnsOperation(owner, scope, generation) && _phase == PlacementPhase.Reconciling)
            {
                _phase = PlacementPhase.OutcomeUnknown;
            }
        }
    }
}
