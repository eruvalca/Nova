using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Teams;

/// <summary>
/// Describes an administrator request to change a team's graduation-year eligibility cutoff.
/// </summary>
/// <param name="TeamId">The team identifier to update.</param>
/// <param name="GraduationYear">The new minimum eligible player graduation year.</param>
public sealed record UpdateTeamGraduationYearInput(long TeamId, int GraduationYear);

/// <summary>
/// Applies tenant-safe team lifecycle and graduation-year mutations with club-administrator authorization.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for team mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for mutation outcomes.</param>
public sealed partial class TeamLifecycleService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TeamLifecycleService> logger)
{
    /// <summary>
    /// Archives a team when no active-campaign placement references it.
    /// </summary>
    /// <param name="teamId">The team identifier to archive.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ArchiveAsync(
        long teamId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(teamId, LifecycleStatus.Archived, cancellationToken);

    /// <summary>
    /// Restores an archived team to active use and clears archive provenance.
    /// </summary>
    /// <param name="teamId">The team identifier to restore.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    public Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> RestoreAsync(
        long teamId,
        CancellationToken cancellationToken = default)
        => TransitionAsync(teamId, LifecycleStatus.Active, cancellationToken);

    /// <summary>
    /// Changes an active team's graduation-year cutoff when all active placements remain eligible.
    /// </summary>
    /// <param name="input">The team identifier and proposed graduation year.</param>
    /// <param name="cancellationToken">A token that cancels the database operation.</param>
    /// <returns>Success, validation, not found, forbidden, or conflict information.</returns>
    public async Task<OneOf<
        Success,
        Error<IReadOnlyDictionary<string, string[]>>,
        NotFound,
        LifecycleForbidden,
        LifecycleConflict>> UpdateGraduationYearAsync(
            UpdateTeamGraduationYearInput input,
            CancellationToken cancellationToken = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0)
        {
            LogTeamGraduationYearValidationFailed(input.TeamId);
            return new Error<IReadOnlyDictionary<string, string[]>>(errors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogTeamLifecycleForbidden(input.TeamId, currentUserProvider.UserId ?? 0);
            return new LifecycleForbidden("You must be a club administrator to change permanent team data.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireTeamMutationLockAsync(input.TeamId, cancellationToken);
        var team = await db.Teams
            .SingleOrDefaultAsync(candidate => candidate.TeamId == input.TeamId, cancellationToken);

        if (team is null || team.ClubId != clubId)
        {
            LogTeamNotFound(input.TeamId, clubId);
            return new NotFound();
        }

        if (team.LifecycleStatus == LifecycleStatus.Archived)
        {
            LogTeamLifecycleConflict(input.TeamId, team.LifecycleStatus);
            return new LifecycleConflict("Restore the archived team before changing its graduation year.");
        }

        var createsIneligiblePlacement = await db.PlayerCampaignAssignments
            .AnyAsync(
                assignment => assignment.TeamId == input.TeamId
                && assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.Player.GraduationYear < input.GraduationYear,
                cancellationToken);

        if (createsIneligiblePlacement)
        {
            LogTeamGraduationYearBlocked(input.TeamId, input.GraduationYear);
            return new LifecycleConflict(
                "Resolve active-campaign placements that would become ineligible before changing the team's graduation year.");
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
            return new LifecycleConflict("The team changed. Reload it and try again.");
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
    /// <returns>Success, not found, forbidden, or conflict information.</returns>
    private async Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> TransitionAsync(
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
            var hasActivePlacement = await db.PlayerCampaignAssignments
                .AnyAsync(
                    assignment => assignment.TeamId == teamId && assignment.Campaign.Status == CampaignStatus.Active,
                    cancellationToken);

            if (hasActivePlacement)
            {
                LogTeamArchiveBlocked(teamId);
                return new LifecycleConflict(
                    "Resolve every active-campaign placement before archiving the team.");
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
    /// Validates team graduation-year input values that do not require database access.
    /// </summary>
    /// <param name="input">The team update input to validate.</param>
    /// <returns>A field-keyed validation-error dictionary.</returns>
    private static Dictionary<string, string[]> Validate(UpdateTeamGraduationYearInput input)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.TeamId <= 0)
        {
            errors[nameof(input.TeamId)] = ["A team identifier is required."];
        }

        if (input.GraduationYear <= 0)
        {
            errors[nameof(input.GraduationYear)] = ["A graduation year greater than zero is required."];
        }

        return errors;
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
    /// Logs a team archive blocked by an active-campaign placement.
    /// </summary>
    /// <param name="teamId">The blocked team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team archive blocked by active-campaign placement for TeamId={TeamId}.")]
    private partial void LogTeamArchiveBlocked(long teamId);

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
    /// Logs a team graduation-year change blocked by an active placement.
    /// </summary>
    /// <param name="teamId">The blocked team identifier.</param>
    /// <param name="graduationYear">The proposed graduation year.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team graduation-year change to {GraduationYear} blocked for TeamId={TeamId}.")]
    private partial void LogTeamGraduationYearBlocked(long teamId, int graduationYear);

    /// <summary>
    /// Logs a successful team graduation-year change.
    /// </summary>
    /// <param name="teamId">The changed team identifier.</param>
    /// <param name="graduationYear">The applied graduation year.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "TeamId={TeamId} graduation year changed to {GraduationYear} by UserId={ActorUserId}.")]
    private partial void LogTeamGraduationYearChanged(long teamId, int graduationYear, long actorUserId);
}
