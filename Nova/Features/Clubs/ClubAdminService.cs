using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
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
/// <param name="clubMembershipClaimRefresher">The claim refresher used after a role change.</param>
/// <param name="logger">The logger used for warning-level access failures.</param>
public sealed partial class ClubAdminService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ClubMembershipClaimRefresher clubMembershipClaimRefresher,
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
            isCurrentUserSoleAdmin);
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

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DemoteClubAdminAsync(DemoteAdminInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long actorUserId)
        {
            return ServiceProblem.Forbidden("You must be authenticated to demote club admins.");
        }

        if (currentUserProvider.ClubId is not long actorClubId)
        {
            return ServiceProblem.Forbidden("You must be a club member to demote club admins.");
        }

        if (!currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("You must be a club admin to demote club admins.");
        }

        var targetUser = await userManager.FindByIdAsync(input.TargetUserId.ToString());
        if (targetUser is null)
        {
            return ServiceProblem.NotFound("The specified member was not found.");
        }

        if (targetUser.ClubId != actorClubId)
        {
            LogDemoteRejected(input.TargetUserId, actorClubId);
            return ServiceProblem.Forbidden("The specified user is not a member of your club.");
        }

        var isAdmin = await userManager.IsInRoleAsync(targetUser, Roles.ClubAdmin);
        if (!isAdmin)
        {
            // Demoting a non-admin is a no-op success.
            return true;
        }

        var clubAdmins = await userManager.GetUsersInRoleAsync(Roles.ClubAdmin);
        var adminCount = clubAdmins.Count(u => u.ClubId == actorClubId);
        // Best-effort last-admin guard. This is a read-then-write check, so two concurrent
        // demotions could both observe adminCount > 1 and leave the club without any admins.
        // Accepted as out of scope: the window is tiny and the impact is recoverable via the
        // admin-assignment flow. Introduce serialization here only if it becomes a real problem.
        if (adminCount <= 1)
        {
            return ServiceProblem.Conflict("The club must always have at least one administrator. Promote another member before demoting this one.");
        }

        var result = await userManager.RemoveFromRoleAsync(targetUser, Roles.ClubAdmin);
        if (!result.Succeeded)
        {
            var roleErrors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceProblem.ServerError(roleErrors);
        }

        await clubMembershipClaimRefresher.MarkUserClaimsStaleAsync(targetUser);

        LogAdminDemoted(input.TargetUserId, actorClubId, actorUserId);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forbidden club-admin roster access attempt for ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogForbiddenClubAdminAccess(long clubId, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demoted ClubAdmin from user {TargetUserId} in club {ClubId} by user {ActorUserId}.")]
    private partial void LogAdminDemoted(long targetUserId, long clubId, long actorUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demote ClubAdmin rejected: target {TargetUserId} not in actor's club {ClubId}.")]
    private partial void LogDemoteRejected(long targetUserId, long clubId);
}
