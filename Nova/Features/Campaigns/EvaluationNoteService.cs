using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;

namespace Nova.Features.Campaigns;

/// <summary>Applies author-only versioned notes with immutable, client-replayable receipts.</summary>
/// <param name="dbContextFactory">The tenant context factory.</param>
/// <param name="currentUserProvider">The authenticated scope.</param>
/// <param name="logger">The structured mutation logger.</param>
internal sealed partial class EvaluationNoteService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<EvaluationNoteService> logger) : ICampaignEvaluationNoteService
{
    private readonly EvaluationMutationExecutor _executor = new(dbContextFactory, currentUserProvider);

    /// <inheritdoc />
    public Task<ServiceResult<EvaluationNoteMutationSuccess>> AddAsync(AddEvaluationNoteInput input, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(input, async (db, actor, club, expiresAt) =>
        {
            var participant = await EvaluationMutationExecutor.GetWritableParticipantAsync(db, input.PlayerCampaignAssignmentId, club, cancellationToken);
            if (participant.IsProblem)
            {
                return new ServiceResult<EvaluationNoteMutationSuccess>(participant.Problem);
            }

            var note = new NoteEntity
            {
                Content = input.Content,
                CreationOperationId = input.OperationId,
                PlayerCampaignAssignmentId = input.PlayerCampaignAssignmentId,
                ClubId = club,
                CreatedById = actor
            };
            db.Notes.Add(note);
            await db.SaveChangesAsync(cancellationToken);
            return Complete(note, input.OperationId, expiresAt);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<EvaluationNoteMutationSuccess>> EditAsync(EditEvaluationNoteInput input, CancellationToken cancellationToken = default) =>
        ChangeAsync(input, input.NoteId, input.ExpectedVersion, input.Content, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<EvaluationNoteMutationSuccess>> DeleteAsync(DeleteEvaluationNoteInput input, CancellationToken cancellationToken = default) =>
        ChangeAsync(input, input.NoteId, input.ExpectedVersion, content: null, cancellationToken);

    /// <summary>Changes only the author's reviewed version under campaign and player locks.</summary>
    private Task<ServiceResult<EvaluationNoteMutationSuccess>> ChangeAsync(EvaluationOperationInput input, long noteId,
        Guid expectedVersion, string? content, CancellationToken token) =>
        _executor.ExecuteAsync(input, async (db, actor, club, expiresAt) =>
        {
            var assignmentId = await db.Notes.Where(note => note.NoteId == noteId)
                .Select(note => (long?)note.PlayerCampaignAssignmentId).SingleOrDefaultAsync(token);
            if (assignmentId is null)
            {
                return new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.NotFound());
            }

            var participant = await EvaluationMutationExecutor.GetWritableParticipantAsync(db, assignmentId.Value, club, token);
            if (participant.IsProblem)
            {
                return new ServiceResult<EvaluationNoteMutationSuccess>(participant.Problem);
            }

            var note = await db.Notes.SingleOrDefaultAsync(candidate => candidate.NoteId == noteId, token);
            if (note is null)
            {
                return new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.NotFound());
            }

            if (note.CreatedById != actor)
            {
                return new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.Forbidden("Only the author can edit or delete this shared note."));
            }

            if (note.Version != expectedVersion)
            {
                return new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.Conflict("This note changed after you opened it. Refresh the note and review the newer version; your draft is still available."));
            }

            if (content is null)
            {
                db.Notes.Remove(note);
            }
            else
            {
                note.Content = content;
                note.Version = Guid.NewGuid();
            }

            await db.SaveChangesAsync(token);
            return Complete(note, input.OperationId, expiresAt);
        }, token);

    /// <summary>Builds the original operation result independently of later note changes.</summary>
    private ServiceResult<EvaluationNoteMutationSuccess> Complete(NoteEntity note, Guid operationId, DateTimeOffset expiresAt)
    {
        LogNoteMutationPrepared(operationId, note.NoteId, note.PlayerCampaignAssignmentId);
        return new EvaluationNoteMutationSuccess(note.NoteId, note.Version,
            new EvaluationMutationReceipt(operationId, note.PlayerCampaignAssignmentId, DateTimeOffset.UtcNow, expiresAt));
    }

    /// <summary>Logs transactional preparation without logging note content or claiming commitment.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation operation {OperationId} prepared for NoteId={NoteId}, AssignmentId={AssignmentId}.")]
    private partial void LogNoteMutationPrepared(Guid operationId, long noteId, long assignmentId);
}
