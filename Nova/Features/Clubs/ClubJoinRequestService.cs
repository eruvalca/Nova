using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Clubs;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubJoinRequestService"/>: manages club join requests.
/// </summary>
public sealed partial class ClubJoinRequestService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ClubMembershipClaimRefresher clubMembershipClaimRefresher,
    UserManager<NovaUserEntity> userManager,
    ILogger<ClubJoinRequestService> logger) : IClubJoinRequestService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubJoinRequestDto>> GetCurrentUserPendingRequestAsync(CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.NotFound();
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.ClubJoinRequests
            .Include(e => e.Club)
            .Include(e => e.RequestingUser)
            .Where(e => e.RequestingUserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        return request is null
            ? ServiceProblem.NotFound()
            : request.ToClubJoinRequestDto();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ClubJoinRequestDto>> CreateJoinRequestAsync(long clubId, CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.Forbidden("You must be signed in to submit a join request.");
        }

        // Check if user already belongs to a club
        if (currentUserProvider.ClubId.HasValue)
        {
            return ServiceProblem.Conflict("You are already a member of a club.");
        }

        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Check for existing pending request
            var existingRequest = await db.ClubJoinRequests
                .AnyAsync(e => e.RequestingUserId == userId && e.Status == RequestStatus.Pending, cancellationToken);

            if (existingRequest)
            {
                return ServiceProblem.Conflict("You already have a pending join request.");
            }

            // Check if club exists
            var clubExists = await db.Clubs.AnyAsync(c => c.ClubId == clubId, cancellationToken);
            if (!clubExists)
            {
                return ServiceProblem.NotFound("The specified club does not exist.");
            }

            // Create join request
            var joinRequest = new ClubJoinRequestEntity
            {
                ClubId = clubId,
                RequestingUserId = userId,
                Status = RequestStatus.Pending,
                CreatedById = userId
            };

            db.ClubJoinRequests.Add(joinRequest);
            await db.SaveChangesAsync(cancellationToken);

            // Reload with Club and RequestingUser navigations
            var reloaded = await db.ClubJoinRequests
                .Include(e => e.Club)
                .Include(e => e.RequestingUser)
                .FirstAsync(e => e.ClubJoinRequestId == joinRequest.ClubJoinRequestId, cancellationToken);

            LogJoinRequestCreated(userId, clubId, joinRequest.ClubJoinRequestId);
            return reloaded.ToClubJoinRequestDto();
        }
        catch (DbUpdateException ex)
        {
            LogJoinRequestCreationFailed(ex, userId, clubId);
            return ServiceProblem.ServerError("Failed to create the join request. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> CancelJoinRequestAsync(long requestId, CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.NotFound();
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(e => e.ClubJoinRequestId == requestId, cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        // Ownership check
        if (request.RequestingUserId != userId)
        {
            return ServiceProblem.Forbidden("You do not own this join request.");
        }

        // Status guard - only allow cancelling pending requests
        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be cancelled.");
        }

        db.ClubJoinRequests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);

        LogJoinRequestCancelled(userId, requestId);
        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubJoinRequestDto>>> GetClubJoinRequestsAsync(
        long clubId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin || currentUserProvider.ClubId != clubId)
        {
            return ServiceProblem.Forbidden("You are not an administrator of this club.");
        }

        await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

        var requests = await db.ClubJoinRequests
            .Include(e => e.Club)
            .Include(e => e.RequestingUser)
            .Where(e => e.ClubId == clubId && e.Status == RequestStatus.Pending)
            .OrderBy(e => e.ClubJoinRequestId)
            .ToListAsync(cancellationToken);

        var dtos = requests.Select(r => r.ToClubJoinRequestDto()).ToList().AsReadOnly();
        return dtos;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ApproveJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("You are not a club administrator.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(e => e.ClubJoinRequestId == requestId, cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be approved.");
        }

        request.Status = RequestStatus.Approved;
        await db.SaveChangesAsync(cancellationToken);

        // Re-fetch the requesting user through UserManager (its own tracked instance) to avoid
        // an identity-map conflict, then assign them to the club and bump their security stamp.
        var requestingUser = await userManager.FindByIdAsync(request.RequestingUserId.ToString());
        if (requestingUser is not null)
        {
            requestingUser.ClubId = request.ClubId;
            await userManager.UpdateAsync(requestingUser);
            await clubMembershipClaimRefresher.MarkUserClaimsStaleAsync(requestingUser);
        }
        else
        {
            LogApproveRequestingUserMissing(request.RequestingUserId, requestId);
        }

        LogJoinRequestApproved(currentUserProvider.UserId ?? 0, requestId, request.RequestingUserId, request.ClubId);
        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RejectJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("You are not a club administrator.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(e => e.ClubJoinRequestId == requestId, cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be rejected.");
        }

        request.Status = RequestStatus.Rejected;
        await db.SaveChangesAsync(cancellationToken);

        LogJoinRequestRejected(currentUserProvider.UserId ?? 0, requestId, request.RequestingUserId);
        return new Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request approved: RequestId={RequestId} by AdminUserId={AdminUserId} for RequestingUserId={RequestingUserId} into ClubId={ClubId}.")]
    private partial void LogJoinRequestApproved(long adminUserId, long requestId, long requestingUserId, long clubId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request rejected: RequestId={RequestId} by AdminUserId={AdminUserId} for RequestingUserId={RequestingUserId}.")]
    private partial void LogJoinRequestRejected(long adminUserId, long requestId, long requestingUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approved join request but requesting user not found: RequestingUserId={RequestingUserId}, RequestId={RequestId}.")]
    private partial void LogApproveRequestingUserMissing(long requestingUserId, long requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request created: RequestId={RequestId} for UserId={UserId} to ClubId={ClubId}.")]
    private partial void LogJoinRequestCreated(long userId, long clubId, long requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create join request for UserId={UserId} to ClubId={ClubId}.")]
    private partial void LogJoinRequestCreationFailed(Exception exception, long userId, long clubId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request cancelled: RequestId={RequestId} by UserId={UserId}.")]
    private partial void LogJoinRequestCancelled(long userId, long requestId);
}
