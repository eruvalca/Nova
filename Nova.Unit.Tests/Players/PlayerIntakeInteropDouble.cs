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

    /// <summary>Gets or sets whether a dirty-state write fails the way a torn-down boundary does.</summary>
    public bool FailDirtyWithCancellation { get; set; }

    /// <summary>
    /// Gets or sets a gate that holds the answer to a clear open after the record is already gone, the way a
    /// busy circuit would deliver a removal that has already happened.
    /// </summary>
    public TaskCompletionSource? ClearResumeGate { get; set; }

    /// <summary>Gets or sets the number of departure-guard attach attempts that fail before one succeeds.</summary>
    public int FailGuardAttachAttempts { get; set; }

    /// <summary>
    /// Gets or sets whether an attach installs the guard and then fails, the way a cancelled or torn-down
    /// boundary does after the browser has already installed it.
    /// </summary>
    public bool FailAttachAfterInstalling { get; set; }

    /// <summary>Gets or sets a gate that holds the departure-guard attach open, the way slow interop would.</summary>
    public TaskCompletionSource? GuardAttachGate { get; set; }

    /// <summary>Gets or sets the signal that completes once an attach has installed the guard.</summary>
    public TaskCompletionSource? GuardAttachSettled { get; set; }

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

    /// <summary>Gets the number of reads that returned an answer.</summary>
    public int ReadCount { get; private set; }

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
        PlayerCreationRecoveryRead read;
        if (_unreadable.TryGetValue(key, out var raw))
        {
            read = new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Unreadable, null, raw);
        }
        else
        {
            read = _pending.TryGetValue(key, out var pending)
                ? new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Pending, pending, null)
                : new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Empty, null, null);
        }

        ReadCount++;
        return read;
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

        if (_pending.TryGetValue(key, out var existing))
        {
            if (existing.Payload.OperationId != pending.Payload.OperationId)
            {
                throw new JSException("Recover the existing player creation before starting another.");
            }

            // One operation identity carries one exact command: a replay writes the retained bytes back, so
            // different bytes under that identity are refused rather than allowed to replace the command the
            // dispatch is accounted for by, exactly as the module refuses them.
            if (!string.Equals(existing.ToJson(), pending.ToJson(), StringComparison.Ordinal))
            {
                throw new JSException("Recover the retained player creation before replacing its command.");
            }
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
        if (ClearResumeGate is { } resumeGate)
        {
            await resumeGate.Task;
        }

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
    public async Task AttachDepartureGuardAsync(ElementReference root, object receiver, string lease, CancellationToken cancellationToken)
    {
        GuardAttachCount++;
        if (GuardAttachGate is { } gate)
        {
            await gate.Task;
        }

        if (FailGuardAttachAttempts > 0)
        {
            FailGuardAttachAttempts--;
            throw new JSException("The departure guard module could not be imported.");
        }

        // The boundary installs the guard as soon as the module runs; a cancelled await loses the
        // answer, not the installation, which is exactly the ownership disposal has to settle.
        GuardAttached = true;
        GuardLease = lease;
        GuardAttachSettled?.TrySetResult();
        if (FailAttachAfterInstalling)
        {
            throw new JSException("The departure guard's answer was lost after it was installed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Gets the number of dirty-state writes the boundary was asked to record.</summary>
    public int DirtyAttempts { get; private set; }

    /// <inheritdoc />
    public Task MarkDirtyAsync(string lease, bool dirty, CancellationToken cancellationToken)
    {
        DirtyAttempts++;
        // The module lets only the mounted board's own lease write its dirty state, so a late update from a
        // superseded mounting is ignored rather than overwriting the guard the board on screen owns.
        if (!GuardAttached || !string.Equals(GuardLease, lease, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (FailDirtyWithCancellation)
        {
            throw new OperationCanceledException("The dirty-state write was cancelled by teardown.");
        }

        Dirty = dirty;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DetachDepartureGuardAsync(string lease, CancellationToken cancellationToken)
    {
        GuardDetachCount++;
        // The module detaches only the guard that owns the lease, so a stale mounting's teardown cannot take a
        // newer mount's guard with it.
        if (GuardAttached && string.Equals(GuardLease, lease, StringComparison.Ordinal))
        {
            GuardAttached = false;
            GuardLease = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>Gets the number of departure-guard detach calls, which tell a mounting's teardown apart from a missing attach.</summary>
    public int GuardDetachCount { get; private set; }

    /// <inheritdoc />
    public Task FocusFirstFieldAsync(ElementReference root, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Gets the region selectors focus was requested for, in order.</summary>
    public List<string> FocusRegions { get; } = [];

    /// <inheritdoc />
    public Task FocusRegionAsync(ElementReference root, string selector, CancellationToken cancellationToken)
    {
        FocusRegions.Add(selector);
        return Task.CompletedTask;
    }

    private static string OwnerKey(long actorUserId, long clubId) => $"{actorUserId}:{clubId}";
}
