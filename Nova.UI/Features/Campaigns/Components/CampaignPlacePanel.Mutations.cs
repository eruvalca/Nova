using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Owns the single confirm, dispatch, replay and reconcile placement path.</summary>
public partial class CampaignPlacePanel
{
    private enum PlacementPhase { Editing, Confirming, Submitting, OutcomeUnknown, Reconciling, ConflictReview }
    private PlacementPhase _phase;
    private int _operationGeneration;
    private CampaignWorkspacePlacementState? _pendingState;
    private bool _pendingPosture;
    private UpdateCampaignPlacementInput? _confirmation;
    private UpdateCampaignPlacementInput? _pendingCommand;
    private bool _decisionOpened;
    private Guid? _keepOperationId;
    private bool _recoveryExpired;
    private bool _focusConfirmation;
    private ElementReference _confirmationHeading;
    private string? _confirmationOwner;
    private string? _settledScope;
    private bool IsPostureChanged => _appliedStatus != CampaignStatus
        || !string.Equals(PersistedOwner, EffectiveOwner, StringComparison.Ordinal);
    private bool AuthoritativeEvidenceFresh => _queue is not null && _queueError is null && !_queueStale
        && !_queueRowsStale && _selectedError is null && !AuthorityLoadFailed
        && _adoptedQueueRequest == _queueRequestSequence && !IsDiscoveryChanged && !IsPostureChanged
        && _appliedParticipantId == SelectedParticipantId;
    private bool CanSave => CanRecordDecision && _storageReady && !_saving && _selected is not null
        && _phase == PlacementPhase.Editing && IsDraftDirty && _draftOutcome != PlacementOutcome.Undecided
        && (_draftOutcome != PlacementOutcome.Assigned || (_draftTeamId is not null && !_teamChoicesLoading
            && _teamChoicesError is null && VisibleTeamChoices.Any(t => t.TeamId == _draftTeamId && !t.Unavailable)));
    private bool IsMissingTeamForAssignment => _draftOutcome == PlacementOutcome.Assigned && _draftTeamId is null
        && _teamChoicesError is null && _compatibleTeams.Count > 0;

    private void InvalidateDecision()
    {
        _confirmation = null;
        _confirmationOwner = null;
        _decisionOpened = false;
        if (_phase == PlacementPhase.Confirming) { _phase = PlacementPhase.Editing; }
    }

    private void BeginDecision()
    {
        if (_saving || _pendingCommand is not null || IsClosed || !CanEditPlacements || !AuthoritativeEvidenceFresh) { return; }
        _decisionOpened = true;
        _draftOutcome = PlacementOutcome.Assigned;
        _draftTeamId = _selected?.EffectiveTeam?.TeamId;
    }

    private Task OnDraftOutcomeChangedAsync(ChangeEventArgs args)
    {
        if (!CanRecordDecision || _saving || !Enum.TryParse<PlacementOutcome>(args.Value?.ToString(), true, out var outcome)
            || !Enum.IsDefined(outcome) || outcome == PlacementOutcome.Undecided) { return Task.CompletedTask; }
        _confirmation = null;
        _phase = PlacementPhase.Editing;
        _draftOutcome = outcome;
        if (outcome != PlacementOutcome.Assigned) { _draftTeamId = null; }
        _saveError = null;
        return Task.CompletedTask;
    }

    private Task OnDraftTeamChangedAsync(ChangeEventArgs args)
    {
        if (!CanRecordDecision || _saving) { return Task.CompletedTask; }
        _confirmation = null;
        _phase = PlacementPhase.Editing;
        _draftTeamId = long.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && VisibleTeamChoices.Any(t => t.TeamId == id && !t.Unavailable) ? id : null;
        _saveError = null;
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (!CanSave || _selected is not { } selected || _draftToken is not { } token) { return; }
        _keepOperationId = null;
        var input = new UpdateCampaignPlacementInput(selected.PlayerCampaignAssignmentId, _draftOutcome,
            _draftOutcome == PlacementOutcome.Assigned ? _draftTeamId : null, token, Guid.CreateVersion7());
        if (_draftOutcome == PlacementOutcome.Withdrawn || selected.LocalDecision is not null || selected.EffectiveDecision is not null)
        {
            _confirmation = input;
            _confirmationOwner = EffectiveOwner;
            _phase = PlacementPhase.Confirming;
            _focusConfirmation = true;
            return;
        }
        await DispatchAsync(input);
    }

