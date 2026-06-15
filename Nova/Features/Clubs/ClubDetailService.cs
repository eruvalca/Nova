using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Account;
using Nova.Shared.Clubs;
using Nova.Shared.Results;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubDetailService"/> for loading a club's detail view data.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory for club and member queries.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks and user context.</param>
/// <param name="logger">The logger used for warning-level security and lookup failures.</param>
public sealed partial class ClubDetailService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubDetailService> logger) : IClubDetailService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubDetailDto>> GetClubDetailAsync(long clubId, CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view club details.");
        }

        if (currentUserProvider.ClubId is not long currentUserClubId || currentUserClubId != clubId)
        {
            LogForbiddenClubDetailAccess(clubId, currentUserId);
            return ServiceProblem.Forbidden("You do not have permission to view this club.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        var club = await db.Clubs
            .Where(c => c.ClubId == clubId)
            .Select(c => new
            {
                c.ClubId,
                c.Name,
                c.City,
                c.State
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (club is null)
        {
            LogClubNotFound(clubId, currentUserId);
            return ServiceProblem.NotFound("The requested club was not found.");
        }

        var members = await db.Users
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .Select(u => new ClubRosterMemberDto(u.Id, u.FirstName + " " + u.LastName, u.Id == currentUserId))
            .ToListAsync(cancellationToken);

        return new ClubDetailDto(
            club.ClubId,
            club.Name,
            club.City,
            club.State,
            members.AsReadOnly(),
            currentUserProvider.IsClubAdmin);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forbidden club detail access attempt for ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogForbiddenClubDetailAccess(long clubId, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Club detail not found for ClubId={ClubId} requested by UserId={UserId}.")]
    private partial void LogClubNotFound(long clubId, long userId);
}
