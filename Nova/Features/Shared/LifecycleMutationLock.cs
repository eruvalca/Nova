using Microsoft.EntityFrameworkCore;

namespace Nova.Features.Shared;

/// <summary>
/// Serializes lifecycle-sensitive PostgreSQL mutations that span multiple tables.
/// </summary>
internal static class LifecycleMutationLock
{
    extension(DbContext db)
    {
        /// <summary>
        /// Acquires a transaction-scoped lock for mutations involving one player.
        /// </summary>
        /// <param name="playerId">The player identifier whose lifecycle-sensitive mutations must be serialized.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquirePlayerMutationLockAsync(long playerId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, playerId, cancellationToken);

        /// <summary>
        /// Acquires a transaction-scoped lock for mutations involving one team.
        /// </summary>
        /// <param name="teamId">The team identifier whose lifecycle-sensitive mutations must be serialized.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireTeamMutationLockAsync(long teamId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, -teamId, cancellationToken);

        /// <summary>
        /// Acquires a transaction-scoped lock for mutations involving one campaign.
        /// </summary>
        /// <param name="campaignId">The campaign identifier whose lifecycle-sensitive mutations must be serialized.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireCampaignMutationLockAsync(long campaignId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, long.MinValue + campaignId, cancellationToken);

        /// <summary>
        /// Acquires a transaction-scoped lock for mutations involving one tag definition.
        /// </summary>
        /// <param name="tagDefinitionId">The tag-definition identifier whose lifecycle-sensitive mutations must be serialized.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireTagMutationLockAsync(long tagDefinitionId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, (long.MinValue / 2) + tagDefinitionId, cancellationToken);
    }

    /// <summary>
    /// Acquires a PostgreSQL advisory transaction lock and remains a no-op for the SQLite unit harness.
    /// </summary>
    /// <param name="db">The context participating in the current database transaction.</param>
    /// <param name="lockKey">The signed key separating player and team lock namespaces.</param>
    /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
    /// <returns>A task representing lock acquisition.</returns>
    private static async Task AcquirePostgresLockAsync(
        DbContext db,
        long lockKey,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey})",
                cancellationToken);
        }
    }
}