    private async Task ConfirmChangeAsync()
    {
        if (_phase != PlacementPhase.Confirming || _confirmation is not { } input || !CanRecordDecision
            || input.PlayerCampaignAssignmentId != _selected?.PlayerCampaignAssignmentId
            || input.ExpectedConcurrencyToken != _draftToken || !string.Equals(_confirmationOwner, EffectiveOwner, StringComparison.Ordinal)) { return; }
        _confirmation = null;
        await DispatchAsync(input);
    }

    private void CancelConfirmation()
    {
        _confirmation = null;
        _phase = PlacementPhase.Editing;
        ApplyDraftFromSelection();
        _stageFocusTarget = true;
    }

    private string ConfirmationText
    {
        get
        {
            var name = _selected?.DisplayName;
            var team = VisibleTeamChoices.FirstOrDefault(t => t.TeamId == _confirmation?.TeamId)?.Name;
            var prior = _selected?.EffectiveTeam?.TeamName ?? _selected?.LocalTeam?.TeamName;
            var teamSuffix = team is null ? string.Empty : $" on {team}";
            if (_draftOutcome == PlacementOutcome.Withdrawn)
            {
                return $"Withdraw {name} for this season? This decision is final in this campaign.";
            }
            if (_context?.CanSupersedeWithdrawal == true)
            {
                return $"Make {name} available again and record {CampaignPlaceDisplay.OutcomeLabel(_draftOutcome)}{teamSuffix}? The earlier campaign remains unchanged.";
            }
            if (_draftOutcome == PlacementOutcome.Assigned && prior is not null)
            {
                return $"Move {name} from {prior} to {team}?";
            }
            return $"Change {name} to {CampaignPlaceDisplay.OutcomeLabel(_draftOutcome)}{teamSuffix} for this campaign?";
        }
    }

    private async Task DispatchAsync(UpdateCampaignPlacementInput input)
    {
        if (_saving || !_storageReady || _storageModule is null) { return; }
        var owner = EffectiveOwner;
        var scope = StorageScope;
        var generation = ++_operationGeneration;
        _phase = PlacementPhase.Submitting;
        _saveError = null;
        _saveMessage = null;
        try
        {
            await _storageModule.InvokeVoidAsync("writePending", ComponentCancellationToken, scope, input);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            _pendingCommand = input;
            await SendPendingAsync(input, owner, scope, generation);
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
            && exception is Microsoft.JSInterop.JSException or InvalidOperationException)
        {
            if (OwnsOperation(owner, scope, generation))
            {
                _saveError = "The pending placement could not be stored. Retry storage before saving.";
                _storageReady = false;
            }
        }
        finally
        {
            if (OwnsOperation(owner, scope, generation) && _phase == PlacementPhase.Submitting)
            {
                _phase = _pendingCommand is null ? PlacementPhase.Editing : PlacementPhase.OutcomeUnknown;
            }
        }
    }

    private bool OwnsOperation(string owner, string scope, int generation) => !ComponentCancellationToken.IsCancellationRequested
        && generation == _operationGeneration && string.Equals(owner, EffectiveOwner, StringComparison.Ordinal) && string.Equals(scope, StorageScope, StringComparison.Ordinal);

