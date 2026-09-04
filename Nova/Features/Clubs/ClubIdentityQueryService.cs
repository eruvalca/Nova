using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;

namespace Nova.Features.Clubs;

/// <summary>Loads identity for the club selected by the trusted membership context.</summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club state.</param>
/// <param name="logger">The logger for club identity read failures.</param>
public sealed partial class ClubIdentityQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubIdentityQueryService> logger) : IClubIdentityQueryService
{
    public async Task<ServiceResult<ClubIdentityResult>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is null || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("A current club membership is required.");
        }

        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var identity = await db.Clubs
                .Where(club => club.ClubId == clubId)
                .Select(club => new ClubIdentityResult
                {
                    ClubId = club.ClubId,
                    Name = club.Name,
                    City = club.City,
                    State = club.State,
                    HasCrest = db.ClubCrests.Any(crest => crest.ClubId == club.ClubId)
                })
                .SingleOrDefaultAsync(cancellationToken);

            return identity is null
                ? ServiceProblem.NotFound("The current club was not found.")
                : identity;
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogClubIdentityReadFailed(exception, currentUserProvider.UserId ?? 0, clubId);
            return ServiceProblem.ServerError("The current club identity is unavailable.");
        }
    }

    /// <summary>
    /// Logs a club identity read failure.
    /// </summary>
    /// <param name="exception">The thrown exception.</param>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Club identity read failed for UserId={UserId}, ClubId={ClubId}.")]
    private partial void LogClubIdentityReadFailed(Exception exception, long userId, long clubId);
}
