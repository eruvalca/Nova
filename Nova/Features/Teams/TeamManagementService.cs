using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Teams;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
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
    /// <summary>
    /// The conflict message returned when a club already owns a team with the same name and
    /// graduation year.
    /// </summary>
    private const string DuplicateTeamMessage =
        "A team with that name and graduation year already exists.";

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

        var creationOperationId = Guid.CreateVersion7();
        return await ExecuteWithFreshContextAsync(
            db => CreateTeamAsync(db, input, actorUserId, clubId, creationOperationId, cancellationToken),
            db => VerifyTeamCreationAsync(db, clubId, creationOperationId, cancellationToken),
            cancellationToken);
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

        return await ExecuteWithFreshContextAsync(
            db => UpdateTeamAsync(db, input, actorUserId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs a team-management mutation inside EF Core's retrying execution strategy while creating a
    /// fresh tenant context for each execution attempt.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the mutation attempt.</typeparam>
    /// <param name="operation">The mutation to run with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels the strategy setup or mutation attempt.</param>
    /// <returns>The result returned by the successful execution-strategy attempt.</returns>
    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        Func<NovaDbContext, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await operation(db);
        });
    }

    /// <summary>
    /// Runs a team-management mutation inside EF Core's retrying execution strategy and verifies
    /// whether an ambiguous commit succeeded before allowing the strategy to replay the mutation.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the mutation attempt.</typeparam>
    /// <param name="operation">The mutation to run with a fresh tenant context.</param>
    /// <param name="verifySucceeded">The verification query to run with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels strategy setup, mutation, or verification.</param>
    /// <returns>The mutation result or the reconstructed result from successful commit verification.</returns>
    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        Func<NovaDbContext, Task<TResult>> operation,
        Func<NovaDbContext, Task<ExecutionResult<TResult>>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            (Operation: operation, VerifySucceeded: verifySucceeded),
            async (state, _) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.Operation(db);
            },
            async (state, _) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.VerifySucceeded(db);
            },
            cancellationToken);
    }

    /// <summary>
    /// Creates one team using a single transactional execution attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested team details.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for this logical creation operation.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The created team or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<TeamDto>> CreateTeamAsync(
        NovaDbContext db,
        CreateTeamInput input,
        long actorUserId,
        long clubId,
        Guid creationOperationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize with concurrent team mutations in the same club so the duplicate-name probe and
        // the insert observe the same snapshot.
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);

        if (await TeamNameExistsAsync(db, clubId, input.Name, input.GraduationYear, excludedTeamId: null, cancellationToken))
        {
            LogDuplicateTeamName(clubId, input.GraduationYear);
            return ServiceProblem.Conflict(DuplicateTeamMessage);
        }

        var team = new TeamEntity
        {
            Name = input.Name,
            GraduationYear = input.GraduationYear,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreationOperationId = creationOperationId,
            CreatedById = actorUserId
        };
        db.Teams.Add(team);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogTeamMutationConcurrencyConflict(team.TeamId);
            return ServiceProblem.Conflict("The team could not be created. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogDuplicateTeamName(clubId, input.GraduationYear);
            return ServiceProblem.Conflict(DuplicateTeamMessage);
        }

        LogTeamCreated(team.TeamId, actorUserId);
        return team.ToTeamDto();
    }

    /// <summary>
    /// Checks whether a team-creation transaction with an uncertain commit outcome was committed and
    /// reconstructs its successful service result without replaying the insert.
    /// </summary>
    /// <param name="db">The fresh tenant context used for commit verification.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for the logical creation operation.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed team was found.</returns>
    private async Task<ExecutionResult<ServiceResult<TeamDto>>> VerifyTeamCreationAsync(
        NovaDbContext db,
        long clubId,
        Guid creationOperationId,
        CancellationToken cancellationToken)
    {
        var team = await db.Teams
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClubId == clubId
                    && candidate.CreationOperationId == creationOperationId,
                cancellationToken);

        if (team is null)
        {
            return new ExecutionResult<ServiceResult<TeamDto>>(successful: false, default!);
        }

        LogTeamCreationCommitRecovered(team.TeamId, creationOperationId, clubId);
        return new ExecutionResult<ServiceResult<TeamDto>>(successful: true, team.ToTeamDto());
    }

    /// <summary>
    /// Updates one team using a single transactional execution attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested team updates.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The updated team or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<TeamDto>> UpdateTeamAsync(
        NovaDbContext db,
        UpdateTeamInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Lock every player currently placed on this team before locking the team itself.
        //
        // A team graduation-year change and a player graduation-year change validate the same
        // invariant (an Assigned placement in an Active campaign requires
        // Player.GraduationYear >= Team.GraduationYear) from opposite sides.
        // PlayerManagementService.UpdatePlayerAsync holds only the player lock, so without this the
        // two mutations take disjoint locks, each reads the other's pre-change value, both pass
        // policy evaluation, and together they commit an ineligible placement.
        //
        // Locking players first (ascending) and the team second matches the single global order
        // every writer of this invariant already follows: campaign, then players ascending, then
        // team. CampaignPlacementService takes campaign then player then team, the player service
        // takes only a player, and TeamLifecycleService takes only a team, so each path takes a
        // subsequence of that order and no cycle - and therefore no deadlock - is possible. Any new
        // placement-mutation path must follow the same order.
        var lockedPlayerIds = await db.PlayerCampaignAssignments
            .Where(assignment =>
                assignment.TeamId == input.TeamId
                && assignment.PlacementOutcome == PlacementOutcome.Assigned
                && assignment.Campaign.Status == CampaignStatus.Active)
            .Select(assignment => assignment.PlayerId)
            .Distinct()
            .OrderBy(playerId => playerId)
            .ToListAsync(cancellationToken);
        foreach (var playerId in lockedPlayerIds)
        {
            await db.AcquirePlayerMutationLockAsync(playerId, cancellationToken);
        }

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

        // Check placement eligibility only when the graduation year actually changes.
        if (input.GraduationYear != team.GraduationYear)
        {
            var assignedPlacements = await db.PlayerCampaignAssignments
                .Where(assignment =>
                    assignment.TeamId == input.TeamId
                    && assignment.PlacementOutcome == PlacementOutcome.Assigned
                    && assignment.Campaign.Status == CampaignStatus.Active)
                .Select(assignment => new TeamAssignedPlacementFacts(
                    assignment.PlayerCampaignAssignmentId,
                    assignment.CampaignId,
                    assignment.PlayerId,
                    assignment.Player.GraduationYear))
                .ToListAsync(cancellationToken);

            var decision = TeamGraduationYearPolicy.Evaluate(input.GraduationYear, assignedPlacements);

            // Fail safe if a placement appeared for an unlocked player between computing the lock
            // set and taking the team lock. CampaignPlacementService can assign a player to this
            // team in that window, so this is reachable; it surfaces as a retryable conflict rather
            // than a silently unenforced invariant.
            if (assignedPlacements.Exists(placement => !lockedPlayerIds.Contains(placement.PlayerId)))
            {
                LogTeamPlacementSetChangedUnderLock(input.TeamId);
                return ServiceProblem.Conflict("The team's placements changed while validating eligibility. Reload and try again.");
            }

            var blocked = decision.Match(
                _ => (ServiceProblem?)null,
                blockedOutcome =>
                {
                    LogTeamGraduationYearBlocked(input.TeamId, blockedOutcome.Blockers.Count);
                    return (ServiceProblem?)ServiceProblem.Conflict(
                        "The proposed graduation year would make one or more Active-campaign placements ineligible.",
                        TeamLifecycleProblemExtensions.CreateGraduationYearBlockerExtensions(blockedOutcome.Blockers));
                });

            if (blocked.HasValue)
            {
                return blocked.Value;
            }
        }

        if (!string.Equals(input.Name, team.Name, StringComparison.Ordinal)
            || input.GraduationYear != team.GraduationYear)
        {
            if (await TeamNameExistsAsync(db, clubId, input.Name, input.GraduationYear, input.TeamId, cancellationToken))
            {
                LogDuplicateTeamName(clubId, input.GraduationYear);
                return ServiceProblem.Conflict(DuplicateTeamMessage);
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
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogDuplicateTeamName(clubId, input.GraduationYear);
            return ServiceProblem.Conflict(DuplicateTeamMessage);
        }

        LogTeamUpdated(team.TeamId, actorUserId);
        return team.ToTeamDto();
    }

    /// <summary>
    /// Determines whether the club already owns a team with the supplied name and graduation year.
    /// </summary>
    /// <param name="db">The tenant context for the current execution attempt.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="name">The proposed team name.</param>
    /// <param name="graduationYear">The proposed graduation year.</param>
    /// <param name="excludedTeamId">The team being updated, excluded from the probe, when applicable.</param>
    /// <param name="cancellationToken">A token that cancels the probe query.</param>
    /// <returns><see langword="true"/> when a conflicting team already exists.</returns>
    private static Task<bool> TeamNameExistsAsync(
        NovaDbContext db,
        long clubId,
        string name,
        int graduationYear,
        long? excludedTeamId,
        CancellationToken cancellationToken)
        => db.Teams.AnyAsync(
            candidate => candidate.ClubId == clubId
                && candidate.Name == name
                && candidate.GraduationYear == graduationYear
                && (excludedTeamId == null || candidate.TeamId != excludedTeamId),
            cancellationToken);

    /// <summary>
    /// Determines whether a persistence failure was caused by a unique-index violation. The check is
    /// text-based so it holds for both the Npgsql production provider (SQLSTATE 23505) and the SQLite
    /// provider used by the tenancy unit-test harness, without either provider being referenced here.
    /// </summary>
    /// <param name="exception">The persistence failure to classify.</param>
    /// <returns><see langword="true"/> when the failure was a unique-index violation.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message;
        return message is not null
            && (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Logs a team update abandoned because its placement set changed after the player lock set was
    /// computed, leaving an unlocked player in the eligibility facts.
    /// </summary>
    /// <param name="teamId">The affected team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team placement set changed under lock for TeamId={TeamId}; eligibility could not be validated safely.")]
    private partial void LogTeamPlacementSetChangedUnderLock(long teamId);

    /// <summary>Logs a team mutation that failed due to a concurrent data change.</summary>
    /// <param name="teamId">The affected team identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team mutation concurrency conflict for TeamId={TeamId}.")]
    private partial void LogTeamMutationConcurrencyConflict(long teamId);

    /// <summary>Logs a team mutation rejected because the club already owns a matching team.</summary>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="graduationYear">The conflicting graduation year.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate team name rejected for ClubId={ClubId} and GraduationYear={GraduationYear}.")]
    private partial void LogDuplicateTeamName(long clubId, int graduationYear);

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

    /// <summary>Logs a team-creation result reconstructed from an ambiguous commit.</summary>
    /// <param name="teamId">The committed team identifier.</param>
    /// <param name="operationId">The stable identifier for the logical creation operation.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Team creation OperationId={OperationId} recovered committed TeamId={TeamId} in ClubId={ClubId} after an ambiguous commit.")]
    private partial void LogTeamCreationCommitRecovered(long teamId, Guid operationId, long clubId);
}
