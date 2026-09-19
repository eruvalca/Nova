using Microsoft.JSInterop;
using Nova.UI.Features.Players.Services;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// The browser storage boundary must survive a transient collocated-module import failure instead
/// of caching that failure for the life of the circuit.
/// </summary>
public sealed class PlayerCreationRecoveryStoreTests
{
    /// <summary>A read after a transient import failure succeeds, so a retry re-imports the module.</summary>
    [Fact]
    public async Task RecoveryStoreReimportsTheModuleAfterATransientImportFailureAsync()
    {
        await using var interop = new RecoveryStoreInterop();
        await using var store = new PlayerCreationRecoveryStore(interop);

        await Should.ThrowAsync<JSException>(
            () => store.ReadAsync(101, 42, TestContext.Current.CancellationToken));

        var read = await store.ReadAsync(101, 42, TestContext.Current.CancellationToken);

        read.Kind.ShouldBe(PlayerCreationRecoveryKind.Empty);
        interop.ImportCalls.ShouldBe(2);
    }
}

/// <summary>
/// The collocated module's import boundary as the browser reports it: the first import fails the
/// way a transient fetch failure does, and every later import is served.
/// </summary>
internal sealed class RecoveryStoreInterop : IJSRuntime, IJSObjectReference
{
    private readonly StoredPlayerCreationRecovery _nothingRetained = new(null, null);

    /// <summary>Gets the number of times the module was imported.</summary>
    public int ImportCalls { get; private set; }

    /// <summary>Gets or sets whether the next import attempt fails.</summary>
    public bool FailNextImport { get; set; } = true;

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, default, args);

    /// <inheritdoc />
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (string.Equals(identifier, "import", StringComparison.Ordinal))
        {
            ImportCalls++;
            if (FailNextImport)
            {
                FailNextImport = false;
                return ValueTask.FromException<TValue>(new JSException("The module could not be fetched."));
            }

            return ValueTask.FromResult((TValue)(object)this);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Every other call is the module's own owner-scoped read, which reports nothing retained.
        return ValueTask.FromResult((TValue)(object)_nothingRetained);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
