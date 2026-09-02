using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Seasons;

/// <summary>Provides retry-safe season creation, metadata updates, and advancement.</summary>
/// <param name="dbContextFactory">The tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user and club state.</param>
/// <param name="logger">The logger used for season lifecycle outcomes.</param>
public sealed partial class SeasonCommandService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<SeasonCommandService> logger) : ISeasonCommandService
{
    /// <inheritdoc />
    public async Task<ServiceResult<SeasonSummary>> CreateAsync(
        CreateSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogSeasonCreateValidationFailed(input.OperationId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
            LogSeasonCreateForbidden(currentUserProvider.UserId ?? 0, input.OperationId);
            return ServiceProblem.Forbidden("You must be a club administrator to create seasons.");
        }

        var normalizedInput = input with { Name = input.Name.Trim() };
        return await ExecuteRetrySafeAsync(
            db => CreateAttemptAsync(db, normalizedInput, actorUserId, clubId, cancellationToken),
            async db =>
            {
                var committed = await FindCommittedSeasonAsync(
                    db,
                    clubId,
                    normalizedInput.OperationId,
                    cancellationToken);
                if (committed is null)
                {
                    return new ExecutionResult<ServiceResult<SeasonSummary>>(false, default!);
                }

                var replay = ToCreateReplayResult(committed);
                if (replay.IsSuccess)
                {
                    LogSeasonCreateCommitRecovered(
                        normalizedInput.OperationId,
                        committed.SeasonId,
                        clubId);
                }
                else
                {
                    LogSeasonCreateConflict(
                        normalizedInput.OperationId,
                        clubId,
                        "operation-kind-collision");
                }

                return new ExecutionResult<ServiceResult<SeasonSummary>>(true, replay);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeasonSummary>> UpdateAsync(
        long seasonId,
        UpdateSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (seasonId <= 0)
        {
            validationErrors[nameof(seasonId)] = ["A valid season identifier is required."];
        }

        if (validationErrors.Count > 0)
        {
            LogSeasonUpdateValidationFailed(seasonId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
            LogSeasonUpdateForbidden(currentUserProvider.UserId ?? 0, seasonId);
            return ServiceProblem.Forbidden("You must be a club administrator to update seasons.");
        }

        var normalizedInput = input with { Name = input.Name.Trim() };
        return await ExecuteAsync(
            db => UpdateAttemptAsync(
                db,
                seasonId,
                normalizedInput,
                actorUserId,
                clubId,
                cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<StartNextSeasonResult>> StartNextAsync(
        StartNextSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogSeasonAdvanceValidationFailed(input.OperationId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
            LogSeasonAdvanceForbidden(currentUserProvider.UserId ?? 0, input.OperationId);
            return ServiceProblem.Forbidden("You must be a club administrator to start the next season.");
        }

        var normalizedInput = input with { Name = input.Name.Trim() };
        return await ExecuteRetrySafeAsync(
            db => StartNextAttemptAsync(db, normalizedInput, actorUserId, clubId, cancellationToken),
            async db =>
            {
                var current = await FindCommittedSeasonAsync(
                    db,
                    clubId,
                    normalizedInput.OperationId,
                    cancellationToken);
                if (current is null)
                {
                    return new ExecutionResult<ServiceResult<StartNextSeasonResult>>(false, default!);
                }

                var replay = ToStartNextReplayResult(
                    current,
                    normalizedInput.ExpectedCurrentSeasonId);
                if (replay.IsSuccess)
                {
                    LogSeasonAdvanceCommitRecovered(
                        normalizedInput.OperationId,
                        normalizedInput.ExpectedCurrentSeasonId,
                        current.SeasonId,
                        clubId);
                }
                else
                {
                    LogSeasonAdvanceConflict(
                        normalizedInput.OperationId,
                        normalizedInput.ExpectedCurrentSeasonId,
                        clubId,
                        "operation-predecessor-mismatch");
                }

                return new ExecutionResult<ServiceResult<StartNextSeasonResult>>(true, replay);
            },
            cancellationToken);
    }

    /// <summary>Executes one transaction that creates the club's first current season.</summary>
    private async Task<ServiceResult<SeasonSummary>> CreateAttemptAsync(
        NovaDbContext db,
        CreateSeasonInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);

        var committed = await FindCommittedSeasonAsync(db, clubId, input.OperationId, cancellationToken);
        if (committed is not null)
        {
            var replay = ToCreateReplayResult(committed);
            if (replay.IsSuccess)
            {
                LogSeasonCreateReplayed(input.OperationId, committed.SeasonId, clubId);
            }
            else
            {
                LogSeasonCreateConflict(input.OperationId, clubId, "operation-kind-collision");
            }

            return replay;
        }

        var club = await db.Clubs.SingleOrDefaultAsync(club => club.ClubId == clubId, cancellationToken);
        if (club is null)
        {
            LogSeasonCreateConflict(input.OperationId, clubId, "club-not-found");
            return ServiceProblem.NotFound();
        }

        if (club.CurrentSeasonId is not null)
        {
            LogSeasonCreateConflict(input.OperationId, clubId, "current-season-exists");
            return ServiceProblem.Conflict("The club already has a current season. Start the next season instead.");
        }

        if (await db.Seasons.AnyAsync(season => season.Name == input.Name, cancellationToken))
        {
            LogSeasonCreateConflict(input.OperationId, clubId, "duplicate-name");
            return ServiceProblem.Conflict("A season with that name already exists.");
        }

        var season = NewSeason(
            input.OperationId,
            SeasonCreationKind.Standalone,
            creationPreviousSeasonId: null,
            input.Name,
            input.StartDate,
            input.EndDate,
            actorUserId,
            clubId);
        db.Seasons.Add(season);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            club.CurrentSeasonId = season.SeasonId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogSeasonCreated(input.OperationId, season.SeasonId, clubId, actorUserId);
            return ToSummary(season, isCurrent: true);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSeasonCreateConflict(input.OperationId, clubId, "current-season-concurrency");
            return ServiceProblem.Conflict("The club's current season changed. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogSeasonCreateConflict(input.OperationId, clubId, "operation-or-name-uniqueness");
            return ServiceProblem.Conflict("The season operation or season name already exists.");
        }
    }

    /// <summary>Executes one optimistic-concurrency-protected metadata update.</summary>
    private async Task<ServiceResult<SeasonSummary>> UpdateAttemptAsync(
        NovaDbContext db,
        long seasonId,
        UpdateSeasonInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);

        var season = await db.Seasons.SingleOrDefaultAsync(
            season => season.SeasonId == seasonId,
            cancellationToken);
        if (season is null)
        {
            LogSeasonUpdateConflict(seasonId, clubId, "not-found");
            return ServiceProblem.NotFound();
        }

        if (await db.Seasons.AnyAsync(
            other => other.SeasonId != seasonId && other.Name == input.Name,
            cancellationToken))
        {
            LogSeasonUpdateConflict(seasonId, clubId, "duplicate-name");
            return ServiceProblem.Conflict("A season with that name already exists.");
        }

        var campaignDateErrors = new Dictionary<string, string[]>();
        if (await db.Campaigns.AnyAsync(
            campaign => campaign.SeasonId == seasonId
                && campaign.StartDate < input.StartDate,
            cancellationToken))
        {
            campaignDateErrors[nameof(UpdateSeasonInput.StartDate)] =
                ["The season start date must be on or before every linked campaign start date."];
        }

        if (input.EndDate is DateOnly seasonEndDate
            && await db.Campaigns.AnyAsync(
                campaign => campaign.SeasonId == seasonId
                    && (campaign.StartDate > seasonEndDate
                        || campaign.EndDate == null
                        || campaign.EndDate > seasonEndDate),
                cancellationToken))
        {
            campaignDateErrors[nameof(UpdateSeasonInput.EndDate)] =
                ["The season end date must contain every linked campaign."];
        }

        if (campaignDateErrors.Count > 0)
        {
            LogSeasonUpdateConflict(seasonId, clubId, "campaign-window-validation");
            return ServiceProblem.Validation(campaignDateErrors);
        }

        db.Entry(season).Property(value => value.ConcurrencyToken).OriginalValue = input.ExpectedConcurrencyToken;
        season.Name = input.Name;
        season.StartDate = input.StartDate;
        season.EndDate = input.EndDate;
        season.ConcurrencyToken = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var isCurrent = await db.Clubs.AnyAsync(
                club => club.ClubId == clubId && club.CurrentSeasonId == seasonId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogSeasonUpdated(seasonId, clubId, actorUserId);
            return ToSummary(season, isCurrent);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSeasonUpdateConflict(seasonId, clubId, "metadata-concurrency");
            return ServiceProblem.Conflict("The season changed. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogSeasonUpdateConflict(seasonId, clubId, "name-uniqueness");
            return ServiceProblem.Conflict("A season with that name already exists.");
        }
    }

    /// <summary>Executes one atomic current-season advancement attempt.</summary>
    private async Task<ServiceResult<StartNextSeasonResult>> StartNextAttemptAsync(
        NovaDbContext db,
        StartNextSeasonInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);

        var committed = await FindCommittedSeasonAsync(db, clubId, input.OperationId, cancellationToken);
        if (committed is not null)
        {
            var replay = ToStartNextReplayResult(committed, input.ExpectedCurrentSeasonId);
            if (replay.IsSuccess)
            {
                LogSeasonAdvanceReplayed(
                    input.OperationId,
                    input.ExpectedCurrentSeasonId,
                    committed.SeasonId,
                    clubId);
            }
            else
            {
                LogSeasonAdvanceConflict(
                    input.OperationId,
                    input.ExpectedCurrentSeasonId,
                    clubId,
                    "operation-predecessor-mismatch");
            }

            return replay;
        }

        var club = await db.Clubs.SingleOrDefaultAsync(club => club.ClubId == clubId, cancellationToken);
        if (club?.CurrentSeasonId is not long currentSeasonId)
        {
            LogSeasonAdvanceConflict(
                input.OperationId,
                input.ExpectedCurrentSeasonId,
                clubId,
                "no-current-season");
            return ServiceProblem.Conflict("The club does not have a current season.");
        }

        if (currentSeasonId != input.ExpectedCurrentSeasonId)
        {
            LogSeasonAdvanceConflict(
                input.OperationId,
                input.ExpectedCurrentSeasonId,
                clubId,
                "stale-current-season");
            return ServiceProblem.Conflict("The current season changed. Reload and try again.");
        }

        if (await db.Campaigns.AnyAsync(
            campaign => campaign.SeasonId == currentSeasonId && campaign.Status != CampaignStatus.Closed,
            cancellationToken))
        {
            LogSeasonAdvanceConflict(
                input.OperationId,
                input.ExpectedCurrentSeasonId,
                clubId,
                "open-current-season-campaign");
            return ServiceProblem.Conflict("Every campaign in the current season must be closed first.");
        }

        if (await db.Seasons.AnyAsync(season => season.Name == input.Name, cancellationToken))
        {
            LogSeasonAdvanceConflict(
                input.OperationId,
                input.ExpectedCurrentSeasonId,
                clubId,
                "duplicate-name");
            return ServiceProblem.Conflict("A season with that name already exists.");
        }

        var season = NewSeason(
            input.OperationId,
            SeasonCreationKind.Advancement,
            currentSeasonId,
            input.Name,
            input.StartDate,
            input.EndDate,
            actorUserId,
            clubId);
        db.Seasons.Add(season);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            club.CurrentSeasonId = season.SeasonId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            LogSeasonAdvanced(
                input.OperationId,
                currentSeasonId,
                season.SeasonId,
                clubId,
                actorUserId);
            return new StartNextSeasonResult
            {
                PreviousSeasonId = currentSeasonId,
                CurrentSeason = ToSummary(season, isCurrent: true)
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSeasonAdvanceConflict(
                input.OperationId,
                input.ExpectedCurrentSeasonId,
                clubId,
                "current-season-concurrency");
            return ServiceProblem.Conflict("The current season changed. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            LogSeasonAdvanceConflict(
                input.OperationId,
                input.ExpectedCurrentSeasonId,
                clubId,
                "operation-or-name-uniqueness");
            return ServiceProblem.Conflict("The season operation or season name already exists.");
        }
    }

    /// <summary>Creates a new tracked season entity.</summary>
    private static SeasonEntity NewSeason(
        Guid operationId,
        SeasonCreationKind creationKind,
        long? creationPreviousSeasonId,
        string name,
        DateOnly startDate,
        DateOnly? endDate,
        long actorUserId,
        long clubId)
        => new()
        {
            CreationOperationId = operationId,
            CreationKind = creationKind,
            CreationPreviousSeasonId = creationPreviousSeasonId,
            Name = name,
            StartDate = startDate,
            EndDate = endDate,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedById = actorUserId,
            ClubId = clubId
        };

    /// <summary>Finds an operation-created season only when it is now the club's current season.</summary>
    private static Task<CommittedSeason?> FindCommittedSeasonAsync(
        NovaDbContext db,
        long clubId,
        Guid operationId,
        CancellationToken cancellationToken)
        => db.Seasons
            .AsNoTracking()
            .Where(season => season.ClubId == clubId
                && season.CreationOperationId == operationId
                && season.Club.CurrentSeasonId == season.SeasonId)
            .Select(season => new CommittedSeason
            {
                SeasonId = season.SeasonId,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                ConcurrencyToken = season.ConcurrencyToken,
                CreationKind = season.CreationKind,
                CreationPreviousSeasonId = season.CreationPreviousSeasonId
            })
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Reconstructs a valid standalone-creation replay or rejects an operation-kind collision.</summary>
    private static ServiceResult<SeasonSummary> ToCreateReplayResult(CommittedSeason committed)
        => committed.CreationKind == SeasonCreationKind.Standalone
            && committed.CreationPreviousSeasonId is null
            ? ToSummary(committed)
            : ServiceProblem.Conflict(
                "The operation identifier was already used for a different season transition.");

    /// <summary>Reconstructs a valid advancement replay only for its persisted predecessor.</summary>
    /// <param name="committed">The operation-created season that is currently selected by the club.</param>
    /// <param name="expectedCurrentSeasonId">The current season identifier supplied by the caller.</param>
    /// <returns>The reconstructed advancement result, or a conflict when no transition occurred.</returns>
    private static ServiceResult<StartNextSeasonResult> ToStartNextReplayResult(
        CommittedSeason committed,
        long expectedCurrentSeasonId)
        => committed.CreationKind != SeasonCreationKind.Advancement
            || committed.CreationPreviousSeasonId != expectedCurrentSeasonId
            ? ServiceProblem.Conflict(
                "The operation identifier was already used for a different season transition.")
            : new StartNextSeasonResult
            {
                PreviousSeasonId = committed.CreationPreviousSeasonId.Value,
                CurrentSeason = ToSummary(committed)
            };

    /// <summary>Maps a season to its public summary.</summary>
    private static SeasonSummary ToSummary(SeasonEntity season, bool isCurrent)
        => new()
        {
            SeasonId = season.SeasonId,
            Name = season.Name,
            StartDate = season.StartDate,
            EndDate = season.EndDate,
            IsCurrent = isCurrent,
            ConcurrencyToken = season.ConcurrencyToken
        };

    /// <summary>Maps a committed operation projection to its public current-season summary.</summary>
    private static SeasonSummary ToSummary(CommittedSeason season)
        => new()
        {
            SeasonId = season.SeasonId,
            Name = season.Name,
            StartDate = season.StartDate,
            EndDate = season.EndDate,
            IsCurrent = true,
            ConcurrencyToken = season.ConcurrencyToken
        };

    /// <summary>Executes a mutation with a fresh context for every retry attempt.</summary>
    private async Task<TResult> ExecuteAsync<TResult>(
        Func<NovaDbContext, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await operation(db);
        });
    }

    /// <summary>Executes and verifies a retry-safe mutation with fresh contexts.</summary>
    private async Task<TResult> ExecuteRetrySafeAsync<TResult>(
        Func<NovaDbContext, Task<TResult>> operation,
        Func<NovaDbContext, Task<ExecutionResult<TResult>>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (Operation: operation, Verify: verifySucceeded),
            async (state, _) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.Operation(db);
            },
            async (state, _) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.Verify(db);
            },
            cancellationToken);
    }

