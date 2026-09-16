using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Configurations;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Npgsql;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Represents every supported outcome of a campaign-close operation.
/// </summary>
[GenerateOneOf]
internal partial class CampaignCloseResult : OneOfBase<
    Success,
    NotFound,
    LifecycleForbidden,
    CampaignCloseBlocked,
    LifecycleConflict,
    LifecycleOutcomeUnknown>
{
}

/// <summary>An attempted commit could not be acknowledged; current lifecycle state cannot prove this operation.</summary>
internal readonly record struct LifecycleOutcomeUnknown;

/// <summary>
/// Applies tenant-safe campaign close and reopen lifecycle transitions with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for lifecycle mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for lifecycle outcomes.</param>
internal sealed partial class CampaignLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignLifecycleService> logger) : ICampaignLifecycleService
{
    /// <inheritdoc />
    async Task<ServiceResult<Success>> ICampaignLifecycleService.CloseAsync(
        long campaignId,
        CancellationToken cancellationToken)
    {
        var outcome = await CloseAsync(campaignId, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            blocked => ServiceProblem.Conflict(blocked.Detail, blocked.Errors),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            _ => ServiceProblem.ServerError("The lifecycle request outcome is unknown. Refresh the campaign before taking another action."));
    }

    /// <inheritdoc />
    async Task<ServiceResult<Success>> ICampaignLifecycleService.ReopenAsync(
        long campaignId,
        CancellationToken cancellationToken)
    {
        var outcome = await ReopenAsync(campaignId, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            _ => ServiceProblem.ServerError("The lifecycle request outcome is unknown. Refresh the campaign before taking another action."));
    }

