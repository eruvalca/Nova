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

        return await ExecuteWithFreshContextAsync(
            db => AddNoteAsync(db, input, actorUserId, clubId, cancellationToken),
            db => VerifyAddCommittedAsync(db, input, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one add attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="input">The validated note input.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on add; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    private async Task<OneOf<EvaluationNoteMutationSuccess, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>> AddNoteAsync(
        NovaDbContext db,
        AddEvaluationNoteInput input,
        long actorUserId,
        long clubId,
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
            PlayerCampaignAssignmentId = participation.PlayerCampaignAssignmentId,
            ClubId = default,
            CreatedById = default
        };
        db.Notes.Add(note);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogNoteAdded(note.NoteId, input.PlayerCampaignAssignmentId, actorUserId);
        return new EvaluationNoteMutationSuccess(note.NoteId);
    }

    /// <summary>
    /// Verifies whether an add that may have committed ambiguously is visible to the tenant, and
    /// reconstructs the add result when it is.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this verification attempt.</param>
    /// <param name="input">The validated note input.</param>
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
            AddEvaluationNoteInput input,
            long clubId,
            CancellationToken cancellationToken)
    {
        // Notes have no durable operation key, so the tenant-scoped content fingerprint identifies the
        // row this request created. Two concurrent identical adds could theoretically be credited to
        // one request; both rows carry the same content and assignment, so the reconstructed id remains
        // faithful, and the strategy replays for a genuine pre-commit failure. This cross-request race
        // is a named deferred follow-up rather than a schema change (issue 71 scopes out entities and
        // migrations).
        var note = await db.Notes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClubId == clubId
                    && candidate.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId
                    && candidate.Content == input.Content,
                cancellationToken);

        return note is null
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
                    new EvaluationNoteMutationSuccess(note.NoteId));
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

        return await ExecuteWithFreshContextAsync(
            db => EditNoteAsync(db, input, actorUserId, clubId, cancellationToken),
            db => VerifyEditCommittedAsync(db, input, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs one edit attempt inside a transaction on a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this attempt.</param>
    /// <param name="input">The validated note input.</param>
    /// <param name="actorUserId">The authorized acting user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on edit; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    private async Task<OneOf<Success, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>> EditNoteAsync(
        NovaDbContext db,
        EditEvaluationNoteInput input,
        long actorUserId,
        long clubId,
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

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogNoteEdited(input.NoteId, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Verifies whether an edit that may have committed ambiguously is visible to the tenant, and
    /// reconstructs the edit result when it is.
    /// </summary>
    /// <param name="db">The fresh tenant context created for this verification attempt.</param>
    /// <param name="input">The validated note input.</param>
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
            EditEvaluationNoteInput input,
            long clubId,
            CancellationToken cancellationToken)
    {
        var note = await db.Notes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == input.NoteId
                    && candidate.ClubId == clubId
                    && candidate.Content == input.Content,
                cancellationToken);

        return note is null
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
            db => DeleteNoteAsync(db, noteId, actorUserId, clubId, cancellationToken),
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
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// Success on deletion; not-found, forbidden, or conflict information otherwise.
    /// </returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> DeleteNoteAsync(
        NovaDbContext db,
        long noteId,
        long actorUserId,
        long clubId,
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
