using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Players.Services;

/// <summary>
/// The manual intake board's browser boundary: durable owner-scoped creation recovery, the
/// uncommitted-departure guard, and the focus transitions its states require.
/// </summary>
/// <remarks>
/// Storage failures throw. A caller must never dispatch a command whose write failed, because an
/// unpersisted commit cannot be recovered after a reload.
/// </remarks>
public interface IPlayerIntakeInterop
{
    /// <summary>Reads the owner's retained command without dispatching or mutating it.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <returns>Empty, a dispatchable command, or the exact unreadable bytes.</returns>
    Task<PlayerCreationRecoveryRead> ReadAsync(long actorUserId, long clubId, CancellationToken cancellationToken);

    /// <summary>Persists the exact command before it is dispatched.</summary>
    /// <param name="pending">The command to retain.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    Task WriteAsync(PendingPlayerCreation pending, CancellationToken cancellationToken);

    /// <summary>Clears storage only when it still holds this operation; never clears another command.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <param name="operationId">The settled operation identity.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <returns><see langword="true"/> when the matching record was removed.</returns>
    Task<bool> ClearAsync(long actorUserId, long clubId, Guid operationId, CancellationToken cancellationToken);

    /// <summary>Discards retained bytes only when they still match the exact value the member reviewed.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <param name="expectedValue">The exact bytes inspected in the unreadable state.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <returns><see langword="true"/> when the reviewed bytes were removed.</returns>
    Task<bool> DiscardInvalidAsync(long actorUserId, long clubId, string expectedValue, CancellationToken cancellationToken);

    /// <summary>Registers the uncommitted-departure guard on the mounted board.</summary>
    /// <param name="root">The board's element reference.</param>
    /// <param name="receiver">The component's object reference notified before a protected departure.</param>
    /// <param name="lease">The ownership lease identifying this mounting.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    Task AttachDepartureGuardAsync(ElementReference root, object receiver, string lease, CancellationToken cancellationToken);

    /// <summary>Marks whether the mounted board currently holds uncommitted input.</summary>
    /// <param name="lease">The ownership lease identifying the mounting.</param>
    /// <param name="dirty">Whether input would be lost by leaving.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    Task MarkDirtyAsync(string lease, bool dirty, CancellationToken cancellationToken);

    /// <summary>Removes the departure guard, aborting its listeners.</summary>
    /// <param name="lease">The ownership lease identifying the mounting.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    Task DetachDepartureGuardAsync(string lease, CancellationToken cancellationToken);

    /// <summary>Moves focus to the board's first editable control.</summary>
    /// <param name="root">The board's element reference.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    Task FocusFirstFieldAsync(ElementReference root, CancellationToken cancellationToken);

    /// <summary>Moves focus to a named region inside the board.</summary>
    /// <param name="root">The board's element reference.</param>
    /// <param name="selector">The region selector to focus.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    Task FocusRegionAsync(ElementReference root, string selector, CancellationToken cancellationToken);
}
