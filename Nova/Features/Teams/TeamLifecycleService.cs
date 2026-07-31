using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Nova.Shared.Validation;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Teams;

/// <summary>
/// Applies tenant-safe team lifecycle and graduation-year mutations with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for team mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class TeamLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TeamLifecycleService> logger) : ITeamLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ArchiveAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(teamId, LifecycleStatus.Archived, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            blocked => ServiceProblem.Conflict(
                "Resolve every active-campaign placement before archiving the team.",
                TeamLifecycleProblemExtensions.CreateArchiveBlockerExtensions(blocked.Blockers)));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RestoreAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await TransitionAsync(teamId, LifecycleStatus.Active, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail),
            blocked => MapUnexpectedRestoreBlocked(teamId, blocked));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> UpdateGraduationYearAsync(
        UpdateTeamGraduationYearInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogTeamGraduationYearValidationFailed(input.TeamId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTeamLifecycleForbidden(input.TeamId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to change permanent team data.");
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

        if (team.LifecycleStatus == LifecycleStatus.Archived)
        {
            LogTeamLifecycleConflict(input.TeamId, team.LifecycleStatus);
            return ServiceProblem.Conflict("Restore the archived team before changing its graduation year.");
        }

        var blockers = await db.PlayerCampaignAssignments
            .Where(
                assignment => assignment.TeamId == input.TeamId
                    && assignment.PlacementOutcome == PlacementOutcome.Assigned
                    && assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.Player.GraduationYear < input.GraduationYear)
            .Select(
                assignment => new TeamGraduationYearBlockerItem
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
                "Resolve active-campaign placements that would become ineligible before changing the team's graduation year.",
                TeamLifecycleProblemExtensions.CreateGraduationYearBlockerExtensions(blockers));
        }

        team.GraduationYear = input.GraduationYear;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTeamMutationConcurrencyConflict(input.TeamId);
            return ServiceProblem.Conflict("The team changed. Reload it and try again.");
        }

        LogTeamGraduationYearChanged(input.TeamId, input.GraduationYear, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Applies the requested team lifecycle status after authorization and integrity checks.
    /// </summary>
    /// <param name="teamId">The team identifier to mutate.</param>
    /// <param name="targetStatus">The lifecycle status to apply.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Internal lifecycle outcomes before boundary mapping to shared service contracts.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, TeamArchiveBlockedConflict>> TransitionAsync(
        long teamId,
        LifecycleStatus targetStatus,
        CancellationToken cancellationToken)
    {
        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTeamLifecycleForbidden(teamId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to change team lifecycle state.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireTeamMutationLockAsync(teamId, cancellationToken);
        var team = await db.Teams
            .SingleOrDefaultAsync(candidate => candidate.TeamId == teamId, cancellationToken);

        if (team is null || team.ClubId != clubId)
        {
            LogTeamNotFound(teamId, clubId);
            return new NotFound();
        }

        if (team.LifecycleStatus == targetStatus)
        {
            LogTeamLifecycleConflict(teamId, targetStatus);
            return new LifecycleConflict($"The team is already {targetStatus.ToString().ToLowerInvariant()}.");
        }

        if (targetStatus == LifecycleStatus.Archived)
        {
            var activePlacements = await db.PlayerCampaignAssignments
                .Where(
                    assignment => assignment.TeamId == teamId
                        && assignment.Campaign.Status == CampaignStatus.Active)
                .Select(
                    assignment => new
                    {
                        assignment.CampaignId,
                        CampaignName = assignment.Campaign.Name,
                        assignment.PlayerCampaignAssignmentId
                    })
                .ToListAsync(cancellationToken);

            if (activePlacements.Count > 0)
            {
                var blockers = activePlacements
                    .GroupBy(
                        placement => new { placement.CampaignId, placement.CampaignName },
                        placement => placement.PlayerCampaignAssignmentId)
                    .Select(
                        group => new TeamArchiveBlocker
                        {
                            CampaignId = group.Key.CampaignId,
                            CampaignName = group.Key.CampaignName,
                            PlacementIds = group.OrderBy(id => id).ToList().AsReadOnly()
                        })
                    .OrderBy(blocker => blocker.CampaignId)
                    .ToList()
                    .AsReadOnly();

                LogTeamArchiveBlocked(teamId, blockers.Count);
                return new TeamArchiveBlockedConflict(blockers);
            }

            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = actorUserId;
        }
        else
        {
            team.LifecycleStatus = LifecycleStatus.Active;
            team.ArchivedAt = null;
            team.ArchivedById = null;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTeamMutationConcurrencyConflict(teamId);
            return new LifecycleConflict("The team's lifecycle changed. Reload it and try again.");
        }

        LogTeamLifecycleChanged(teamId, targetStatus, actorUserId);
        return new Success();
    }

    /// <summary>
    /// Maps an invariant-violating archive blocker returned during restore to a server error.
    /// </summary>
    /// <param name="teamId">The team identifier being restored.</param>
    /// <param name="blocked">The unexpected archive blocker outcome.</param>
    /// <returns>A server error that does not expose archive-specific guidance for restore.</returns>
    private ServiceResult<Success> MapUnexpectedRestoreBlocked(
        long teamId,
        TeamArchiveBlockedConflict blocked)
    {
        LogUnexpectedTeamRestoreBlocked(teamId, blocked.Blockers.Count);
        return ServiceProblem.ServerError("The team could not be restored because of an unexpected lifecycle conflict.");
    }

    /// <summary>
    /// Logs a team mutation rejected because the caller is not a club administrator.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unauthenticated.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team mutation forbidden for TeamId={TeamId} by UserId={UserId}.")]
    private partial void LogTeamLifecycleForbidden(long teamId, long userId);

    /// <summary>
    /// Logs a team mutation whose team is unavailable in the current tenant.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "TeamId={TeamId} was not found for ClubId={ClubId}.")]
    private partial void LogTeamNotFound(long teamId, long clubId);

    /// <summary>
    /// Logs a team mutation that conflicts with its lifecycle status.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="status">The conflicting lifecycle status.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "TeamId={TeamId} mutation conflicts with lifecycle status {Status}.")]
    private partial void LogTeamLifecycleConflict(long teamId, LifecycleStatus status);

    /// <summary>
    /// Logs a team archive blocked by active-campaign placements.
    /// </summary>
    /// <param name="teamId">The blocked team identifier.</param>
    /// <param name="campaignCount">The number of active campaigns blocking archive.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team archive blocked by active-campaign placement for TeamId={TeamId} across CampaignCount={CampaignCount}.")]
    private partial void LogTeamArchiveBlocked(long teamId, int campaignCount);

    /// <summary>
    /// Logs an invariant violation where restore unexpectedly produced archive blockers.
    /// </summary>
    /// <param name="teamId">The team identifier being restored.</param>
    /// <param name="campaignCount">The number of unexpected blocking campaigns.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Team restore unexpectedly returned archive blockers for TeamId={TeamId} across CampaignCount={CampaignCount}.")]
    private partial void LogUnexpectedTeamRestoreBlocked(long teamId, int campaignCount);

    /// <summary>
    /// Logs a team mutation rejected because the team changed concurrently.
    /// </summary>
    /// <param name="teamId">The concurrently changed team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team mutation concurrency conflict for TeamId={TeamId}.")]
    private partial void LogTeamMutationConcurrencyConflict(long teamId);

    /// <summary>
    /// Logs a successful team lifecycle transition.
    /// </summary>
    /// <param name="teamId">The changed team identifier.</param>
    /// <param name="status">The applied lifecycle status.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} lifecycle changed to {Status} by UserId={ActorUserId}.")]
    private partial void LogTeamLifecycleChanged(long teamId, LifecycleStatus status, long actorUserId);

    /// <summary>
    /// Logs invalid team graduation-year input.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team graduation-year validation failed for TeamId={TeamId}.")]
    private partial void LogTeamGraduationYearValidationFailed(long teamId);

    /// <summary>
    /// Logs a team graduation-year change blocked by active placements.
    /// </summary>
    /// <param name="teamId">The blocked team identifier.</param>
    /// <param name="blockerCount">The number of blocked placements.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team graduation-year change blocked for TeamId={TeamId} across BlockerCount={BlockerCount}.")]
    private partial void LogTeamGraduationYearBlocked(long teamId, int blockerCount);

    /// <summary>
    /// Logs a successful team graduation-year change.
    /// </summary>
    /// <param name="teamId">The changed team identifier.</param>
    /// <param name="graduationYear">The applied graduation year.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} graduation year changed to {GraduationYear} by UserId={ActorUserId}.")]
    private partial void LogTeamGraduationYearChanged(long teamId, int graduationYear, long actorUserId);

    /// <summary>
    /// Represents an archive conflict with structured active-campaign placement blockers.
    /// </summary>
    /// <param name="Blockers">The grouped blocker details loaded under the lifecycle lock.</param>
    private readonly record struct TeamArchiveBlockedConflict(IReadOnlyList<TeamArchiveBlocker> Blockers);
}
