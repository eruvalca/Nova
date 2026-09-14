
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Records, reconciles, and recovers the selected participant's campaign-local placement decision.
/// </summary>
/// <remarks>
/// There is exactly one mutation path here. The surface presents the participant's local concurrency token,
/// adopts the replacement token the server returns, and then re-reads the queue, the unfiltered section totals,
/// and the selected participant's evidence authoritatively. It never applies the submitted values as truth and
/// never re-derives eligibility on the client.
/// </remarks>
public partial class CampaignPlacePanel
{
    /// <summary>The discovery state requested while a save was in flight, applied once the save settles.</summary>
    private CampaignWorkspacePlacementState? _pendingState;

    /// <summary>
    /// Gets a value indicating whether the drafted decision may be submitted.
    /// </summary>
    /// <remarks>
    /// <c>Assigned</c> without a team is an invalid state, so the submit is blocked rather than dispatched
    /// and refused; the written reason is rendered beside the decision controls.
    /// </remarks>
    private bool CanSave => CanRecordDecision && !_saving && _selected is not null && IsDraftDirty
        && (_draftOutcome != PlacementOutcome.Assigned || _draftTeamId is not null);

    /// <summary>
    /// Gets a value indicating whether the drafted decision is blocked by a missing team.
    /// </summary>
    private bool IsMissingTeamForAssignment => _draftOutcome == PlacementOutcome.Assigned
        && _draftTeamId is null
        && _teamChoicesError is null
        && _compatibleTeams.Count > 0;

