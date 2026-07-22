using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Describes a request to apply a tag definition to a campaign participation.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The participation identifier to tag.</param>
/// <param name="PlayerTagId">The tag-definition identifier to apply.</param>
public sealed record ApplyCampaignTagApplicationInput(long PlayerCampaignAssignmentId, long PlayerTagId);

/// <summary>
/// Describes a request to remove one campaign tag application.
/// </summary>
/// <param name="CampaignTagApplicationId">The campaign tag application identifier to remove.</param>
public sealed record RemoveCampaignTagApplicationInput(long CampaignTagApplicationId);

/// <summary>
/// Reports the created campaign tag application identifier.
/// </summary>
/// <param name="CampaignTagApplicationId">The created campaign tag application identifier.</param>
public readonly record struct CampaignTagApplicationMutationSuccess(long CampaignTagApplicationId);

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
    ILogger<CampaignTagApplicationService> logger)
{
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
        var errors = Validate(input);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        var application = new Entities.CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = input.PlayerCampaignAssignmentId,
            PlayerTagId = input.PlayerTagId,
            ClubId = clubId,
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
        var errors = Validate(input);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        db.CampaignTagApplications.Remove(application);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogRemoveSucceeded(input.CampaignTagApplicationId, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Validates apply-input values that do not require database access.
    /// </summary>
    /// <param name="input">The apply-input values to validate.</param>
    /// <returns>A field-keyed validation-error dictionary.</returns>
    private static Dictionary<string, string[]> Validate(ApplyCampaignTagApplicationInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.PlayerCampaignAssignmentId <= 0)
        {
            errors[nameof(input.PlayerCampaignAssignmentId)] = ["A campaign participation identifier is required."];
        }

        if (input.PlayerTagId <= 0)
        {
            errors[nameof(input.PlayerTagId)] = ["A tag-definition identifier is required."];
        }

        return errors;
    }

    /// <summary>
    /// Validates remove-input values that do not require database access.
    /// </summary>
    /// <param name="input">The remove-input values to validate.</param>
    /// <returns>A field-keyed validation-error dictionary.</returns>
    private static Dictionary<string, string[]> Validate(RemoveCampaignTagApplicationInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.CampaignTagApplicationId <= 0)
        {
            errors[nameof(input.CampaignTagApplicationId)] = ["A campaign tag application identifier is required."];
        }

        return errors;
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
}
