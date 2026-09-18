using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.UI.Features.Players.Services;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// An in-memory stand-in for the collocated board module that reproduces its owner-scoped, exact
/// retention semantics without a browser.
/// </summary>
internal sealed class PlayerIntakeInteropDouble : IPlayerIntakeInterop
{
    private readonly Dictionary<string, PendingPlayerCreation> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unreadable = new(StringComparer.Ordinal);

    /// <summary>Gets or sets whether reads fail the way an unavailable browser storage would.</summary>
    public bool FailReads { get; set; }

    /// <summary>Gets or sets a gate that holds every read open, the way slow storage would.</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>Gets or sets a gate that holds every write open, the way slow storage would.</summary>
    public TaskCompletionSource? WriteGate { get; set; }

    /// <summary>Gets or sets whether writes fail the way an unavailable browser storage would.</summary>
    public bool FailWrites { get; set; }

    /// <summary>Gets or sets a gate that holds every clear open, the way slow storage would.</summary>
    public TaskCompletionSource? ClearGate { get; set; }

    /// <summary>Gets or sets whether clears report that no matching record was removed.</summary>
    public bool FailClears { get; set; }

    /// <summary>Gets or sets the number of departure-guard attach attempts that fail before one succeeds.</summary>
    public int FailGuardAttachAttempts { get; set; }

    /// <summary>Gets the number of departure-guard attach attempts, including failed ones.</summary>
    public int GuardAttachCount { get; private set; }

    /// <summary>Gets the number of write attempts, including ones a gate is still holding.</summary>
    public int WriteAttempts { get; private set; }

    /// <summary>Gets the number of accepted writes.</summary>
    public int WriteCount { get; private set; }

    /// <summary>Gets the number of accepted clears.</summary>
    public int ClearCount { get; private set; }

    /// <summary>Gets the number of accepted discards.</summary>
    public int DiscardCount { get; private set; }

    /// <summary>Gets the exact JSON of the most recent accepted write.</summary>
    public string? LastWriteJson { get; private set; }

    /// <summary>Gets whether the departure guard is currently attached.</summary>
    public bool GuardAttached { get; private set; }

    /// <summary>Gets the lease of the currently attached departure guard, as the module would call back with.</summary>
    public string? GuardLease { get; private set; }

    /// <summary>Gets whether the mounted board currently holds uncommitted input.</summary>
    public bool Dirty { get; private set; }

    /// <summary>Seeds a retained command for an owner, as a previous dispatch would have left it.</summary>
    /// <param name="pending">The retained command.</param>
    public void Seed(PendingPlayerCreation pending) => _pending[OwnerKey(pending.ActorUserId, pending.Payload.ClubId)] = pending;

    /// <summary>Seeds unreadable retained bytes for an owner.</summary>
    /// <param name="actorUserId">The authenticated member owning the bytes.</param>
    /// <param name="clubId">The club owning the bytes.</param>
    /// <param name="raw">The exact bytes.</param>
    public void SeedUnreadable(long actorUserId, long clubId, string raw) => _unreadable[OwnerKey(actorUserId, clubId)] = raw;

    /// <summary>Reads the retained command for an owner, when one exists.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <returns>The retained command, or null.</returns>
    public PendingPlayerCreation? Read(long actorUserId, long clubId)
        => _pending.TryGetValue(OwnerKey(actorUserId, clubId), out var pending) ? pending : null;

    /// <inheritdoc />
    public async Task<PlayerCreationRecoveryRead> ReadAsync(long actorUserId, long clubId, CancellationToken cancellationToken)
    {
        if (ReadGate is { } gate)
        {
            await gate.Task;
        }

        if (FailReads)
        {
            throw new JSException("Storage is unavailable.");
        }

        var key = OwnerKey(actorUserId, clubId);
        if (_unreadable.TryGetValue(key, out var raw))
        {
            return new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Unreadable, null, raw);
        }

        return _pending.TryGetValue(key, out var pending)
            ? new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Pending, pending, null)
            : new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Empty, null, null);
    }

    /// <inheritdoc />
    public async Task WriteAsync(PendingPlayerCreation pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);
        WriteAttempts++;
        if (WriteGate is { } writeGate)
        {
            await writeGate.Task;
        }

        if (FailWrites)
        {
            throw new JSException("Storage is unavailable.");
        }

        var key = OwnerKey(pending.ActorUserId, pending.Payload.ClubId);
        if (_unreadable.ContainsKey(key))
        {
            throw new JSException("Set aside the retained player creation before starting another.");
        }

        if (_pending.TryGetValue(key, out var existing)
            && existing.Payload.OperationId != pending.Payload.OperationId)
        {
            throw new JSException("Recover the existing player creation before starting another.");
        }

        _pending[key] = pending;
        WriteCount++;
        LastWriteJson = pending.ToJson();
    }

    /// <summary>Gets the number of clear attempts, including ones a gate is still holding.</summary>
    public int ClearAttempts { get; private set; }

    /// <inheritdoc />
    public async Task<bool> ClearAsync(long actorUserId, long clubId, Guid operationId, CancellationToken cancellationToken)
    {
        ClearAttempts++;
        if (ClearGate is { } clearGate)
        {
            await clearGate.Task;
        }

        var key = OwnerKey(actorUserId, clubId);
        if (FailClears
            || !_pending.TryGetValue(key, out var existing)
            || existing.Payload.OperationId != operationId)
        {
            return false;
        }

        _pending.Remove(key);
        ClearCount++;
        return true;
    }

    /// <inheritdoc />
    public Task<bool> DiscardInvalidAsync(long actorUserId, long clubId, string expectedValue, CancellationToken cancellationToken)
    {
        var key = OwnerKey(actorUserId, clubId);
        if (!_unreadable.TryGetValue(key, out var raw) || !string.Equals(raw, expectedValue, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        _unreadable.Remove(key);
        DiscardCount++;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task AttachDepartureGuardAsync(ElementReference root, object receiver, string lease, CancellationToken cancellationToken)
    {
        GuardAttachCount++;
        if (FailGuardAttachAttempts > 0)
        {
            FailGuardAttachAttempts--;
            throw new JSException("The departure guard module could not be imported.");
        }

        GuardAttached = true;
        GuardLease = lease;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkDirtyAsync(string lease, bool dirty, CancellationToken cancellationToken)
    {
        Dirty = dirty;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DetachDepartureGuardAsync(string lease, CancellationToken cancellationToken)
    {
        GuardAttached = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FocusFirstFieldAsync(ElementReference root, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task FocusRegionAsync(ElementReference root, string selector, CancellationToken cancellationToken) => Task.CompletedTask;

    private static string OwnerKey(long actorUserId, long clubId) => $"{actorUserId}:{clubId}";
}
