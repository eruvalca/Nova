using Microsoft.EntityFrameworkCore;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Shared polling helpers for deterministic PostgreSQL advisory-lock race tests.
/// </summary>
internal static class PostgresAdvisoryLockTestHelper
{
    /// <summary>
    /// Waits until PostgreSQL reports a session blocked on an advisory lock.
    /// </summary>
    /// <param name="db">The context holding the transaction-scoped advisory lock.</param>
    /// <param name="cancellationToken">A token that cancels polling.</param>
    /// <returns>A task representing the polling operation.</returns>
    public static async Task WaitForAdvisoryLockWaiterAsync(
        DbContext db,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var hasWaiter = await db.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity
                        WHERE wait_event_type = 'Lock'
                          AND wait_event = 'advisory'
                    ) AS "Value"
                    """)
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
