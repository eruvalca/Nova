using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;

namespace Nova.Features.Clubs;

/// <summary>Loads identity for the club selected by the trusted membership context.</summary>
public sealed class ClubIdentityQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider) : IClubIdentityQueryService
{
    public async Task<ServiceResult<ClubIdentityResult>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is null || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("A current club membership is required.");
        }

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
}