    private async Task SendPendingAsync(UpdateCampaignPlacementInput input, string owner, string scope, int generation)
    {
        ServiceResult<PlacementMutationSuccess> result;
        try { result = await placementService.UpdatePlacementAsync(input, ComponentCancellationToken); }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
            && exception is HttpRequestException or OperationCanceledException)
        {
            result = ServiceProblem.ServerError("The placement save could not be confirmed.");
        }
        if (!OwnsOperation(owner, scope, generation)) { return; }
        if (result.IsProblem && !PlacementMutationRejection.IsNotCommitted(result.Problem, input.OperationId))
        {
            _recoveryExpired = PlacementMutationRejection.IsExpired(result.Problem, input.OperationId);
            _phase = PlacementPhase.OutcomeUnknown;
            _saveError = _recoveryExpired
                ? "The recovery window expired. The earlier result is unknown. Review current placement before making a new decision."
                : "The save could not be confirmed. Recover this save before starting another placement.";
            await ApplyPendingStateAsync();
            return;
        }
        var receipt = result.IsSuccess ? result.Value.Receipt : null;
        if (result.IsSuccess && (receipt is null || receipt.OperationId != input.OperationId
            || receipt.PlayerCampaignAssignmentId != input.PlayerCampaignAssignmentId))
        {
            _phase = PlacementPhase.OutcomeUnknown;
            _saveError = "The save response did not identify this operation. Recover this save before continuing.";
            return;
        }
        await SettleCommandAsync(input, result, owner, scope, generation);
    }

    private async Task SettleCommandAsync(UpdateCampaignPlacementInput input, ServiceResult<PlacementMutationSuccess> result,
        string owner, string scope, int generation)
    {
        // Clear only the matching persisted operation; a cleanup failure keeps it available for exact replay.
        try { await _storageModule!.InvokeVoidAsync("clearPending", ComponentCancellationToken, scope, input.OperationId); }
        catch (Microsoft.JSInterop.JSException)
        {
            if (OwnsOperation(owner, scope, generation))
            {
                _phase = PlacementPhase.OutcomeUnknown;
                _saveError = result.IsSuccess ? "Placement saved. Recovery storage could not be cleared; recover to finish." : "The save was refused. Recover to clear its stored result.";
            }
            return;
        }
        if (!OwnsOperation(owner, scope, generation)) { return; }
        _pendingCommand = null;
        if (result.IsSuccess)
        {
            _settledScope = scope;
            _saveMessage = "Placement saved. The original operation is confirmed.";
        }
        _phase = PlacementPhase.Reconciling;
        await OnReloadRequested.InvokeAsync();
        if (!OwnsOperation(owner, scope, generation)) { return; }
        var reconciliation = await ReconcileAsync();
        if (!OwnsOperation(owner, scope, generation)) { return; }
        if (reconciliation == ReconcileOutcome.PageCorrected)
        {
            _pageCorrectionSettlement = new(input, result, owner, scope, generation);
            // Parameters can arrive inside the navigation callback, before the deferred record exists.
            await ResumePageCorrectionSettlementAsync();
            return;
        }
        if (reconciliation == ReconcileOutcome.Obsolete)
        {
            _keepOperationId = null;
            EnterConflict("Refresh current placement evidence before editing.");
            return;
        }
        await ApplyPendingStateAsync();
        if (!OwnsOperation(owner, scope, generation)) { return; }
        CompleteSettlement(input, result);
    }

    private void CompleteSettlement(UpdateCampaignPlacementInput input, ServiceResult<PlacementMutationSuccess> result)
    {
        var fresh = AuthoritativeEvidenceFresh && !_selectedLoading && !_queueLoading;
        _phase = fresh ? PlacementPhase.Editing : PlacementPhase.ConflictReview;
        if (!result.IsSuccess)
        {
            _keepOperationId = null;
            _saveError = FirstValidationMessage(result.Problem.Errors) ?? result.Problem.Detail ?? SaveFailureFallbackMessage;
            if (result.Problem.Kind == ServiceProblemKind.Conflict || !fresh) { EnterConflict(_saveError); }
            return;
        }
        _saveMessage = fresh ? $"Placement saved. {SectionCount("NeedsPlacement")} need placement."
            : "Placement saved. Current placement evidence could not be refreshed.";
        if (fresh && _selected?.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId
            && _selected.LocalDecision?.ConcurrencyToken != result.Value.ConcurrencyToken && !IsClosed)
        {
            _saveMessage = "Placement saved. A later decision is now shown below.";
        }
        if (!fresh) { EnterConflict("Refresh current placement evidence before editing."); }
        if (_keepOperationId == input.OperationId && fresh && input.PlayerCampaignAssignmentId == SelectedParticipantId && _queue is { Rows.Count: > 0 })
        {
            _keepOperationId = null;
            var next = _queue.Rows[0];
            navigation.NavigateTo(ComposePlaceUrl?.Invoke(next.PlayerCampaignAssignmentId)
                ?? CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, State, placementParticipantId: next.PlayerCampaignAssignmentId));
        }
    }

    private void EnterConflict(string? detail)
    {
        _phase = PlacementPhase.ConflictReview;
        _conflictMessage = FirstNonBlank(detail, ConflictFallbackMessage);
        _shouldFocusConflict = true;
    }

    private async Task ReloadAfterConflictAsync()
    {
        if (_saving || _reconciling) { return; }
        var owner = EffectiveOwner;
        var scope = StorageScope;
        var generation = ++_operationGeneration;
        _reconciling = true;
        _phase = PlacementPhase.Reconciling;
        try
        {
            await OnReloadRequested.InvokeAsync();
            if (!OwnsOperation(owner, scope, generation)) { return; }
            await LoadQueueAsync(State);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            await RefreshSelectionAsync(force: true);
            if (!OwnsOperation(owner, scope, generation)) { return; }
            if (AuthoritativeEvidenceFresh && !_selectedLoading && !_queueLoading)
            {
                _phase = PlacementPhase.Editing;
                _conflictMessage = null;
                _saveError = null;
            }
            else { EnterConflict("Current placement evidence could not be refreshed. Try again."); }
        }
        finally
        {
            if (OwnsOperation(owner, scope, generation)) { _reconciling = false; }
            if (OwnsOperation(owner, scope, generation) && _phase == PlacementPhase.Reconciling)
            {
                EnterConflict("Current placement evidence could not be refreshed. Try again.");
            }
        }
    }

    private async Task RetryQueueAsync()
    {
        if (_saving) { return; }
        await LoadQueueAsync(State);
        await RefreshSelectionAsync();
    }

    /// <summary>
    /// Applies a discovery state that arrived while a save was in flight.
    /// </summary>
    /// <returns>A task that completes when the pending state has been applied.</returns>
    private async Task ApplyPendingStateAsync()
    {
        // A lifecycle or authority boundary deferred during a save is reconciled first: it replaces every
        // region, so applying a discovery state on top of it would read the new posture with the old scope.
        if (_pendingPosture)
        {
            _pendingPosture = false;
            ClearPostureEvidence();
            await LoadInitialAsync();
            return;
        }

        if (_pendingState is { } pending)
        {
            _pendingState = null;
            await LoadQueueAsync(pending);
            await RefreshSelectionAsync();
            return;
        }

        // A participant-only change deferred during the save has to be reconciled even when the outcome was
        // refused, or the sheet and the next save would keep targeting the participant the URL moved past.
        if (_appliedParticipantId != SelectedParticipantId)
        {
            await RefreshSelectionAsync();
        }
    }

    /// <summary>
    /// Resolves the first structured validation message, when the server supplied structured errors.
    /// </summary>
    /// <param name="errors">The structured validation errors.</param>
    /// <returns>The first message, or <see langword="null"/> when none was supplied.</returns>
    private static string? FirstValidationMessage(IReadOnlyDictionary<string, string[]>? errors)
        => errors?.Values
            .SelectMany(messages => messages)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

    /// <summary>
    /// Returns the first non-blank value from the supplied candidates.
    /// </summary>
    /// <param name="values">The candidate values, most preferred first.</param>
    /// <returns>The first non-blank value, or <see langword="null"/> when none is present.</returns>
    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
