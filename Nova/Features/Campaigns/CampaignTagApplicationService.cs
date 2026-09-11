using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;

namespace Nova.Features.Campaigns;

/// <summary>Applies and removes shared traits with atomic definition resolution and durable replay.</summary>
/// <param name="dbContextFactory">The tenant context factory.</param>
/// <param name="currentUserProvider">The authenticated scope.</param>
/// <param name="logger">The structured mutation logger.</param>
internal sealed partial class CampaignTagApplicationService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignTagApplicationService> logger) : ICampaignTagApplicationService
{
    private readonly EvaluationMutationExecutor _executor = new(dbContextFactory, currentUserProvider);

    /// <inheritdoc />
    public Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(ApplyCampaignTagApplicationInput input, CancellationToken cancellationToken = default) =>
        ApplyAsync(input, input.PlayerCampaignAssignmentId, input.PlayerTagId, label: null, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<CampaignTagApplicationMutationSuccess>> CreateAndApplyAsync(CreateAndApplyCampaignTagInput input, CancellationToken cancellationToken = default) =>
        ApplyAsync(input, input.PlayerCampaignAssignmentId, tagId: null, input.Label, cancellationToken);

    /// <summary>Resolves a definition and application within the same locked transaction as the receipt.</summary>
    private Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(EvaluationOperationInput input, long assignmentId,
        long? tagId, string? label, CancellationToken token) =>
        _executor.ExecuteAsync(input, async (db, actor, club, expiresAt) =>
        {
            var participant = await EvaluationMutationExecutor.GetWritableParticipantAsync(db, assignmentId, club, token);
            if (participant.IsProblem)
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(participant.Problem);
            }

            var resolved = await ResolveDefinitionAsync(db, tagId, label, input.OperationId, actor, club, token);
            if (resolved.IsProblem)
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(resolved.Problem);
            }

            var tag = resolved.Value;
            var application = await db.CampaignTagApplications.SingleOrDefaultAsync(candidate =>
                candidate.PlayerCampaignAssignmentId == assignmentId && candidate.PlayerTagId == tag.PlayerTagId, token);
            var alreadyApplied = application is not null;
            if (application is null)
            {
                application = new CampaignTagApplicationEntity
                {
                    PlayerCampaignAssignmentId = assignmentId,
                    AuthorDisplayName = await EvaluationMutationExecutor.GetActorDisplayNameAsync(db, actor, token),
                    PlayerTagId = tag.PlayerTagId,
                    ClubId = club,
                    CreatedById = actor,
                    CreationOperationId = input.OperationId
                };
                db.CampaignTagApplications.Add(application);
                await db.SaveChangesAsync(token);
            }

            return Complete(application, input.OperationId, alreadyApplied, expiresAt);
        }, token);

    /// <summary>Preserves first-created casing, rejects archived names, and keeps existing tags usable at the cap.</summary>
    private static async Task<ServiceResult<PlayerTagEntity>> ResolveDefinitionAsync(NovaDbContext db, long? tagId,
        string? label, Guid operationId, long actor, long club, CancellationToken token)
    {
        var displayName = label is null ? null : CollaborativeTagPolicy.NormalizeDisplayName(label);
        var normalizedName = displayName is null ? null : CollaborativeTagPolicy.NormalizeKey(displayName);
        var existingId = tagId ?? await db.PlayerTags.Where(tag => tag.NormalizedName == normalizedName)
            .Select(tag => (long?)tag.PlayerTagId).SingleOrDefaultAsync(token);
        if (existingId.HasValue)
        {
            await db.AcquireTagMutationLockAsync(existingId.Value, token);
            var existing = await db.PlayerTags.SingleOrDefaultAsync(tag => tag.PlayerTagId == existingId.Value, token);
            if (existing is null)
            {
                return ServiceProblem.NotFound();
            }

            return existing.LifecycleStatus == LifecycleStatus.Archived
                ? ServiceProblem.Conflict("This trait is archived. Ask a club administrator to restore it before applying it.")
                : existing;
        }

        if (tagId.HasValue || displayName is null)
        {
            return ServiceProblem.NotFound();
        }

        if (await db.PlayerTags.CountAsync(tag => tag.LifecycleStatus == LifecycleStatus.Active, token) >= TagDefinitionLimits.MaxActiveTagDefinitions)
        {
            return ServiceProblem.Conflict($"This club has {TagDefinitionLimits.MaxActiveTagDefinitions} active traits. Apply an existing trait, or ask an administrator to archive an unused definition.");
        }

        var created = new PlayerTagEntity
        {
            Name = displayName,
            NormalizedName = normalizedName!,
            Color = CollaborativeTagPolicy.DefaultColor,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club,
            CreatedById = actor,
            CreationOperationId = operationId
        };
        db.PlayerTags.Add(created);
        await db.SaveChangesAsync(token);
        return created;
    }

    /// <inheritdoc />
    public Task<ServiceResult<CampaignTagApplicationMutationSuccess>> RemoveAsync(RemoveCampaignTagApplicationInput input, CancellationToken cancellationToken = default) =>
        _executor.ExecuteAsync(input, async (db, actor, club, expiresAt) =>
        {
            var identity = await db.CampaignTagApplications.Where(application => application.CampaignTagApplicationId == input.CampaignTagApplicationId)
                .Select(application => new { application.PlayerCampaignAssignmentId, application.PlayerTagId }).SingleOrDefaultAsync(cancellationToken);
            if (identity is null)
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(ServiceProblem.NotFound());
            }

            var participant = await EvaluationMutationExecutor.GetWritableParticipantAsync(db, identity.PlayerCampaignAssignmentId, club, cancellationToken);
            if (participant.IsProblem)
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(participant.Problem);
            }

            await db.AcquireTagMutationLockAsync(identity.PlayerTagId, cancellationToken);
            var application = await db.CampaignTagApplications.Include(candidate => candidate.PlayerTag)
                .SingleOrDefaultAsync(candidate => candidate.CampaignTagApplicationId == input.CampaignTagApplicationId, cancellationToken);
            if (application is null)
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(ServiceProblem.NotFound());
            }

            if (application.CreatedById != actor && !await EvaluationMutationExecutor.IsAdministratorAsync(db, actor, cancellationToken))
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(ServiceProblem.Forbidden("Only the person who applied this trait or a club administrator can remove it."));
            }

            if (application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived)
            {
                return new ServiceResult<CampaignTagApplicationMutationSuccess>(ServiceProblem.Conflict("Archived trait applications remain part of the shared history and cannot be removed."));
            }

            db.CampaignTagApplications.Remove(application);
            await db.SaveChangesAsync(cancellationToken);
            return Complete(application, input.OperationId, alreadyApplied: false, expiresAt);
        }, cancellationToken);

    /// <summary>Returns immutable proof without changing the ownership of an existing application.</summary>
    private ServiceResult<CampaignTagApplicationMutationSuccess> Complete(CampaignTagApplicationEntity application,
        Guid operationId, bool alreadyApplied, DateTimeOffset expiresAt)
    {
        LogTagMutationPrepared(operationId, application.CampaignTagApplicationId);
        return new CampaignTagApplicationMutationSuccess(application.CampaignTagApplicationId, application.PlayerTagId, alreadyApplied,
            new EvaluationMutationReceipt(operationId, application.PlayerCampaignAssignmentId, DateTimeOffset.UtcNow, expiresAt));
    }

    /// <summary>Logs preparation without logging trait text or claiming commitment.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Evaluation operation {OperationId} prepared for ApplicationId={ApplicationId}.")]
    private partial void LogTagMutationPrepared(Guid operationId, long applicationId);
}
