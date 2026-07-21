using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Players;

/// <summary>
/// Applies tenant-safe player lifecycle transitions with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for lifecycle mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for lifecycle outcomes.</param>
public sealed partial class PlayerLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<PlayerLifecycleService> logger)
{
    /// <summary>
    /// Archives a player after confirming no active campaign participation remains undecided.
    /// </summary>
    /// <param name="playerId">The player identifier to archive.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ArchiveAsync(
        long playerId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(playerId, LifecycleStatus.Archived, cancellationToken);

    /// <summary>
    /// Restores an archived player to active use and clears archive provenance.
    /// </summary>
    /// <param name="playerId">The player identifier to restore.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> RestoreAsync(
        long playerId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(playerId, LifecycleStatus.Active, cancellationToken);

    /// <summary>
    /// Applies the requested lifecycle status after authorization and integrity checks.
    /// </summary>
    /// <param name="playerId">The player identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> TransitionAsync(
        long playerId,
        LifecycleStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogPlayerLifecycleForbidden(playerId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to change player lifecycle state.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquirePlayerMutationLockAsync(playerId, cancellationToken);
        var player = await db.Players
            .SingleOrDefaultAsync(candidate => candidate.PlayerId == playerId, cancellationToken);

        if (player is null || player.ClubId != clubId)
        {
            LogPlayerNotFound(playerId, clubId);
            return new NotFound();
        }

        if (player.LifecycleStatus == targetStatus)
        {
            LogPlayerLifecycleConflict(playerId, targetStatus);
            return new LifecycleConflict($"The player is already {targetStatus.ToString().ToLowerInvariant()}.");
        }

        if (targetStatus == LifecycleStatus.Archived)
        {
            var hasUndecidedActiveParticipation = await db.PlayerCampaignAssignments
                .AnyAsync(
                    assignment => assignment.PlayerId == playerId
                        && assignment.Campaign.EndDate == null
                        && assignment.PlacementOutcome == PlacementOutcome.Undecided,
                    cancellationToken);

            if (hasUndecidedActiveParticipation)
            {
                LogPlayerArchiveBlocked(playerId);
                return new LifecycleConflict(
                    "Resolve every undecided active-campaign participation before archiving the player.");
            }

            player.LifecycleStatus = LifecycleStatus.Archived;
            player.ArchivedAt = DateTimeOffset.UtcNow;
            player.ArchivedById = actorUserId;
        }
        else
        {
            player.LifecycleStatus = LifecycleStatus.Active;
            player.ArchivedAt = null;
            player.ArchivedById = null;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogPlayerLifecycleConcurrencyConflict(playerId);
            return new LifecycleConflict("The player's lifecycle changed. Reload it and try again.");
        }

        LogPlayerLifecycleChanged(playerId, targetStatus, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Logs a lifecycle request rejected because the caller is not a club administrator.
    /// </summary>
    /// <param name="playerId">The requested player identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player lifecycle mutation forbidden for PlayerId={PlayerId} by UserId={UserId}.")]
    private partial void LogPlayerLifecycleForbidden(long playerId, long userId);

    /// <summary>
    /// Logs a lifecycle request whose player is unavailable in the current tenant.
    /// </summary>
    /// <param name="playerId">The requested player identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "PlayerId={PlayerId} was not found for ClubId={ClubId}.")]
    private partial void LogPlayerNotFound(long playerId, long clubId);

    /// <summary>
    /// Logs a redundant player lifecycle transition.
    /// </summary>
    /// <param name="playerId">The requested player identifier.</param>
    /// <param name="status">The already-current lifecycle status.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "PlayerId={PlayerId} is already in lifecycle status {Status}.")]
    private partial void LogPlayerLifecycleConflict(long playerId, LifecycleStatus status);

    /// <summary>
    /// Logs a player archive blocked by unresolved active-campaign participation.
    /// </summary>
    /// <param name="playerId">The blocked player identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player archive blocked by undecided active-campaign participation for PlayerId={PlayerId}.")]
    private partial void LogPlayerArchiveBlocked(long playerId);

    /// <summary>
    /// Logs a lifecycle transition rejected because the player changed concurrently.
    /// </summary>
    /// <param name="playerId">The concurrently changed player identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player lifecycle concurrency conflict for PlayerId={PlayerId}.")]
    private partial void LogPlayerLifecycleConcurrencyConflict(long playerId);

    /// <summary>
    /// Logs a successful player lifecycle transition.
    /// </summary>
    /// <param name="playerId">The changed player identifier.</param>
    /// <param name="status">The applied lifecycle status.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerId={PlayerId} lifecycle changed to {Status} by UserId={ActorUserId}.")]
    private partial void LogPlayerLifecycleChanged(long playerId, LifecycleStatus status, long actorUserId);
}
