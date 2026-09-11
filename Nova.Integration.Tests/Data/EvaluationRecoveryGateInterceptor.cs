using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nova.Integration.Tests.Data;

/// <summary>Pauses ambiguous recovery before it acquires authorization locks, allowing a later committed operation.</summary>
internal sealed class EvaluationRecoveryGateInterceptor(FailFirstCommittedTransactionInterceptor failure) : DbCommandInterceptor
{
    private readonly TaskCompletionSource _attempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _gated;

    public Task WaitForVerificationAttemptAsync(CancellationToken token) => _attempted.Task.WaitAsync(token);
    public void Release() => _release.TrySetResult();

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (failure.FailureCount > 0 && command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)
            && Interlocked.Exchange(ref _gated, 1) == 0)
        {
            _attempted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}
