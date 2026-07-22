using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
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
    ILogger<TagDefinitionLifecycleService> logger)
{
    /// <summary>
    /// Archives a tag definition while preserving existing player associations.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier to archive.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ArchiveAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(tagDefinitionId, LifecycleStatus.Archived, cancellationToken);

    /// <summary>
    /// Restores an archived tag definition to active use and clears archive provenance.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier to restore.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> RestoreAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(tagDefinitionId, LifecycleStatus.Active, cancellationToken);

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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
}
