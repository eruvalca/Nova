using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Validation;

namespace Nova.UI.Features.Players.Services;

/// <summary>
/// The exact one logical manual creation retained for one actor in one club: the caller, the
/// immutable command payload, and the deadline the server will honour for it.
/// </summary>
public sealed record PendingPlayerCreation
{
    /// <summary>The authenticated member the command belongs to.</summary>
    public required long ActorUserId { get; init; }

    /// <summary>The exclusive replay deadline: the operation's own creation time plus its fixed lifetime.</summary>
    public required DateTimeOffset RecoveryExpiresAt { get; init; }

    /// <summary>The exact command, including its UUIDv7 operation identity and original club.</summary>
    public required CreatePlayerInput Payload { get; init; }

    /// <summary>Serializes this command as the exact bytes retained in owner-scoped storage.</summary>
    /// <returns>The retained JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
}

/// <summary>What owner-scoped storage holds right now.</summary>
public enum PlayerCreationRecoveryKind
{
    /// <summary>No retained command exists for this owner.</summary>
    Empty,

    /// <summary>A structurally valid, dispatchable command was retained.</summary>
    Pending,

    /// <summary>The retained bytes cannot be dispatched and are preserved for an explicit discard.</summary>
    Unreadable
}

/// <summary>A tab-independent read of the owner's retained command, or its exact unreadable bytes.</summary>
/// <param name="Kind">Which of the three outcomes storage reported.</param>
/// <param name="Pending">The dispatchable command, when <paramref name="Kind"/> is <see cref="PlayerCreationRecoveryKind.Pending"/>.</param>
/// <param name="InvalidValue">The exact retained bytes, when <paramref name="Kind"/> is <see cref="PlayerCreationRecoveryKind.Unreadable"/>.</param>
public sealed record PlayerCreationRecoveryRead(
    PlayerCreationRecoveryKind Kind,
    PendingPlayerCreation? Pending,
    string? InvalidValue);

/// <summary>
/// The JavaScript module's raw read result: the exact retained bytes, or the exact unreadable bytes.
/// </summary>
/// <param name="Json">The retained command's exact bytes, when they are structurally valid.</param>
/// <param name="InvalidValue">The exact bytes that cannot be dispatched.</param>
internal sealed record StoredPlayerCreationRecovery(string? Json, string? InvalidValue);

/// <summary>
/// Wraps the collocated <c>PlayerIntakeBoard.razor.js</c> module so the manual intake board can
/// persist, read, clear and deliberately discard one pending creation command.
/// </summary>
/// <remarks>
/// Storage failures throw. A caller must not dispatch a command whose write failed: an unpersisted
/// commit cannot be recovered after a reload. Every read re-validates the typed payload rather than
/// trusting the retained bytes.
/// </remarks>
/// <param name="js">The JavaScript runtime used to import the collocated module.</param>
internal sealed class PlayerCreationRecoveryStore(IJSRuntime js) : IPlayerIntakeInterop, IAsyncDisposable
{
    private const string ModulePath = "./_content/Nova.UI/Features/Players/Components/PlayerIntakeBoard.razor.js";

    private readonly Lazy<Task<IJSObjectReference>> _module =
        new(() => js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask());

