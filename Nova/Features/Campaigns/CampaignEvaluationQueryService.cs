using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Campaigns;

/// <summary>Reads independently bounded evidence and complete trait choices for one authorized participant.</summary>
/// <param name="factory">The read-only tenant context factory.</param>
/// <param name="currentUser">The authenticated scope.</param>
internal sealed class CampaignEvaluationQueryService(IDbContextFactory<NovaReadDbContext> factory, ICurrentUserProvider currentUser)
    : ICampaignEvaluationQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>> GetNotesAsync(GetEvaluationHistoryInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var access = await GetAccessAsync(db, input, cancellationToken);
        if (access.IsProblem)
        {
            return access.Problem;
        }

        var query = db.Notes.AsNoTracking().Where(note => note.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId);
        List<NoteEntity> rows;
        if (db.Database.IsNpgsql())
        {
            if (input.BeforeCreatedAt?.ToUniversalTime() is { } before && input.BeforeId is { } beforeId)
            {
                query = query.Where(note => note.CreatedAt < before || (note.CreatedAt == before && note.NoteId < beforeId));
            }

            rows = await query.OrderByDescending(note => note.CreatedAt).ThenByDescending(note => note.NoteId).Take(21).ToListAsync(cancellationToken);
        }
        else
        {
            // SQLite's unit harness cannot compare DateTimeOffset; production always pages in PostgreSQL.
            rows = (await query.ToListAsync(cancellationToken)).Where(note => IsBefore(note.CreatedAt, note.NoteId, input))
                .OrderByDescending(note => note.CreatedAt).ThenByDescending(note => note.NoteId).Take(21).ToList();
        }

        var items = rows.Take(20).Select(note => new CampaignParticipantNoteDto(note.NoteId, note.Content,
            note.AuthorDisplayName, note.CreatedAt, note.ModifiedAt,
            access.Value.Writable && note.CreatedById == access.Value.Actor,
            access.Value.Writable && note.CreatedById == access.Value.Actor, note.Version)).ToList();
        return new EvaluationHistoryPage<CampaignParticipantNoteDto>(items,
            rows.Count > 20 ? new EvaluationHistoryCursor(items[^1].CreatedAt, items[^1].NoteId) : null);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>> GetApplicationsAsync(GetEvaluationHistoryInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var access = await GetAccessAsync(db, input, cancellationToken);
        if (access.IsProblem)
        {
            return access.Problem;
        }

        var query = db.CampaignTagApplications.AsNoTracking().Include(application => application.PlayerTag)
            .Where(application => application.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId);
        List<CampaignTagApplicationEntity> rows;
        if (db.Database.IsNpgsql())
        {
            if (input.BeforeCreatedAt?.ToUniversalTime() is { } before && input.BeforeId is { } beforeId)
            {
                query = query.Where(application => application.CreatedAt < before || (application.CreatedAt == before && application.CampaignTagApplicationId < beforeId));
            }

            rows = await query.OrderByDescending(application => application.CreatedAt).ThenByDescending(application => application.CampaignTagApplicationId)
                .Take(21).ToListAsync(cancellationToken);
        }
        else
        {
            rows = (await query.ToListAsync(cancellationToken)).Where(application => IsBefore(application.CreatedAt, application.CampaignTagApplicationId, input))
                .OrderByDescending(application => application.CreatedAt).ThenByDescending(application => application.CampaignTagApplicationId).Take(21).ToList();
        }

        var items = rows.Take(20).Select(application => new CampaignParticipantTagApplicationDto(application.CampaignTagApplicationId,
            application.PlayerTagId, application.PlayerTag.Name, application.PlayerTag.Color,
            application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived, application.AuthorDisplayName,
            application.CreatedAt, access.Value.Writable && application.PlayerTag.LifecycleStatus == LifecycleStatus.Active
                && (application.CreatedById == access.Value.Actor || access.Value.IsAdministrator))).ToList();
        return new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>(items,
            rows.Count > 20 ? new EvaluationHistoryCursor(items[^1].AppliedAt, items[^1].CampaignTagApplicationId) : null);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<EvaluationTagChoice>>> GetTagChoicesAsync(GetCampaignParticipantDetailInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var access = await GetAccessAsync(db, new GetEvaluationHistoryInput
        {
            CampaignId = input.CampaignId,
            PlayerCampaignAssignmentId = input.PlayerCampaignAssignmentId
        }, cancellationToken);
        if (access.IsProblem)
        {
            return access.Problem;
        }

        var choices = await db.PlayerTags.AsNoTracking().Where(tag => tag.LifecycleStatus == LifecycleStatus.Active)
            .OrderBy(tag => tag.Name).ThenBy(tag => tag.PlayerTagId)
            .Select(tag => new EvaluationTagChoice(tag.PlayerTagId, tag.Name, tag.Color,
                tag.CampaignTagApplications.Where(application => application.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId)
                    .Select(application => (long?)application.CampaignTagApplicationId).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return choices.AsReadOnly();
    }

    /// <summary>Rechecks persisted membership, tenant visibility and current capabilities for every region.</summary>
    private async Task<ServiceResult<EvidenceAccess>> GetAccessAsync(NovaReadDbContext db, GetEvaluationHistoryInput input, CancellationToken token)
    {
        if (currentUser.UserId is not long actor || currentUser.ClubId is not long club
            || !await db.Users.AnyAsync(user => user.Id == actor && user.ClubId == club, token))
        {
            return ServiceProblem.Forbidden("You must currently belong to this club to read evaluation evidence.");
        }

        var participant = await db.PlayerCampaignAssignments.Where(assignment => assignment.ClubId == club
                && assignment.Campaign.ClubId == club && assignment.Player.ClubId == club
                && assignment.CampaignId == input.CampaignId && assignment.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId)
            .Select(assignment => new { assignment.Campaign.Status, assignment.Player.LifecycleStatus }).SingleOrDefaultAsync(token);
        if (participant is null)
        {
            return ServiceProblem.NotFound();
        }

        var normalizedRole = Roles.ClubAdmin.ToUpperInvariant();
        var administrator = await (from userRole in db.UserRoles
                                   join role in db.Roles on userRole.RoleId equals role.Id
                                   where userRole.UserId == actor && role.NormalizedName == normalizedRole
                                   select userRole.UserId).AnyAsync(token);
        return new EvidenceAccess(actor, participant.Status == CampaignStatus.Active && participant.LifecycleStatus == LifecycleStatus.Active, administrator);
    }

    /// <summary>Applies the same exclusive cursor to the SQLite harness's in-memory ordering.</summary>
    private static bool IsBefore(DateTimeOffset createdAt, long id, GetEvaluationHistoryInput input) =>
        input.BeforeCreatedAt is not { } before || createdAt < before || (createdAt == before && id < input.BeforeId);

    /// <summary>Current authority for this evidence read.</summary>
    private sealed record EvidenceAccess(long Actor, bool Writable, bool IsAdministrator);
}
