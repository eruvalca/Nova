using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Tags;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Tags;

/// <summary>
/// Creates and updates tag definitions with club-administrator authorization and race-safe,
/// case-insensitive per-club name uniqueness.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for operation outcomes.</param>
public sealed partial class TagDefinitionService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TagDefinitionService> logger) : ITagDefinitionService
{
    /// <summary>
    /// The conflict message returned when a club already owns a tag definition with the same name.
    /// </summary>
    private const string DuplicateTagDefinitionMessage =
        "A tag definition with that name already exists.";

    /// <summary>
    /// The number of days a tag-definition mutation receipt is retained before it is pruned.
    /// </summary>
    private const int MutationReceiptRetentionDays = 1;

    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionDto>> CreateAsync(
        CreateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogTagDefinitionCreateValidationFailed();
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTagDefinitionCreateForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to create tag definitions.");
        }

        var creationOperationId = Guid.CreateVersion7();
        return await ExecuteWithFreshContextAsync(
            (db, commitAttempted) => CreateTagDefinitionAsync(db, input, actorUserId, clubId, creationOperationId, commitAttempted, cancellationToken),
            db => VerifyTagDefinitionCreationAsync(db, clubId, creationOperationId, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionDto>> UpdateAsync(
        UpdateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogTagDefinitionUpdateValidationFailed(input.TagId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTagDefinitionUpdateForbidden(input.TagId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to edit tag definitions.");
        }

        var updateOperationId = Guid.CreateVersion7();
        return await ExecuteWithFreshContextAsync(
            (db, commitAttempted) => UpdateTagDefinitionAsync(db, input, actorUserId, clubId, updateOperationId, commitAttempted, cancellationToken),
            db => VerifyTagDefinitionUpdateCommittedAsync(db, clubId, updateOperationId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs a tag-definition mutation inside EF Core's retrying execution strategy and verifies
    /// whether an ambiguous commit succeeded before allowing the strategy to replay the mutation.
    /// Verification only runs for an attempt that reached its commit; a transient failure raised
    /// before the commit cannot have applied the mutation, so the observed state belongs to an
    /// earlier request and must not be credited to this one.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the mutation attempt.</typeparam>
    /// <param name="operation">The mutation to run with a fresh tenant context and commit tracker.</param>
    /// <param name="verifySucceeded">The verification query to run with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels strategy setup, mutation, or verification.</param>
    /// <returns>The mutation result or the reconstructed result from successful commit verification.</returns>
    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        Func<NovaDbContext, CommitAttemptTracker, Task<TResult>> operation,
        Func<NovaDbContext, Task<ExecutionResult<TResult>>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (Operation: operation, VerifySucceeded: verifySucceeded, CommitAttempted: commitAttempted),
            async (state, _) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.Operation(db, state.CommitAttempted);
            },
            async (state, _) =>
            {
                if (!state.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<TResult>(successful: false, default!);
                }

                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.VerifySucceeded(db);
            },
            cancellationToken);
    }

    /// <summary>
    /// Tracks whether a mutation attempt reached its commit, scoping ambiguous-commit verification
    /// to attempts that could actually have applied the mutation.
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
    /// Creates one tag definition using a single transactional execution attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested tag-definition details.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for this logical creation operation.</param>
    /// <param name="commitAttempted">Tracks whether this attempt reached its commit.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The created tag definition or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<TagDefinitionDto>> CreateTagDefinitionAsync(
        NovaDbContext db,
        CreateTagDefinitionInput input,
        long actorUserId,
        long clubId,
        Guid creationOperationId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize with concurrent tag-definition mutations in the same club so the duplicate-name
        // probe and the insert observe the same snapshot.
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);

        var name = input.Name.Trim();
        var normalizedName = name.ToUpperInvariant();

        if (await TagNormalizedNameExistsAsync(db, clubId, normalizedName, excludedTagId: null, cancellationToken))
        {
            LogDuplicateTagDefinitionName(clubId);
            return ServiceProblem.Conflict(DuplicateTagDefinitionMessage);
        }

        var tagDefinition = new PlayerTagEntity
        {
            Name = name,
            NormalizedName = normalizedName,
            Color = input.Color.Trim().ToUpperInvariant(),
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreationOperationId = creationOperationId,
            CreatedById = actorUserId
        };
        db.PlayerTags.Add(tagDefinition);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTagDefinitionMutationConcurrencyConflict(tagDefinition.PlayerTagId);
            return ServiceProblem.Conflict("The tag definition could not be created. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogDuplicateTagDefinitionName(clubId);
            return ServiceProblem.Conflict(DuplicateTagDefinitionMessage);
        }

        LogTagDefinitionCreated(tagDefinition.PlayerTagId, actorUserId);
        return tagDefinition.ToTagDefinitionDto();
    }

    /// <summary>
    /// Checks whether a tag-definition creation transaction with an uncertain commit outcome was
    /// committed and reconstructs its successful service result without replaying the insert.
    /// </summary>
    /// <param name="db">The fresh tenant context used for commit verification.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for the logical creation operation.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed tag definition was found.</returns>
    private static async Task<ExecutionResult<ServiceResult<TagDefinitionDto>>> VerifyTagDefinitionCreationAsync(
        NovaDbContext db,
        long clubId,
        Guid creationOperationId,
        CancellationToken cancellationToken)
    {
        var tagDefinition = await db.PlayerTags
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClubId == clubId
                    && candidate.CreationOperationId == creationOperationId,
                cancellationToken);

        return tagDefinition is null
            ? new ExecutionResult<ServiceResult<TagDefinitionDto>>(successful: false, default!)
            : new ExecutionResult<ServiceResult<TagDefinitionDto>>(successful: true, tagDefinition.ToTagDefinitionDto());
    }