    /// <summary>Reads the owner's retained command without dispatching or mutating it.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <returns>Empty, a dispatchable command, or the exact unreadable bytes.</returns>
    /// <exception cref="JSException">Storage could not be read.</exception>
    public async Task<PlayerCreationRecoveryRead> ReadAsync(long actorUserId, long clubId, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        var stored = await module.InvokeAsync<StoredPlayerCreationRecovery?>("readRecovery", cancellationToken, actorUserId, clubId)
            ?? throw new InvalidOperationException("Player creation recovery storage returned no state.");

        if (stored.Json is not { } json)
        {
            return stored.InvalidValue is { } invalid
                ? new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Unreadable, null, invalid)
                : new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Empty, null, null);
        }

        return TryDeserialize(json, out var pending)
            ? new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Pending, pending, null)
            // Structurally valid JSON that contradicts its own typed contract is still unusable evidence.
            : new PlayerCreationRecoveryRead(PlayerCreationRecoveryKind.Unreadable, null, json);
    }

    /// <summary>Persists the exact command before it is dispatched.</summary>
    /// <param name="pending">The command to retain.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <exception cref="JSException">Storage refused or could not persist the command.</exception>
    public async Task WriteAsync(PendingPlayerCreation pending, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var module = await LoadAsync(cancellationToken);
        _ = await module.InvokeAsync<string?>(
            "writePending",
            cancellationToken,
            pending.ActorUserId,
            pending.Payload.ClubId,
            pending.ToJson());
    }

    /// <summary>Clears storage only when it still holds this operation; never clears another command.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <param name="operationId">The settled operation identity.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <returns><see langword="true"/> when the matching record was removed.</returns>
    /// <exception cref="JSException">Storage could not be cleared.</exception>
    public async Task<bool> ClearAsync(long actorUserId, long clubId, Guid operationId, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        return await module.InvokeAsync<bool>("clearPending", cancellationToken, actorUserId, clubId, operationId.ToString("D"));
    }

    /// <summary>Discards retained bytes only when they still match the exact value the user reviewed.</summary>
    /// <param name="actorUserId">The authenticated member owning the command.</param>
    /// <param name="clubId">The club owning the command.</param>
    /// <param name="expectedValue">The exact bytes inspected in the unreadable state.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <returns><see langword="true"/> when the reviewed bytes were removed.</returns>
    /// <exception cref="JSException">Storage could not be cleared.</exception>
    public async Task<bool> DiscardInvalidAsync(long actorUserId, long clubId, string expectedValue, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        return await module.InvokeAsync<bool>("discardInvalidPending", cancellationToken, actorUserId, clubId, expectedValue);
    }

    /// <inheritdoc />
    public async Task AttachDepartureGuardAsync(ElementReference root, object receiver, string lease, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        await module.InvokeVoidAsync("attachDepartureGuard", cancellationToken, root, receiver, lease);
    }

    /// <summary>Marks whether the mounted board currently holds uncommitted input.</summary>
    /// <param name="dirty">Whether input would be lost by leaving.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    /// <exception cref="JSException">The guard state could not be updated.</exception>
    public async Task MarkDirtyAsync(string lease, bool dirty, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        await module.InvokeVoidAsync("markDirty", cancellationToken, lease, dirty);
    }

    /// <summary>Removes the departure guard, aborting its listeners.</summary>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    public async Task DetachDepartureGuardAsync(string lease, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        await module.InvokeVoidAsync("detachDepartureGuard", cancellationToken, lease);
    }

    /// <summary>Moves focus to the board's first editable control.</summary>
    /// <param name="root">The board's element reference.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    public async Task FocusFirstFieldAsync(ElementReference root, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        await module.InvokeVoidAsync("focusFirstField", cancellationToken, root);
    }

    /// <summary>Moves focus to a named region inside the board.</summary>
    /// <param name="root">The board's element reference.</param>
    /// <param name="selector">The region selector to focus.</param>
    /// <param name="cancellationToken">A token that cancels the interop call.</param>
    public async Task FocusRegionAsync(ElementReference root, string selector, CancellationToken cancellationToken)
    {
        var module = await LoadAsync(cancellationToken);
        await module.InvokeVoidAsync("focusRegion", cancellationToken, root, selector);
    }

    /// <summary>Requires the retained JSON to satisfy the same contract as a dispatched command.</summary>
    private static bool TryDeserialize(string json, out PendingPlayerCreation? pending)
    {
        pending = null;
        try
        {
            var candidate = JsonSerializer.Deserialize<PendingPlayerCreation>(json, JsonSerializerOptions.Web);
            if (candidate is null || candidate.ActorUserId <= 0 || candidate.Payload is null
                || InputValidator.Validate(candidate.Payload).Count > 0
                || !PlayerCreationOperation.TryGetCreatedAt(candidate.Payload.OperationId, out var createdAt)
                || candidate.RecoveryExpiresAt != createdAt.Add(PlayerCreationOperation.Lifetime))
            {
                return false;
            }

            pending = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<IJSObjectReference> LoadAsync(CancellationToken cancellationToken)
    {
        var load = _module.Value;
        return await load.WaitAsync(cancellationToken)
            ?? throw new InvalidOperationException("The player intake board module could not be imported.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_module.IsValueCreated)
        {
            return;
        }

        try
        {
            var module = await _module.Value;
            await module.DisposeAsync();
        }
        catch (JSException)
        {
            // A disposed browser context cannot release the module; nothing is left to clean up.
        }
        catch (OperationCanceledException)
        {
            // The circuit ended before the module load finished; nothing is left to clean up.
        }
    }
}
