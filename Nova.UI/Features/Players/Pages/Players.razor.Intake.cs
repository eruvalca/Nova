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
    private bool _intakeContextUnavailable;
    private int _intakeContextVersion;
    private PlayerCreationRecoveryState _recoveryState;
    private string? _invalidRetainedValue;
    private bool _storageUnavailable;
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
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        var result = await ReceiveDirectoryReadAsync(
            () => intakeContextService.GetPlayerIntakeContextAsync(new GetPlayerIntakeContextInput { ClubId = clubId }, token),
            "intake context",
            token);
        if (version != _intakeContextVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

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
            return;
        }

        var version = _identityVersion;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        var read = await _board.ReadRecoveryAsync(token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _recoveryScope = CurrentScope;
        if (read is null)
        {
            _storageUnavailable = true;
            _recoveryState = PlayerCreationRecoveryState.None;
            _pendingCreate = null;
            _invalidRetainedValue = null;
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
        // replacement identity is allocated only when nothing is retained.
        var adoptingRetainedCommand = _pendingCreate is null;
        var command = _pendingCreate ?? _createForm.ToCreateInput(Guid.CreateVersion7(), clubId);
        if (adoptingRetainedCommand && !await RetainAsync(command))
        {
            _isMutating = false;
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
    /// <returns><see langword="true"/> when the command is durably retained.</returns>
    private async Task<bool> RetainAsync(CreatePlayerInput command)
    {
        if (!PlayerCreationOperation.TryGetDeadline(command.OperationId, DateTimeOffset.UtcNow, out var deadline))
        {
            _mutationError = "This addition's 24-hour window has closed. Review the Players directory before adding again.";
            _recoveryState = PlayerCreationRecoveryState.Expired;
            return false;
        }

        if (_board is null || !await _board.PersistAsync(command, deadline, _identitySource!.Token))
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
            await _board.ClearAsync(command.OperationId, _identitySource!.Token);
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
            await ReleaseRetainedAsync(command.OperationId);
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
    private async Task ReleaseRetainedAsync(Guid operationId)
    {
        _pendingCreate = null;
        _retainedPlayerName = null;
        _recoveryState = PlayerCreationRecoveryState.None;
        if (_board is not null)
        {
            await _board.ClearAsync(operationId, _identitySource!.Token);
        }
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

        var operationId = _pendingCreate?.OperationId;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        if (_invalidRetainedValue is { } invalid)
        {
            _ = await _board.DiscardUnreadableAsync(invalid, token);
        }
        else if (operationId is { } id)
        {
            _ = await _board.ClearAsync(id, token);
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
        _createForm = PlayerFormState.CreateDefault();
        _fieldErrors = null;
        _mutationError = null;
        _statusMessage = null;
        _graduationYearBlockers = [];
        _creationDuplicate = null;
        _recoveryState = PlayerCreationRecoveryState.None;
        _board?.MarkCommittedOrClosed();
        _board?.RequestFocusOnFirstField();
        await RestoreRecoveryAsync();
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
