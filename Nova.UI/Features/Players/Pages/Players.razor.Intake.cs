using System.Globalization;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Players.Components;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players.Pages;

/// <summary>
/// Manual intake and profile correction for one authenticated club: the durable pending-creation
/// command, its reconciliation and abandonment paths, and per-field server feedback.
/// </summary>
public partial class Players
{
    private PlayerIntakeBoard? _board;
    private PlayerIntakeContext? _intakeContext;
    private bool _intakeContextLoading;
    private bool _intakeContextUnavailable;
    private int _intakeContextVersion;
    private PlayerCreationRecoveryState _recoveryState;
    private string? _invalidRetainedValue;
    private bool _recoveryChecked;
    private bool _storageUnavailable;

    /// <summary>
    /// The settled operation whose record the browser could not release, or null when none is
    /// outstanding. A receipt already proves its outcome, so the retry releases this exact record
    /// instead of re-reading a decision that is already made.
    /// </summary>
    private Guid? _unreleasedOperationId;
    private string? _recoveryScope;
    private PlayerCreationCompletion? _receipt;
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;
    private string? _retainedPlayerName;

    /// <summary>The authenticated member's numeric identity, or zero before the claim is applied.</summary>
    private long OwnerUserId => long.TryParse(_userId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    /// <summary>Reads the club's Active campaign so the board can state the enrollment consequence.</summary>
    private async Task LoadIntakeContextAsync()
    {
        if (_clubId is not long clubId)
        {
            return;
        }

        var version = ++_intakeContextVersion;
        var identity = _identityVersion;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        var result = await ReceiveDirectoryReadAsync(
            () => intakeContextService.GetPlayerIntakeContextAsync(new GetPlayerIntakeContextInput { ClubId = clubId }, token),
            "intake context",
            token);
        if (version != _intakeContextVersion || identity != _identityVersion
            || ComponentCancellationToken.IsCancellationRequested)
        {
            // The identity owns this consequence: a read that answered after a club or capability
            // change must not state the previous club's campaign for the identity now on screen.
            return;
        }

        // Both settled outcomes — read and unread — stop the board naming a check in progress; the
        // route boundary armed it, so the board never guesses a campaign fact while this read runs.
        _intakeContextLoading = false;
        result.Switch(
            context =>
            {
                _intakeContext = context;
                _intakeContextUnavailable = false;
            },
            _ =>
            {
                _intakeContext = null;
                _intakeContextUnavailable = true;
            });
    }

    /// <summary>
    /// Restores the owner's retained command before a new addition is offered. Runs only after
    /// interactive attachment, because the storage boundary is browser-only.
    /// </summary>
    private async Task RestoreRecoveryAsync()
    {
        if (_board is null || !_canManagePlayers)
        {
            // No board is displayed to refuse input, so no scope stays claimed as unchecked.
            _recoveryScope = null;
            return;
        }

        var version = _identityVersion;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        var read = await _board.ReadRecoveryAsync(token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // This read proved nothing for the current identity, so release the scope claim rather
            // than leaving the board refusing input forever behind an unsettled read.
            _recoveryScope = null;
            return;
        }

        // Every path below has now examined the owner's retained command, so the board may accept
        // input; a board that could not be checked this way would be stuck refusing input.
        _recoveryChecked = true;
        _recoveryScope = CurrentScope;
        if (read is null)
        {
            // Unreadable storage does not disprove an earlier dispatch: an in-memory retained
            // command is still the only evidence of it and must not be released.
            _storageUnavailable = true;
            if (_pendingCreate is null && _invalidRetainedValue is null)
            {
                _recoveryState = PlayerCreationRecoveryState.None;
            }
            return;
        }

        _storageUnavailable = false;
        switch (read.Kind)
        {
            case PlayerCreationRecoveryKind.Pending when read.Pending is { } pending:
                _pendingCreate = pending.Payload;
                _retainedPlayerName = $"{pending.Payload.FirstName} {pending.Payload.LastName}";
                _invalidRetainedValue = null;
                _createForm = PlayerFormState.FromPendingCommand(pending.Payload);
                _recoveryState = PlayerCreationOperation.TryGetDeadline(pending.Payload.OperationId, DateTimeOffset.UtcNow, out _)
                    ? PlayerCreationRecoveryState.Unresolved
                    : PlayerCreationRecoveryState.Expired;
                break;
            case PlayerCreationRecoveryKind.Unreadable:
                _pendingCreate = null;
                _retainedPlayerName = null;
                _invalidRetainedValue = read.InvalidValue;
                _recoveryState = PlayerCreationRecoveryState.Unreadable;
                break;
            default:
                _pendingCreate = null;
                _retainedPlayerName = null;
                _invalidRetainedValue = null;
                _recoveryState = PlayerCreationRecoveryState.None;
                break;
        }
    }

    /// <summary>
    /// Commits one manual creation. The exact command is durably retained before dispatch, and the
    /// outcome is settled only by receipt-backed evidence.
    /// </summary>
    private async Task CreatePlayerAsync()
    {
        if (!_canManagePlayers || _isMutating || _board is null || _clubId is not long clubId)
        {
            return;
        }

        var version = _identityVersion;
        _isMutating = true;
        _mutationError = null;
        _fieldErrors = null;
        _receipt = null;
        _graduationYearBlockers = [];

        // One logical creation keeps one identity: a retained command is replayed unchanged, and a
        // replacement identity is allocated only when nothing is retained. A replay is retained
        // again before it is dispatched, because another tab may have released the record, and a
        // dispatch whose exact request is no longer recoverable leaves its outcome unknowable.
        var command = _pendingCreate ?? _createForm.ToCreateInput(Guid.CreateVersion7(), clubId);
        if (!await RetainAsync(command, version))
        {
            if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
            {
                // The retention outcome belongs to the identity that asked for it, so a failure here
                // is not published into the page now on screen, which owns its own submission state.
                return;
            }

            _isMutating = false;
            return;
        }

        // Retaining crosses the browser boundary, so this page may have been re-scoped while it
        // ran. The captured identity still owns this request, or nothing is dispatched or mutated.
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _pendingCreate = command;
        var result = await ReceiveAsync(playerManagementService.CreateAsync(command, _identitySource!.Token));
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _isMutating = false;
        if (result.IsSuccess)
        {
            await SettleCommittedAsync(result.Value, command);
            return;
        }

        await SettleProblemAsync(result.Problem, command);
    }

    /// <summary>Persists the exact command before dispatch; a failed write never enables a commit.</summary>
    /// <param name="command">The exact command to retain.</param>
    /// <param name="version">The identity version that owns the command.</param>
    /// <returns><see langword="true"/> when the command is durably retained.</returns>
    private async Task<bool> RetainAsync(CreatePlayerInput command, int version)
    {
        if (!PlayerCreationOperation.TryGetDeadline(command.OperationId, DateTimeOffset.UtcNow, out var deadline))
        {
            _mutationError = "This addition's 24-hour window has closed. Review the Players directory before adding again.";
            _recoveryState = PlayerCreationRecoveryState.Expired;
            return false;
        }

        var persisted = _board is not null
            && await _board.PersistAsync(command, deadline, _identitySource!.Token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // The write crossed the browser boundary, so what it returned belongs to the identity that
            // asked for it: neither its failure nor its success is the replacement page's news.
            return false;
        }

        if (!persisted)
        {
            _storageUnavailable = true;
            _mutationError = "This addition could not be retained safely, so nothing was sent. Retry storage before adding.";
            return false;
        }

        _storageUnavailable = false;
        _recoveryState = PlayerCreationRecoveryState.Unresolved;
        _retainedPlayerName = $"{command.FirstName} {command.LastName}";
        return true;
    }

    /// <summary>Settles a committed operation from its immutable receipt, never from a later read.</summary>
    private async Task SettleCommittedAsync(PlayerCreationCompletion completion, CreatePlayerInput command)
    {
        // Settlement continues across the release boundary, so it carries the ownership it started
        // with: the caller has already proved this receipt belongs to the current identity.
        var version = _identityVersion;
        _receipt = completion;
        _pendingCreate = null;
        _retainedPlayerName = null;
        _invalidRetainedValue = null;
        _recoveryState = PlayerCreationRecoveryState.None;
        _creationDuplicate = null;
        _mutationError = null;
        _fieldErrors = null;
        _statusMessage = completion.Enrollment is { } enrollment
            ? $"Player created. Enrolled in {enrollment.CampaignName}."
            : "Player created. Ready for the next campaign opening.";

        if (_board is not null)
        {
            var released = await _board.ClearAsync(command.OperationId, _identitySource!.Token);
            if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
            {
                // The release crossed the boundary and this page has since been re-scoped, so the
                // continuation must not close the guard, refresh the new owner's data or move focus
                // into a form that no longer holds this receipt.
                return;
            }

            if (!released)
            {
                // The receipt settles this operation, so it is shown regardless; the unreleased
                // bytes are reported instead of pretending the browser released what it did not.
                _storageUnavailable = true;
                _unreleasedOperationId = command.OperationId;
            }
            else
            {
                _unreleasedOperationId = null;
            }

            _board.MarkCommittedOrClosed();
        }

        await RefreshDirectoryAsync();
        await LoadIntakeContextAsync();
        _board?.RequestFocusOnRegion("#intake-receipt-heading");
    }

    /// <summary>Classifies a creation problem into definitively rejected and unresolved outcomes.</summary>
    private async Task SettleProblemAsync(ServiceProblem problem, CreatePlayerInput command)
    {
        if (PlayerCreationProblems.IsNotCommitted(problem, command.OperationId))
        {
            // Receipt-backed refusal: this operation provably did not create, so nothing is retained.
            if (!await ReleaseRetainedAsync(command.OperationId))
            {
                // The release crossed a re-scope, so this refusal belongs to the identity that
                // dispatched it; the new owner's page is not told about another club's outcome.
                return;
            }

            _mutationError = problem.Detail ?? "This addition was refused.";
            if (PlayerCreationProblems.TryGetDuplicate(problem, out var duplicate))
            {
                _creationDuplicate = duplicate;
                _createForm = PlayerFormState.FromPendingCommand(command);
            }

            return;
        }

        if (problem.Kind == ServiceProblemKind.Validation)
        {
            // Validation feedback does not prove this operation failed to create, and the client
            // cannot verify that the server performed no writes. Only a receipt-backed duplicate
            // rejection settles an operation as not committed, so the command stays retained and
            // its field feedback is shown beside the unresolved outcome.
            _recoveryState = PlayerCreationRecoveryState.Unresolved;
            _fieldErrors = problem.Errors;
            _mutationError = problem.Detail
                ?? "The server rejected these values. The earlier addition's result is still unknown.";
            return;
        }

        // Anything else — denial, expiry, mismatch, timeout, cancellation or unusable evidence —
        // leaves the earlier commitment unresolved. The exact command stays retained.
        _recoveryState = PlayerCreationProblems.IsExpired(problem)
            ? PlayerCreationRecoveryState.Expired
            : PlayerCreationRecoveryState.Unresolved;
        _mutationError = problem.Detail ?? "The addition did not return a result.";
    }

    /// <summary>Removes the retained request only after the operation is settled or provably unexecuted.</summary>
    /// <returns><see langword="true"/> when this continuation still owns the page it started on.</returns>
    private async Task<bool> ReleaseRetainedAsync(Guid operationId)
    {
        // The release is attempted before the settled state is cleared, so a browser that refuses it
        // is reported instead of leaving bytes that a later mount would show as unresolved again.
        var version = _identityVersion;
        var released = _board is null || await _board.ClearAsync(operationId, _identitySource!.Token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // The clear crossed the boundary and this page has since been re-scoped: the outcome
            // belongs to the identity that dispatched it, and the state now on screen is not ours.
            return false;
        }

        if (!released)
        {
            // The operation stays blocked rather than released in name only: the browser still holds
            // the exact request, so the member keeps the retry that can actually release it.
            _storageUnavailable = true;
            return true;
        }

        _pendingCreate = null;
        _retainedPlayerName = null;
        _recoveryState = PlayerCreationRecoveryState.None;
        return true;
    }

    /// <summary>
    /// Removes an unresolved or unreadable retained request after the member reviewed the directory.
    /// The earlier result stays unknown and is never replayed to fabricate success.
    /// </summary>
    private async Task SetAsideRetainedAsync()
    {
        if (_board is null)
        {
            return;
        }

        var version = _identityVersion;
        var operationId = _pendingCreate?.OperationId;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        var released = true;
        if (_invalidRetainedValue is { } invalid)
        {
            released = await _board.DiscardUnreadableAsync(invalid, token);
        }
        else if (operationId is { } id)
        {
            released = await _board.ClearAsync(id, token);
        }

        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!released)
        {
            // The member's decision is durable only once the bytes are gone. A browser that refused
            // the removal still holds the exact request, so the board keeps showing it and offers
            // the storage retry rather than reporting a set-aside that did not happen.
            _storageUnavailable = true;
            _mutationError = "This addition was not set aside because the browser kept the retained request. Retry storage.";
            return;
        }

        _pendingCreate = null;
        _retainedPlayerName = null;
        _invalidRetainedValue = null;
        _recoveryState = PlayerCreationRecoveryState.None;
        _mutationError = null;
        _fieldErrors = null;
        _createForm = PlayerFormState.CreateDefault();
        _board.MarkCommittedOrClosed();
        _board.RequestFocusOnFirstField();
        await ReconcileAfterSetAsideAsync();
    }

