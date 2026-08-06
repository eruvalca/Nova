using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Corrects season metadata without deleting linked campaigns or initiating season closeout.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for season mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for season metadata correction outcomes.</param>
public sealed partial class SeasonMetadataService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<SeasonMetadataService> logger) : ISeasonMetadataService
{
    /// <inheritdoc />
    public async Task<ServiceResult<UpdateSeasonMetadataResult>> UpdateAsync(
        UpdateSeasonMetadataInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogSeasonMetadataValidationFailed(input.SeasonId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogSeasonMetadataForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden(
                "You must be a club administrator to update season metadata.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var season = await db.Seasons
            .SingleOrDefaultAsync(s => s.SeasonId == input.SeasonId, cancellationToken);

        if (season is null || season.ClubId != clubId)
        {
            LogSeasonNotFound(input.SeasonId, clubId);
            return ServiceProblem.NotFound("The season was not found.");
        }

        // Duplicate name check, excluding the current season, scoped to the current club
        var isDuplicate = await db.Seasons.AnyAsync(
            s => s.ClubId == clubId && s.Name == input.Name && s.SeasonId != input.SeasonId,
            cancellationToken);
        if (isDuplicate)
        {
            LogDuplicateSeasonName(clubId, input.SeasonId);
            return ServiceProblem.Conflict(
                "A season with that name already exists.");
        }

        season.Name = input.Name;
        season.StartDate = input.StartDate;
        season.EndDate = input.EndDate;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogSeasonMetadataConcurrencyConflict(input.SeasonId);
            return ServiceProblem.Conflict(
                "The season changed. Reload it and try again.");
        }

        LogSeasonMetadataUpdated(input.SeasonId, actorUserId);
        return new UpdateSeasonMetadataResult(
            season.SeasonId,
            season.Name,
            season.StartDate,
            season.EndDate);
    }

    /// <summary>Logs structural validation failure before database access.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season metadata update validation failed for SeasonId={SeasonId}.")]
    private partial void LogSeasonMetadataValidationFailed(long seasonId);

    /// <summary>Logs a metadata update rejected because the caller is not a club administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season metadata update forbidden for UserId={UserId}.")]
    private partial void LogSeasonMetadataForbidden(long userId);

    /// <summary>Logs a season that is unavailable in the current tenant.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "SeasonId={SeasonId} was not found for ClubId={ClubId}.")]
    private partial void LogSeasonNotFound(long seasonId, long clubId);

    /// <summary>Logs a duplicate season name conflict.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season name conflict for ClubId={ClubId}, SeasonId={SeasonId}.")]
    private partial void LogDuplicateSeasonName(long clubId, long seasonId);

    /// <summary>Logs a concurrency conflict during season metadata update.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season metadata concurrency conflict for SeasonId={SeasonId}.")]
    private partial void LogSeasonMetadataConcurrencyConflict(long seasonId);

    /// <summary>Logs a successful season metadata update.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "SeasonId={SeasonId} metadata updated by UserId={ActorUserId}.")]
    private partial void LogSeasonMetadataUpdated(long seasonId, long actorUserId);
}
