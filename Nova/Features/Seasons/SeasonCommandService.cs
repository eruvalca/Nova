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
public sealed class SeasonCommandService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider) : ISeasonCommandService
{
    /// <inheritdoc />
    public async Task<ServiceResult<SeasonSummary>> CreateAsync(
        CreateSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
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
                return committed is null
                    ? new ExecutionResult<ServiceResult<SeasonSummary>>(false, default!)
                    : new ExecutionResult<ServiceResult<SeasonSummary>>(true, committed);
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
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetAdministrator(out _, out var clubId))
        {
            return ServiceProblem.Forbidden("You must be a club administrator to update seasons.");
        }

        var normalizedInput = input with { Name = input.Name.Trim() };
        return await ExecuteAsync(
            db => UpdateAttemptAsync(db, seasonId, normalizedInput, clubId, cancellationToken),
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
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
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
                var result = current is null
                    ? null
                    : new StartNextSeasonResult
                    {
                        PreviousSeasonId = normalizedInput.ExpectedCurrentSeasonId,
                        CurrentSeason = current
                    };
                return result is null
                    ? new ExecutionResult<ServiceResult<StartNextSeasonResult>>(false, default!)
                    : new ExecutionResult<ServiceResult<StartNextSeasonResult>>(true, result);
            },
            cancellationToken);
    }

    /// <summary>Executes one transaction that creates the club's first current season.</summary>
    private static async Task<ServiceResult<SeasonSummary>> CreateAttemptAsync(
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
            return committed;
        }

        var club = await db.Clubs.SingleOrDefaultAsync(club => club.ClubId == clubId, cancellationToken);
        if (club is null)
        {
            return ServiceProblem.NotFound();
        }

        if (club.CurrentSeasonId is not null)
        {
            return ServiceProblem.Conflict("The club already has a current season. Start the next season instead.");
        }

        if (await db.Seasons.AnyAsync(season => season.Name == input.Name, cancellationToken))
        {
            return ServiceProblem.Conflict("A season with that name already exists.");
        }

        var season = NewSeason(input.OperationId, input.Name, input.StartDate, input.EndDate, actorUserId, clubId);
        db.Seasons.Add(season);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            club.CurrentSeasonId = season.SeasonId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToSummary(season, isCurrent: true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceProblem.Conflict("The club's current season changed. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ServiceProblem.Conflict("The season operation or season name already exists.");
        }
    }

    /// <summary>Executes one optimistic-concurrency-protected metadata update.</summary>
    private static async Task<ServiceResult<SeasonSummary>> UpdateAttemptAsync(
        NovaDbContext db,
        long seasonId,
        UpdateSeasonInput input,
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
            return ServiceProblem.NotFound();
        }

        if (await db.Seasons.AnyAsync(
            other => other.SeasonId != seasonId && other.Name == input.Name,
            cancellationToken))
        {
            return ServiceProblem.Conflict("A season with that name already exists.");
        }

        var campaignOutsideWindow = await db.Campaigns.AnyAsync(
            campaign => campaign.SeasonId == seasonId
                && (campaign.StartDate < input.StartDate
                    || (input.EndDate != null
                        && (campaign.StartDate > input.EndDate
                            || campaign.EndDate == null
                            || campaign.EndDate > input.EndDate))),
            cancellationToken);
        if (campaignOutsideWindow)
        {
            return ServiceProblem.Validation(
                nameof(UpdateSeasonInput.EndDate),
                "The season dates must contain every linked campaign.");
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
            return ToSummary(season, isCurrent);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceProblem.Conflict("The season changed. Reload it and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ServiceProblem.Conflict("A season with that name already exists.");
        }
    }

    /// <summary>Executes one atomic current-season advancement attempt.</summary>
    private static async Task<ServiceResult<StartNextSeasonResult>> StartNextAttemptAsync(
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
            return new StartNextSeasonResult
            {
                PreviousSeasonId = input.ExpectedCurrentSeasonId,
                CurrentSeason = committed
            };
        }

        var club = await db.Clubs.SingleOrDefaultAsync(club => club.ClubId == clubId, cancellationToken);
        if (club?.CurrentSeasonId is not long currentSeasonId)
        {
            return ServiceProblem.Conflict("The club does not have a current season.");
        }

        if (currentSeasonId != input.ExpectedCurrentSeasonId)
        {
            return ServiceProblem.Conflict("The current season changed. Reload and try again.");
        }

        if (await db.Campaigns.AnyAsync(
            campaign => campaign.SeasonId == currentSeasonId && campaign.Status != CampaignStatus.Closed,
            cancellationToken))
        {
            return ServiceProblem.Conflict("Every campaign in the current season must be closed first.");
        }

        if (await db.Seasons.AnyAsync(season => season.Name == input.Name, cancellationToken))
        {
            return ServiceProblem.Conflict("A season with that name already exists.");
        }

        var season = NewSeason(input.OperationId, input.Name, input.StartDate, input.EndDate, actorUserId, clubId);
        db.Seasons.Add(season);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            club.CurrentSeasonId = season.SeasonId;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StartNextSeasonResult
            {
                PreviousSeasonId = currentSeasonId,
                CurrentSeason = ToSummary(season, isCurrent: true)
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceProblem.Conflict("The current season changed. Reload and try again.");
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return ServiceProblem.Conflict("The season operation or season name already exists.");
        }
    }

    /// <summary>Creates a new tracked season entity.</summary>
    private static SeasonEntity NewSeason(
        Guid operationId,
        string name,
        DateOnly startDate,
        DateOnly? endDate,
        long actorUserId,
        long clubId)
        => new()
        {
            CreationOperationId = operationId,
            Name = name,
            StartDate = startDate,
            EndDate = endDate,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedById = actorUserId,
            ClubId = clubId
        };

    /// <summary>Finds an operation-created season only when it is now the club's current season.</summary>
    private static Task<SeasonSummary?> FindCommittedSeasonAsync(
        NovaDbContext db,
        long clubId,
        Guid operationId,
        CancellationToken cancellationToken)
        => db.Seasons
            .AsNoTracking()
            .Where(season => season.ClubId == clubId
                && season.CreationOperationId == operationId
                && season.Club.CurrentSeasonId == season.SeasonId)
            .Select(season => new SeasonSummary
            {
                SeasonId = season.SeasonId,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                IsCurrent = true,
                ConcurrencyToken = season.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);

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
}
