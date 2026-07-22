using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Represents every supported outcome of a campaign-close operation.
/// </summary>
[GenerateOneOf]
public partial class CampaignCloseResult : OneOfBase<
    Success,
    NotFound,
    LifecycleForbidden,
    CampaignCloseBlocked,
    LifecycleConflict>
{
}

/// <summary>
/// Applies tenant-safe campaign close and reopen lifecycle transitions with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for lifecycle mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for lifecycle outcomes.</param>
public sealed partial class CampaignLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignLifecycleService> logger)
{
    /// <summary>
    /// Closes a campaign only when every participant has a final outcome, every assigned placement remains eligible,
    /// and no assigned team is archived.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to close.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, blocker, or conflict information.</returns>
    public async Task<CampaignCloseResult> CloseAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCampaignLifecycleForbidden(campaignId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to close a campaign.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireCampaignMutationLockAsync(campaignId, cancellationToken);

        var campaign = await db.Campaigns
            .SingleOrDefaultAsync(candidate => candidate.CampaignId == campaignId, cancellationToken);

        if (campaign is null || campaign.ClubId != clubId)
        {
            LogCampaignNotFound(campaignId, clubId);
            return new NotFound();
        }

        if (campaign.Status == CampaignStatus.Closed)
        {
            LogCampaignLifecycleConflict(campaignId, CampaignStatus.Closed);
            return new LifecycleConflict("The campaign is already closed.");
        }

        var assignmentStates = await db.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == campaignId)
            .Select(assignment => new CampaignAssignmentClosureState(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlacementOutcome,
                assignment.Player.GraduationYear,
                assignment.TeamId,
                assignment.Team == null ? null : assignment.Team.GraduationYear,
                assignment.Team == null ? null : assignment.Team.LifecycleStatus))
            .ToListAsync(cancellationToken);

        var closureDecision = CampaignClosurePolicy.Evaluate(assignmentStates);
        return await closureDecision.Match(ApplyClosureAsync, RejectClosureAsync);

        async Task<CampaignCloseResult> ApplyClosureAsync(CampaignMayClose _)
        {
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow;
            campaign.ClosedById = actorUserId;

            db.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
            {
                CampaignId = campaign.CampaignId,
                ClubId = campaign.ClubId,
                EventType = CampaignLifecycleEventType.Closed,
                CreatedById = default
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                LogCampaignLifecycleConcurrencyConflict(campaignId);
                return new LifecycleConflict("The campaign changed. Reload it and try again.");
            }

            LogCampaignLifecycleChanged(campaignId, CampaignStatus.Closed, actorUserId);
            return new Success();
        }

        Task<CampaignCloseResult> RejectClosureAsync(CampaignCloseBlocked blocked)
        {
            LogCampaignCloseBlocked(
                campaignId,
                blocked.UndecidedCount,
                blocked.IneligibleCount,
                blocked.ArchivedTeamCount);
            return Task.FromResult<CampaignCloseResult>(blocked);
        }
    }

    /// <summary>
    /// Reopens a closed campaign and records the transition as an append-only lifecycle event.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to reopen.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ReopenAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCampaignLifecycleForbidden(campaignId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to reopen a campaign.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireCampaignMutationLockAsync(campaignId, cancellationToken);

        var campaign = await db.Campaigns
            .SingleOrDefaultAsync(candidate => candidate.CampaignId == campaignId, cancellationToken);

        if (campaign is null || campaign.ClubId != clubId)
        {
            LogCampaignNotFound(campaignId, clubId);
            return new NotFound();
        }

        if (campaign.Status == CampaignStatus.Active)
        {
            LogCampaignLifecycleConflict(campaignId, CampaignStatus.Active);
            return new LifecycleConflict("The campaign is already active.");
        }

        campaign.Status = CampaignStatus.Active;
        campaign.ClosedAt = null;
        campaign.ClosedById = null;

        db.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
        {
            CampaignId = campaign.CampaignId,
            ClubId = campaign.ClubId,
            EventType = CampaignLifecycleEventType.Reopened,
            CreatedById = default
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogCampaignLifecycleConcurrencyConflict(campaignId);
            return new LifecycleConflict("The campaign changed. Reload it and try again.");
        }

        LogCampaignLifecycleChanged(campaignId, CampaignStatus.Active, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Logs a lifecycle request rejected because the caller is not a club administrator.
    /// </summary>
    /// <param name="campaignId">The requested campaign identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign lifecycle mutation forbidden for CampaignId={CampaignId} by UserId={UserId}.")]
    private partial void LogCampaignLifecycleForbidden(long campaignId, long userId);

    /// <summary>
    /// Logs a lifecycle request whose campaign is unavailable in the current tenant.
    /// </summary>
    /// <param name="campaignId">The requested campaign identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "CampaignId={CampaignId} was not found for ClubId={ClubId}.")]
    private partial void LogCampaignNotFound(long campaignId, long clubId);

    /// <summary>
    /// Logs a campaign close request blocked by participation readiness or assignment integrity rules.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="undecidedCount">The number of undecided participation rows.</param>
    /// <param name="ineligibleCount">The number of ineligible assigned participation rows.</param>
    /// <param name="archivedTeamCount">The number of assigned participation rows referencing archived teams.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign close blocked for CampaignId={CampaignId}. Undecided={UndecidedCount}, Ineligible={IneligibleCount}, ArchivedTeam={ArchivedTeamCount}.")]
    private partial void LogCampaignCloseBlocked(long campaignId, int undecidedCount, int ineligibleCount, int archivedTeamCount);

    /// <summary>
    /// Logs a redundant campaign lifecycle transition.
    /// </summary>
    /// <param name="campaignId">The requested campaign identifier.</param>
    /// <param name="status">The already-current lifecycle status.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "CampaignId={CampaignId} is already in lifecycle status {Status}.")]
    private partial void LogCampaignLifecycleConflict(long campaignId, CampaignStatus status);

    /// <summary>
    /// Logs a lifecycle transition rejected because the campaign changed concurrently.
    /// </summary>
    /// <param name="campaignId">The concurrently changed campaign identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign lifecycle concurrency conflict for CampaignId={CampaignId}.")]
    private partial void LogCampaignLifecycleConcurrencyConflict(long campaignId);

    /// <summary>
    /// Logs a successful campaign lifecycle transition.
    /// </summary>
    /// <param name="campaignId">The changed campaign identifier.</param>
    /// <param name="status">The applied lifecycle status.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "CampaignId={CampaignId} lifecycle changed to {Status} by UserId={ActorUserId}.")]
    private partial void LogCampaignLifecycleChanged(long campaignId, CampaignStatus status, long actorUserId);
}
