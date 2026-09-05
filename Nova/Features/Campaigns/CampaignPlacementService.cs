using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Campaigns;
using Nova.Features.Activity;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Reports that the current user is not an approved club member authorized to mutate campaign placements.
/// </summary>
/// <param name="Detail">A description of the authorization failure.</param>
public readonly record struct PlacementForbidden(string Detail);

/// <summary>
/// Reports that a placement changed after the caller loaded it.
/// </summary>
/// <param name="Detail">A description of the concurrency conflict.</param>
public readonly record struct PlacementConflict(string Detail);

/// <summary>
/// Represents every supported outcome of a campaign-placement mutation.
/// </summary>
[GenerateOneOf]
public partial class PlacementUpdateResult : OneOfBase<
    PlacementMutationSuccess,
    Error<IReadOnlyDictionary<string, string[]>>,
    NotFound,
    PlacementForbidden,
    PlacementConflict>
{
}

/// <summary>
/// Applies tenant-safe campaign placement mutations with approved-club-member authorization and optimistic concurrency.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for the placement mutation.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class CampaignPlacementService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignPlacementService> logger) : ICampaignPlacementService
{
    /// <inheritdoc />
    async Task<ServiceResult<PlacementMutationSuccess>> ICampaignPlacementService.UpdatePlacementAsync(
        UpdateCampaignPlacementInput input,
        CancellationToken cancellationToken)
    {
        var outcome = await UpdatePlacementAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<PlacementMutationSuccess>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <summary>
    /// Updates one campaign participant's outcome and optional team.
    /// </summary>
    /// <param name="input">The requested placement values and expected concurrency token.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// The new concurrency token on success; validation, not-found, forbidden, or conflict information otherwise.
    /// </returns>
    public async Task<PlacementUpdateResult> UpdatePlacementAsync(
        UpdateCampaignPlacementInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogPlacementValidationFailed(input.PlayerCampaignAssignmentId);
            return new Error<IReadOnlyDictionary<string, string[]>>(validationErrors);
        }

        if (currentUserProvider.GetCurrentUserState().Value is not ClubMember member)
        {
            LogPlacementForbidden(input.PlayerCampaignAssignmentId, currentUserProvider.UserId ?? 0);
            return new PlacementForbidden("You must be an approved club member to update campaign placements.");
        }

        var userId = member.UserId;
        var clubId = member.ClubId;

        // The replacement token also identifies an immutable receipt for this logical request.
        var replacementToken = Guid.NewGuid();
        var isClubAdmin = currentUserProvider.IsClubAdmin;

        return await ExecuteWithFreshContextAsync(
            (db, commitAttempted) => UpdatePlacementAttemptAsync(
                db, input, userId, clubId, isClubAdmin, replacementToken, commitAttempted, cancellationToken),
            db => VerifyPlacementCommittedAsync(
                db, input.PlayerCampaignAssignmentId, replacementToken, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs a placement mutation inside EF Core's retrying execution strategy with a fresh
    /// tenant context per attempt, and verifies whether an ambiguous commit succeeded before the
    /// strategy replays the mutation. Verification only runs for an attempt that reached its commit;
    /// a transient failure raised before the commit cannot have applied the mutation, so the observed
    /// state belongs to an earlier request and must not be credited to this one.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the operation.</typeparam>
    /// <param name="operation">The mutation to execute with a fresh tenant context and commit tracker.</param>
    /// <param name="verifySucceeded">The verification query to run with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels strategy setup, the mutation, or verification.</param>
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
    /// Executes one transactional campaign placement update attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested placement values and expected concurrency token.</param>
    /// <param name="userId">The acting club member identifier.</param>
    /// <param name="clubId">The current tenant club identifier.</param>
    /// <param name="isClubAdmin">Whether the acting member may supersede a prior withdrawal.</param>
    /// <param name="replacementToken">The stable token this logical request writes on success.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels database work.</param>
    /// <returns>The placement update result for this attempt.</returns>
    private async Task<PlacementUpdateResult> UpdatePlacementAttemptAsync(
        NovaDbContext db,
        UpdateCampaignPlacementInput input,
        long userId,
        long clubId,
        bool isClubAdmin,
        Guid replacementToken,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);

        var currentSeasonId = await db.Clubs
            .Where(club => club.ClubId == clubId)
            .Select(club => club.CurrentSeasonId)
            .SingleOrDefaultAsync(cancellationToken);

        var participation = await db.PlayerCampaignAssignments
            .Include(assignment => assignment.Player)
            .Include(assignment => assignment.Campaign)
            .SingleOrDefaultAsync(
                assignment => assignment.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId,
                cancellationToken);

        if (participation is null
            || participation.ClubId != clubId
            || participation.Player.ClubId != clubId
            || participation.Campaign.ClubId != clubId)
        {
            LogPlacementNotFound(input.PlayerCampaignAssignmentId, clubId);
            return new NotFound();
        }

        await db.AcquireCampaignMutationLockAsync(participation.CampaignId, cancellationToken);
        await db.Entry(participation.Campaign).ReloadAsync(cancellationToken);

        await db.AcquirePlayerMutationLockAsync(participation.PlayerId, cancellationToken);
        await db.Entry(participation.Player).ReloadAsync(cancellationToken);
        await db.Entry(participation).ReloadAsync(cancellationToken);

        // Preserve lifecycle rejection precedence, then reject stale edits before interpreting
        // the winner's outcome (which may now be a terminal withdrawal).
        if (participation.Campaign.Status == CampaignStatus.Active
            && participation.Player.LifecycleStatus == LifecycleStatus.Active
            && participation.ConcurrencyToken != input.ExpectedConcurrencyToken)
        {
            LogPlacementConflict(input.PlayerCampaignAssignmentId, userId);
            return new PlacementConflict("The placement was changed by another user. Reload it and try again.");
        }

        // Select saved truth before considering team validity. Invalid or archived latest teams
        // never cause a fallback to an earlier assignment. Enrollment rows cannot supersede.
        var latestDecisionEntity = await db.PlayerCampaignAssignments
            .Include(assignment => assignment.Campaign)
            .Where(assignment => assignment.PlayerId == participation.PlayerId
                && assignment.Campaign.SeasonId == participation.Campaign.SeasonId
                && assignment.Campaign.Status != CampaignStatus.Draft
                && assignment.PlacementOutcome != PlacementOutcome.Undecided)
            .OrderByDescending(assignment => assignment.Campaign.SeasonOpeningSequence)
            .ThenByDescending(assignment => assignment.PlayerCampaignAssignmentId)
            .FirstOrDefaultAsync(cancellationToken);
        var latestDecision = latestDecisionEntity?.ToSavedPlacementDecision();

        // The old team-name snapshot must come from post-lock state: the team navigation is not
        // loaded up front (only Player and Campaign are included above), so it is first loaded
        // after the team locks. Lock the affected teams in a deterministic order (interleaved
        // {old, new} sorted by identifier) so concurrent switches cannot deadlock, then load the
        // target team and the old-team reference before reading either team name.
        var teamIdsToLock = new[] { participation.TeamId, latestDecision?.TeamId, input.TeamId }
            .Where(teamId => teamId.HasValue)
            .Select(teamId => teamId!.Value)
            .Distinct()
            .OrderBy(teamId => teamId)
            .ToList();

        foreach (var lockedTeamId in teamIdsToLock)
        {
            await db.AcquireTeamMutationLockAsync(lockedTeamId, cancellationToken);
        }

        TeamEntity? team = null;
        if (input.TeamId is long teamId)
        {
            team = await db.Teams
                .SingleOrDefaultAsync(candidate => candidate.TeamId == teamId, cancellationToken);
        }

        if (latestDecisionEntity?.TeamId is not null)
        {
            await db.Entry(latestDecisionEntity).Reference(assignment => assignment.Team).LoadAsync(cancellationToken);
        }

        var placementDecision = CampaignPlacementPolicy.Evaluate(
            new PlacementDecisionContext(
                participation.Campaign.Status,
                participation.Player.LifecycleStatus,
                participation.Player.GraduationYear,
                input.TeamId.HasValue,
                team is not null && team.ClubId == clubId,
                team?.LifecycleStatus,
                team?.GraduationYear)
            {
                IsCurrentSeason = currentSeasonId == participation.Campaign.SeasonId,
                CampaignId = participation.CampaignId,
                SeasonId = participation.Campaign.SeasonId,
                SeasonOpeningSequence = participation.Campaign.SeasonOpeningSequence ?? 0,
                LatestDecision = latestDecision,
                IsClubAdmin = isClubAdmin,
                RequestedOutcome = input.Outcome,
                RequestedTeamId = input.TeamId,
                EffectiveTeamIsValid = latestDecisionEntity?.Team is { } effectiveTeam
                    && effectiveTeam.ClubId == clubId
                    && effectiveTeam.LifecycleStatus == LifecycleStatus.Active
                    && participation.Player.GraduationYear >= effectiveTeam.GraduationYear,
            });

        return await placementDecision.Match(
            ApplyPlacementAsync,
            RejectNonActiveCampaignAsync,
            RejectArchivedPlayerAsync,
            RejectUnavailableTeamAsync,
            RejectArchivedTeamAsync,
            RejectIneligiblePlayerAsync,
            RejectSeasonAsync,
            RejectTerminalWithdrawalAsync,
            RejectWithdrawalAuthorityAsync);

        async Task<PlacementUpdateResult> ApplyPlacementAsync(PlacementMayApply decision)
        {
            if (decision.IsNoOp)
            {
                return new PlacementMutationSuccess(participation.ConcurrencyToken);
            }

            db.Entry(participation)
                .Property(assignment => assignment.ConcurrencyToken)
                .OriginalValue = input.ExpectedConcurrencyToken;

            // Capture the prior placement state and the old team-name snapshot before mutation so
            // the durable event can describe the transition; a no-op save emits nothing.
            var previousOutcome = latestDecision?.Outcome;
            var previousTeamId = latestDecision?.TeamId;
            var previousTeamName = latestDecisionEntity?.Team?.Name;
            var placementKind = decision.IsSupersession ? ActivityEventKind.PlacementSuperseded : ActivityEventPolicy.ClassifyPlacementTransition(
                previousOutcome,
                previousTeamId,
                input.Outcome,
                input.TeamId);

            participation.PlacementOutcome = input.Outcome;
            participation.TeamId = input.TeamId;
            participation.ConcurrencyToken = replacementToken;

            // The actor is a club member (tenant-visible), so the write context resolves the
            // snapshot deterministically.
            var actorName = await db.Users
                .Where(user => user.Id == userId)
                .Select(user => user.FirstName + " " + user.LastName)
                .FirstOrDefaultAsync(cancellationToken) ?? "Unknown user";

            participation.DecisionRecordedAt = DateTimeOffset.UtcNow;
            participation.DecisionRecordedById = userId;
            participation.DecisionActorDisplayName = actorName;

            await PruneExpiredMutationReceiptsAsync(db, cancellationToken);
            db.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
            {
                OperationId = replacementToken,
                PlayerCampaignAssignmentId = participation.PlayerCampaignAssignmentId,
                ConcurrencyToken = replacementToken,
                ClubId = default,
                CreatedById = default,
            });

            if (placementKind is ActivityEventKind kind)
            {
                ActivityEventWriter.AppendPlacement(
                    db,
                    participation.ClubId,
                    participation.CampaignId,
                    kind,
                    userId,
                    actorName,
                    new PlacementContext
                    {
                        PlayerId = participation.PlayerId,
                        SeasonId = participation.Campaign.SeasonId,
                        PreviousCampaignId = latestDecision?.CampaignId,
                        PreviousCampaignName = latestDecisionEntity?.Campaign.Name,
                        PreviousPlayerCampaignAssignmentId = latestDecision?.PlayerCampaignAssignmentId,
                        PreviousTeamId = previousTeamId,
                        TeamId = input.TeamId,
                        CampaignId = participation.CampaignId,
                        CampaignName = participation.Campaign.Name,
                        PlayerCampaignAssignmentId = participation.PlayerCampaignAssignmentId,
                        PlayerDisplayName = participation.Player.FullName,
                        PreviousOutcome = previousOutcome,
                        Outcome = input.Outcome,
                        PreviousTeamName = previousTeamName,
                        TeamName = team?.Name,
                    });
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                commitAttempted.MarkAttempted();
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                LogPlacementConflict(input.PlayerCampaignAssignmentId, userId);
                return new PlacementConflict("The placement was changed by another user. Reload it and try again.");
            }

            LogPlacementUpdated(input.PlayerCampaignAssignmentId, userId);
            return new PlacementMutationSuccess(replacementToken);
        }

        Task<PlacementUpdateResult> RejectNonActiveCampaignAsync(PlacementCampaignNotActive _)
        {
            LogPlacementCampaignNotActive(input.PlayerCampaignAssignmentId, participation.CampaignId);
            return Task.FromResult<PlacementUpdateResult>(
                new PlacementConflict("Only active campaigns can accept placement changes."));
        }

        Task<PlacementUpdateResult> RejectSeasonAsync(PlacementSeasonConflict _)
        {
            LogPlacementDecisionRejected(input.PlayerCampaignAssignmentId, userId, "SeasonConflict");
            return Task.FromResult<PlacementUpdateResult>(new PlacementConflict(
                "Only the current season's latest active campaign can accept placement decisions."));
        }

        Task<PlacementUpdateResult> RejectTerminalWithdrawalAsync(PlacementWithdrawalTerminal _)
        {
            LogPlacementDecisionRejected(input.PlayerCampaignAssignmentId, userId, "WithdrawalTerminal");
            return Task.FromResult<PlacementUpdateResult>(new PlacementConflict(
                "Withdrawn is final in its owning campaign. An administrator must record a superseding decision in a later active campaign."));
        }

        Task<PlacementUpdateResult> RejectWithdrawalAuthorityAsync(PlacementWithdrawalRequiresAdmin _)
        {
            LogPlacementDecisionRejected(input.PlayerCampaignAssignmentId, userId, "WithdrawalRequiresAdmin");
            return Task.FromResult<PlacementUpdateResult>(new PlacementForbidden(
                "Only a club administrator can supersede a prior campaign's Withdrawn decision."));
        }

        Task<PlacementUpdateResult> RejectArchivedPlayerAsync(PlacementPlayerArchived _)
        {
            LogPlacementPlayerArchived(input.PlayerCampaignAssignmentId, participation.PlayerId);
            return Task.FromResult<PlacementUpdateResult>(
                new PlacementConflict("Archived players cannot receive new placement decisions."));
        }

        Task<PlacementUpdateResult> RejectUnavailableTeamAsync(PlacementTeamUnavailable _)
        {
            LogPlacementTeamNotFound(input.PlayerCampaignAssignmentId, input.TeamId!.Value, clubId);
            return Task.FromResult<PlacementUpdateResult>(new NotFound());
        }

        Task<PlacementUpdateResult> RejectArchivedTeamAsync(PlacementTeamArchived _)
        {
            LogPlacementTeamArchived(input.PlayerCampaignAssignmentId, input.TeamId!.Value);
            return Task.FromResult<PlacementUpdateResult>(
                new PlacementConflict("Archived teams cannot receive new placements."));
        }

        Task<PlacementUpdateResult> RejectIneligiblePlayerAsync(PlacementTeamIneligible _)
        {
            LogPlacementEligibilityFailed(input.PlayerCampaignAssignmentId, input.TeamId!.Value);
            return Task.FromResult<PlacementUpdateResult>(
                new Error<IReadOnlyDictionary<string, string[]>>(
                    new Dictionary<string, string[]>
                    {
                        [nameof(input.TeamId)] =
                        [
                            "The player's graduation year must be greater than or equal to the team's graduation year."
                        ]
                    }));
        }
    }

    /// <summary>
    /// Verifies whether an ambiguous placement commit persisted this request's immutable receipt, and
    /// reconstructs the success result when it did.
    /// </summary>
    /// <param name="db">The fresh tenant context used for commit verification.</param>
    /// <param name="playerCampaignAssignmentId">The campaign participation identifier.</param>
    /// <param name="replacementToken">The stable token this logical request generated.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>Whether the commit landed, along with the reconstructed result when it did.</returns>
    private async Task<ExecutionResult<PlacementUpdateResult>> VerifyPlacementCommittedAsync(
        NovaDbContext db,
        long playerCampaignAssignmentId,
        Guid replacementToken,
        CancellationToken cancellationToken)
    {
        var persistedToken = await db.PlacementMutationReceipts
            .AsNoTracking()
            .Where(receipt => receipt.PlayerCampaignAssignmentId == playerCampaignAssignmentId
                && receipt.OperationId == replacementToken)
            .Select(receipt => (Guid?)receipt.ConcurrencyToken)
            .SingleOrDefaultAsync(cancellationToken);

        if (persistedToken != replacementToken)
        {
            return new ExecutionResult<PlacementUpdateResult>(successful: false, default!);
        }

        LogPlacementCommitRecovered(playerCampaignAssignmentId, replacementToken);
        return new ExecutionResult<PlacementUpdateResult>(
            successful: true,
            new PlacementMutationSuccess(replacementToken));
    }

    /// <summary>Prunes expired commit evidence within the current tenant and mutation transaction.</summary>
    /// <param name="db">The mutation context.</param>
    /// <param name="cancellationToken">Cancels receipt pruning.</param>
    /// <returns>A task representing the retention operation.</returns>
    private static async Task PruneExpiredMutationReceiptsAsync(NovaDbContext db, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        if (db.Database.IsNpgsql())
        {
            await db.PlacementMutationReceipts.Where(receipt => receipt.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var receipts = await db.PlacementMutationReceipts.ToListAsync(cancellationToken);
        db.PlacementMutationReceipts.RemoveRange(receipts.Where(receipt => receipt.CreatedAt < cutoff));
    }

    /// <summary>
    /// Logs a rejected placement request containing invalid input values.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement validation failed for AssignmentId={AssignmentId}.")]
    private partial void LogPlacementValidationFailed(long assignmentId);

    /// <summary>
    /// Logs a placement request rejected because the caller is not an approved club member.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement forbidden for AssignmentId={AssignmentId} by UserId={UserId}.")]
    private partial void LogPlacementForbidden(long assignmentId, long userId);

    /// <summary>
    /// Logs a placement request whose participation is unavailable in the current tenant.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign participation AssignmentId={AssignmentId} was not found for ClubId={ClubId}.")]
    private partial void LogPlacementNotFound(long assignmentId, long clubId);

    /// <summary>
    /// Logs a placement request whose team is unavailable in the current tenant.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "TeamId={TeamId} for AssignmentId={AssignmentId} was not found for ClubId={ClubId}.")]
    private partial void LogPlacementTeamNotFound(long assignmentId, long teamId, long clubId);

    /// <summary>
    /// Logs a placement rejected because its campaign is not active.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="campaignId">The campaign identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement rejected for AssignmentId={AssignmentId} because CampaignId={CampaignId} is not active.")]
    private partial void LogPlacementCampaignNotActive(long assignmentId, long campaignId);

    /// <summary>
    /// Logs a placement rejected because its player is archived.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="playerId">The archived player identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement rejected for AssignmentId={AssignmentId} because PlayerId={PlayerId} is archived.")]
    private partial void LogPlacementPlayerArchived(long assignmentId, long playerId);

    /// <summary>
    /// Logs a placement rejected because its requested team is archived.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="teamId">The archived team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement rejected for AssignmentId={AssignmentId} because TeamId={TeamId} is archived.")]
    private partial void LogPlacementTeamArchived(long assignmentId, long teamId);

    /// <summary>
    /// Logs a placement rejected by the graduation-year eligibility rule.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="teamId">The ineligible team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement eligibility failed for AssignmentId={AssignmentId} and TeamId={TeamId}.")]
    private partial void LogPlacementEligibilityFailed(long assignmentId, long teamId);

    /// <summary>
    /// Logs a placement rejected because its expected token was stale.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="userId">The acting club member identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement conflict for AssignmentId={AssignmentId} by UserId={UserId}.")]
    private partial void LogPlacementConflict(long assignmentId, long userId);

    /// <summary>Logs a rejected same-season placement decision.</summary>
    /// <param name="assignmentId">The requested participation identifier.</param>
    /// <param name="userId">The acting club member identifier.</param>
    /// <param name="reason">The stable domain rejection reason.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Placement decision rejected for AssignmentId={AssignmentId} by UserId={UserId}: {Reason}.")]
    private partial void LogPlacementDecisionRejected(long assignmentId, long userId, string reason);

    /// <summary>
    /// Logs a successful placement mutation.
    /// </summary>
    /// <param name="assignmentId">The updated campaign participation identifier.</param>
    /// <param name="userId">The acting club member identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign placement updated for AssignmentId={AssignmentId} by UserId={UserId}.")]
    private partial void LogPlacementUpdated(long assignmentId, long userId);

    /// <summary>Logs a placement result reconstructed from an ambiguous commit.</summary>
    /// <param name="assignmentId">The campaign participation identifier.</param>
    /// <param name="replacementToken">The stable replacement token this logical request generated.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign placement AssignmentId={AssignmentId} recovered commit with replacement token {ReplacementToken} after an ambiguous commit.")]
    private partial void LogPlacementCommitRecovered(long assignmentId, Guid replacementToken);
}
