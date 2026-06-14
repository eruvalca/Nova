using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Account;
using Nova.Shared.Account;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Account;

/// <summary>
/// Server-side implementation of <see cref="IClubMemberService"/>: lists club members and assigns ClubAdmin.
/// </summary>
public sealed partial class ClubMemberService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubMemberService> logger) : IClubMemberService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubMemberDto>>> GetClubMembersAsync(CancellationToken cancellationToken = default)
    {
        // Get current user ID and club ID
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.Forbidden("You must be a club member to list members.");
        }

        if (currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You must be a club member to list members.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        // Query other members of the club (exclude the current user)
        var members = await db.Users
            .Where(u => u.ClubId == clubId && u.Id != userId)
            .ToListAsync(cancellationToken);

        // Map to DTOs
        var dtos = members
            .Select(u => u.ToClubMemberDto())
            .ToList()
            .AsReadOnly();

        return dtos;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> AssignClubAdminAsync(long targetUserId, CancellationToken cancellationToken = default)
    {
        // Get current user ID and club ID
        if (currentUserProvider.UserId is not long actorUserId)
        {
            return ServiceProblem.Forbidden("You must be authenticated to assign club admin roles.");
        }

        if (currentUserProvider.ClubId is not long actorClubId)
        {
            return ServiceProblem.Forbidden("You must be a club member to assign club admin roles.");
        }

        if (!currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("You must be a club admin to assign club admin roles.");
        }

        // Load target user
        var targetUser = await userManager.FindByIdAsync(targetUserId.ToString());
        if (targetUser is null)
        {
            return ServiceProblem.NotFound("The specified member was not found.");
        }

        // Verify target is in the same club
        if (targetUser.ClubId != actorClubId)
        {
            LogAssignRejected(targetUserId, actorClubId);
            return ServiceProblem.Forbidden("The specified user is not a member of your club.");
        }

        // Check if already a ClubAdmin (idempotent)
        var isAlreadyAdmin = await userManager.IsInRoleAsync(targetUser, Roles.ClubAdmin);
        if (isAlreadyAdmin)
        {
            return true;
        }

        // Assign the role
        var result = await userManager.AddToRoleAsync(targetUser, Roles.ClubAdmin);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ServiceProblem.ServerError(errors);
        }

        LogAdminAssigned(targetUserId, actorClubId, actorUserId);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Assigned ClubAdmin to user {TargetUserId} in club {ClubId} by user {ActorUserId}.")]
    private partial void LogAdminAssigned(long targetUserId, long clubId, long actorUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Assign ClubAdmin rejected: target {TargetUserId} not in actor's club {ClubId}.")]
    private partial void LogAssignRejected(long targetUserId, long clubId);
}
