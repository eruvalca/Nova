using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Adds Draft opening and deletion behavior to the campaign lifecycle service.
/// </summary>
public sealed partial class CampaignLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<OpenCampaignResult>> OpenAsync(
        long campaignId,
        OpenCampaignInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCampaignOpenForbidden(campaignId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to open a campaign.");
        }

        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (CampaignId: campaignId, Input: input, ActorUserId: actorUserId, ClubId: clubId),
            async (state, token) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await OpenAttemptAsync(
                    db,
                    state.CampaignId,
                    state.Input,
                    state.ActorUserId,
                    state.ClubId,
                    token);
            },
            async (state, token) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                var receipt = await FindOpeningReceiptAsync(
                    db,
                    state.ClubId,
                    state.CampaignId,
                    state.Input.OperationId,
                    token);
                return receipt.IsSuccess
                    ? new ExecutionResult<ServiceResult<OpenCampaignResult>>(true, receipt)
                    : new ExecutionResult<ServiceResult<OpenCampaignResult>>(false, default!);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> DeleteDraftAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCampaignDeleteForbidden(campaignId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to delete a Draft campaign.");
        }

        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        var commitAttempted = new CommitAttemptTracker();
        return await strategy.ExecuteAsync(
            (CampaignId: campaignId, ActorUserId: actorUserId, ClubId: clubId, CommitAttempted: commitAttempted),
            async (state, token) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await DeleteDraftAttemptAsync(
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
                    return new ExecutionResult<ServiceResult<Success>>(false, default!);
                }

                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                var deleted = !await db.Campaigns.AnyAsync(
                    campaign => campaign.CampaignId == state.CampaignId && campaign.ClubId == state.ClubId,
                    token);
                var tombstone = await HasDeletionTombstoneAsync(db, state.CampaignId, token);
                return deleted && tombstone
                    ? new ExecutionResult<ServiceResult<Success>>(true, new Success())
                    : new ExecutionResult<ServiceResult<Success>>(false, default!);
            },
            cancellationToken);
    }

    /// <summary>
    /// Applies one opening attempt inside the globally ordered lifecycle locks.
    /// </summary>
    private async Task<ServiceResult<OpenCampaignResult>> OpenAttemptAsync(
        NovaDbContext db,
        long campaignId,
        OpenCampaignInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);
        await db.AcquireCampaignMutationLockAsync(campaignId, cancellationToken);

        var receipt = await FindOpeningReceiptAsync(db, clubId, campaignId, input.OperationId, cancellationToken);
        if (receipt.IsSuccess)
        {
            return receipt;
        }

        var operationOwner = await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.OpeningOperationId == input.OperationId)
            .Select(campaign => campaign.CampaignId)
            .SingleOrDefaultAsync(cancellationToken);
        if (operationOwner != 0 && operationOwner != campaignId)
        {
            return ServiceProblem.Conflict("The opening operation identifier was already used for another campaign.");
        }

        var campaign = await db.Campaigns
            .SingleOrDefaultAsync(candidate => candidate.CampaignId == campaignId, cancellationToken);
        if (campaign is null || campaign.ClubId != clubId)
        {
            return ServiceProblem.NotFound();
        }

        if (campaign.Status != CampaignStatus.Draft)
        {
            return ServiceProblem.Conflict("Only a Draft campaign can be opened.");
        }

        var currentSeasonId = await db.Clubs
            .Where(club => club.ClubId == clubId)
            .Select(club => club.CurrentSeasonId)
            .SingleAsync(cancellationToken);
        if (campaign.SeasonId != currentSeasonId)
        {
            return ServiceProblem.Conflict("Only a Draft in the club's current season can be opened.");
        }

        var blockingCampaign = await db.Campaigns
            .Where(candidate => candidate.CampaignId != campaignId && candidate.Status == CampaignStatus.Active)
            .OrderBy(candidate => candidate.CampaignId)
            .Select(candidate => new BlockingActiveCampaign(candidate.CampaignId, candidate.Name))
            .FirstOrDefaultAsync(cancellationToken);
        var activePlayerIds = await db.Players
            .Where(player => player.LifecycleStatus == LifecycleStatus.Active)
            .OrderBy(player => player.PlayerId)
            .Select(player => player.PlayerId)
            .ToListAsync(cancellationToken);
        var activeTeamCount = await db.Teams
            .CountAsync(team => team.LifecycleStatus == LifecycleStatus.Active, cancellationToken);

        var blockerErrors = BuildOpeningBlockerErrors(activePlayerIds.Count, blockingCampaign);
        if (blockerErrors.Count > 0)
        {
            return ServiceProblem.Conflict("The campaign is not ready to open.", blockerErrors);
        }

        var nextOpeningSequence = (await db.Campaigns
            .Where(candidate => candidate.SeasonId == campaign.SeasonId)
            .MaxAsync(candidate => candidate.SeasonOpeningSequence, cancellationToken) ?? 0) + 1;
        var openedAt = DateTimeOffset.UtcNow;
        campaign.OpeningOperationId = input.OperationId;
        campaign.OpenedAt = openedAt;
        campaign.OpenedById = actorUserId;
        campaign.SeasonOpeningSequence = nextOpeningSequence;
        campaign.InitialEnrolledPlayerCount = activePlayerIds.Count;
        campaign.InitialActiveTeamCount = activeTeamCount;
        campaign.Status = CampaignStatus.Active;

        CampaignParticipationWriter.StageEnrollments(db, clubId, campaignId, activePlayerIds);
        var actorName = await ResolveActorNameAsync(db, actorUserId, cancellationToken);
        ActivityEventWriter.AppendCampaignLifecycle(
            db,
            clubId,
            campaignId,
            ActivityEventKind.CampaignOpened,
            actorUserId,
            actorName,
            campaign.Name);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceProblem.Conflict("The campaign changed. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IsOneActiveCampaignViolation(exception))
        {
            return ServiceProblem.Conflict("Another campaign is already active for this club.");
        }
        catch (DbUpdateException)
        {
            return ServiceProblem.Conflict("The campaign could not be opened because related campaign data changed.");
        }

        LogCampaignOpened(campaignId, activePlayerIds.Count, actorUserId);
        return ToOpeningReceipt(campaign);
    }

    /// <summary>
    /// Applies one idempotent Draft deletion attempt.
    /// </summary>
    private async Task<ServiceResult<Success>> DeleteDraftAttemptAsync(
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
            return await HasDeletionTombstoneAsync(db, campaignId, cancellationToken)
                ? new Success()
                : ServiceProblem.NotFound();
        }

        if (campaign.Status != CampaignStatus.Draft)
        {
            return ServiceProblem.Conflict("Only an unopened Draft campaign can be deleted.");
        }

        var actorName = await ResolveActorNameAsync(db, actorUserId, cancellationToken);
        ActivityEventWriter.AppendCampaignLifecycle(
            db,
            clubId,
            campaignId,
            ActivityEventKind.CampaignDraftDeleted,
            actorUserId,
            actorName,
            campaign.Name);
        db.Campaigns.Remove(campaign);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceProblem.Conflict("The Draft campaign changed. Reload it and try again.");
        }

        LogCampaignDraftDeleted(campaignId, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Finds an exact immutable opening receipt for an idempotent replay.
    /// </summary>
    private static async Task<ServiceResult<OpenCampaignResult>> FindOpeningReceiptAsync(
        NovaDbContext db,
        long clubId,
        long campaignId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var campaign = await db.Campaigns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClubId == clubId
                    && candidate.CampaignId == campaignId
                    && candidate.OpeningOperationId == operationId,
                cancellationToken);
        return campaign is null ? ServiceProblem.NotFound() : ToOpeningReceipt(campaign);
    }

    /// <summary>
    /// Maps persisted opening evidence to its boundary receipt.
    /// </summary>
    private static OpenCampaignResult ToOpeningReceipt(CampaignEntity campaign)
    {
        IReadOnlyList<CampaignOpeningWarning> warnings = campaign.InitialActiveTeamCount == 0
            ? [CampaignOpeningWarning.NoActiveTeams]
            : [];
        return new OpenCampaignResult(
            campaign.OpeningOperationId!.Value,
            campaign.CampaignId,
            campaign.OpenedAt!.Value,
            campaign.OpenedById!.Value,
            campaign.InitialEnrolledPlayerCount!.Value,
            campaign.InitialActiveTeamCount!.Value,
            warnings);
    }

    /// <summary>
    /// Builds every structured blocker present in the locked readiness snapshot.
    /// </summary>
    private static IReadOnlyDictionary<string, string[]> BuildOpeningBlockerErrors(
        int activePlayerCount,
        BlockingActiveCampaign? blockingCampaign)
    {
        var errors = new Dictionary<string, string[]>();
        if (activePlayerCount == 0)
        {
            errors[CampaignOpeningProblemKeys.NoActivePlayers] = ["The club has no active players to enroll."];
        }

        if (blockingCampaign is not null)
        {
            errors[CampaignOpeningProblemKeys.AnotherCampaignActive] = ["Another campaign is already active for this club."];
            errors[CampaignOpeningProblemKeys.BlockingCampaignId] = [blockingCampaign.CampaignId.ToString()];
            errors[CampaignOpeningProblemKeys.BlockingCampaignName] = [blockingCampaign.CampaignName];
        }

        return errors;
    }

    /// <summary>
    /// Determines whether a durable deletion tombstone exists in the current tenant.
    /// </summary>
    private static Task<bool> HasDeletionTombstoneAsync(
        NovaDbContext db,
        long campaignId,
        CancellationToken cancellationToken)
        => db.ActivityEvents.AnyAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignDraftDeleted,
            cancellationToken);

    /// <summary>
    /// Resolves the acting administrator's display-name snapshot.
    /// </summary>
    private static async Task<string> ResolveActorNameAsync(
        NovaDbContext db,
        long actorUserId,
        CancellationToken cancellationToken)
        => await db.Users
            .Where(user => user.Id == actorUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Unknown user";

    /// <summary>Logs a forbidden campaign-open request.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign open forbidden for CampaignId={CampaignId} by UserId={UserId}.")]
    private partial void LogCampaignOpenForbidden(long campaignId, long userId);

    /// <summary>Logs a forbidden Draft-delete request.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign Draft deletion forbidden for CampaignId={CampaignId} by UserId={UserId}.")]
    private partial void LogCampaignDeleteForbidden(long campaignId, long userId);

    /// <summary>Logs a committed campaign opening.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "CampaignId={CampaignId} opened with PlayerCount={PlayerCount} by UserId={ActorUserId}.")]
    private partial void LogCampaignOpened(long campaignId, int playerCount, long actorUserId);

    /// <summary>Logs a committed Draft deletion.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Draft CampaignId={CampaignId} deleted by UserId={ActorUserId}.")]
    private partial void LogCampaignDraftDeleted(long campaignId, long actorUserId);
}
