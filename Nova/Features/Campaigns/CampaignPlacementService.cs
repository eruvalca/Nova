using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Validation;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Reports the new concurrency token after a placement mutation succeeds.
/// </summary>
/// <param name="ConcurrencyToken">The token callers must use for the next mutation.</param>
public readonly record struct PlacementMutationSuccess(Guid ConcurrencyToken);

/// <summary>
/// Reports that the current user is not authorized to mutate campaign placements.
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
/// Applies tenant-safe campaign placement mutations with administrator authorization and optimistic concurrency.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for the placement mutation.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class CampaignPlacementService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignPlacementService> logger)
{
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

        if (currentUserProvider.UserId is not long userId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogPlacementForbidden(input.PlayerCampaignAssignmentId, currentUserProvider.UserId ?? 0);
            return new PlacementForbidden("You must be a club administrator to update campaign placements.");
        }
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

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

        TeamEntity? team = null;
        if (input.TeamId is long teamId)
        {
            await db.AcquireTeamMutationLockAsync(teamId, cancellationToken);
            team = await db.Teams
                .SingleOrDefaultAsync(candidate => candidate.TeamId == teamId, cancellationToken);
        }

        var placementDecision = CampaignPlacementPolicy.Evaluate(
            new PlacementDecisionContext(
                participation.Campaign.Status,
                participation.Player.LifecycleStatus,
                participation.Player.GraduationYear,
                input.TeamId.HasValue,
                team is not null && team.ClubId == clubId,
                team?.LifecycleStatus,
                team?.GraduationYear));

        return await placementDecision.Match(
            ApplyPlacementAsync,
            RejectClosedCampaignAsync,
            RejectArchivedPlayerAsync,
            RejectUnavailableTeamAsync,
            RejectArchivedTeamAsync,
            RejectIneligiblePlayerAsync);

        async Task<PlacementUpdateResult> ApplyPlacementAsync(PlacementMayApply _)
        {
            db.Entry(participation)
                .Property(assignment => assignment.ConcurrencyToken)
                .OriginalValue = input.ExpectedConcurrencyToken;

            participation.PlacementOutcome = input.Outcome;
            participation.TeamId = input.TeamId;
            participation.ConcurrencyToken = Guid.NewGuid();

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                LogPlacementConflict(input.PlayerCampaignAssignmentId, userId);
                return new PlacementConflict("The placement was changed by another user. Reload it and try again.");
            }

            LogPlacementUpdated(input.PlayerCampaignAssignmentId, userId);
            return new PlacementMutationSuccess(participation.ConcurrencyToken);
        }

        Task<PlacementUpdateResult> RejectClosedCampaignAsync(PlacementCampaignClosed _)
        {
            LogPlacementCampaignClosed(input.PlayerCampaignAssignmentId, participation.CampaignId);
            return Task.FromResult<PlacementUpdateResult>(
                new PlacementConflict("Closed campaigns are read-only and cannot accept placement changes."));
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
    /// Logs a rejected placement request containing invalid input values.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement validation failed for AssignmentId={AssignmentId}.")]
    private partial void LogPlacementValidationFailed(long assignmentId);

    /// <summary>
    /// Logs a placement request rejected because the caller is not a club administrator.
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
    /// Logs a placement rejected because its campaign is closed.
    /// </summary>
    /// <param name="assignmentId">The requested campaign participation identifier.</param>
    /// <param name="campaignId">The closed campaign identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement rejected for AssignmentId={AssignmentId} because CampaignId={CampaignId} is closed.")]
    private partial void LogPlacementCampaignClosed(long assignmentId, long campaignId);

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
    /// <param name="userId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign placement conflict for AssignmentId={AssignmentId} by UserId={UserId}.")]
    private partial void LogPlacementConflict(long assignmentId, long userId);

    /// <summary>
    /// Logs a successful placement mutation.
    /// </summary>
    /// <param name="assignmentId">The updated campaign participation identifier.</param>
    /// <param name="userId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign placement updated for AssignmentId={AssignmentId} by UserId={UserId}.")]
    private partial void LogPlacementUpdated(long assignmentId, long userId);
}
