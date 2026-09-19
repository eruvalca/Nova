using System.Globalization;
using Microsoft.JSInterop;
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

    /// <summary>
    /// Whether this instance has already reconciled the prerendered intake read. The snapshot is adopted
    /// once per instance — the prerender and interactive attach pair — so a later entry to the form reads
    /// the campaign again rather than adopting what an earlier entry settled.
    /// </summary>
    private bool _intakeContextStarted;
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
    private bool HasReceiptCleanupOutstanding
        => _receipt is not null && (_unreleasedOperationId is not null || _invalidRetainedValue is not null);
    private string? _recoveryScope;
    private PlayerCreationCompletion? _receipt;
    private IReadOnlyDictionary<string, string[]>? _fieldErrors;
    private string? _retainedPlayerName;

    /// <summary>The authenticated member's numeric identity, or zero before the claim is applied.</summary>
    private long OwnerUserId => long.TryParse(_userId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    /// <summary>
    /// The recovery read that owns this visit. Every read takes the next generation, and only the newest one
    /// may claim the board's readiness or publish its snapshot, so a read that answers after the member left
    /// and re-entered the form cannot replace input the newer read has already enabled.
    /// </summary>
    private int _recoveryAttemptVersion;

    /// <summary>Reads the club's Active campaign so the board can state the enrollment consequence.</summary>
    private async Task LoadIntakeContextAsync()
    {
        if (_clubId is not long clubId)
        {
            return;
        }

        if (!_intakeContextStarted)
        {
            // The prerendered page already showed this read's outcome, so attaching the client adopts it
            // instead of asking the same question of the club a second time.
            _intakeContextStarted = true;
            if (RestoreIntakeContext())
            {
                return;
            }
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

        // Both settled outcomes are published, because the prerendered page showed one of them: an
        // attaching client adopts the same answer rather than reading the campaign again.
        PersistedIntakeContext = _intakeContext;
        PersistedIntakeContextUnavailable = _intakeContextUnavailable;
    }

    /// <summary>
    /// Adopts the intake read the prerender already made for this owner, so attaching the client does not
    /// repeat it. Mirrors the roster snapshot: a read that never settled, or one another identity owns,
    /// leaves the question to this visit.
    /// </summary>
    /// <returns><see langword="true"/> when the prerendered read answered for this owner.</returns>
    private bool RestoreIntakeContext()
    {
        if (PersistedIntakeContext is null && !PersistedIntakeContextUnavailable)
        {
            return false;
        }

        if (!string.Equals(SnapshotScope, CurrentScope, StringComparison.Ordinal))
        {
            return false;
        }

        _intakeContextLoading = false;
        _intakeContext = PersistedIntakeContext;
        _intakeContextUnavailable = PersistedIntakeContextUnavailable;
        return true;
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
        var route = _routeVersion;
        var attempt = ++_recoveryAttemptVersion;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        var read = await _board.ReadRecoveryAsync(token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // This read proved nothing for the current identity, so release the scope claim rather
            // than leaving the board refusing input forever behind an unsettled read.
            _recoveryScope = null;
            return;
        }

        if (attempt != _recoveryAttemptVersion || route != _routeVersion || !_showCreateForm)
        {
            // The member left and re-entered the form while this read was in flight, or a newer read for this
            // visit superseded it, so what it carries belongs to a visit this one has left. Publishing it, or
            // claiming the board's readiness for it, would let a stale storage snapshot replace input that
            // the newer read has already enabled; the claim and the readiness belong to that newer read.
            return;
        }

        _recoveryScope = CurrentScope;
        if (read is null)
        {
            // Storage refused the read, so the owner's retained state is still unknown and the board
            // keeps withholding input: opening it would let a later retry land a retained command over
            // values typed meanwhile. Unreadable storage does not disprove an earlier dispatch, so an
            // in-memory retained command stays the only evidence of it and is never released.
            _storageUnavailable = true;
            if (_pendingCreate is null && _invalidRetainedValue is null)
            {
                _recoveryState = PlayerCreationRecoveryState.None;
            }

            return;
        }

        // The owner's retained command has actually been examined — for this scope, by a read that
        // answered — so the board may accept input now. A read that fails leaves that unproven, which is
        // what keeps a failed retry or refresh from reopening the form over unknown retained state.
        _recoveryChecked = true;
        _storageUnavailable = false;
        ApplyRecoveryRead(read);
        // A generic unreadable recovery has its own blocking card. When an immutable receipt owns the
        // same bytes, keep its cleanup panel visible instead so the confirmed outcome is not relabelled.
        _storageUnavailable = _receipt is not null && read.Kind == PlayerCreationRecoveryKind.Unreadable;
    }

    /// <summary>Applies one settled recovery read to the board's retained state.</summary>
    /// <param name="read">The read to apply.</param>
    private void ApplyRecoveryRead(PlayerCreationRecoveryRead read)
    {
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
        // A submission publishes its own outcome: a previous refusal's duplicate panel describes an earlier
        // operation, and the board withholds the replay while that panel stands, so neither may outlive the
        // attempt it belonged to.
        _creationDuplicate = null;

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

        // The settlement's storage cleanup belongs to this operation: the submit state stays busy until the
        // outcome has been applied, so a resolution action cannot start a second operation over the record this
        // one still owns.
        await ApplyCreationOutcomeAsync(result, command);
        _isMutating = false;
    }

    /// <summary>
    /// Applies one creation outcome to the visit that dispatched it. A member who left the create form while
    /// the request was in flight is told nothing about it here: the exact command stays retained, because it is
    /// the only durable evidence of a dispatch that may well have committed, and the same operation identity
    /// recovers the same receipt server-side when they return and replay it. A member who left and came back is
    /// not in that position — their board read the same retained record, so this outcome settles the operation
    /// it already displays. The twenty-third pass also withheld the outcome from a board that no longer held
    /// the command; that interleaving is impossible now that the actions resolving the retained record are
    /// unavailable while a submission is in flight (<c>CanResolveRetained</c>), and publishing whenever the form
    /// shows is what keeps a committed receipt visible instead of dropping it.
    /// </summary>
    /// <param name="result">The outcome the server returned for the retained command.</param>
    /// <param name="command">The exact retained command the outcome belongs to.</param>
    /// <returns>A task that completes when the outcome has been applied.</returns>
    private async Task ApplyCreationOutcomeAsync(ServiceResult<PlayerCreationCompletion> result, CreatePlayerInput command)
    {
        if (!_showCreateForm)
        {
            // A committed creation still refreshes the directory, which is the one place its new player
            // is visible to the member who walked away from the form.
            if (result.IsSuccess)
            {
                await RefreshDirectoryAsync();
            }

            return;
        }

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

        var release = await ReleasedOrAlreadyGoneAsync(command.OperationId, version, _identitySource!.Token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // The release crossed the boundary and this page has since been re-scoped, so the
            // continuation must not close the guard, refresh the new owner's data or move focus into a
            // form that no longer holds this receipt.
            return;
        }

        if (!release.IsGone)
        {
            // The receipt settles this operation, so it is shown regardless; the retained bytes are
            // reported instead of pretending the browser released what it did not. Unreadable bytes
            // cannot be removed by operation id, so preserve their exact value for deliberate discard.
            _storageUnavailable = true;
            _invalidRetainedValue = release.UnreadableValue;
            _recoveryState = release.UnreadableValue is null
                ? PlayerCreationRecoveryState.None
                : PlayerCreationRecoveryState.Unreadable;
            _unreleasedOperationId = release.UnreadableValue is null ? command.OperationId : null;
        }
        else
        {
            _unreleasedOperationId = null;
        }

        _board?.MarkCommittedOrClosed();

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

    /// <summary>
    /// Releases the exact retained operation through the board when it is rendered, or straight
    /// through the browser boundary when it is not: the durable record outlives the board, so a
    /// settlement that arrives after a route change must still be able to clear it.
    /// </summary>
    /// <param name="operationId">The settled operation identity.</param>
    /// <param name="cancellationToken">A token that cancels the release.</param>
    /// <returns><see langword="true"/> when the matching record was removed.</returns>
    private async Task<bool> ClearRetainedAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (_board is not null)
        {
            return await _board.ClearAsync(operationId, cancellationToken);
        }

        try
        {
            return await intakeInterop.ClearAsync(OwnerUserId, _clubId ?? 0, operationId, cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            // Nothing in memory holds the record now, so an unreachable boundary means it stays in
            // storage and the caller must report that rather than claim a release.
            return false;
        }
    }

    /// <summary>
    /// Resolves whether one removal left the record it settles gone. A removal that happened proves that
    /// directly; one that found nothing to remove is resolved by reading storage, because "nothing was
    /// there" is not the browser refusing — another same-owner tab's settlement, or the member's own
    /// set-aside, can have taken the record first, and claiming the browser is holding a record that is no
    /// longer there strands the member on a retry that can never find it again. The follow-up read belongs
    /// to the page this removal started on, so a continuation that has already been re-scoped stops here
    /// rather than reading for a page that owns its own state.
    /// </summary>
    /// <param name="remove">The removal to attempt.</param>
    /// <param name="operationId">
    /// The operation whose record the removal settles, when it settles one operation's record, or null when
    /// it settles whatever record the member was shown.
    /// </param>
    /// <param name="version">The identity version that owns this removal.</param>
    /// <param name="cancellationToken">A token that cancels the boundary work.</param>
    /// <returns><see langword="true"/> when the record the removal settles is gone.</returns>
    private async Task<bool> RemovalSucceededOrTheRecordIsGoneAsync(
        Func<Task<bool>> remove, Guid? operationId, int version, CancellationToken cancellationToken)
    {
        if (await remove())
        {
            return true;
        }

        return version == _identityVersion
            && !ComponentCancellationToken.IsCancellationRequested
            && await RetainedRecordIsGoneAsync(operationId, cancellationToken);
    }

    /// <summary>
    /// Releases one settled operation's record, accepting one a same-owner tab already removed and
    /// preserving unreadable bytes that require deliberate discard rather than another exact clear.
    /// </summary>
    /// <param name="operationId">The settled operation identity.</param>
    /// <param name="version">The identity version that owns this release.</param>
    /// <param name="cancellationToken">A token that cancels the boundary work.</param>
    /// <returns>Whether the settled record is gone and any unreadable value still occupying its owner scope.</returns>
    private async Task<(bool IsGone, string? UnreadableValue)> ReleasedOrAlreadyGoneAsync(
        Guid operationId, int version, CancellationToken cancellationToken)
    {
        if (await ClearRetainedAsync(operationId, cancellationToken))
        {
            return (true, null);
        }

        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return (false, null);
        }

        var read = await ReadRetainedRecoveryAsync(cancellationToken);
        if (read is null)
        {
            return (false, null);
        }

        if (read.Kind == PlayerCreationRecoveryKind.Empty)
        {
            return (true, null);
        }

        if (read.Kind == PlayerCreationRecoveryKind.Unreadable)
        {
            return (false, read.InvalidValue);
        }

        var isReplaced = read.Pending is { } pending && pending.Payload.OperationId != operationId;
        return (isReplaced, null);
    }

    /// <summary>Reads the owner's retained state through the rendered board or its browser boundary.</summary>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The retained state, or null when the boundary could not answer.</returns>
    private async Task<PlayerCreationRecoveryRead?> ReadRetainedRecoveryAsync(CancellationToken cancellationToken)
    {
        if (_board is not null)
        {
            return await _board.ReadRecoveryAsync(cancellationToken);
        }

        try
        {
            return await intakeInterop.ReadAsync(OwnerUserId, _clubId ?? 0, cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            // A boundary that cannot answer leaves the browser's state unknown, which the caller must
            // keep reporting as held rather than claim a release.
            return null;
        }
    }

    /// <summary>
    /// Reads the owner's retained state to tell a refused removal apart from a record that is not there.
    /// </summary>
    /// <param name="operationId">
    /// The operation whose record the removal settles, when it settles one. This owner's storage holds one
    /// record, so a record naming a different operation proves this operation's is no longer there.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns><see langword="true"/> only when a read answered that the record the removal settles is gone.</returns>
    private async Task<bool> RetainedRecordIsGoneAsync(Guid? operationId, CancellationToken cancellationToken)
    {
        var read = await ReadRetainedRecoveryAsync(cancellationToken);
        if (read is null)
        {
            return false;
        }

        if (read.Kind == PlayerCreationRecoveryKind.Empty)
        {
            return true;
        }

        // Unreadable bytes cannot be identified, so they keep blocking: only a record that names another
        // operation proves this one's was replaced rather than kept.
        return operationId is { } settled
            && read.Kind == PlayerCreationRecoveryKind.Pending
            && read.Pending is { } pending
            && pending.Payload.OperationId != settled;
    }

    /// <summary>Removes the retained request only after the operation is settled or provably unexecuted.</summary>
    /// <returns><see langword="true"/> when this continuation still owns the page it started on.</returns>
    private async Task<bool> ReleaseRetainedAsync(Guid operationId)
    {
        // The release is attempted before the settled state is cleared, so a browser that refuses it
        // is reported instead of leaving bytes that a later mount would show as unresolved again.
        var version = _identityVersion;
        var release = await ReleasedOrAlreadyGoneAsync(operationId, version, _identitySource!.Token);
        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // The clear crossed the boundary and this page has since been re-scoped: the outcome
            // belongs to the identity that dispatched it, and the state now on screen is not ours.
            return false;
        }

        if (!release.IsGone)
        {
            // The operation stays blocked rather than released in name only. If the retained value has
            // become unreadable, preserve it for the explicit discard path instead of offering an exact
            // clear that can never match it.
            _storageUnavailable = true;
            if (release.UnreadableValue is { } unreadable)
            {
                _invalidRetainedValue = unreadable;
                _pendingCreate = null;
                _retainedPlayerName = null;
                _recoveryState = PlayerCreationRecoveryState.Unreadable;
            }

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
        // A settlement that is still cleaning up owns the record this decision would resolve.
        if (_isMutating || _board is null)
        {
            return;
        }

        var version = _identityVersion;
        var route = _routeVersion;
        var operationId = _pendingCreate?.OperationId;
        var token = _identitySource?.Token ?? ComponentCancellationToken;
        // The removal settles the record the member was shown, so any record still retained keeps the
        // decision blocked rather than reporting a set-aside over bytes that are still there.
        var released = true;
        if (_invalidRetainedValue is { } invalid)
        {
            released = await RemovalSucceededOrTheRecordIsGoneAsync(
                () => _board.DiscardUnreadableAsync(invalid, token), null, version, token);
        }
        else if (operationId is { } id)
        {
            released = await RemovalSucceededOrTheRecordIsGoneAsync(
                () => _board.ClearAsync(id, token), null, version, token);
        }

        if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        await ApplySetAsideOutcomeAsync(released, route != _routeVersion || !_showCreateForm);
    }

    /// <summary>
    /// Applies one answered set-aside. The removal is the member's decision and stands for whatever view is on
    /// screen; only the visit that asked for it may rewrite the form, its messages or the departure guard.
    /// </summary>
    /// <param name="released">Whether the browser removed the retained record.</param>
    /// <param name="leftTheForm">Whether the visit that asked for the removal has left the create form.</param>
    /// <returns>A task that completes when the decision has been applied.</returns>
    private async Task ApplySetAsideOutcomeAsync(bool released, bool leftTheForm)
    {
        if (!released)
        {
            if (leftTheForm)
            {
                // The browser still holds the record and the visit that asked for its removal has left: the
                // form now on screen owns its own report of that, and its input is none of this removal's
                // business.
                return;
            }

            // The member's decision is durable only once the bytes are gone. A browser that refused the
            // removal still holds the exact request, so the board keeps showing it and offers the storage
            // retry rather than reporting a set-aside that did not happen.
            _storageUnavailable = true;
            _mutationError = "This addition was not set aside because the browser kept the retained request. Retry storage.";
            return;
        }

        // The bytes are gone, so nothing retained survives in memory either, and the board is left holding no
        // panel for an operation it no longer keeps.
        var refusedDuplicate = _creationDuplicate is not null;
        _pendingCreate = null;
        _retainedPlayerName = null;
        _invalidRetainedValue = null;
        _creationDuplicate = null;
        // The removal is this page's own proof that the browser's storage answers, so an earlier
        // failure's retry affordance goes with the record it asked the member to retry.
        _storageUnavailable = false;
        _recoveryState = PlayerCreationRecoveryState.None;
        if (leftTheForm)
        {
            return;
        }

        _mutationError = null;
        _fieldErrors = null;
        _createForm = PlayerFormState.CreateDefault();
        _board?.MarkCommittedOrClosed();
        _board?.RequestFocusOnFirstField();
        await ReconcileAfterSetAsideAsync(refusedDuplicate);
    }

    /// <summary>Refreshes authoritative evidence so the member resolves an unresolved addition deliberately.</summary>
    /// <param name="refusedDuplicate">Whether the set-aside settled a receipt-backed refusal rather than an unknown outcome.</param>
    private async Task ReconcileAfterSetAsideAsync(bool refusedDuplicate)
    {
        _statusMessage = _receipt is not null
            ? "Unreadable retained request discarded. The player creation receipt remains authoritative."
            : RetainedSetAsideStatus(refusedDuplicate);
        // The next blank form owns a new enrollment consequence. Re-arm and refresh it alongside the
        // directory so a campaign change cannot leave the previous attempt's preview on screen.
        _intakeContextLoading = true;
        await Task.WhenAll(RefreshDirectoryAsync(), LoadIntakeContextAsync());
    }

    /// <summary>
    /// Names the result a completed set-aside leaves behind. A receipt-backed refusal was settled before the
    /// member set the request aside, so the copy names that outcome instead of sending them to look for a
    /// player the refusal already accounted for.
    /// </summary>
    /// <param name="refusedDuplicate">Whether the set-aside settled a receipt-backed refusal.</param>
    /// <returns>The status message for the outcome the decision settled.</returns>
    internal static string RetainedSetAsideStatus(bool refusedDuplicate) => refusedDuplicate
        ? "Retained addition set aside. The refusal stands and the player it matched is untouched."
        : "Retained addition set aside. The earlier result stays unknown; check the directory for the player.";

    /// <summary>Retries the browser storage boundary after it was reported unavailable.</summary>
    private async Task RetryStorageAsync()
    {
        // The retry speaks for the record a settlement still owns, so it waits for that settlement.
        if (_isMutating)
        {
            return;
        }

        // A settled receipt already proves its outcome, so the retry releases the exact record this
        // receipt settled rather than re-reading a decision that is already made. That release accepts a
        // record another tab already took, so the retry cannot be stranded on bytes that are gone.
        if (_unreleasedOperationId is { } unreleased && _board is not null)
        {
            var version = _identityVersion;
            var release = await ReleasedOrAlreadyGoneAsync(
                unreleased, version, _identitySource?.Token ?? ComponentCancellationToken);
            if (version != _identityVersion || ComponentCancellationToken.IsCancellationRequested)
            {
                // The release crossed a re-scope, so its outcome belongs to the identity that asked for
                // it; the page now on screen owns its own retry state.
                return;
            }

            // A newer settled operation owns the retry state now, so only this exact record's release
            // may report success for it.
            if (release.IsGone && _unreleasedOperationId == unreleased)
            {
                _unreleasedOperationId = null;
                _storageUnavailable = false;
            }
            else if (release.UnreadableValue is { } unreadable && _unreleasedOperationId == unreleased)
            {
                _unreleasedOperationId = null;
                _invalidRetainedValue = unreadable;
                _recoveryState = PlayerCreationRecoveryState.Unreadable;
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
        // The receipt belongs to a settlement that may still be cleaning up, and that settlement owns the record
        // the next addition would read.
        if (_isMutating)
        {
            return;
        }

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
        // The consequence belongs to this attempt too: the Active campaign may have changed while the receipt was
        // on screen, so the next addition reads it again and names the check until that read settles.
        _intakeContextLoading = true;
        // This read can also land a retained command, so withhold input until it settles; focus is
        // requested once it has, so it lands in a field the member can actually use.
        _recoveryChecked = false;
        // Claim the scope before awaiting so this read has exactly one owner. Releasing it here would
        // let the render that precedes the await start a second read, whose landed command could
        // overwrite input the member typed meanwhile.
        _recoveryScope = CurrentScope;
        await RestoreRecoveryAsync();
        await LoadIntakeContextAsync();
        _board?.RequestFocusOnFirstField();
    }

    /// <summary>Leaves the board for the member's chosen destination after an explicit confirmation.</summary>
    private void LeaveBoard(string url)
    {
        // "Leave and discard" is an explicit discard, so the typed values must not be waiting when the
        // member returns. A durable retained command is untouched: its own frozen copy is what the
        // board shows for it, and no confirmation was shown about that.
        _statusMessage = null;
        _fieldErrors = null;
        _mutationError = null;
        _graduationYearBlockers = [];
        _creationDuplicate = null;
        _createForm = _pendingCreate is { } retained
            ? PlayerFormState.FromPendingCommand(retained)
            : PlayerFormState.CreateDefault();
        _editForm = null;
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