    /// <summary>
    /// Applies a drafted outcome, clearing the team whenever the draft leaves <c>Assigned</c>.
    /// </summary>
    /// <param name="args">The change event carrying the drafted outcome token.</param>
    /// <returns>A completed task.</returns>
    private Task OnDraftOutcomeChangedAsync(ChangeEventArgs args)
    {
        if (!Enum.TryParse<PlacementOutcome>(args.Value?.ToString(), ignoreCase: true, out var outcome)
            || outcome == PlacementOutcome.Undecided)
        {
            // Undecided is technical participation, not a decision the mutation accepts, so it can never be
            // drafted as a submitted outcome.
            return Task.CompletedTask;
        }

        _draftOutcome = outcome;
        if (outcome != PlacementOutcome.Assigned)
        {
            // Assigned without a team and a team without Assigned are both invalid states, so leaving
            // Assigned clears the drafted team rather than leaving a stale selection behind.
            _draftTeamId = null;
        }

        _saveError = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a drafted compatible team.
    /// </summary>
    /// <param name="args">The change event carrying the drafted team identifier.</param>
    /// <returns>A completed task.</returns>
    private Task OnDraftTeamChangedAsync(ChangeEventArgs args)
    {
        _draftTeamId = long.TryParse(args.Value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var teamId)
            && teamId > 0
            ? teamId
            : null;
        _saveError = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records the drafted decision against the selected participant's local concurrency token.
    /// </summary>
    /// <returns>A task that completes when the mutation and its reconciliation finish.</returns>
    private async Task SaveAsync()
    {
        if (!CanSave || _selected is not { } selected || _draftToken is not { } token)
        {
            return;
        }

        if (_draftOutcome == PlacementOutcome.Assigned && _draftTeamId is null)
        {
            _saveError = "Choose a team for an assigned participant.";
            return;
        }

        _saving = true;
        _saveError = null;
        _saveMessage = null;

        var outcome = MutationOutcome.Refused;
        try
        {
            var input = new UpdateCampaignPlacementInput(
                selected.PlayerCampaignAssignmentId,
                _draftOutcome,
                _draftOutcome == PlacementOutcome.Assigned ? _draftTeamId : null,
                token);

            outcome = await SendMutationAsync(input);

            if (outcome == MutationOutcome.Unconfirmed)
            {
                await ReconcileAsync();
                _saveError = "The save could not be confirmed. This view was refreshed from the server; check the placement before saving again.";
            }
        }
        finally
        {
            // Always release the save gate so the controls never stay stuck in the saving state. On the
            // unconfirmed path this runs after reconciliation, so a resubmit cannot race it.
            _saving = false;
        }

        if (outcome == MutationOutcome.Unconfirmed)
        {
            return;
        }

        await SettleAsync(outcome == MutationOutcome.Committed, _draftOutcome);
    }

    /// <summary>
    /// Represents the outcome of sending one placement mutation.
    /// </summary>
    private enum MutationOutcome
    {
        /// <summary>The server accepted the mutation.</summary>
        Committed,

        /// <summary>The server refused the mutation with a specific problem.</summary>
        Refused,

        /// <summary>The transport failed before a response arrived, so the result is unknown.</summary>
        Unconfirmed
    }

    /// <summary>
    /// Sends the mutation, telling a committed result apart from a refusal and from an unconfirmed transport
    /// failure. A lost response can still mean the mutation committed, so that case is never reported as a
    /// refusal.
    /// </summary>
    /// <param name="input">The mutation input.</param>
    /// <returns>The outcome of the send.</returns>
    private async Task<MutationOutcome> SendMutationAsync(UpdateCampaignPlacementInput input)
    {
        try
        {
            var result = await placementService.UpdatePlacementAsync(input, ComponentCancellationToken);
            return ApplyMutationResult(result) ? MutationOutcome.Committed : MutationOutcome.Refused;
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return MutationOutcome.Unconfirmed;
        }
    }

    /// <summary>
    /// Applies a mutation response, adopting the replacement token or surfacing the refusal.
    /// </summary>
    /// <param name="result">The mutation response to apply.</param>
    /// <returns><see langword="true"/> when the mutation committed.</returns>
    private bool ApplyMutationResult(ServiceResult<PlacementMutationSuccess> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(
            success =>
            {
                // Adopt the replacement token so a later edit in this session presents current state.
                _draftToken = success.ConcurrencyToken;
                return true;
            },
            problem =>
            {
                ApplyMutationProblem(problem);
                return false;
            });
    }

    /// <summary>
    /// Maps a refused mutation onto the decision controls or the conflict posture.
    /// </summary>
    /// <param name="problem">The refusal returned by the mutation.</param>
    private void ApplyMutationProblem(ServiceProblem problem)
    {
        _saveError = null;
        switch (problem.Kind)
        {
            case ServiceProblemKind.Conflict:
                EnterConflict(problem.Detail);
                break;
            case ServiceProblemKind.Validation:
                _saveError = FirstValidationMessage(problem.Errors)
                    ?? FirstNonBlank(problem.Detail, SaveFailureFallbackMessage);
                break;
            case ServiceProblemKind.Forbidden:
            case ServiceProblemKind.NotFound:
                _saveError = FirstNonBlank(problem.Detail, "This placement can no longer be updated.");
                break;
            default:
                _saveError = FirstNonBlank(problem.Detail, SaveFailureFallbackMessage);
                break;
        }
    }

    /// <summary>
    /// Reconciles every affected region after the mutation settles, then applies any deferred discovery state.
    /// </summary>
    /// <param name="saved">Whether the mutation committed.</param>
    /// <returns>A task that completes when reconciliation finishes.</returns>
    private async Task SettleAsync(bool saved, PlacementOutcome recordedOutcome)
    {
        if (!saved)
        {
            await ApplyPendingStateAsync();
            return;
        }

        var reconciled = await ReconcileAsync();
        if (_conflictActive)
        {
            await ApplyPendingStateAsync();
            return;
        }

        if (!reconciled)
        {
            // The committed save moved the participant off the last page, so the corrected read is still in
            // flight. Do not announce totals taken from the page being replaced.
            _saveMessage = "Placement saved. The queue page was corrected and is reloading.";
            await ApplyPendingStateAsync();
            return;
        }

        if (_queueError is null && !_queueStale && _selectedError is null)
        {
            _saveMessage = DescribeSave(recordedOutcome);
        }
        else
        {
            // The mutation persisted, but the authoritative refresh did not answer. Do not announce
            // success beside evidence that could not be confirmed.
            _saveError = "Placement saved, but the authoritative result could not be refreshed.";
        }

        await ApplyPendingStateAsync();
    }

    /// <summary>
    /// Names what the save changed, because the destination's story is that the authoritative count moves.
    /// </summary>
    /// <param name="outcome">The outcome that was recorded, captured before reconciliation rebuilds the draft.</param>
    /// <returns>The written success statement.</returns>
    private string DescribeSave(PlacementOutcome outcome)
        => $"Placement saved. {CampaignPlaceDisplay.OutcomeLabel(outcome)} · {SectionCount("NeedsPlacement")} need placement.";

    /// <summary>
    /// Enters the conflict posture: editing is blocked until an authoritative reload establishes the winner.
    /// </summary>
    /// <param name="detail">The server's conflict detail, when one was supplied.</param>
    private void EnterConflict(string? detail)
    {
        _conflictActive = true;
        _conflictMessage = FirstNonBlank(detail, ConflictFallbackMessage);
        _shouldFocusConflict = true;
    }

    /// <summary>
    /// Reloads every affected region after a conflict and resumes editing from authoritative state.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private async Task ReloadAfterConflictAsync()
    {
        if (_reconciling)
        {
            return;
        }

        _reconciling = true;
        try
        {
            _queueStale = false;
            _pendingState = null;
            await LoadQueueAsync(State);
            await RefreshSelectionAsync(force: true);
            _saveMessage = null;
            _saveError = null;
            PersistQueue();

            // The campaign detail owns lifecycle truth; ask the workspace for the authoritative reload so a
            // Closed transition or an authority change is reflected rather than masked by the local reload.
            await OnReloadRequested.InvokeAsync();

            // Clearing the conflict last keeps the reload affordance disabled for the whole recovery and
            // resumes editing only once authoritative state has actually been re-established.
            _conflictActive = false;
            _conflictMessage = null;
        }
        finally
        {
            _reconciling = false;
        }
    }

    /// <summary>
    /// Reloads the queue on demand after a regional read failure.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private async Task RetryQueueAsync()
    {
        if (_saving)
        {
            return;
        }

        await LoadQueueAsync(State);
        await RefreshSelectionAsync();
    }

    /// <summary>
    /// Applies a discovery state that arrived while a save was in flight.
    /// </summary>
    /// <returns>A task that completes when the pending state has been applied.</returns>
    private async Task ApplyPendingStateAsync()
    {
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
