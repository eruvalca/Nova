using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
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
            conflict => ServiceProblem.Conflict(conflict.Detail));
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
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

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

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        // Records whether the most recent attempt reached CommitAsync. Verification is only
        // meaningful for that attempt: a transient failure raised before the commit cannot have
        // applied the closure, so the observed status belongs to some earlier request and must
        // not be mistaken for this one's ambiguous commit.
        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (CampaignId: campaignId, ActorUserId: actorUserId, ClubId: clubId, CommitAttempted: commitAttempted),
            async (state, token) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await CloseAttemptAsync(
                    db,
                    state.CampaignId,
                    state.ActorUserId,
                    state.ClubId,
                    state.CommitAttempted,
                    token);
            },
            async (state, token) =>
            {
                if (!state.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<CampaignCloseResult>(successful: false, default!);
                }

                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await VerifyClosureCommittedAsync(
                    db,
                    state.CampaignId,
                    state.ActorUserId,
                    state.ClubId,
                    token);
            },
            cancellationToken);
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
    private async Task<CampaignCloseResult> CloseAttemptAsync(
        NovaDbContext db,
        long campaignId,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
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
    /// Determines whether an ambiguous close commit already applied the closure so the execution
    /// strategy can report success instead of replaying the attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context used for verification.</param>
    /// <param name="campaignId">The campaign identifier that was being closed.</param>
    /// <param name="actorUserId">The administrator who requested the closure.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>A successful result when the closure is already persisted; otherwise unsuccessful.</returns>
    private async Task<ExecutionResult<CampaignCloseResult>> VerifyClosureCommittedAsync(
        NovaDbContext db,
        long campaignId,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        var applied = await db.Campaigns
            .AsNoTracking()
            .Where(candidate => candidate.CampaignId == campaignId && candidate.ClubId == clubId)
            .Select(candidate => new { candidate.Status, candidate.ClosedById })
            .SingleOrDefaultAsync(cancellationToken);

        if (applied is { Status: CampaignStatus.Closed, ClosedById: var closedById }
            && closedById == actorUserId)
        {
            LogCampaignLifecycleCommitVerified(campaignId);
            return new ExecutionResult<CampaignCloseResult>(successful: true, new Success());
        }

        return new ExecutionResult<CampaignCloseResult>(successful: false, default!);
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

        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        // Records whether the most recent attempt reached CommitAsync. Verification is only
        // meaningful for that attempt: a transient failure raised before the commit cannot have
        // applied the reopen, so the observed status belongs to some earlier request and must
        // not be mistaken for this one's ambiguous commit.
        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (CampaignId: campaignId, ActorUserId: actorUserId, ClubId: clubId, CommitAttempted: commitAttempted),
            async (state, token) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await ReopenAttemptAsync(
                    db,
                    state.CampaignId,
                    state.ActorUserId,
                    state.ClubId,
                    state.CommitAttempted,
                    token);
            },
            async (state, token) =>
            {
                if (!state.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>(
                        successful: false,
                        default!);
                }

                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await VerifyReopenCommittedAsync(db, state.CampaignId, state.ClubId, token);
            },
            cancellationToken);
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
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ReopenAttemptAsync(
        NovaDbContext db,
        long campaignId,
        long actorUserId,
        long clubId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);
        await db.AcquireCampaignMutationLockAsync(campaignId, cancellationToken);

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
        if (campaign.SeasonId != currentSeasonId)
        {
            LogCampaignReopenHistoricalSeasonConflict(campaignId, campaign.SeasonId, currentSeasonId);
            return new LifecycleConflict(
                "Only a campaign in the club's current season can be reopened.");
        }

        if (campaign.Status == CampaignStatus.Active)
        {
            LogCampaignLifecycleConflict(campaignId, CampaignStatus.Active);
            return new LifecycleConflict("The campaign is already active.");
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

        LogCampaignLifecycleChanged(campaignId, CampaignStatus.Active, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Determines whether an ambiguous reopen commit already applied the transition so the execution
    /// strategy can report success instead of replaying the attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context used for verification.</param>
    /// <param name="campaignId">The campaign identifier that was being reopened.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>A successful result when the reopen is already persisted; otherwise unsuccessful.</returns>
    private async Task<ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>> VerifyReopenCommittedAsync(
        NovaDbContext db,
        long campaignId,
        long clubId,
        CancellationToken cancellationToken)
    {
        var applied = await db.Campaigns
            .AsNoTracking()
            .Where(candidate => candidate.CampaignId == campaignId && candidate.ClubId == clubId)
            .Select(candidate => new { candidate.Status, candidate.ClosedById })
            .SingleOrDefaultAsync(cancellationToken);

        if (applied is { Status: CampaignStatus.Active, ClosedById: null })
        {
            LogCampaignLifecycleCommitVerified(campaignId);
            return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>(
                successful: true,
                new Success());
        }

        return new ExecutionResult<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>>(
            successful: false,
            default!);
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
