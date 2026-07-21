using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Describes an administrator request to update a campaign participant's placement.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The campaign participation identifier to update.</param>
/// <param name="Outcome">The new placement outcome.</param>
/// <param name="TeamId">The assigned team identifier, required only for an assigned outcome.</param>
/// <param name="ExpectedConcurrencyToken">The token observed when the placement was loaded.</param>
public sealed record UpdateCampaignPlacementCommand(
    long PlayerCampaignAssignmentId,
    PlacementOutcome Outcome,
    long? TeamId,
    Guid ExpectedConcurrencyToken);

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
/// Applies tenant-safe campaign placement mutations with administrator authorization and optimistic concurrency.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for the mutation transaction.</param>
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
    /// <param name="command">The requested placement values and expected concurrency token.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>
    /// The new concurrency token on success; validation, not-found, forbidden, or conflict information otherwise.
    /// </returns>
    public async Task<OneOf<
        PlacementMutationSuccess,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        PlacementForbidden,
        PlacementConflict>> UpdatePlacementAsync(
            UpdateCampaignPlacementCommand command,
            CancellationToken cancellationToken = default)
    {
        var validationErrors = Validate(command);
        if (validationErrors.Count > 0)
        {
            LogPlacementValidationFailed(command.PlayerCampaignAssignmentId);
            return new Error<IReadOnlyDictionary<string, string[]>>(validationErrors);
        }

        if (currentUserProvider.UserId is not long userId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogPlacementForbidden(command.PlayerCampaignAssignmentId, currentUserProvider.UserId ?? 0);
            return new PlacementForbidden("You must be a club administrator to update campaign placements.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var participation = await db.PlayerCampaignAssignments
            .Include(assignment => assignment.Player)
            .Include(assignment => assignment.Campaign)
            .SingleOrDefaultAsync(
                assignment => assignment.PlayerCampaignAssignmentId == command.PlayerCampaignAssignmentId,
                cancellationToken);

        if (participation is null
            || participation.ClubId != clubId
            || participation.Player.ClubId != clubId
            || participation.Campaign.ClubId != clubId)
        {
            LogPlacementNotFound(command.PlayerCampaignAssignmentId, clubId);
            return new NotFound();
        }

        if (command.TeamId is long teamId)
        {
            var team = await db.Teams
                .SingleOrDefaultAsync(candidate => candidate.TeamId == teamId, cancellationToken);

            if (team is null || team.ClubId != clubId)
            {
                LogPlacementTeamNotFound(command.PlayerCampaignAssignmentId, teamId, clubId);
                return new NotFound();
            }

            if (participation.Player.GraduationYear < team.GraduationYear)
            {
                LogPlacementEligibilityFailed(command.PlayerCampaignAssignmentId, teamId);
                return new Error<IReadOnlyDictionary<string, string[]>>(
                    new Dictionary<string, string[]>
                    {
                        [nameof(command.TeamId)] =
                        [
                            "The player's graduation year must be greater than or equal to the team's graduation year."
                        ]
                    });
            }
        }

        db.Entry(participation)
            .Property(assignment => assignment.ConcurrencyToken)
            .OriginalValue = command.ExpectedConcurrencyToken;

        participation.PlacementOutcome = command.Outcome;
        participation.TeamId = command.TeamId;
        participation.ConcurrencyToken = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogPlacementConflict(command.PlayerCampaignAssignmentId, userId);
            return new PlacementConflict("The placement was changed by another user. Reload it and try again.");
        }

        LogPlacementUpdated(command.PlayerCampaignAssignmentId, userId);
        return new PlacementMutationSuccess(participation.ConcurrencyToken);
    }

    /// <summary>
    /// Validates command values that do not require database access.
    /// </summary>
    /// <param name="command">The command to validate.</param>
    /// <returns>A field-keyed validation-error dictionary.</returns>
    private static Dictionary<string, string[]> Validate(UpdateCampaignPlacementCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.PlayerCampaignAssignmentId <= 0)
        {
            errors[nameof(command.PlayerCampaignAssignmentId)] = ["A campaign participation identifier is required."];
        }

        if (!Enum.IsDefined(command.Outcome))
        {
            errors[nameof(command.Outcome)] = ["The placement outcome is invalid."];
        }

        if (command.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[nameof(command.ExpectedConcurrencyToken)] = ["A concurrency token is required."];
        }

        var hasTeam = command.TeamId.HasValue;
        if (command.Outcome == PlacementOutcome.Assigned && !hasTeam)
        {
            errors[nameof(command.TeamId)] = ["A team is required for an assigned outcome."];
        }
        else if (command.Outcome != PlacementOutcome.Assigned && hasTeam)
        {
            errors[nameof(command.TeamId)] = ["A team is only allowed for an assigned outcome."];
        }

        return errors;
    }

    /// <summary>
    /// Logs a rejected placement request containing invalid command values.
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