    /// <summary>Refreshes authoritative evidence so the member resolves an unresolved addition deliberately.</summary>
    private async Task ReconcileAfterSetAsideAsync()
    {
        _statusMessage = "Retained addition set aside. The earlier result stays unknown; check the directory for the player.";
        await RefreshDirectoryAsync();
    }

    /// <summary>Retries the browser storage boundary after it was reported unavailable.</summary>
    private async Task RetryStorageAsync()
    {
        // A settled receipt already proves its outcome, so the retry releases the exact record the
        // browser kept rather than re-reading a decision that is already made.
        if (_unreleasedOperationId is { } unreleased && _board is not null)
        {
            var version = _identityVersion;
            var cleared = await _board.ClearAsync(unreleased, _identitySource?.Token ?? ComponentCancellationToken);
            if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
            {
                // The clear crossed a re-scope, so its outcome belongs to the identity that asked for
                // it; the page now on screen owns its own retry state.
                return;
            }

            // A newer settled operation owns the retry state now, so only this exact record's release
            // may report success for it.
            if (cleared && _unreleasedOperationId == unreleased)
            {
                _unreleasedOperationId = null;
                _storageUnavailable = false;
            }

            return;
        }

        // This read can also land a retained command, so withhold input until it settles rather
        // than letting a landed recovery replace values the member typed meanwhile.
        _recoveryChecked = false;
        await RestoreRecoveryAsync();
        if (!_storageUnavailable)
        {
            _mutationError = null;
        }
    }