    /// <summary>Resolves the current administrator and club identifiers.</summary>
    private bool TryGetAdministrator(out long actorUserId, out long clubId)
    {
        if (currentUserProvider.UserId is long userId
            && currentUserProvider.ClubId is long currentClubId
            && currentUserProvider.IsClubAdmin)
        {
            actorUserId = userId;
            clubId = currentClubId;
            return true;
        }

        actorUserId = default;
        clubId = default;
        return false;
    }

    /// <summary>Determines whether a database update failed on a unique constraint.</summary>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message;
        return message is not null
            && (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Logs structural rejection of a standalone season creation request.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season creation validation failed for OperationId={OperationId}.")]
    private partial void LogSeasonCreateValidationFailed(Guid operationId);

    /// <summary>Logs standalone season creation rejected because the caller is not an administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season creation forbidden for UserId={UserId}, OperationId={OperationId}.")]
    private partial void LogSeasonCreateForbidden(long userId, Guid operationId);

    /// <summary>Logs a standalone season creation conflict.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season creation conflict for OperationId={OperationId}, ClubId={ClubId}, Reason={Reason}.")]
    private partial void LogSeasonCreateConflict(Guid operationId, long clubId, string reason);

    /// <summary>Logs successful standalone season creation.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Season creation OperationId={OperationId} committed as SeasonId={SeasonId} for ClubId={ClubId} by UserId={ActorUserId}.")]
    private partial void LogSeasonCreated(Guid operationId, long seasonId, long clubId, long actorUserId);

    /// <summary>Logs a successful idempotent replay of standalone season creation.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Season creation OperationId={OperationId} replayed committed SeasonId={SeasonId} for ClubId={ClubId}.")]
    private partial void LogSeasonCreateReplayed(Guid operationId, long seasonId, long clubId);

    /// <summary>Logs successful verification after an ambiguous standalone season creation commit.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Season creation OperationId={OperationId} recovered committed SeasonId={SeasonId} for ClubId={ClubId} after an ambiguous commit.")]
    private partial void LogSeasonCreateCommitRecovered(Guid operationId, long seasonId, long clubId);

    /// <summary>Logs structural rejection of a season metadata update.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season metadata update validation failed for SeasonId={SeasonId}.")]
    private partial void LogSeasonUpdateValidationFailed(long seasonId);

    /// <summary>Logs a season metadata update rejected because the caller is not an administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season metadata update forbidden for UserId={UserId}, SeasonId={SeasonId}.")]
    private partial void LogSeasonUpdateForbidden(long userId, long seasonId);

    /// <summary>Logs a season metadata update conflict or contextual rejection.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season metadata update conflict for SeasonId={SeasonId}, ClubId={ClubId}, Reason={Reason}.")]
    private partial void LogSeasonUpdateConflict(long seasonId, long clubId, string reason);

    /// <summary>Logs a successful season metadata update.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "SeasonId={SeasonId} metadata updated for ClubId={ClubId} by UserId={ActorUserId}.")]
    private partial void LogSeasonUpdated(long seasonId, long clubId, long actorUserId);

    /// <summary>Logs structural rejection of a season advancement request.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season advancement validation failed for OperationId={OperationId}.")]
    private partial void LogSeasonAdvanceValidationFailed(Guid operationId);

    /// <summary>Logs season advancement rejected because the caller is not an administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season advancement forbidden for UserId={UserId}, OperationId={OperationId}.")]
    private partial void LogSeasonAdvanceForbidden(long userId, Guid operationId);

    /// <summary>Logs a season advancement conflict.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season advancement conflict for OperationId={OperationId}, ExpectedCurrentSeasonId={ExpectedCurrentSeasonId}, ClubId={ClubId}, Reason={Reason}.")]
    private partial void LogSeasonAdvanceConflict(
        Guid operationId,
        long expectedCurrentSeasonId,
        long clubId,
        string reason);

    /// <summary>Logs a successful season advancement.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Season advancement OperationId={OperationId} moved ClubId={ClubId} from SeasonId={PreviousSeasonId} to SeasonId={CurrentSeasonId} by UserId={ActorUserId}.")]
    private partial void LogSeasonAdvanced(
        Guid operationId,
        long previousSeasonId,
        long currentSeasonId,
        long clubId,
        long actorUserId);

    /// <summary>Logs a successful idempotent replay of season advancement.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Season advancement OperationId={OperationId} replayed ClubId={ClubId} transition from SeasonId={PreviousSeasonId} to SeasonId={CurrentSeasonId}.")]
    private partial void LogSeasonAdvanceReplayed(
        Guid operationId,
        long previousSeasonId,
        long currentSeasonId,
        long clubId);

    /// <summary>Logs successful verification after an ambiguous season advancement commit.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "Season advancement OperationId={OperationId} recovered ClubId={ClubId} transition from SeasonId={PreviousSeasonId} to SeasonId={CurrentSeasonId} after an ambiguous commit.")]
    private partial void LogSeasonAdvanceCommitRecovered(
        Guid operationId,
        long previousSeasonId,
        long currentSeasonId,
        long clubId);

    /// <summary>Captures the durable identity of one operation-created current season.</summary>
    private sealed class CommittedSeason
    {
        /// <summary>Gets the created season identifier.</summary>
        public long SeasonId { get; init; }
        /// <summary>Gets the stored season name.</summary>
        public required string Name { get; init; }
        /// <summary>Gets the stored start date.</summary>
        public DateOnly StartDate { get; init; }
        /// <summary>Gets the optional stored end date.</summary>
        public DateOnly? EndDate { get; init; }
        /// <summary>Gets the metadata concurrency token.</summary>
        public Guid ConcurrencyToken { get; init; }
        /// <summary>Gets the command path that originally created the season.</summary>
        public SeasonCreationKind CreationKind { get; init; }
        /// <summary>Gets the exact predecessor recorded for an advancement operation.</summary>
        public long? CreationPreviousSeasonId { get; init; }
    }
}
