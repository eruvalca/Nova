using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Features.Teams;
using Nova.Shared.Validation;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Teams;

/// <summary>
/// Applies tenant-safe team lifecycle mutations with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for team mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class TeamLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TeamLifecycleService> logger) : ITeamLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ArchiveAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(teamId, LifecycleStatus.Archived, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            blocked => ServiceProblem.Conflict(
                "Resolve every active-campaign placement before archiving the team.",
                TeamLifecycleProblemExtensions.CreateArchiveBlockerExtensions(blocked.Blockers)));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RestoreAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(teamId, LifecycleStatus.Active, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            blocked => MapUnexpectedRestoreBlocked(teamId, blocked));
    }

    /// <summary>
    /// Applies the requested team lifecycle status after authorization and integrity checks.
    /// </summary>
    /// <param name="teamId">The team identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Internal lifecycle outcomes before boundary mapping to shared service contracts.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>> TransitionAsync(
        long teamId,
        LifecycleStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTeamLifecycleForbidden(teamId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to change team lifecycle state.");
        }

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        // Records whether the most recent attempt reached CommitAsync. Verification is only
        // meaningful for that attempt: a transient failure raised before the commit cannot have
        // applied the transition, so the observed status belongs to some earlier request and must
        // not be mistaken for this one's ambiguous commit.
        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (TeamId: teamId, TargetStatus: targetStatus, ActorUserId: actorUserId, ClubId: clubId, CommitAttempted: commitAttempted),
            async (state, token) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await ApplyTransitionAsync(
                    db,
                    state.TeamId,
                    state.TargetStatus,
                    state.ActorUserId,
                    state.ClubId,
                    state.CommitAttempted,
                    token);
            },
            async (state, token) =>
            {
                if (!state.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>>(
                        successful: false,
                        default!);
                }

                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await VerifyTransitionCommittedAsync(db, state.TeamId, state.TargetStatus, state.ClubId, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Tracks whether a lifecycle attempt reached its commit, scoping ambiguous-commit verification
    /// to attempts that could actually have applied the transition.
    /// </summary>
    private sealed class CommitAttemptTracker
    {
        private int _attempted;

        /// <summary>Gets a value indicating whether the current attempt reached its commit.</summary>
        public bool Attempted => Volatile.Read(ref _attempted) == 1;

        /// <summary>Clears the flag at the start of an execution-strategy attempt.</summary>
        public void Reset() => Volatile.Write(ref _attempted, 0);

        /// <summary>Marks that the current attempt is about to commit.</summary>
        public void MarkAttempted() => Volatile.Write(ref _attempted, 1);
    }

    /// <summary>
    /// Determines whether an ambiguous commit already applied the requested transition so the
    /// execution strategy can report success instead of replaying the attempt.
    /// </summary>
    /// <remarks>
    /// The execution strategy invokes this only after a transient failure interrupted an attempt
    /// that had already reached its commit, so finding the target status applied means this
    /// operation's commit reached the database. Without it a replay would observe the applied status
    /// and return a spurious conflict to a caller whose archive or restore actually succeeded.
    /// </remarks>
    /// <param name="db">The fresh tenant context used for verification.</param>
    /// <param name="teamId">The team identifier that was being mutated.</param>
    /// <param name="targetStatus">The lifecycle status the interrupted attempt was applying.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>A successful result when the transition is already persisted; otherwise unsuccessful.</returns>
    private async Task<ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>>> VerifyTransitionCommittedAsync(
        NovaDbContext db,
        long teamId,
        LifecycleStatus targetStatus,
        long clubId,
        CancellationToken cancellationToken)
    {
        var appliedStatus = await db.Teams
            .Where(candidate => candidate.TeamId == teamId && candidate.ClubId == clubId)
            .Select(candidate => (LifecycleStatus?)candidate.LifecycleStatus)
            .SingleOrDefaultAsync(cancellationToken);

        if (appliedStatus == targetStatus)
        {
            LogTeamTransitionCommitVerified(teamId, targetStatus);
            return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>>(
                successful: true,
                new Success());
        }

        return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>>(
            successful: false,
            default!);
    }

    /// <summary>
    /// Applies one lifecycle transition attempt inside a single transaction using a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="teamId">The team identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Internal lifecycle outcomes before boundary mapping to shared service contracts.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>> ApplyTransitionAsync(
        NovaDbContext db,
        long teamId,
        LifecycleStatus targetStatus,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireTeamMutationLockAsync(teamId, cancellationToken);
        var team = await db.Teams
            .SingleOrDefaultAsync(candidate => candidate.TeamId == teamId, cancellationToken);

        if (team is null || team.ClubId != clubId)
        {
            LogTeamNotFound(teamId, clubId);
            return new NotFound();
        }

        if (team.LifecycleStatus == targetStatus)
        {
            LogTeamLifecycleConflict(teamId, targetStatus);
            return new LifecycleConflict($"The team is already {targetStatus.ToString().ToLowerInvariant()}.");
        }

        if (targetStatus == LifecycleStatus.Archived)
        {
            var activePlacements = await db.PlayerCampaignAssignments
                .Where(
                    assignment => assignment.TeamId == teamId
                        && assignment.Campaign.Status == CampaignStatus.Active)
                .Select(
                    assignment => new
                    {
                        assignment.CampaignId,
                        CampaignName = assignment.Campaign.Name,
                        assignment.PlayerCampaignAssignmentId
                    })
                .ToListAsync(cancellationToken);

            if (activePlacements.Count > 0)
            {
                var blockers = activePlacements
                    .GroupBy(
                        placement => new { placement.CampaignId, placement.CampaignName },
                        placement => placement.PlayerCampaignAssignmentId)
                    .Select(
                        group => new TeamArchiveBlocker
                        {
                            CampaignId = group.Key.CampaignId,
                            CampaignName = group.Key.CampaignName,
                            PlacementIds = group.OrderBy(id => id).ToList().AsReadOnly()
                        })
                    .OrderBy(blocker => blocker.CampaignId)
                    .ToList()
                    .AsReadOnly();

                LogTeamArchiveBlocked(teamId, blockers.Count);
                return new TeamArchiveBlockedConflict(blockers);
            }

            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = actorUserId;
        }
        else
        {
            team.LifecycleStatus = LifecycleStatus.Active;
            team.ArchivedAt = null;
            team.ArchivedById = null;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTeamMutationConcurrencyConflict(teamId);
            return new LifecycleConflict("The team's lifecycle changed. Reload it and try again.");
        }

        LogTeamLifecycleChanged(teamId, targetStatus, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Maps an invariant-violating archive blocker returned during restore to a server error.
    /// </summary>
    /// <param name="teamId">The team identifier being restored.</param>
    /// <param name="blocked">The unexpected archive blocker outcome.</param>
    /// <returns>A server error that does not expose archive-specific guidance for restore.</returns>
    private ServiceResult<Success> MapUnexpectedRestoreBlocked(
        long teamId,
        TeamArchiveBlockedConflict blocked)
    {
        LogUnexpectedTeamRestoreBlocked(teamId, blocked.Blockers.Count);
        return ServiceProblem.ServerError("The team could not be restored because of an unexpected lifecycle conflict.");
    }

    /// <summary>
    /// Logs a team mutation rejected because the caller is not a club administrator.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team mutation forbidden for TeamId={TeamId} by UserId={UserId}.")]
    private partial void LogTeamLifecycleForbidden(long teamId, long userId);

    /// <summary>
    /// Logs a team mutation whose team is unavailable in the current tenant.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "TeamId={TeamId} was not found for ClubId={ClubId}.")]
    private partial void LogTeamNotFound(long teamId, long clubId);

    /// <summary>
    /// Logs a team mutation that conflicts with its lifecycle status.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="status">The conflicting lifecycle status.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "TeamId={TeamId} mutation conflicts with lifecycle status {Status}.")]
    private partial void LogTeamLifecycleConflict(long teamId, LifecycleStatus status);

    /// <summary>
    /// Logs a team archive blocked by active-campaign placements.
    /// </summary>
    /// <param name="teamId">The blocked team identifier.</param>
    /// <param name="campaignCount">The number of active campaigns blocking archive.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team archive blocked by active-campaign placement for TeamId={TeamId} across CampaignCount={CampaignCount}.")]
    private partial void LogTeamArchiveBlocked(long teamId, int campaignCount);

    /// <summary>
    /// Logs an invariant violation where restore unexpectedly produced archive blockers.
    /// </summary>
    /// <param name="teamId">The team identifier being restored.</param>
    /// <param name="campaignCount">The number of unexpected blocking campaigns.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Team restore unexpectedly returned archive blockers for TeamId={TeamId} across CampaignCount={CampaignCount}.")]
    private partial void LogUnexpectedTeamRestoreBlocked(long teamId, int campaignCount);

    /// <summary>
    /// Logs a team mutation rejected because the team changed concurrently.
    /// </summary>
    /// <param name="teamId">The concurrently changed team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team mutation concurrency conflict for TeamId={TeamId}.")]
    private partial void LogTeamMutationConcurrencyConflict(long teamId);

    /// <summary>
    /// Logs a successful team lifecycle transition.
    /// </summary>
    /// <param name="teamId">The changed team identifier.</param>
    /// <param name="status">The applied lifecycle status.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} lifecycle changed to {Status} by UserId={ActorUserId}.")]
    private partial void LogTeamLifecycleChanged(long teamId, LifecycleStatus status, long actorUserId);

    /// <summary>
    /// Logs an ambiguous commit that verification confirmed had already applied the transition.
    /// </summary>
    /// <param name="teamId">The verified team identifier.</param>
    /// <param name="status">The lifecycle status found already applied.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} transition to {Status} was already committed before the transient failure; skipping replay.")]
    private partial void LogTeamTransitionCommitVerified(long teamId, LifecycleStatus status);

    /// <summary>
    /// Represents an archive conflict with structured active-campaign placement blockers.
    /// </summary>
    /// <param name="Blockers">The grouped blocker details loaded under the lifecycle lock.</param>
    private readonly record struct TeamArchiveBlockedConflict(IReadOnlyList<TeamArchiveBlocker> Blockers);
}
