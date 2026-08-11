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
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Applies tenant-safe add, edit, and delete operations for evaluation notes scoped to campaign participations.
/// Any approved club member may add notes to an Active campaign; only the author or a club administrator
/// may edit or delete notes while the campaign remains Active.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for note mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class EvaluationNoteService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<EvaluationNoteService> logger) : ICampaignEvaluationNoteService
{
    private const int MutationReceiptRetentionDays = 1;

    /// <inheritdoc />
    async Task<ServiceResult<EvaluationNoteMutationSuccess>> ICampaignEvaluationNoteService.AddAsync(
        AddEvaluationNoteInput input,
        CancellationToken cancellationToken)
    {
        var outcome = await AddAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<EvaluationNoteMutationSuccess>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <inheritdoc />
    async Task<ServiceResult<Success>> ICampaignEvaluationNoteService.EditAsync(
        EditEvaluationNoteInput input,
        CancellationToken cancellationToken)
    {
        var outcome = await EditAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <inheritdoc />
    async Task<ServiceResult<Success>> ICampaignEvaluationNoteService.DeleteAsync(
        long noteId,
        CancellationToken cancellationToken)
    {
        var outcome = await DeleteAsync(noteId, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <summary>
    /// Adds a new evaluation note to a campaign participation record.
    /// Any club member may add notes while the campaign is Active.
    /// </summary>
    /// <param name="input">The note content and target participation identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on add; validation errors, not-found, forbidden, or conflict information otherwise.
    /// </returns>
    public async Task<OneOf<EvaluationNoteMutationSuccess, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>> AddAsync(
        AddEvaluationNoteInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            LogNoteValidationFailed(nameof(AddAsync), input.PlayerCampaignAssignmentId);
            return new Error<IReadOnlyDictionary<string, string[]>>(errors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            LogNoteForbidden(nameof(AddAsync), input.PlayerCampaignAssignmentId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be an approved club member to add evaluation notes.");
        }

        var creationOperationId = Guid.CreateVersion7();

        return await ExecuteWithFreshContextAsync(
            (db, commitAttempted) => AddNoteAsync(
                db,
                input,
                actorUserId,
                clubId,
                creationOperationId,
                commitAttempted,
                cancellationToken),
            db => VerifyAddCommittedAsync(db, creationOperationId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one add attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="input">The validated note input.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for this logical creation operation.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on add; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    private async Task<OneOf<EvaluationNoteMutationSuccess, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>> AddNoteAsync(
        NovaDbContext db,
        AddEvaluationNoteInput input,
        long actorUserId,
        long clubId,
        Guid creationOperationId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var participation = await db.PlayerCampaignAssignments
            .Include(assignment => assignment.Campaign)
            .Include(assignment => assignment.Player)
            .SingleOrDefaultAsync(
                assignment => assignment.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId,
                cancellationToken);

        if (participation is null
            || participation.ClubId != clubId
            || participation.Campaign.ClubId != clubId
            || participation.Player.ClubId != clubId)
        {
            LogNoteNotFound(nameof(AddAsync), input.PlayerCampaignAssignmentId, clubId);
            return new NotFound();
        }

        await db.AcquireCampaignMutationLockAsync(participation.CampaignId, cancellationToken);
        await db.Entry(participation.Campaign).ReloadAsync(cancellationToken);

        if (participation.Campaign.Status == CampaignStatus.Closed)
        {
            LogNoteCampaignClosed(nameof(AddAsync), input.PlayerCampaignAssignmentId, participation.CampaignId);
            return new LifecycleConflict("Closed campaigns are read-only and cannot accept new notes.");
        }

        var note = new NoteEntity
        {
            Content = input.Content,
            CreationOperationId = creationOperationId,
            PlayerCampaignAssignmentId = participation.PlayerCampaignAssignmentId,
            ClubId = default,
            CreatedById = default
        };
        db.Notes.Add(note);

        await PruneExpiredMutationReceiptsAsync(db, cancellationToken);

        // Persist the note first so its database-generated identifier can be recorded on the
        // durable mutation receipt written in the same transaction. The receipt survives later
        // note edits and deletes, keeping ambiguous-commit verification tied to this operation.
        await db.SaveChangesAsync(cancellationToken);

        db.EvaluationNoteMutationReceipts.Add(new EvaluationNoteMutationReceiptEntity
        {
            OperationId = creationOperationId,
            NoteId = note.NoteId,
            MutationType = EvaluationNoteMutationType.Added,
            ClubId = clubId,
            CreatedById = actorUserId
        });

        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);

        LogNoteAdded(note.NoteId, input.PlayerCampaignAssignmentId, actorUserId);
        return new EvaluationNoteMutationSuccess(note.NoteId);
    }

    /// <summary>
    /// Verifies whether an add that may have committed ambiguously left a durable mutation receipt,
    /// and reconstructs the add result when it did.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this verification attempt.</param>
    /// <param name="creationOperationId">The stable identifier for the logical creation operation.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Whether the add committed, along with the reconstructed result when it did.</returns>
    private static async Task<ExecutionResult<OneOf<
        EvaluationNoteMutationSuccess,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        LifecycleForbidden,
        LifecycleConflict>>> VerifyAddCommittedAsync(
            NovaDbContext db,
            Guid creationOperationId,
            long clubId,
            CancellationToken cancellationToken)
    {
        // The durable mutation receipt is written in the same transaction as the note and has no
        // foreign key to the note row, so it still proves this request's add committed even when a
        // concurrent delete removed the note before verification ran. Replaying would otherwise
        // resurrect a note the user deliberately deleted.
        var receipt = await db.EvaluationNoteMutationReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.OperationId == creationOperationId
                    && candidate.ClubId == clubId,
                cancellationToken);

        return receipt is null
            ? new ExecutionResult<OneOf<
                EvaluationNoteMutationSuccess,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                LifecycleForbidden,
                LifecycleConflict>>(successful: false, default!)
            : new ExecutionResult<OneOf<
                EvaluationNoteMutationSuccess,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                LifecycleForbidden,
                LifecycleConflict>>(
                    successful: true,
                    new EvaluationNoteMutationSuccess(receipt.NoteId));
    }

    /// <summary>
    /// Edits the content of an existing evaluation note.
    /// Only the original author or a club administrator may edit a note while the campaign is Active.
    /// </summary>
    /// <param name="input">The note identifier and updated content.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on edit; validation errors, not-found, forbidden, or conflict information otherwise.
    /// </returns>
    public async Task<OneOf<Success, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>> EditAsync(
        EditEvaluationNoteInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            LogNoteValidationFailed(nameof(EditAsync), input.NoteId);
            return new Error<IReadOnlyDictionary<string, string[]>>(errors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            LogNoteForbidden(nameof(EditAsync), input.NoteId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be an approved club member to edit evaluation notes.");
        }

        var editOperationId = Guid.CreateVersion7();

        return await ExecuteWithFreshContextAsync(
            (db, commitAttempted) => EditNoteAsync(db, input, actorUserId, clubId, editOperationId, commitAttempted, cancellationToken),
            db => VerifyEditCommittedAsync(db, editOperationId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one edit attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="input">The validated note input.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="editOperationId">The stable identifier for this logical edit operation.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on edit; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    private async Task<OneOf<Success, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>> EditNoteAsync(
        NovaDbContext db,
        EditEvaluationNoteInput input,
        long actorUserId,
        long clubId,
        Guid editOperationId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var note = await db.Notes
            .Include(n => n.PlayerCampaignAssignment)
                .ThenInclude(assignment => assignment.Campaign)
            .Include(n => n.PlayerCampaignAssignment)
                .ThenInclude(assignment => assignment.Player)
            .SingleOrDefaultAsync(n => n.NoteId == input.NoteId, cancellationToken);

        if (note is null
            || note.ClubId != clubId
            || note.PlayerCampaignAssignment.ClubId != clubId
            || note.PlayerCampaignAssignment.Campaign.ClubId != clubId
            || note.PlayerCampaignAssignment.Player.ClubId != clubId)
        {
            LogNoteNotFound(nameof(EditAsync), input.NoteId, clubId);
            return new NotFound();
        }

        var isAuthor = note.CreatedById == actorUserId;
        var isAdmin = currentUserProvider.IsClubAdmin;
        if (!isAuthor && !isAdmin)
        {
            LogNoteForbidden(nameof(EditAsync), input.NoteId, actorUserId);
            return new LifecycleForbidden("Only the note author or a club administrator may edit evaluation notes.");
        }

        await db.AcquireCampaignMutationLockAsync(note.PlayerCampaignAssignment.CampaignId, cancellationToken);
        await db.Entry(note.PlayerCampaignAssignment.Campaign).ReloadAsync(cancellationToken);

        if (note.PlayerCampaignAssignment.Campaign.Status == CampaignStatus.Closed)
        {
            LogNoteCampaignClosed(nameof(EditAsync), input.NoteId, note.PlayerCampaignAssignment.CampaignId);
            return new LifecycleConflict("Closed campaigns are read-only and cannot accept note edits.");
        }

        note.Content = input.Content;

        await PruneExpiredMutationReceiptsAsync(db, cancellationToken);

        // Record a durable edit receipt in the same transaction as the content change so an
        // ambiguous-commit retry can verify THIS operation applied without comparing mutable note
        // content (which a newer concurrent edit could legitimately change).
        db.EvaluationNoteMutationReceipts.Add(new EvaluationNoteMutationReceiptEntity
        {
            OperationId = editOperationId,
            NoteId = note.NoteId,
            MutationType = EvaluationNoteMutationType.Edited,
            ClubId = clubId,
            CreatedById = actorUserId
        });

        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);

        LogNoteEdited(input.NoteId, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Verifies whether an edit that may have committed ambiguously left a durable mutation receipt,
    /// and reconstructs the edit result when it did.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this verification attempt.</param>
    /// <param name="editOperationId">The stable identifier for the logical edit operation.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Whether the edit committed, along with the reconstructed result when it did.</returns>
    private static async Task<ExecutionResult<OneOf<
        Success,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        LifecycleForbidden,
        LifecycleConflict>>> VerifyEditCommittedAsync(
            NovaDbContext db,
            Guid editOperationId,
            long clubId,
            CancellationToken cancellationToken)
    {
        // The durable edit receipt is written in the same transaction as the content change, so it
        // proves THIS request's edit committed even when a newer concurrent edit changed the note
        // content before verification ran. Comparing note content instead would fail verification
        // and cause a replay that overwrites the newer edit.
        var receiptExists = await db.EvaluationNoteMutationReceipts
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.OperationId == editOperationId
                    && candidate.ClubId == clubId,
                cancellationToken);

        return !receiptExists
            ? new ExecutionResult<OneOf<
                Success,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                LifecycleForbidden,
                LifecycleConflict>>(successful: false, default!)
            : new ExecutionResult<OneOf<
                Success,
                Error<IReadOnlyDictionary<string, string[]>>,
                NotFound,
                LifecycleForbidden,
                LifecycleConflict>>(successful: true, new Success());
    }

    /// <summary>
    /// Deletes mutation receipts older than the retention window so the durable verification
    /// artifact does not accumulate unboundedly with note mutations. Runs inside the mutation
    /// transaction so a transient-failure retry replays the prune along with the mutation, and the
    /// tenant filter scopes the prune to the current club.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    private static async Task PruneExpiredMutationReceiptsAsync(NovaDbContext db, CancellationToken cancellationToken)
    {
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-MutationReceiptRetentionDays);
        if (db.Database.IsNpgsql())
        {
            // A set-based delete is idempotent: two concurrent mutations in the same club that both
            // select the same expired receipts will not fight over tracked deletes. After one
            // transaction deletes them, the other's DELETE affects zero rows instead of throwing
            // DbUpdateConcurrencyException. It also avoids loading every receipt on each mutation.
            await db.EvaluationNoteMutationReceipts
                .Where(receipt => receipt.CreatedAt < retentionCutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // SQLite cannot translate DateTimeOffset comparisons to SQL, so the tenant-filtered candidate
        // set is loaded and the age filter is applied in memory. Receipts are pruned daily, keeping
        // the per-club set small and the table bounded.
        var expiredReceipts = (await db.EvaluationNoteMutationReceipts
                .ToListAsync(cancellationToken))
            .Where(receipt => receipt.CreatedAt < retentionCutoff)
            .ToList();
        if (expiredReceipts.Count > 0)
        {
            db.EvaluationNoteMutationReceipts.RemoveRange(expiredReceipts);
        }
    }

    /// <summary>
    /// Deletes an evaluation note.
    /// Only the original author or a club administrator may delete a note while the campaign is Active.
    /// </summary>
    /// <param name="noteId">The identifier of the note to delete.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on deletion; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    public async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> DeleteAsync(
        long noteId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            LogNoteForbidden(nameof(DeleteAsync), noteId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be an approved club member to delete evaluation notes.");
        }

        return await ExecuteWithFreshContextAsync(
            (db, commitAttempted) => DeleteNoteAsync(db, noteId, actorUserId, clubId, commitAttempted, cancellationToken),
            db => VerifyDeleteCommittedAsync(db, noteId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one delete attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="noteId">The identifier of the note to delete.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on deletion; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> DeleteNoteAsync(
        NovaDbContext db,
        long noteId,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var note = await db.Notes
            .Include(n => n.PlayerCampaignAssignment)
                .ThenInclude(assignment => assignment.Campaign)
            .Include(n => n.PlayerCampaignAssignment)
                .ThenInclude(assignment => assignment.Player)
            .SingleOrDefaultAsync(n => n.NoteId == noteId, cancellationToken);

        if (note is null
            || note.ClubId != clubId
            || note.PlayerCampaignAssignment.ClubId != clubId
            || note.PlayerCampaignAssignment.Campaign.ClubId != clubId
            || note.PlayerCampaignAssignment.Player.ClubId != clubId)
        {
            LogNoteNotFound(nameof(DeleteAsync), noteId, clubId);
            return new NotFound();
        }

        var isAuthor = note.CreatedById == actorUserId;
        var isAdmin = currentUserProvider.IsClubAdmin;
        if (!isAuthor && !isAdmin)
        {
            LogNoteForbidden(nameof(DeleteAsync), noteId, actorUserId);
            return new LifecycleForbidden("Only the note author or a club administrator may delete evaluation notes.");
        }

        await db.AcquireCampaignMutationLockAsync(note.PlayerCampaignAssignment.CampaignId, cancellationToken);
        await db.Entry(note.PlayerCampaignAssignment.Campaign).ReloadAsync(cancellationToken);

        if (note.PlayerCampaignAssignment.Campaign.Status == CampaignStatus.Closed)
        {
            LogNoteCampaignClosed(nameof(DeleteAsync), noteId, note.PlayerCampaignAssignment.CampaignId);
            return new LifecycleConflict("Closed campaigns are read-only and cannot accept note deletions.");
        }

        db.Notes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);

        LogNoteDeleted(noteId, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Verifies whether a delete that may have committed ambiguously is absent from the tenant, and
    /// reconstructs the delete result when it is.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this verification attempt.</param>
    /// <param name="noteId">The identifier of the note that was deleted.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Whether the delete committed, along with the reconstructed result when it did.</returns>
    private static async Task<ExecutionResult<OneOf<
        Success,
        NotFound,
        LifecycleForbidden,
        LifecycleConflict>>> VerifyDeleteCommittedAsync(
            NovaDbContext db,
            long noteId,
            long clubId,
            CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == noteId && candidate.ClubId == clubId,
                cancellationToken);

        return note is not null
            ? new ExecutionResult<OneOf<
                Success,
                NotFound,
                LifecycleForbidden,
                LifecycleConflict>>(successful: false, default!)
            : new ExecutionResult<OneOf<
                Success,
                NotFound,
                LifecycleForbidden,
                LifecycleConflict>>(successful: true, new Success());
    }

    /// <summary>
    /// Runs an evaluation-note mutation inside EF Core's retrying execution strategy and verifies
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

        // Records whether the most recent attempt reached CommitAsync. Verification is only
        // meaningful for that attempt: a transient failure raised before the commit cannot have
        // applied the mutation, so the observed state belongs to some earlier request and must
        // not be mistaken for this one's ambiguous commit.
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

    /// <summary>Logs a note mutation request that failed input validation.</summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="subjectId">The note or participation identifier provided in the request.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation note validation failed for {Operation} on SubjectId={SubjectId}.")]
    private partial void LogNoteValidationFailed(string operation, long subjectId);

    /// <summary>Logs a note mutation request rejected because the caller lacks authorization.</summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="subjectId">The note or participation identifier provided in the request.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation note mutation forbidden for {Operation} on SubjectId={SubjectId} by UserId={UserId}.")]
    private partial void LogNoteForbidden(string operation, long subjectId, long userId);

    /// <summary>Logs a note mutation request whose target is unavailable in the current tenant.</summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="subjectId">The note or participation identifier provided in the request.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation note {Operation} target SubjectId={SubjectId} was not found for ClubId={ClubId}.")]
    private partial void LogNoteNotFound(string operation, long subjectId, long clubId);

    /// <summary>Logs a note mutation request rejected because its campaign is closed.</summary>
    /// <param name="operation">The operation name.</param>
    /// <param name="subjectId">The note or participation identifier provided in the request.</param>
    /// <param name="campaignId">The closed campaign identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Evaluation note {Operation} rejected for SubjectId={SubjectId} because CampaignId={CampaignId} is closed.")]
    private partial void LogNoteCampaignClosed(string operation, long subjectId, long campaignId);

    /// <summary>Logs a successfully created evaluation note.</summary>
    /// <param name="noteId">The new note identifier.</param>
    /// <param name="assignmentId">The campaign participation identifier the note was added to.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation note NoteId={NoteId} added to AssignmentId={AssignmentId} by UserId={ActorUserId}.")]
    private partial void LogNoteAdded(long noteId, long assignmentId, long actorUserId);

    /// <summary>Logs a successfully edited evaluation note.</summary>
    /// <param name="noteId">The edited note identifier.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation note NoteId={NoteId} edited by UserId={ActorUserId}.")]
    private partial void LogNoteEdited(long noteId, long actorUserId);

    /// <summary>Logs a successfully deleted evaluation note.</summary>
    /// <param name="noteId">The deleted note identifier.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation note NoteId={NoteId} deleted by UserId={ActorUserId}.")]
    private partial void LogNoteDeleted(long noteId, long actorUserId);
}
