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
        /// Acquires a transaction-scoped club-season lock that serializes creation, advancement,
        /// metadata correction, and campaign operations that depend on current-season identity.
        /// Acquire this lock before the club-roster lock and before entity-specific lifecycle locks.
        /// </summary>
        /// <param name="clubId">The club identifier whose current-season state is being guarded.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireClubSeasonLockAsync(long clubId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, (long.MinValue / 16) + clubId, cancellationToken);

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

        /// <summary>
        /// Acquires a transaction-scoped lock for terminal mutations of one join request (approve,
        /// reject, or cancel), serializing concurrent transitions so the loser observes the winner's
        /// committed status and returns a conflict instead of appending contradictory evidence.
        /// </summary>
        /// <param name="joinRequestId">The join-request identifier whose terminal transition must be serialized.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireJoinRequestLockAsync(long joinRequestId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, (long.MinValue / 8) + joinRequestId, cancellationToken);

        /// <summary>
        /// Acquires a transaction-scoped club-roster lock that serializes player creation and profile
        /// identity updates within the same club, guarding enrollment and import duplicate checks.
        /// Campaign creation acquires this after the club-season lock; profile updates acquire it
        /// before the player lock.
        /// </summary>
        /// <param name="clubId">The club identifier whose roster mutations must be serialized.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireClubRosterLockAsync(long clubId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, (long.MinValue / 4) + clubId, cancellationToken);

        /// <summary>
        /// Acquires a transaction-scoped lock for club membership and ClubAdmin role mutations.
        /// Acquire required user-membership locks before this lock, then acquire request-specific
        /// locks after it.
        /// </summary>
        /// <param name="clubId">The club identifier whose membership set is being guarded.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireClubMembershipLockAsync(long clubId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, (long.MinValue / 32) + clubId, cancellationToken);

        /// <summary>
        /// Acquires a transaction-scoped lock for mutations that can change one user's club
        /// membership. Acquire all required user locks in ascending user-id order before any
        /// club-membership or request-specific lock.
        /// </summary>
        /// <param name="userId">The user identifier whose club membership is being guarded.</param>
        /// <param name="cancellationToken">A token that cancels lock acquisition.</param>
        /// <returns>A task representing lock acquisition.</returns>
        public Task AcquireUserMembershipLockAsync(long userId, CancellationToken cancellationToken)
            => AcquirePostgresLockAsync(db, (long.MinValue / 64) + userId, cancellationToken);
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
