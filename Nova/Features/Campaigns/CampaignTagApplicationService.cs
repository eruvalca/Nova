using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using Npgsql;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Reports that the current user is not authorized to mutate campaign tag applications.
/// </summary>
/// <param name="Detail">A description of the authorization failure.</param>
public readonly record struct CampaignTagApplicationForbidden(string Detail);

/// <summary>
/// Reports that a campaign tag application mutation conflicts with lifecycle or uniqueness rules.
/// </summary>
/// <param name="Detail">A description of the conflict.</param>
public readonly record struct CampaignTagApplicationConflict(string Detail);

/// <summary>
/// Applies tenant-safe campaign tag application add and remove mutations.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class CampaignTagApplicationService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignTagApplicationService> logger) : ICampaignTagApplicationService
{
    /// <summary>
    /// How long durable removal receipts are retained before the next removal prunes them. Verification
    /// runs immediately after commit, so a one-day window keeps the table bounded without affecting
    /// ambiguous-commit detection.
    /// </summary>
    private const int RemovalReceiptRetentionDays = 1;

    /// <inheritdoc />
    async Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ICampaignTagApplicationService.ApplyAsync(
        ApplyCampaignTagApplicationInput input,
        CancellationToken cancellationToken)
    {
        var outcome = await ApplyAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<CampaignTagApplicationMutationSuccess>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <inheritdoc />
    async Task<ServiceResult<Success>> ICampaignTagApplicationService.RemoveAsync(
        RemoveCampaignTagApplicationInput input,
        CancellationToken cancellationToken)
    {
        var outcome = await RemoveAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <summary>
    /// Applies one active tag definition to one participation in an active campaign.
    /// </summary>
    /// <param name="input">The target participation and tag-definition identifiers.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, validation, not found, forbidden, or conflict information.</returns>
    public async Task<OneOf<
        CampaignTagApplicationMutationSuccess,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        CampaignTagApplicationForbidden,
        CampaignTagApplicationConflict>> ApplyAsync(
            ApplyCampaignTagApplicationInput input,
            CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            LogApplyValidationFailed(input.PlayerCampaignAssignmentId, input.PlayerTagId);
            return new Error<IReadOnlyDictionary<string, string[]>>(errors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            LogApplyForbidden(input.PlayerCampaignAssignmentId, input.PlayerTagId, currentUserProvider.UserId ?? 0);
            return new CampaignTagApplicationForbidden("You must belong to a club to apply campaign tags.");
        }

        var creationOperationId = Guid.CreateVersion7();

        return await ExecuteWithFreshContextAsync(
            db => ApplyMutationAsync(db, input, actorUserId, clubId, creationOperationId, cancellationToken),
            db => VerifyApplyCommittedAsync(db, creationOperationId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one apply attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="input">The target participation and tag-definition identifiers.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for this logical apply operation.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, validation, not found, forbidden, or conflict information.</returns>
    private async Task<OneOf<
        CampaignTagApplicationMutationSuccess,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        CampaignTagApplicationForbidden,
        CampaignTagApplicationConflict>> ApplyMutationAsync(
            NovaDbContext db,
            ApplyCampaignTagApplicationInput input,
            long actorUserId,
            long clubId,
            Guid creationOperationId,
            CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var participation = await db.PlayerCampaignAssignments
            .Include(assignment => assignment.Campaign)
            .SingleOrDefaultAsync(
                assignment => assignment.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId,
                cancellationToken);
        if (participation is null || participation.ClubId != clubId || participation.Campaign.ClubId != clubId)
        {
            LogApplyNotFound(input.PlayerCampaignAssignmentId, input.PlayerTagId, clubId);
            return new NotFound();
        }

        await db.AcquireCampaignMutationLockAsync(participation.CampaignId, cancellationToken);
        await db.Entry(participation.Campaign).ReloadAsync(cancellationToken);
        if (participation.Campaign.Status == CampaignStatus.Closed)
        {
            LogApplyCampaignClosed(input.PlayerCampaignAssignmentId, participation.CampaignId, input.PlayerTagId);
            return new CampaignTagApplicationConflict("Closed campaigns are read-only and cannot accept tag applications.");
        }

        await db.AcquireTagMutationLockAsync(input.PlayerTagId, cancellationToken);
        var tagDefinition = await db.PlayerTags
            .SingleOrDefaultAsync(candidate => candidate.PlayerTagId == input.PlayerTagId, cancellationToken);
        if (tagDefinition is null || tagDefinition.ClubId != clubId)
        {
            LogApplyNotFound(input.PlayerCampaignAssignmentId, input.PlayerTagId, clubId);
            return new NotFound();
        }

        if (tagDefinition.LifecycleStatus == LifecycleStatus.Archived)
        {
            LogApplyTagDefinitionArchived(input.PlayerCampaignAssignmentId, input.PlayerTagId);
            return new CampaignTagApplicationConflict("Archived tag definitions cannot be applied.");
        }

        var alreadyApplied = await db.CampaignTagApplications
            .AnyAsync(
                candidate => candidate.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId
                    && candidate.PlayerTagId == input.PlayerTagId,
                cancellationToken);
        if (alreadyApplied)
        {
            LogApplyDuplicate(input.PlayerCampaignAssignmentId, input.PlayerTagId);
            return new CampaignTagApplicationConflict("The selected tag has already been applied to this participation.");
        }

        var application = new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = input.PlayerCampaignAssignmentId,
            PlayerTagId = input.PlayerTagId,
            ClubId = clubId,
            CreationOperationId = creationOperationId,
            CreatedById = actorUserId
        };
        db.CampaignTagApplications.Add(application);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            LogApplyDuplicate(input.PlayerCampaignAssignmentId, input.PlayerTagId);
            return new CampaignTagApplicationConflict("The selected tag has already been applied to this participation.");
        }

        LogApplySucceeded(input.PlayerCampaignAssignmentId, input.PlayerTagId, actorUserId, application.CampaignTagApplicationId);
        return new CampaignTagApplicationMutationSuccess(application.CampaignTagApplicationId);
    }

    /// <summary>
    /// Reconstructs a successful apply result when the commit outcome is uncertain.
    /// </summary>
    /// <param name="db">The fresh tenant context created for verification.</param>
    /// <param name="creationOperationId">The stable identifier for the logical apply operation.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Whether the apply committed, along with the reconstructed result when it did.</returns>
    private static async Task<ExecutionResult<OneOf<
        CampaignTagApplicationMutationSuccess,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        CampaignTagApplicationForbidden,
        CampaignTagApplicationConflict>>> VerifyApplyCommittedAsync(
            NovaDbContext db,
            Guid creationOperationId,
            long clubId,
            CancellationToken cancellationToken)
    {
        // The durable creation operation id scopes verification to exactly the row this request
        // created. A concurrently created row with the same pair can never be credited to this
        // request, so the strategy replays and the fresh duplicate probe produces the correct
        // Conflict instead of a false success with another request's application id.
        var application = await db.CampaignTagApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClubId == clubId
                    && candidate.CreationOperationId == creationOperationId,
                cancellationToken);

        return application is null
            ? new ExecutionResult<OneOf<
                CampaignTagApplicationMutationSuccess,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                CampaignTagApplicationForbidden,
                CampaignTagApplicationConflict>>(successful: false, default!)
            : new ExecutionResult<OneOf<
                CampaignTagApplicationMutationSuccess,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                CampaignTagApplicationForbidden,
                CampaignTagApplicationConflict>>(
                    successful: true,
                    new CampaignTagApplicationMutationSuccess(application.CampaignTagApplicationId));
    }

    /// <summary>
    /// Removes one campaign tag application when authorized by ownership or club-administrator role.
    /// </summary>
    /// <param name="input">The campaign tag application to remove.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, validation, not found, forbidden, or conflict information.</returns>
    public async Task<OneOf<
        Success,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        CampaignTagApplicationForbidden,
        CampaignTagApplicationConflict>> RemoveAsync(
            RemoveCampaignTagApplicationInput input,
            CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            LogRemoveValidationFailed(input.CampaignTagApplicationId);
            return new Error<IReadOnlyDictionary<string, string[]>>(errors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            LogRemoveForbidden(input.CampaignTagApplicationId, currentUserProvider.UserId ?? 0);
            return new CampaignTagApplicationForbidden("You must belong to a club to remove campaign tags.");
        }

        var removalOperationId = Guid.CreateVersion7();

        return await ExecuteWithFreshContextAsync(
            db => RemoveMutationAsync(db, input, actorUserId, clubId, removalOperationId, cancellationToken),
            db => VerifyRemoveCommittedAsync(db, removalOperationId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one remove attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="input">The campaign tag application to remove.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="removalOperationId">The stable identifier for this logical remove operation.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, validation, not found, forbidden, or conflict information.</returns>
    private async Task<OneOf<
        Success,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        CampaignTagApplicationForbidden,
        CampaignTagApplicationConflict>> RemoveMutationAsync(
            NovaDbContext db,
            RemoveCampaignTagApplicationInput input,
            long actorUserId,
            long clubId,
            Guid removalOperationId,
            CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var application = await db.CampaignTagApplications
            .Include(candidate => candidate.PlayerCampaignAssignment)
                .ThenInclude(assignment => assignment.Campaign)
            .Include(candidate => candidate.PlayerTag)
            .SingleOrDefaultAsync(
                candidate => candidate.CampaignTagApplicationId == input.CampaignTagApplicationId,
                cancellationToken);
        if (application is null
            || application.ClubId != clubId
            || application.PlayerCampaignAssignment.ClubId != clubId
            || application.PlayerCampaignAssignment.Campaign.ClubId != clubId
            || application.PlayerTag.ClubId != clubId)
        {
            LogRemoveNotFound(input.CampaignTagApplicationId, clubId);
            return new NotFound();
        }

        await db.AcquireCampaignMutationLockAsync(application.PlayerCampaignAssignment.CampaignId, cancellationToken);
        await db.Entry(application.PlayerCampaignAssignment.Campaign).ReloadAsync(cancellationToken);
        if (application.PlayerCampaignAssignment.Campaign.Status == CampaignStatus.Closed)
        {
            LogRemoveCampaignClosed(input.CampaignTagApplicationId, application.PlayerCampaignAssignment.CampaignId);
            return new CampaignTagApplicationConflict("Closed campaigns are read-only and cannot remove tag applications.");
        }

        await db.AcquireTagMutationLockAsync(application.PlayerTagId, cancellationToken);
        await db.Entry(application.PlayerTag).ReloadAsync(cancellationToken);
        if (application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived)
        {
            LogRemoveTagDefinitionArchived(input.CampaignTagApplicationId, application.PlayerTagId);
            return new CampaignTagApplicationConflict("Archived tag definitions cannot be changed.");
        }

        if (!currentUserProvider.IsClubAdmin && application.CreatedById != actorUserId)
        {
            LogRemoveForbidden(input.CampaignTagApplicationId, actorUserId);
            return new CampaignTagApplicationForbidden("Only the applying user or a club administrator can remove this tag application.");
        }

        await PruneExpiredRemovalReceiptsAsync(db, cancellationToken);

        db.CampaignTagApplicationRemovalReceipts.Add(new CampaignTagApplicationRemovalReceiptEntity
        {
            RemovalOperationId = removalOperationId,
            CampaignTagApplicationId = application.CampaignTagApplicationId,
            ClubId = clubId,
            CreatedById = actorUserId
        });
        db.CampaignTagApplications.Remove(application);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogRemoveConcurrencyConflict(input.CampaignTagApplicationId);
            return new NotFound();
        }

        LogRemoveSucceeded(input.CampaignTagApplicationId, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Deletes removal receipts older than the retention window so the durable verification artifact
    /// does not accumulate unboundedly with tag removals. Runs inside the remove transaction so a
    /// transient-failure retry replays the prune along with the delete, and the tenant filter scopes
    /// the prune to the current club.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    private static async Task PruneExpiredRemovalReceiptsAsync(NovaDbContext db, CancellationToken cancellationToken)
    {
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-RemovalReceiptRetentionDays);
        if (db.Database.IsNpgsql())
        {
            // A set-based delete is idempotent: two concurrent removals in the same club that both
            // select the same expired receipts will not fight over tracked deletes. After one
            // transaction deletes them, the other's DELETE affects zero rows instead of throwing
            // DbUpdateConcurrencyException. It also avoids loading every receipt on each removal.
            await db.CampaignTagApplicationRemovalReceipts
                .Where(receipt => receipt.CreatedAt < retentionCutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // SQLite cannot translate DateTimeOffset comparisons to SQL, so the tenant-filtered candidate
        // set is loaded and the age filter is applied in memory. Receipts are pruned daily, keeping
        // the per-club set small and the table bounded.
        var expiredReceipts = (await db.CampaignTagApplicationRemovalReceipts
                .ToListAsync(cancellationToken))
            .Where(receipt => receipt.CreatedAt < retentionCutoff)
            .ToList();
        if (expiredReceipts.Count > 0)
        {
            db.CampaignTagApplicationRemovalReceipts.RemoveRange(expiredReceipts);
        }
    }

    /// <summary>
    /// Reconstructs a successful remove result when the commit outcome is uncertain.
    /// </summary>
    /// <param name="db">The fresh tenant context created for verification.</param>
    /// <param name="removalOperationId">The stable identifier for the logical remove operation.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Whether the remove committed, along with the reconstructed result when it did.</returns>
    private static async Task<ExecutionResult<OneOf<
        Success,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        CampaignTagApplicationForbidden,
        CampaignTagApplicationConflict>>> VerifyRemoveCommittedAsync(
            NovaDbContext db,
            Guid removalOperationId,
            CancellationToken cancellationToken)
    {
        // The durable removal receipt proves this request's commit reached the database even though
        // the removed row is gone. Without a receipt, the absent row belongs to an earlier request
        // (or never existed) and the strategy must replay so the fresh lookup produces the correct
        // NotFound instead of crediting another request's delete as this request's success.
        var receiptExists = await db.CampaignTagApplicationRemovalReceipts
            .AsNoTracking()
            .AnyAsync(candidate => candidate.RemovalOperationId == removalOperationId, cancellationToken);

        return !receiptExists
            ? new ExecutionResult<OneOf<
                Success,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                CampaignTagApplicationForbidden,
                CampaignTagApplicationConflict>>(successful: false, default!)
            : new ExecutionResult<OneOf<
                Success,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                CampaignTagApplicationForbidden,
                CampaignTagApplicationConflict>>(successful: true, new Success());
    }

    /// <summary>
    /// Runs a campaign tag application mutation inside EF Core's retrying execution strategy and
    /// verifies whether an ambiguous commit succeeded before allowing the strategy to replay the
    /// mutation.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the mutation attempt.</typeparam>
    /// <param name="operation">The mutation to run with a fresh tenant context.</param>
    /// <param name="verifySucceeded">The verification query to run with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels strategy setup, mutation, or verification.</param>
    /// <returns>The mutation result or the reconstructed result from successful commit verification.</returns>
    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        Func<NovaDbContext, Task<TResult>> operation,
        Func<NovaDbContext, Task<ExecutionResult<TResult>>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            (Operation: operation, VerifySucceeded: verifySucceeded),
            async (state, _) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.Operation(db);
            },
            async (state, _) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.VerifySucceeded(db);
            },
            cancellationToken);
    }

    /// <summary>
    /// Logs an apply request rejected due to invalid input values.
    /// </summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application validation failed for AssignmentId={AssignmentId} and TagId={TagId}.")]
    private partial void LogApplyValidationFailed(long assignmentId, long tagId);

    /// <summary>
    /// Logs an apply request rejected because the caller has no club membership context.
    /// </summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application forbidden for AssignmentId={AssignmentId}, TagId={TagId}, UserId={UserId}.")]
    private partial void LogApplyForbidden(long assignmentId, long tagId, long userId);

    /// <summary>
    /// Logs an apply request whose participation or tag-definition target is unavailable in the current tenant.
    /// </summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application target not found for AssignmentId={AssignmentId}, TagId={TagId}, ClubId={ClubId}.")]
    private partial void LogApplyNotFound(long assignmentId, long tagId, long clubId);

    /// <summary>
    /// Logs an apply request rejected because the campaign is closed.
    /// </summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="campaignId">The closed campaign identifier.</param>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application rejected for AssignmentId={AssignmentId}, CampaignId={CampaignId}, TagId={TagId} because the campaign is closed.")]
    private partial void LogApplyCampaignClosed(long assignmentId, long campaignId, long tagId);

    /// <summary>
    /// Logs an apply request rejected because the tag definition is archived.
    /// </summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="tagId">The archived tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application rejected for AssignmentId={AssignmentId} because TagId={TagId} is archived.")]
    private partial void LogApplyTagDefinitionArchived(long assignmentId, long tagId);

    /// <summary>
    /// Logs an apply request rejected because the participation/tag pair already exists.
    /// </summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application duplicate rejected for AssignmentId={AssignmentId} and TagId={TagId}.")]
    private partial void LogApplyDuplicate(long assignmentId, long tagId);

    /// <summary>
    /// Logs a successful apply mutation.
    /// </summary>
    /// <param name="assignmentId">The participation identifier receiving the tag.</param>
    /// <param name="tagId">The applied tag-definition identifier.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="applicationId">The created application identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign tag application created: CampaignTagApplicationId={ApplicationId}, AssignmentId={AssignmentId}, TagId={TagId}, UserId={ActorUserId}.")]
    private partial void LogApplySucceeded(long assignmentId, long tagId, long actorUserId, long applicationId);

    /// <summary>
    /// Logs a remove request rejected due to invalid input values.
    /// </summary>
    /// <param name="applicationId">The requested campaign tag application identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application removal validation failed for CampaignTagApplicationId={ApplicationId}.")]
    private partial void LogRemoveValidationFailed(long applicationId);

    /// <summary>
    /// Logs a remove request rejected because the caller is unauthorized.
    /// </summary>
    /// <param name="applicationId">The requested campaign tag application identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application removal forbidden for CampaignTagApplicationId={ApplicationId} by UserId={UserId}.")]
    private partial void LogRemoveForbidden(long applicationId, long userId);

    /// <summary>
    /// Logs a remove request whose application is unavailable in the current tenant.
    /// </summary>
    /// <param name="applicationId">The requested campaign tag application identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "CampaignTagApplicationId={ApplicationId} was not found for ClubId={ClubId}.")]
    private partial void LogRemoveNotFound(long applicationId, long clubId);

    /// <summary>
    /// Logs a remove request rejected because the campaign is closed.
    /// </summary>
    /// <param name="applicationId">The requested campaign tag application identifier.</param>
    /// <param name="campaignId">The closed campaign identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application removal rejected for CampaignTagApplicationId={ApplicationId} because CampaignId={CampaignId} is closed.")]
    private partial void LogRemoveCampaignClosed(long applicationId, long campaignId);

    /// <summary>
    /// Logs a remove request rejected because the tag definition is archived.
    /// </summary>
    /// <param name="applicationId">The requested campaign tag application identifier.</param>
    /// <param name="tagId">The archived tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application removal rejected for CampaignTagApplicationId={ApplicationId} because TagId={TagId} is archived.")]
    private partial void LogRemoveTagDefinitionArchived(long applicationId, long tagId);

    /// <summary>
    /// Logs a successful remove mutation.
    /// </summary>
    /// <param name="applicationId">The removed campaign tag application identifier.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign tag application removed: CampaignTagApplicationId={ApplicationId}, UserId={ActorUserId}.")]
    private partial void LogRemoveSucceeded(long applicationId, long actorUserId);

    /// <summary>
    /// Logs a remove mutation that failed because the application was concurrently deleted.
    /// </summary>
    /// <param name="applicationId">The campaign tag application identifier that could not be deleted.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign tag application removal concurrency conflict: CampaignTagApplicationId={ApplicationId} was already removed.")]
    private partial void LogRemoveConcurrencyConflict(long applicationId);
}