    /// <summary>
    /// Updates one tag definition using a single transactional execution attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested tag-definition updates.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="updateOperationId">The stable identifier for this logical update operation.</param>
    /// <param name="commitAttempted">Tracks whether this attempt reached its commit.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The updated tag definition or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<TagDefinitionDto>> UpdateTagDefinitionAsync(
        NovaDbContext db,
        UpdateTagDefinitionInput input,
        long actorUserId,
        long clubId,
        Guid updateOperationId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.AcquireTagMutationLockAsync(input.TagId, cancellationToken);

        var tagDefinition = await db.PlayerTags
            .SingleOrDefaultAsync(candidate => candidate.PlayerTagId == input.TagId, cancellationToken);

        if (tagDefinition is null || tagDefinition.ClubId != clubId)
        {
            LogTagDefinitionNotFound(input.TagId, clubId);
            return ServiceProblem.NotFound();
        }

        if (tagDefinition.LifecycleStatus != LifecycleStatus.Active)
        {
            LogTagDefinitionArchivedConflict(input.TagId);
            return ServiceProblem.Conflict("Archived tag definitions cannot be edited through this workflow. Restore the tag definition first.");
        }

        var name = input.Name.Trim();
        var normalizedName = name.ToUpperInvariant();

        if (!string.Equals(normalizedName, tagDefinition.NormalizedName, StringComparison.Ordinal)
            && await TagNormalizedNameExistsAsync(db, clubId, normalizedName, input.TagId, cancellationToken))
        {
            LogDuplicateTagDefinitionName(clubId);
            return ServiceProblem.Conflict(DuplicateTagDefinitionMessage);
        }

        tagDefinition.Name = name;
        tagDefinition.NormalizedName = normalizedName;
        tagDefinition.Color = input.Color.Trim().ToUpperInvariant();

        await PruneExpiredMutationReceiptsAsync(db, cancellationToken);

        // Record a durable update receipt in the same transaction as the field changes so an
        // ambiguous-commit retry can verify THIS update applied without comparing mutable
        // tag-definition fields (which a newer concurrent update could legitimately change).
        db.TagDefinitionMutationReceipts.Add(new TagDefinitionMutationReceiptEntity
        {
            OperationId = updateOperationId,
            PlayerTagId = tagDefinition.PlayerTagId,
            MutationType = TagDefinitionMutationType.Updated,
            ClubId = clubId,
            CreatedById = actorUserId
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTagDefinitionMutationConcurrencyConflict(input.TagId);
            return ServiceProblem.Conflict("The tag definition changed concurrently. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogDuplicateTagDefinitionName(clubId);
            return ServiceProblem.Conflict(DuplicateTagDefinitionMessage);
        }

        LogTagDefinitionUpdated(tagDefinition.PlayerTagId, actorUserId);
        return tagDefinition.ToTagDefinitionDto();
    }

    /// <summary>
    /// Verifies whether a tag-definition update that may have committed ambiguously left a durable
    /// mutation receipt, and reconstructs the updated tag definition when it did.
    /// </summary>
    /// <param name="db">The fresh tenant context used for commit verification.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="updateOperationId">The stable identifier for the logical update operation.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>Whether the update committed, along with the reconstructed result when it did.</returns>
    private static async Task<ExecutionResult<ServiceResult<TagDefinitionDto>>> VerifyTagDefinitionUpdateCommittedAsync(
        NovaDbContext db,
        long clubId,
        Guid updateOperationId,
        CancellationToken cancellationToken)
    {
        var receipt = await db.TagDefinitionMutationReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OperationId == updateOperationId
                    && candidate.ClubId == clubId,
                cancellationToken);

        if (receipt is null)
        {
            return new ExecutionResult<ServiceResult<TagDefinitionDto>>(successful: false, default!);
        }

        // The durable receipt proves THIS request's update committed, so reading the current tag
        // definition reflects this update or a newer concurrent one - never a pre-update state.
        var tagDefinition = await db.PlayerTags
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.PlayerTagId == receipt.PlayerTagId, cancellationToken);

