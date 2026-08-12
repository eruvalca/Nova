using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Tags;

/// <summary>
/// Applies tenant-safe tag-definition lifecycle transitions with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for lifecycle mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for lifecycle outcomes.</param>
public sealed partial class TagDefinitionLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TagDefinitionLifecycleService> logger) : ITagDefinitionLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ArchiveAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(tagDefinitionId, LifecycleStatus.Archived, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RestoreAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(tagDefinitionId, LifecycleStatus.Active, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <summary>
    /// Applies the requested tag-definition lifecycle status after authorization checks.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> TransitionAsync(
        long tagDefinitionId,
        LifecycleStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTagLifecycleForbidden(tagDefinitionId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to change tag-definition lifecycle state.");
        }

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        // Records whether the most recent attempt reached CommitAsync. Verification is only
        // meaningful for that attempt: a transient failure raised before the commit cannot have
        // applied the transition, so the observed status belongs to some earlier request and must
        // not be mistaken for this one's ambiguous commit.
        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (TagDefinitionId: tagDefinitionId, TargetStatus: targetStatus, ActorUserId: actorUserId, ClubId: clubId, CommitAttempted: commitAttempted),
            async (state, token) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await ApplyTransitionAsync(
                    db,
                    state.TagDefinitionId,
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
                    return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>(
                        successful: false,
                        default!);
                }

                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await VerifyTransitionCommittedAsync(db, state.TagDefinitionId, state.TargetStatus, state.ClubId, token);
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
    /// <param name="tagDefinitionId">The tag-definition identifier that was being mutated.</param>
    /// <param name="targetStatus">The lifecycle status the interrupted attempt was applying.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>A successful result when the transition is already persisted; otherwise unsuccessful.</returns>
    private async Task<ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>> VerifyTransitionCommittedAsync(
        NovaDbContext db,
        long tagDefinitionId,
        LifecycleStatus targetStatus,
        long clubId,
        CancellationToken cancellationToken)
    {
        var appliedStatus = await db.PlayerTags
            .Where(candidate => candidate.PlayerTagId == tagDefinitionId && candidate.ClubId == clubId)
            .Select(candidate => (LifecycleStatus?)candidate.LifecycleStatus)
            .SingleOrDefaultAsync(cancellationToken);

        if (appliedStatus == targetStatus)
        {
            LogTagTransitionCommitVerified(tagDefinitionId, targetStatus);
            return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>(
                successful: true,
                new Success());
        }

        return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>(
            successful: false,
            default!);
    }

    /// <summary>
    /// Applies one lifecycle transition attempt inside a single transaction using a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="tagDefinitionId">The tag-definition identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Internal lifecycle outcomes before boundary mapping to shared service contracts.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ApplyTransitionAsync(
        NovaDbContext db,
        long tagDefinitionId,
        LifecycleStatus targetStatus,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireTagMutationLockAsync(tagDefinitionId, cancellationToken);
        var tagDefinition = await db.PlayerTags
            .SingleOrDefaultAsync(candidate => candidate.PlayerTagId == tagDefinitionId, cancellationToken);

        if (tagDefinition is null || tagDefinition.ClubId != clubId)
        {
            LogTagNotFound(tagDefinitionId, clubId);
            return new NotFound();
        }

        if (tagDefinition.LifecycleStatus == targetStatus)
        {
            LogTagLifecycleConflict(tagDefinitionId, targetStatus);
            return new LifecycleConflict(
                $"The tag definition is already {targetStatus.ToString().ToLowerInvariant()}.");
        }

        if (targetStatus == LifecycleStatus.Archived)
        {
            tagDefinition.LifecycleStatus = LifecycleStatus.Archived;
            tagDefinition.ArchivedAt = DateTimeOffset.UtcNow;
            tagDefinition.ArchivedById = actorUserId;
        }
        else
        {
            tagDefinition.LifecycleStatus = LifecycleStatus.Active;
            tagDefinition.ArchivedAt = null;
            tagDefinition.ArchivedById = null;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTagLifecycleConcurrencyConflict(tagDefinitionId);
            return new LifecycleConflict("The tag definition's lifecycle changed. Reload it and try again.");
        }

        LogTagLifecycleChanged(tagDefinitionId, targetStatus, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Logs a lifecycle request rejected because the caller is not a club administrator.
    /// </summary>
    /// <param name="tagDefinitionId">The requested tag-definition identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition lifecycle mutation forbidden for PlayerTagId={TagDefinitionId} by UserId={UserId}.")]
    private partial void LogTagLifecycleForbidden(long tagDefinitionId, long userId);

    /// <summary>
    /// Logs a lifecycle request whose tag definition is unavailable in the current tenant.
    /// </summary>
    /// <param name="tagDefinitionId">The requested tag-definition identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "PlayerTagId={TagDefinitionId} was not found for ClubId={ClubId}.")]
    private partial void LogTagNotFound(long tagDefinitionId, long clubId);

    /// <summary>
    /// Logs a redundant tag-definition lifecycle transition.
    /// </summary>
    /// <param name="tagDefinitionId">The requested tag-definition identifier.</param>
    /// <param name="status">The already-current lifecycle status.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "PlayerTagId={TagDefinitionId} is already in lifecycle status {Status}.")]
    private partial void LogTagLifecycleConflict(long tagDefinitionId, LifecycleStatus status);

    /// <summary>
    /// Logs a lifecycle transition rejected because the tag definition changed concurrently.
    /// </summary>
    /// <param name="tagDefinitionId">The concurrently changed tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition lifecycle concurrency conflict for PlayerTagId={TagDefinitionId}.")]
    private partial void LogTagLifecycleConcurrencyConflict(long tagDefinitionId);

    /// <summary>
    /// Logs a successful tag-definition lifecycle transition.
    /// </summary>
    /// <param name="tagDefinitionId">The changed tag-definition identifier.</param>
    /// <param name="status">The applied lifecycle status.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerTagId={TagDefinitionId} lifecycle changed to {Status} by UserId={ActorUserId}.")]
    private partial void LogTagLifecycleChanged(long tagDefinitionId, LifecycleStatus status, long actorUserId);

    /// <summary>
    /// Logs an ambiguous commit that verification confirmed had already applied the transition.
    /// </summary>
    /// <param name="tagDefinitionId">The verified tag-definition identifier.</param>
    /// <param name="status">The lifecycle status found already applied.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerTagId={TagDefinitionId} transition to {Status} was already committed before the transient failure; skipping replay.")]
    private partial void LogTagTransitionCommitVerified(long tagDefinitionId, LifecycleStatus status);
}