    /// <summary>Starts another addition without replaying any player-specific input.</summary>
    private async Task StartAnotherAdditionAsync()
    {
        _receipt = null;
        // Reset only player-specific input; the board's mode and identity stay with the instance.
        _createForm.ResetForNextAddition();
        _fieldErrors = null;
        _mutationError = null;
        _statusMessage = null;
        _graduationYearBlockers = [];
        _creationDuplicate = null;
        _recoveryState = PlayerCreationRecoveryState.None;
        _board?.MarkCommittedOrClosed();
        // This read can also land a retained command, so withhold input until it settles; focus is
        // requested once it has, so it lands in a field the member can actually use.
        _recoveryChecked = false;
        // Claim the scope before awaiting so this read has exactly one owner. Releasing it here would
        // let the render that precedes the await start a second read, whose landed command could
        // overwrite input the member typed meanwhile.
        _recoveryScope = CurrentScope;
        await RestoreRecoveryAsync();
        _board?.RequestFocusOnFirstField();
    }

    /// <summary>Leaves the board for the member's chosen destination after an explicit confirmation.</summary>
    private void LeaveBoard(string url)
    {
        _statusMessage = null;
        navigationManager.NavigateTo(url);
    }

    /// <summary>Saves corrections for an existing player and refreshes the affected evidence.</summary>
    private async Task UpdatePlayerAsync()
    {
        if (!_canManagePlayers || _isMutating || _editForm is null || _clubId is null)
        {
            return;
        }

        var version = _identityVersion;
        var route = _routeVersion;
        _isMutating = true;
        _mutationError = null;
        _fieldErrors = null;
        _graduationYearBlockers = [];

        var result = await ReceiveAsync(playerManagementService.UpdateAsync(_editForm.ToUpdateInput(), _identitySource!.Token));
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (route != _routeVersion)
        {
            _isMutating = false;
            if (result.IsSuccess)
            {
                await RefreshDirectoryAsync();
            }

            return;
        }

        result.Switch(
            _ =>
            {
                _editForm = null;
                _fieldErrors = null;
                _mutationError = null;
                _statusMessage = "Player updated successfully.";
            },
            problem =>
            {
                _mutationError = problem.Detail ?? "Could not update player.";
                _fieldErrors = problem.Errors;
                if (problem.Kind == ServiceProblemKind.Conflict)
                {
                    _graduationYearBlockers = ExtractGraduationYearBlockers(problem.Errors);
                }
            });

        _isMutating = false;
        if (result.IsSuccess)
        {
            _board?.MarkCommittedOrClosed();
            CancelMutationForm();
            await RefreshDirectoryAsync();
        }
    }

    /// <summary>Builds the destination that reviews the retained player's name in the directory.</summary>
    private Uri BuildReconcileUrl()
        => new(_retainedPlayerName is { Length: > 0 } name
            ? (_urlState with { Search = name, Page = 1, View = "active" }).ToDirectoryUrl()
            : BuildCurrentRosterUrl(), UriKind.Relative);

    /// <summary>Builds a player detail destination that preserves the current return context.</summary>
    private Uri BuildIntakeDetailUrl(long playerId) => new(BuildPlayerDetailUrl(playerId), UriKind.Relative);

    /// <summary>Builds the directory destination that preserves the current return context.</summary>
    private Uri BuildIntakeDirectoryUrl() => new(BuildCurrentRosterUrl(), UriKind.Relative);

    /// <summary>
    /// Extracts field-keyed and structured blockers from a conflict payload.
    /// </summary>
    /// <param name="errors">The service-problem errors dictionary.</param>
    /// <returns>A parsed list of blocker items, or an empty list when unavailable.</returns>
    private static IReadOnlyList<GraduationYearBlockerItem> ExtractGraduationYearBlockers(
        IReadOnlyDictionary<string, string[]>? errors)
        => PlayerGraduationYearBlockers.Extract(errors);
}