        return tagDefinition is null
            ? new ExecutionResult<ServiceResult<TagDefinitionDto>>(successful: false, default!)
            : new ExecutionResult<ServiceResult<TagDefinitionDto>>(successful: true, tagDefinition.ToTagDefinitionDto());
    }

    /// <summary>
    /// Deletes tag-definition mutation receipts older than the retention window so the durable
    /// verification artifact does not accumulate unboundedly with tag mutations. Runs inside the
    /// mutation transaction so a transient-failure retry replays the prune along with the mutation,
    /// and the tenant filter scopes the prune to the current club.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    private static async Task PruneExpiredMutationReceiptsAsync(NovaDbContext db, CancellationToken cancellationToken)
    {
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-MutationReceiptRetentionDays);
        if (db.Database.IsNpgsql())
        {
            await db.TagDefinitionMutationReceipts
                .Where(receipt => receipt.CreatedAt < retentionCutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // SQLite cannot translate DateTimeOffset comparisons to SQL, so the tenant-filtered candidate
        // set is loaded and the age filter is applied in memory.
        var expiredReceipts = (await db.TagDefinitionMutationReceipts
                .ToListAsync(cancellationToken))
            .Where(receipt => receipt.CreatedAt < retentionCutoff)
            .ToList();
        if (expiredReceipts.Count > 0)
        {
            db.TagDefinitionMutationReceipts.RemoveRange(expiredReceipts);
        }
    }

    /// <summary>
    /// Determines whether the club already owns a tag definition with the supplied normalized name.
    /// </summary>
    /// <param name="db">The tenant context for the current execution attempt.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="normalizedName">The case-folded proposed name.</param>
    /// <param name="excludedTagId">The tag definition being updated, excluded from the probe, when applicable.</param>
    /// <param name="cancellationToken">A token that cancels the probe query.</param>
    /// <returns><see langword="true"/> when a conflicting tag definition already exists.</returns>
    private static Task<bool> TagNormalizedNameExistsAsync(
        NovaDbContext db,
        long clubId,
        string normalizedName,
        long? excludedTagId,
        CancellationToken cancellationToken)
        => db.PlayerTags.AnyAsync(
            candidate => candidate.ClubId == clubId
                && candidate.NormalizedName == normalizedName
                && (excludedTagId == null || candidate.PlayerTagId != excludedTagId),
            cancellationToken);

    /// <summary>
    /// Determines whether a persistence failure was caused by a unique-index violation. The check is
    /// text-based so it holds for both the Npgsql production provider (SQLSTATE 23505) and the SQLite
    /// provider used by the tenancy unit-test harness, without either provider being referenced here.
    /// </summary>
    /// <param name="exception">The persistence failure to classify.</param>
    /// <returns><see langword="true"/> when the failure was a unique-index violation.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message;
        return message is not null
            && (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Logs invalid tag-definition creation input.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition create validation failed.")]
    private partial void LogTagDefinitionCreateValidationFailed();

    /// <summary>Logs invalid tag-definition update input.</summary>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition update validation failed for PlayerTagId={TagId}.")]
    private partial void LogTagDefinitionUpdateValidationFailed(long tagId);

    /// <summary>Logs a tag-definition creation request from a non-administrator.</summary>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition create forbidden for UserId={UserId}.")]
    private partial void LogTagDefinitionCreateForbidden(long userId);

    /// <summary>Logs a tag-definition update request from a non-administrator.</summary>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition update forbidden for PlayerTagId={TagId} by UserId={UserId}.")]
    private partial void LogTagDefinitionUpdateForbidden(long tagId, long userId);

    /// <summary>Logs a tag definition that is unavailable in the current tenant.</summary>
    /// <param name="tagId">The requested tag-definition identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "PlayerTagId={TagId} was not found for ClubId={ClubId}.")]
    private partial void LogTagDefinitionNotFound(long tagId, long clubId);

    /// <summary>Logs an update attempt on an archived tag definition.</summary>
    /// <param name="tagId">The archived tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition update conflict: PlayerTagId={TagId} is archived.")]
    private partial void LogTagDefinitionArchivedConflict(long tagId);

    /// <summary>Logs a tag-definition mutation that failed due to a concurrent data change.</summary>
    /// <param name="tagId">The affected tag-definition identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition mutation concurrency conflict for PlayerTagId={TagId}.")]
    private partial void LogTagDefinitionMutationConcurrencyConflict(long tagId);

    /// <summary>Logs a tag-definition mutation rejected because the club already owns a matching name.</summary>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate tag-definition name rejected for ClubId={ClubId}.")]
    private partial void LogDuplicateTagDefinitionName(long clubId);

    /// <summary>Logs a successful tag-definition creation.</summary>
    /// <param name="tagId">The created tag-definition identifier.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerTagId={TagId} created by UserId={ActorUserId}.")]
    private partial void LogTagDefinitionCreated(long tagId, long actorUserId);

    /// <summary>Logs a successful tag-definition update.</summary>
    /// <param name="tagId">The updated tag-definition identifier.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerTagId={TagId} updated by UserId={ActorUserId}.")]
    private partial void LogTagDefinitionUpdated(long tagId, long actorUserId);
}
