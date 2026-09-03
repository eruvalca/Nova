using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Creates Active campaigns and initial participation snapshots in retry-safe tenant transactions.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for each execution attempt.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for campaign creation outcomes.</param>
public sealed partial class CampaignCreationService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignCreationService> logger) : ICampaignCreationService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CreateCampaignResult>> CreateAsync(
        CreateCampaignInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogCampaignCreateValidationFailed(input.OperationId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCampaignCreateForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to create campaigns.");
        }

        var normalizedInput = input.InlineSeason is null
            ? input
            : input with { InlineSeason = input.InlineSeason with { Name = input.InlineSeason.Name.Trim() } };
        return await ExecuteWithFreshContextAsync(
            db => CreateCampaignAsync(db, normalizedInput, actorUserId, clubId, cancellationToken),
            db => VerifyCampaignCreationAsync(db, clubId, input.OperationId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs campaign creation inside EF Core's retrying execution strategy with fresh tenant contexts
    /// and verifies an uncertain commit before replaying the mutation.
    /// </summary>
    /// <typeparam name="TResult">The result produced by campaign creation.</typeparam>
    /// <param name="operation">The mutation to execute with a fresh tenant context.</param>
    /// <param name="verifySucceeded">The verification query to execute with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels execution or verification.</param>
    /// <returns>The mutation result or a reconstructed committed result.</returns>
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
    /// Executes one transactional campaign creation attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The campaign and season request.</param>
    /// <param name="actorUserId">The authenticated club administrator identifier.</param>
    /// <param name="clubId">The current tenant club identifier.</param>
    /// <param name="cancellationToken">A token that cancels database work.</param>
    /// <returns>The committed aggregate or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<CreateCampaignResult>> CreateCampaignAsync(
        NovaDbContext db,
        CreateCampaignInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);

        var committedResult = await FindCommittedResultAsync(
            db,
            clubId,
            input.OperationId,
            cancellationToken);
        if (committedResult is not null)
        {
            return committedResult;
        }

        var club = await db.Clubs.SingleOrDefaultAsync(
            candidate => candidate.ClubId == clubId,
            cancellationToken);
        if (club is null)
        {
            return ServiceProblem.NotFound();
        }

        var season = await ResolveSeasonAsync(
            db,
            input,
            actorUserId,
            clubId,
            club,
            cancellationToken);
        if (season.IsProblem)
        {
            return season.Problem;
        }

        var dateErrors = ValidateCampaignDates(input, season.Value);
        if (dateErrors.Count > 0)
        {
            LogCampaignDateValidationFailed(input.OperationId, season.Value.SeasonId);
            return ServiceProblem.Validation(dateErrors);
        }

        if (season.Value.SeasonId > 0
            && await db.Campaigns.AnyAsync(
                campaign => campaign.SeasonId == season.Value.SeasonId
                    && campaign.Name == input.Name,
                cancellationToken))
        {
            LogDuplicateCampaignName(clubId, season.Value.SeasonId);
            return ServiceProblem.Conflict(
                "A campaign with that name already exists in the selected season.");
        }

        var campaign = new CampaignEntity
        {
            CreationOperationId = input.OperationId,
            SeasonCreatedInline = input.InlineSeason is not null,
            Name = input.Name,
            StartDate = input.StartDate,
            EndDate = input.PlannedEndDate,
            Status = CampaignStatus.Active,
            ClubId = clubId,
            SeasonId = season.Value.SeasonId,
            Season = season.Value,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(campaign);

        try
        {
            var activePlayerIds = await db.Players
                .Where(player => player.LifecycleStatus == LifecycleStatus.Active)
                .Select(player => player.PlayerId)
                .ToListAsync(cancellationToken);
            campaign.InitialEnrolledPlayerCount = activePlayerIds.Count;

            await db.SaveChangesAsync(cancellationToken);

            foreach (var playerId in activePlayerIds)
            {
                db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
                {
                    CampaignId = campaign.CampaignId,
                    PlayerId = playerId,
                    ClubId = clubId,
                    PlacementOutcome = PlacementOutcome.Undecided,
                    CreatedById = actorUserId
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogCampaignCreated(
                input.OperationId,
                campaign.CampaignId,
                season.Value.SeasonId,
                activePlayerIds.Count,
                actorUserId);
            return ToResult(campaign, season.Value, activePlayerIds.Count);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogCampaignCreateConcurrencyConflict(input.OperationId);
            return ServiceProblem.Conflict(
                "The campaign could not be created because related data changed. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogCampaignCreateUniqueConflict(input.OperationId, clubId);
            return ServiceProblem.Conflict(
                "The campaign operation, campaign name, or inline season name already exists.");
        }
    }

    /// <summary>
    /// Resolves a tenant-visible existing season or stages a new inline season.
    /// </summary>
    /// <param name="db">The tenant context participating in campaign creation.</param>
    /// <param name="input">The request containing exactly one season choice.</param>
    /// <param name="actorUserId">The authenticated club administrator identifier.</param>
    /// <param name="clubId">The current tenant club identifier.</param>
    /// <param name="club">The tracked club whose current-season pointer is authoritative.</param>
    /// <param name="cancellationToken">A token that cancels season resolution.</param>
    /// <returns>The selected or staged season, or a known failure.</returns>
    private async Task<ServiceResult<SeasonEntity>> ResolveSeasonAsync(
        NovaDbContext db,
        CreateCampaignInput input,
        long actorUserId,
        long clubId,
        ClubEntity club,
        CancellationToken cancellationToken)
    {
        if (input.ExistingSeasonId is long seasonId)
        {
            var existingSeason = await db.Seasons
                .SingleOrDefaultAsync(season => season.SeasonId == seasonId, cancellationToken);
            if (existingSeason is null)
            {
                LogSeasonNotFound(seasonId, clubId);
                return ServiceProblem.NotFound("The selected season was not found.");
            }

            if (club.CurrentSeasonId != existingSeason.SeasonId)
            {
                return ServiceProblem.Conflict(
                    "The selected season is not the club's current season.");
            }

            return existingSeason;
        }

        var inlineSeason = input.InlineSeason!;
        if (club.CurrentSeasonId is not null)
        {
            return ServiceProblem.Conflict(
                "The club already has a current season. Select it instead of creating an inline season.");
        }

        if (await db.Seasons.AnyAsync(season => season.Name == inlineSeason.Name, cancellationToken))
        {
            LogDuplicateSeasonName(clubId);
            return ServiceProblem.Conflict(
                "A season with that name already exists. Choose a different season name.");
        }

        var season = new SeasonEntity
        {
            CreationOperationId = input.OperationId,
            CreationKind = SeasonCreationKind.InlineCampaign,
            Name = inlineSeason.Name,
            StartDate = inlineSeason.StartDate,
            EndDate = inlineSeason.EndDate,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        club.CurrentSeason = season;
        return season;
    }

    /// <summary>
    /// Validates that campaign dates are fully contained by the selected season.
    /// </summary>
    /// <param name="input">The campaign dates to validate.</param>
    /// <param name="season">The selected or staged season.</param>
    /// <returns>A field-keyed validation dictionary; empty when the dates fit the season.</returns>
    private static Dictionary<string, string[]> ValidateCampaignDates(
        CreateCampaignInput input,
        SeasonEntity season)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.StartDate < season.StartDate)
        {
            errors[nameof(CreateCampaignInput.StartDate)] =
                ["The campaign start date cannot be before the season start date."];
        }

        if (season.EndDate is DateOnly seasonEndDate)
        {
            if (input.StartDate > seasonEndDate)
            {
                errors[nameof(CreateCampaignInput.StartDate)] =
                    ["The campaign start date cannot be after the season end date."];
            }

            errors[nameof(CreateCampaignInput.PlannedEndDate)] = input.PlannedEndDate switch
            {
                null => ["A campaign in a finite season must have a planned end date."],
                DateOnly campaignEndDate when campaignEndDate > seasonEndDate
                    => ["The planned campaign end date cannot be after the season end date."],
                _ => []
            };

            if (errors[nameof(CreateCampaignInput.PlannedEndDate)].Length == 0)
            {
                errors.Remove(nameof(CreateCampaignInput.PlannedEndDate));
            }
        }

        return errors;
    }

    /// <summary>
    /// Finds and reconstructs a previously committed campaign creation result by caller operation ID.
    /// </summary>
    /// <param name="db">The tenant context used for the lookup.</param>
    /// <param name="clubId">The current tenant club identifier.</param>
    /// <param name="operationId">The caller-generated creation operation identifier.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>The committed result, or <see langword="null"/> when the operation is not present.</returns>
    private static Task<CreateCampaignResult?> FindCommittedResultAsync(
        NovaDbContext db,
        long clubId,
        Guid operationId,
        CancellationToken cancellationToken)
        => db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.ClubId == clubId
                && campaign.CreationOperationId == operationId)
            .Select(campaign => new CreateCampaignResult(
                operationId,
                campaign.CampaignId,
                campaign.Name,
                campaign.StartDate,
                campaign.EndDate,
                CampaignStatus.Active,
                campaign.SeasonId,
                campaign.Season.Name,
                campaign.Season.StartDate,
                campaign.Season.EndDate,
                campaign.SeasonCreatedInline,
                campaign.InitialEnrolledPlayerCount))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Checks whether an uncertain commit persisted the campaign aggregate.
    /// </summary>
    /// <param name="db">The fresh tenant context used for verification.</param>
    /// <param name="clubId">The current tenant club identifier.</param>
    /// <param name="operationId">The caller-generated creation operation identifier.</param>
    /// <param name="cancellationToken">A token that cancels verification.</param>
    /// <returns>An execution result indicating whether the committed aggregate was found.</returns>
    private async Task<ExecutionResult<ServiceResult<CreateCampaignResult>>> VerifyCampaignCreationAsync(
        NovaDbContext db,
        long clubId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var result = await FindCommittedResultAsync(db, clubId, operationId, cancellationToken);
        if (result is null)
        {
            return new ExecutionResult<ServiceResult<CreateCampaignResult>>(
                successful: false,
                default!);
        }

        LogCampaignCommitRecovered(
            operationId,
            result.CampaignId,
            result.SeasonId,
            result.EnrolledPlayerCount);
        return new ExecutionResult<ServiceResult<CreateCampaignResult>>(
            successful: true,
            result);
    }

    /// <summary>
    /// Maps tracked campaign and season entities to the shared creation result.
    /// </summary>
    /// <param name="campaign">The committed campaign.</param>
    /// <param name="season">The selected or created season.</param>
    /// <param name="enrolledPlayerCount">The number of initial participations.</param>
    /// <returns>The shared campaign creation result.</returns>
    private static CreateCampaignResult ToResult(
        CampaignEntity campaign,
        SeasonEntity season,
        int enrolledPlayerCount)
        => new(
            campaign.CreationOperationId,
            campaign.CampaignId,
            campaign.Name,
            campaign.StartDate,
            campaign.EndDate,
            campaign.Status,
            season.SeasonId,
            season.Name,
            season.StartDate,
            season.EndDate,
            campaign.SeasonCreatedInline,
            enrolledPlayerCount);

    /// <summary>
    /// Determines whether a persistence failure was caused by a unique-index violation.
    /// </summary>
    /// <param name="exception">The persistence failure to classify.</param>
    /// <returns><see langword="true"/> when the provider reported a unique violation.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message;
        return message is not null
            && (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Logs structural validation rejection before database access.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign creation validation failed for OperationId={OperationId}.")]
    private partial void LogCampaignCreateValidationFailed(Guid operationId);

    /// <summary>Logs campaign creation rejected because the caller is not a club administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign creation forbidden for UserId={UserId}.")]
    private partial void LogCampaignCreateForbidden(long userId);

    /// <summary>Logs a selected season that is not visible in the current tenant.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "SeasonId={SeasonId} was not found for ClubId={ClubId}.")]
    private partial void LogSeasonNotFound(long seasonId, long clubId);

    /// <summary>Logs an inline season name conflict.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Inline season name conflict for ClubId={ClubId}.")]
    private partial void LogDuplicateSeasonName(long clubId);

    /// <summary>Logs a campaign name conflict within a season.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign name conflict for ClubId={ClubId}, SeasonId={SeasonId}.")]
    private partial void LogDuplicateCampaignName(long clubId, long seasonId);

    /// <summary>Logs contextual campaign-to-season date validation rejection.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign date validation failed for OperationId={OperationId}, SeasonId={SeasonId}.")]
    private partial void LogCampaignDateValidationFailed(Guid operationId, long seasonId);

    /// <summary>Logs a campaign creation concurrency conflict.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign creation concurrency conflict for OperationId={OperationId}.")]
    private partial void LogCampaignCreateConcurrencyConflict(Guid operationId);

    /// <summary>Logs a database uniqueness conflict during campaign creation.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign creation uniqueness conflict for OperationId={OperationId}, ClubId={ClubId}.")]
    private partial void LogCampaignCreateUniqueConflict(Guid operationId, long clubId);

    /// <summary>Logs successful campaign creation and initial enrollment.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign creation OperationId={OperationId} committed as CampaignId={CampaignId} in SeasonId={SeasonId} with {PlayerCount} player(s) by UserId={ActorUserId}.")]
    private partial void LogCampaignCreated(
        Guid operationId,
        long campaignId,
        long seasonId,
        int playerCount,
        long actorUserId);

    /// <summary>Logs successful verification after an ambiguous campaign creation commit.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Campaign creation OperationId={OperationId} recovered committed CampaignId={CampaignId} in SeasonId={SeasonId} with {PlayerCount} player(s) after an ambiguous commit.")]
    private partial void LogCampaignCommitRecovered(
        Guid operationId,
        long campaignId,
        long seasonId,
        int playerCount);
}