    /// <summary>
    /// Closes a campaign only when every participant has a final outcome, every assigned placement remains eligible,
    /// and no assigned team is archived.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to close.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, blocker, conflict, or unknown commit acknowledgement.
    /// An unknown result may have committed; never replay it or infer success from mutable state.</returns>
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

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        // Without an operation receipt, an attempted commit cannot be replayed or proved by mutable state.
        var commitAttempted = new CommitAttemptTracker();
        return await strategy.ExecuteAsync(async token =>
        {
            commitAttempted.Reset();
            try
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await CloseAttemptAsync(db, campaignId, actorUserId, clubId, commitAttempted, token);
            }
            catch (Exception exception) when (commitAttempted.Attempted && exception is not OperationCanceledException)
            {
                LogCampaignLifecycleOutcomeUnknown(exception, campaignId, actorUserId);
                return (CampaignCloseResult)new LifecycleOutcomeUnknown();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Applies one campaign-close attempt inside a single transaction using a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="campaignId">The campaign identifier to close.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>The campaign-close result for this attempt.</returns>
#pragma warning disable MA0051 // Keep the guards, effects, and recovery result for this operation together.
    private async Task<CampaignCloseResult> CloseAttemptAsync(
#pragma warning restore MA0051
        NovaDbContext db,
        long campaignId,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireUserMembershipLockAsync(actorUserId, cancellationToken);
        await db.AcquireClubMembershipLockAsync(clubId, cancellationToken);
        if (!await IsCurrentAdministratorAsync(db, actorUserId, clubId, cancellationToken))
        {
            return new LifecycleForbidden("You must currently be a club administrator to change campaign lifecycle.");
        }
        await db.AcquireCampaignMutationLockAsync(campaignId, cancellationToken);

        if (!await IsCurrentAdministratorAsync(db, actorUserId, clubId, cancellationToken))
        {
            return new LifecycleForbidden("Your club administrator authority changed. Refresh the campaign.");
        }

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

        if (campaign.Status != CampaignStatus.Active)
        {
            LogCampaignLifecycleConflict(campaignId, campaign.Status);
            return new LifecycleConflict("Only an active campaign can be closed.");
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

            // The actor is a club member (tenant-visible), so the write context resolves the
            // snapshot deterministically.
            var actorName = await db.Users
                .Where(user => user.Id == actorUserId)
                .Select(user => user.FirstName + " " + user.LastName)
                .FirstOrDefaultAsync(cancellationToken) ?? "Unknown user";

            ActivityEventWriter.AppendCampaignLifecycle(
                db,
                campaign.ClubId,
                campaign.CampaignId,
                ActivityEventKind.CampaignClosed,
                actorUserId,
                actorName,
                campaign.Name);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                commitAttempted.MarkAttempted();
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
    /// <returns>Success, not found, forbidden, conflict, or unknown commit acknowledgement.
    /// An unknown result may have committed; never replay it or infer success from mutable state.</returns>
    public async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown>> ReopenAsync(
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

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        // Without an operation receipt, an attempted commit cannot be replayed or proved by mutable state.
        var commitAttempted = new CommitAttemptTracker();
        return await strategy.ExecuteAsync(async token =>
        {
            commitAttempted.Reset();
            try
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await ReopenAttemptAsync(db, campaignId, actorUserId, clubId, commitAttempted, token);
            }
            catch (Exception exception) when (commitAttempted.Attempted && exception is not OperationCanceledException)
            {
                LogCampaignLifecycleOutcomeUnknown(exception, campaignId, actorUserId);
                return (OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown>)new LifecycleOutcomeUnknown();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Applies one campaign-reopen attempt inside a single transaction using a fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="campaignId">The campaign identifier to reopen.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>The campaign-reopen result for this attempt.</returns>
#pragma warning disable MA0051 // Keep the guards, effects, and recovery result for this operation together.
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown>> ReopenAttemptAsync(
#pragma warning restore MA0051
        NovaDbContext db,
        long campaignId,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireUserMembershipLockAsync(actorUserId, cancellationToken);
        await db.AcquireClubMembershipLockAsync(clubId, cancellationToken);
        if (!await IsCurrentAdministratorAsync(db, actorUserId, clubId, cancellationToken))
        {
            return new LifecycleForbidden("You must currently be a club administrator to change campaign lifecycle.");
        }
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);
        await db.AcquireCampaignMutationLockAsync(campaignId, cancellationToken);

        if (!await IsCurrentAdministratorAsync(db, actorUserId, clubId, cancellationToken))
        {
            return new LifecycleForbidden("Your club administrator authority changed. Refresh the campaign.");
        }

        var campaign = await db.Campaigns
            .SingleOrDefaultAsync(candidate => candidate.CampaignId == campaignId, cancellationToken);

        if (campaign is null || campaign.ClubId != clubId)
        {
            LogCampaignNotFound(campaignId, clubId);
            return new NotFound();
        }

        var currentSeasonId = await db.Clubs
            .Where(club => club.ClubId == clubId)
            .Select(club => club.CurrentSeasonId)
            .SingleOrDefaultAsync(cancellationToken);
        var latestOpeningSequence = await db.Campaigns
            .Where(candidate => candidate.SeasonId == campaign.SeasonId)
            .MaxAsync(candidate => candidate.SeasonOpeningSequence, cancellationToken);
        var anotherActiveCampaign = await db.Campaigns.AnyAsync(candidate => candidate.CampaignId != campaignId
            && candidate.Status == CampaignStatus.Active, cancellationToken);
        var eligibility = CampaignReopenPolicy.Evaluate(campaign.Status, campaign.SeasonId, currentSeasonId,
            campaign.SeasonOpeningSequence, latestOpeningSequence, anotherActiveCampaign);
        var rejection = eligibility.Match<string?>(_ => null, blocked => blocked.Detail);
        if (rejection is not null)
        {
            return new LifecycleConflict(rejection);
        }

        campaign.Status = CampaignStatus.Active;
        campaign.ClosedAt = null;
        campaign.ClosedById = null;

        // The actor is a club member (tenant-visible), so the write context resolves the
        // snapshot deterministically.
        var actorName = await db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown user";

        ActivityEventWriter.AppendCampaignLifecycle(
            db,
            campaign.ClubId,
            campaign.CampaignId,
            ActivityEventKind.CampaignReopened,
            actorUserId,
            actorName,
            campaign.Name);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogCampaignLifecycleConcurrencyConflict(campaignId);
            return new LifecycleConflict("The campaign changed. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IsOneActiveCampaignViolation(exception))
        {
            LogCampaignActiveConflict(campaignId, clubId);
            return new LifecycleConflict("Another campaign is already active for this club.");
        }

        LogCampaignLifecycleChanged(campaignId, CampaignStatus.Active, actorUserId);
        return new Success();
    }
    /// <summary>Rechecks persisted club membership and administrator authority within the mutation locks.</summary>
    /// <returns>Whether the actor still belongs to this club and retains the administrator role.</returns>
    private static async Task<bool> IsCurrentAdministratorAsync(NovaDbContext db, long actor, long club, CancellationToken token)
        => await db.Users.AnyAsync(user => user.Id == actor && user.ClubId == club, token)
            && await PlacementMutationExecutor.IsAdministratorAsync(db, actor, token);

    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign lifecycle outcome unknown for CampaignId={CampaignId} by UserId={ActorUserId}; no automatic replay.")]
    private partial void LogCampaignLifecycleOutcomeUnknown(Exception exception, long campaignId, long actorUserId);

    /// <summary>Determines whether persistence failed on the one-Active-campaign unique index.</summary>
    /// <returns>Whether PostgreSQL reports the named unique-index violation.</returns>
    private static bool IsOneActiveCampaignViolation(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CampaignEntityConfiguration.OneActiveCampaignPerClubIndexName
        };

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

    /// <summary>Logs a reopen rejected because the campaign does not belong to the current season.</summary>
    /// <param name="campaignId">The historical campaign identifier.</param>
    /// <param name="seasonId">The campaign's season identifier.</param>
    /// <param name="currentSeasonId">The club's current season identifier, when one exists.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Campaign reopen rejected; CampaignId={CampaignId} belongs to SeasonId={SeasonId}, but CurrentSeasonId={CurrentSeasonId}.")]
    private partial void LogCampaignReopenHistoricalSeasonConflict(
        long campaignId,
        long seasonId,
        long? currentSeasonId);

    /// <summary>Logs a lifecycle transition rejected by the one-Active-campaign invariant.</summary>
    /// <param name="campaignId">The campaign requested for activation.</param>
    /// <param name="clubId">The owning club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "CampaignId={CampaignId} could not become Active because ClubId={ClubId} already has an Active campaign.")]
    private partial void LogCampaignActiveConflict(long campaignId, long clubId);

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

    /// <summary>
    /// Logs an ambiguous commit that verification confirmed had already applied the transition.
    /// </summary>
    /// <param name="campaignId">The verified campaign identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "CampaignId={CampaignId} lifecycle transition was already committed before the transient failure; skipping replay.")]
    private partial void LogCampaignLifecycleCommitVerified(long campaignId);
}
