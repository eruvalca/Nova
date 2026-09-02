using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubAdminService"/> for loading the club-admin roster.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory for club-member queries.</param>
/// <param name="userManager">The identity user manager for club-admin role membership checks.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks and user context.</param>
/// <param name="logger">The logger used for warning-level access failures.</param>
public sealed partial class ClubAdminService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubAdminService> logger) : IClubAdminService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubAdminSummaryDto>> GetClubAdminSummaryAsync(long clubId, CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin || currentUserProvider.ClubId != clubId)
        {
            LogForbiddenClubAdminAccess(clubId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You do not have permission to view this club summary.");
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
            return ServiceProblem.NotFound("The requested club was not found.");
        }

        var memberCount = await db.Users.CountAsync(u => u.ClubId == clubId, cancellationToken);
        var playerCount = await db.Players.CountAsync(cancellationToken);
        var pendingRequestCount = await db.ClubJoinRequests
            .CountAsync(e => e.ClubId == clubId && e.Status == RequestStatus.Pending, cancellationToken);

        var clubAdmins = await userManager.GetUsersInRoleAsync(Roles.ClubAdmin);
        var adminCount = clubAdmins.Count(u => u.ClubId == clubId);
        var isCurrentUserSoleAdmin = adminCount == 1 && clubAdmins.Any(u => u.ClubId == clubId && u.Id == currentUserProvider.UserId);

        return new ClubAdminSummaryDto(
            club.ClubId,
            club.Name,
            club.City,
            club.State,
            memberCount,
            adminCount,
            pendingRequestCount,
            playerCount,
            isCurrentUserSoleAdmin,
            await db.ClubCrests.AnyAsync(c => c.ClubId == clubId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubMemberDetailDto>>> GetClubRosterAsync(long clubId, CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin || currentUserProvider.ClubId != clubId)
        {
            LogForbiddenClubAdminAccess(clubId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You do not have permission to view this club roster.");
        }

        // The guard above authorizes the caller; NovaReadDbContext supplies tenant-scoped,
        // no-tracking reads for the roster query.
        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        var clubAdmins = await userManager.GetUsersInRoleAsync(Roles.ClubAdmin);
        var adminUserIds = clubAdmins
            .Where(u => u.ClubId == clubId)
            .Select(u => u.Id)
            .ToHashSet();

        var userRows = await db.Users
            .Where(u => u.ClubId == clubId)
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ThenBy(u => u.Id)
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        var users = userRows
            .Select(u => new ClubMemberDetailDto(
                u.Id,
                u.FirstName + " " + u.LastName,
                adminUserIds.Contains(u.Id),
                u.Id == currentUserProvider.UserId))
            .ToList();

        return users.AsReadOnly();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forbidden club-admin roster access attempt for ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogForbiddenClubAdminAccess(long clubId, long userId);

}
