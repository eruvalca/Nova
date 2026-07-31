using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Teams;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Nova.Shared.Validation;

namespace Nova.Features.Teams;

/// <summary>
/// Creates and updates teams with club-administrator authorization and tenant-safe eligibility checks.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for operation outcomes.</param>
public sealed partial class TeamManagementService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TeamManagementService> logger) : ITeamManagementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TeamDto>> CreateAsync(
        CreateTeamInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogTeamCreateValidationFailed();
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTeamCreateForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to create teams.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var team = new TeamEntity
        {
            Name = input.Name,
            GraduationYear = input.GraduationYear,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.Teams.Add(team);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTeamMutationConcurrencyConflict(team.TeamId);
            return ServiceProblem.Conflict("The team could not be created. Reload and try again.");
        }

        LogTeamCreated(team.TeamId, actorUserId);
        return team.ToTeamDto();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TeamDto>> UpdateAsync(
        UpdateTeamInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogTeamUpdateValidationFailed(input.TeamId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTeamUpdateForbidden(input.TeamId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to edit teams.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireTeamMutationLockAsync(input.TeamId, cancellationToken);

        var team = await db.Teams
            .SingleOrDefaultAsync(candidate => candidate.TeamId == input.TeamId, cancellationToken);

        if (team is null || team.ClubId != clubId)
        {
            LogTeamNotFound(input.TeamId, clubId);
            return ServiceProblem.NotFound();
        }

        if (team.LifecycleStatus != LifecycleStatus.Active)
        {
            LogTeamArchivedConflict(input.TeamId);
            return ServiceProblem.Conflict("Archived teams cannot be edited through this workflow. Restore the team first.");
        }

        if (input.GraduationYear != team.GraduationYear)
        {
            var blockers = await db.PlayerCampaignAssignments
                .Where(assignment =>
                    assignment.TeamId == input.TeamId
                    && assignment.PlacementOutcome == PlacementOutcome.Assigned
                    && assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.Player.GraduationYear < input.GraduationYear)
                .Select(assignment => new TeamGraduationYearBlockerItem
                {
                    PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
                    CampaignId = assignment.CampaignId,
                    PlayerId = assignment.PlayerId,
                    PlayerGraduationYear = assignment.Player.GraduationYear
                })
                .ToListAsync(cancellationToken);

            if (blockers.Count > 0)
            {
                LogTeamGraduationYearBlocked(input.TeamId, blockers.Count);
                return ServiceProblem.Conflict(
                    "The proposed graduation year would make one or more Active-campaign placements ineligible.",
                    BuildBlockerErrors(blockers));
            }
        }

        team.Name = input.Name;
        team.GraduationYear = input.GraduationYear;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTeamMutationConcurrencyConflict(input.TeamId);
            return ServiceProblem.Conflict("The team changed concurrently. Reload and try again.");
        }

        LogTeamUpdated(team.TeamId, actorUserId);
        return team.ToTeamDto();
    }

    /// <summary>
    /// Encodes team eligibility blockers into indexed structured error fields.
    /// </summary>
    /// <param name="blockers">The blocked active placements.</param>
    /// <returns>A field-keyed dictionary suitable for a conflict response.</returns>
    private static IReadOnlyDictionary<string, string[]> BuildBlockerErrors(
        IReadOnlyList<TeamGraduationYearBlockerItem> blockers)
    {
        var errors = new Dictionary<string, string[]>();
        for (var index = 0; index < blockers.Count; index++)
        {
            var blocker = blockers[index];
            errors[$"blockers[{index}].assignmentId"] = [blocker.PlayerCampaignAssignmentId.ToString()];
            errors[$"blockers[{index}].campaignId"] = [blocker.CampaignId.ToString()];
            errors[$"blockers[{index}].playerId"] = [blocker.PlayerId.ToString()];
            errors[$"blockers[{index}].playerGraduationYear"] = [blocker.PlayerGraduationYear.ToString()];
        }

        return errors;
    }

    /// <summary>Logs invalid team creation input.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team create validation failed.")]
    private partial void LogTeamCreateValidationFailed();

    /// <summary>Logs invalid team update input.</summary>
    /// <param name="teamId">The requested team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team update validation failed for TeamId={TeamId}.")]
    private partial void LogTeamUpdateValidationFailed(long teamId);

    /// <summary>Logs a team creation request from a non-administrator.</summary>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team create forbidden for UserId={UserId}.")]
    private partial void LogTeamCreateForbidden(long userId);

    /// <summary>Logs a team update request from a non-administrator.</summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team update forbidden for TeamId={TeamId} by UserId={UserId}.")]
    private partial void LogTeamUpdateForbidden(long teamId, long userId);

    /// <summary>Logs a team that is unavailable in the current tenant.</summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "TeamId={TeamId} was not found for ClubId={ClubId}.")]
    private partial void LogTeamNotFound(long teamId, long clubId);

    /// <summary>Logs an update attempt on an archived team.</summary>
    /// <param name="teamId">The archived team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team update conflict: TeamId={TeamId} is archived.")]
    private partial void LogTeamArchivedConflict(long teamId);

    /// <summary>Logs a graduation-year change blocked by assigned placements.</summary>
    /// <param name="teamId">The blocked team identifier.</param>
    /// <param name="blockerCount">The number of blocked placements.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team graduation-year edit blocked for TeamId={TeamId}: {BlockerCount} ineligible placement(s).")]
    private partial void LogTeamGraduationYearBlocked(long teamId, int blockerCount);

    /// <summary>Logs a team mutation that failed due to a concurrent data change.</summary>
    /// <param name="teamId">The affected team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team mutation concurrency conflict for TeamId={TeamId}.")]
    private partial void LogTeamMutationConcurrencyConflict(long teamId);

    /// <summary>Logs a successful team creation.</summary>
    /// <param name="teamId">The created team identifier.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} created by UserId={ActorUserId}.")]
    private partial void LogTeamCreated(long teamId, long actorUserId);

    /// <summary>Logs a successful team update.</summary>
    /// <param name="teamId">The updated team identifier.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} updated by UserId={ActorUserId}.")]
    private partial void LogTeamUpdated(long teamId, long actorUserId);
}
