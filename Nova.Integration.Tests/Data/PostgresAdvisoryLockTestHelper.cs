using Microsoft.EntityFrameworkCore;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Shared polling helpers for deterministic PostgreSQL advisory-lock race tests.
/// </summary>
internal static class PostgresAdvisoryLockTestHelper
{
    /// <summary>
    /// Waits until PostgreSQL reports a session blocked on the specific advisory lock key held by
    /// the calling test's transaction. Scoped to the key (not just "any advisory lock waiter") so
    /// that concurrently running tests — the suite runs <c>ParallelMode.All</c> — cannot satisfy
    /// the poll for each other and mask a lock-waiting regression.
    /// </summary>
    /// <param name="db">The context holding the transaction-scoped advisory lock.</param>
    /// <param name="lockKey">The 64-bit advisory lock key the test holds and its mutation must wait on.</param>
    /// <param name="cancellationToken">A token that cancels polling.</param>
    /// <returns>A task representing the polling operation.</returns>
    public static async Task WaitForAdvisoryLockWaiterAsync(
        DbContext db,
        long lockKey,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var hasWaiter = await db.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_locks
                        WHERE locktype = 'advisory'
                          AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                          AND classid::int8 = (({0}::bigint >> 32) & 4294967295)
                          AND objid::int8 = ({0}::bigint & 4294967295)
                          AND NOT granted
                    ) AS "Value"
                    """,
                    lockKey)
                .SingleAsync(cancellationToken);
            if (hasWaiter)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        throw new TimeoutException("The mutation did not wait for the advisory lock.");
    }
}
