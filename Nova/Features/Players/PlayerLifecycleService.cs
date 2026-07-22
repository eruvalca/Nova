using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
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
    ILogger<PlayerLifecycleService> logger) : IPlayerLifecycleService
{
    /// <summary>
    /// Archives a player after confirming no active campaign participation remains undecided.
    /// </summary>
    /// <param name="playerId">The player identifier to archive.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>A boundary-safe service result with success or ProblemDetails-mappable failure.</returns>
    public async Task<ServiceResult<Success>> ArchiveAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(playerId, LifecycleStatus.Archived, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            blocked => ServiceProblem.Conflict(
                "Resolve every undecided active-campaign participation before archiving the player.",
                PlayerLifecycleProblemExtensions.CreateArchiveBlockerExtensions(blocked.Blockers)));
    }

    /// <summary>
    /// Restores an archived player to active use and clears archive provenance.
    /// </summary>
    /// <param name="playerId">The player identifier to restore.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>A boundary-safe service result with success or ProblemDetails-mappable failure.</returns>
    public async Task<ServiceResult<Success>> RestoreAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(playerId, LifecycleStatus.Active, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            blocked => ServiceProblem.Conflict(
                "Resolve every undecided active-campaign participation before archiving the player.",
                PlayerLifecycleProblemExtensions.CreateArchiveBlockerExtensions(blocked.Blockers)));
    }

    /// <summary>
    /// Applies the requested lifecycle status after authorization and integrity checks.
    /// </summary>
    /// <param name="playerId">The player identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Internal lifecycle outcomes before boundary mapping to shared service contracts.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, PlayerArchiveBlockedConflict>> TransitionAsync(
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

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, PlayerArchiveBlockedConflict>>(async () =>
        {
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
                var blockingParticipations = await db.PlayerCampaignAssignments
                    .Where(
                        assignment => assignment.PlayerId == playerId
                            && assignment.Campaign.Status == CampaignStatus.Active
                            && assignment.PlacementOutcome == PlacementOutcome.Undecided)
                    .Select(assignment => new
                    {
                        assignment.CampaignId,
                        CampaignName = assignment.Campaign.Name,
                        assignment.PlayerCampaignAssignmentId
                    })
                    .ToListAsync(cancellationToken);

                if (blockingParticipations.Count > 0)
                {
                    var blockers = blockingParticipations
                        .GroupBy(
                            entry => new { entry.CampaignId, entry.CampaignName },
                            entry => entry.PlayerCampaignAssignmentId)
                        .Select(group => new PlayerArchiveBlocker
                        {
                            CampaignId = group.Key.CampaignId,
                            CampaignName = group.Key.CampaignName,
                            ParticipationIds = group.OrderBy(id => id).ToList().AsReadOnly()
                        })
                        .OrderBy(blocker => blocker.CampaignId)
                        .ToList()
                        .AsReadOnly();

                    LogPlayerArchiveBlocked(playerId, blockers.Count);
                    return new PlayerArchiveBlockedConflict(blockers);
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
        });
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
    /// <param name="campaignCount">The number of active campaigns currently blocking archive.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player archive blocked by undecided active-campaign participation for PlayerId={PlayerId} across CampaignCount={CampaignCount}.")]
    private partial void LogPlayerArchiveBlocked(long playerId, int campaignCount);

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

    /// <summary>
    /// Represents an archive conflict with structured active-campaign participation blockers.
    /// </summary>
    /// <param name="Blockers">The grouped blocker details loaded under the lifecycle lock.</param>
    private readonly record struct PlayerArchiveBlockedConflict(IReadOnlyList<PlayerArchiveBlocker> Blockers);
}
