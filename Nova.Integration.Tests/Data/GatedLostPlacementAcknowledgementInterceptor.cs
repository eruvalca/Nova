using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Nova.Integration.Tests.Data;

/// <summary>Pauses after placement commits and before its lost acknowledgement starts receipt recovery.</summary>
internal sealed class GatedLostPlacementAcknowledgementInterceptor : DbTransactionInterceptor
{
    private readonly TaskCompletionSource _committed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _failureCount;

    /// <summary>Gets the number of acknowledgements deliberately lost.</summary>
    public int FailureCount => Volatile.Read(ref _failureCount);

    /// <summary>Waits for the durable commit, after PostgreSQL has released mutation locks.</summary>
    public Task WaitForCommitAsync(CancellationToken token) => _committed.Task.WaitAsync(token);

    /// <summary>Allows receipt recovery to begin against the intervening state.</summary>
    public void Release() => _release.TrySetResult();

    /// <inheritdoc />
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _failureCount, 1, 0) != 0) { return; }
        _committed.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);
        throw new NpgsqlException("Simulated lost placement acknowledgement after a durable commit.", new TimeoutException());
    }
}
