using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Players;

/// <summary>
/// Creates and updates player profiles with club-administrator authorization. Player creation
/// atomically enrolls the new player into every Active campaign in the same transaction.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for service outcomes.</param>
public sealed partial class PlayerManagementService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<PlayerManagementService> logger) : IPlayerManagementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDto>> CreateAsync(
        CreatePlayerInput input,
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
            LogPlayerCreateForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to create players.");
        }

        var creationOperationId = Guid.CreateVersion7();
        return await ExecuteWithFreshContextAsync(
            db => CreatePlayerAsync(
                db,
                input,
                actorUserId,
                clubId,
                creationOperationId,
                cancellationToken),
            db => VerifyPlayerCreationAsync(
                db,
                clubId,
                creationOperationId,
                cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDto>> UpdateAsync(
        UpdatePlayerInput input,
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
            LogPlayerUpdateForbidden(input.PlayerId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to edit players.");
        }

        return await ExecuteWithFreshContextAsync(
            db => UpdatePlayerAsync(db, input, actorUserId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Projects a <see cref="PlayerEntity"/> to a <see cref="PlayerDto"/>.
    /// </summary>
    private static PlayerDto ToDto(PlayerEntity player) => new()
    {
        PlayerId = player.PlayerId,
        ClubId = player.ClubId,
        FirstName = player.FirstName,
        LastName = player.LastName,
        DateOfBirth = player.DateOfBirth,
        GraduationYear = player.GraduationYear,
        Gender = player.Gender,
        JerseyNumber = player.JerseyNumber,
        LifecycleStatus = player.LifecycleStatus
    };

    /// <summary>
    /// Runs a player-management mutation inside EF Core's retrying execution strategy while creating
    /// a fresh tenant context for each execution attempt.
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
    /// Runs a player-management mutation inside EF Core's retrying execution strategy and verifies
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
    /// Creates one player and enrolls them in every active campaign using one transactional execution
    /// attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested player profile details.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for this logical creation operation.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The created player or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<PlayerDto>> CreatePlayerAsync(
        NovaDbContext db,
        CreatePlayerInput input,
        long actorUserId,
        long clubId,
        Guid creationOperationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Serialize with campaign creation so late-joining player and concurrent campaign cannot miss each other.
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);

        var player = new PlayerEntity
        {
            FirstName = input.FirstName,
            LastName = input.LastName,
            DateOfBirth = input.DateOfBirth,
            GraduationYear = input.GraduationYear,
            Gender = input.Gender,
            JerseyNumber = input.JerseyNumber,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreationOperationId = creationOperationId,
            CreatedById = default
        };

        db.Players.Add(player);

        try
        {
            // Save first to obtain the generated PlayerId before inserting participations.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogPlayerCreateConcurrencyConflict(actorUserId);
            return ServiceProblem.Conflict("The player could not be created. Reload and try again.");
        }

        var activeCampaignIds = await db.Campaigns
            .Where(campaign => campaign.Status == CampaignStatus.Active)
            .Select(campaign => campaign.CampaignId)
            .ToListAsync(cancellationToken);

        foreach (var campaignId in activeCampaignIds)
        {
            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = campaignId,
                ClubId = clubId,
                PlacementOutcome = PlacementOutcome.Undecided,
                CreatedById = default
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogPlayerCreateConcurrencyConflict(actorUserId);
            return ServiceProblem.Conflict("The player could not be enrolled in campaigns. Reload and try again.");
        }

        LogPlayerCreated(player.PlayerId, activeCampaignIds.Count, actorUserId);
        return ToDto(player);
    }

    /// <summary>
    /// Checks whether a player-creation transaction with an uncertain commit outcome was committed
    /// and reconstructs its successful service result without replaying the insert.
    /// </summary>
    /// <param name="db">The fresh tenant context used for commit verification.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="creationOperationId">The stable identifier for the logical creation operation.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed player was found.</returns>
    private static async Task<ExecutionResult<ServiceResult<PlayerDto>>> VerifyPlayerCreationAsync(
        NovaDbContext db,
        long clubId,
        Guid creationOperationId,
        CancellationToken cancellationToken)
    {
        var player = await db.Players
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ClubId == clubId
                    && candidate.CreationOperationId == creationOperationId,
                cancellationToken);

        return player is null
            ? new ExecutionResult<ServiceResult<PlayerDto>>(successful: false, default!)
            : new ExecutionResult<ServiceResult<PlayerDto>>(successful: true, ToDto(player));
    }

    /// <summary>
    /// Updates one player profile using one transactional execution attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The requested player profile updates.</param>
    /// <param name="actorUserId">The authenticated club-administrator identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The updated player or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<PlayerDto>> UpdatePlayerAsync(
        NovaDbContext db,
        UpdatePlayerInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquirePlayerMutationLockAsync(input.PlayerId, cancellationToken);

        var player = await db.Players
            .SingleOrDefaultAsync(candidate => candidate.PlayerId == input.PlayerId, cancellationToken);

        if (player is null || player.ClubId != clubId)
        {
            LogPlayerNotFound(input.PlayerId, clubId);
            return ServiceProblem.NotFound();
        }

        if (player.LifecycleStatus != LifecycleStatus.Active)
        {
            LogPlayerUpdateArchivedConflict(input.PlayerId);
            return ServiceProblem.Conflict("Archived players cannot be edited through this workflow. Restore the player first.");
        }

        // Check graduation-year eligibility only when the value changes.
        if (input.GraduationYear != player.GraduationYear)
        {
            var assignedPlacements = await db.PlayerCampaignAssignments
                .Where(assignment =>
                    assignment.PlayerId == input.PlayerId
                    && assignment.PlacementOutcome == PlacementOutcome.Assigned
                    && assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.TeamId != null)
                .Select(assignment => new AssignedPlacementFacts(
                    assignment.PlayerCampaignAssignmentId,
                    assignment.CampaignId,
                    assignment.TeamId!.Value,
                    assignment.Team!.GraduationYear))
                .ToListAsync(cancellationToken);

            var decision = PlayerGraduationYearPolicy.Evaluate(input.GraduationYear, assignedPlacements);
            var mayBlock = decision.Match(
                _ => (ServiceProblem?)null,
                blocked =>
                {
                    LogPlayerGraduationYearBlocked(input.PlayerId, blocked.Blockers.Count);
                    var errors = BuildBlockerErrors(blocked.Blockers);
                    return (ServiceProblem?)ServiceProblem.Conflict(
                        "The proposed graduation year would make one or more Active-campaign placements ineligible.",
                        errors);
                });

            if (mayBlock.HasValue)
            {
                return mayBlock.Value;
            }
        }

        player.FirstName = input.FirstName;
        player.LastName = input.LastName;
        player.DateOfBirth = input.DateOfBirth;
        player.GraduationYear = input.GraduationYear;
        player.Gender = input.Gender;
        player.JerseyNumber = input.JerseyNumber;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogPlayerUpdateConcurrencyConflict(input.PlayerId);
            return ServiceProblem.Conflict("The player changed concurrently. Reload and try again.");
        }

        LogPlayerUpdated(player.PlayerId, actorUserId);
        return ToDto(player);
    }

    /// <summary>
    /// Encodes graduation-year blocker items into the ServiceProblem errors dictionary using
    /// indexed keys so clients can read structured fields without parsing text.
    /// </summary>
    private static IReadOnlyDictionary<string, string[]> BuildBlockerErrors(
        IReadOnlyList<GraduationYearBlockerItem> blockers)
    {
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < blockers.Count; i++)
        {
            var b = blockers[i];
            errors[$"blockers[{i}].assignmentId"] = [b.PlayerCampaignAssignmentId.ToString()];
            errors[$"blockers[{i}].campaignId"] = [b.CampaignId.ToString()];
            errors[$"blockers[{i}].teamId"] = [b.TeamId.ToString()];
            errors[$"blockers[{i}].teamGraduationYear"] = [b.TeamGraduationYear.ToString()];
        }
        return errors;
    }

    /// <summary>Logs a create request rejected because the caller is not a club administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player create forbidden for UserId={UserId}.")]
    private partial void LogPlayerCreateForbidden(long userId);

    /// <summary>Logs an update request rejected because the caller is not a club administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player update forbidden for PlayerId={PlayerId} by UserId={UserId}.")]
    private partial void LogPlayerUpdateForbidden(long playerId, long userId);

    /// <summary>Logs a player lookup that returned no result in the current tenant.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "PlayerId={PlayerId} was not found for ClubId={ClubId}.")]
    private partial void LogPlayerNotFound(long playerId, long clubId);

    /// <summary>Logs an update attempt on an archived player.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player update conflict: PlayerId={PlayerId} is archived.")]
    private partial void LogPlayerUpdateArchivedConflict(long playerId);

    /// <summary>Logs a graduation-year change blocked by ineligible active placements.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Graduation-year edit blocked for PlayerId={PlayerId}: {BlockerCount} ineligible placement(s).")]
    private partial void LogPlayerGraduationYearBlocked(long playerId, int blockerCount);

    /// <summary>Logs a create operation that failed due to a concurrent data change.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player create concurrency conflict for UserId={UserId}.")]
    private partial void LogPlayerCreateConcurrencyConflict(long userId);

    /// <summary>Logs an update operation that failed due to a concurrent data change.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player update concurrency conflict for PlayerId={PlayerId}.")]
    private partial void LogPlayerUpdateConcurrencyConflict(long playerId);

    /// <summary>Logs successful player creation with enrollment count.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerId={PlayerId} created and enrolled in {CampaignCount} campaign(s) by UserId={ActorUserId}.")]
    private partial void LogPlayerCreated(long playerId, int campaignCount, long actorUserId);

    /// <summary>Logs successful player profile update.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "PlayerId={PlayerId} updated by UserId={ActorUserId}.")]
    private partial void LogPlayerUpdated(long playerId, long actorUserId);
}
