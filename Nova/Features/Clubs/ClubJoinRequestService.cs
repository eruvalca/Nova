using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Clubs;
using Nova.Features.ClubActivity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubJoinRequestService"/>: manages club join requests.
/// </summary>
public sealed partial class ClubJoinRequestService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ClubMembershipClaimRefresher clubMembershipClaimRefresher,
    ILogger<ClubJoinRequestService> logger,
    IClubActivityEventWriter activityEventWriter) : IClubJoinRequestService
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
            await using var strategyDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
            var strategy = strategyDb.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

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

                var requesterDisplayName = await ReadDisplayNameAsync(db, userId, cancellationToken);
                if (requesterDisplayName is null)
                {
                    return ServiceProblem.NotFound("The requesting user no longer exists.");
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
                activityEventWriter.AppendJoinRequest(db, new JoinRequestActivityEvidence(
                    ClubActivityEventKind.JoinRequestSubmitted, clubId,
                    new ActivityActorEvidence(userId, requesterDisplayName), joinRequest.ClubJoinRequestId,
                    userId, requesterDisplayName));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Reload with Club and RequestingUser navigations
                var reloaded = await db.ClubJoinRequests
                    .Include(e => e.Club)
                    .Include(e => e.RequestingUser)
                    .FirstAsync(e => e.ClubJoinRequestId == joinRequest.ClubJoinRequestId, cancellationToken);

                LogJoinRequestCreated(userId, clubId, joinRequest.ClubJoinRequestId);
                return new ServiceResult<ClubJoinRequestDto>(reloaded.ToClubJoinRequestDto());
            });
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

        await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(
                candidate => candidate.ClubJoinRequestId == requestId
                    && candidate.RequestingUserId == userId,
                cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        var requesterDisplayName = await ReadDisplayNameAsync(db, userId, cancellationToken);
        if (requesterDisplayName is null)
        {
            return ServiceProblem.NotFound("The requesting user no longer exists.");
        }

        // Status guard - only allow cancelling pending requests
        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be cancelled.");
        }

        db.ClubJoinRequests.Remove(request);
        activityEventWriter.AppendJoinRequest(db, new JoinRequestActivityEvidence(
            ClubActivityEventKind.JoinRequestCancelled, request.ClubId,
            new ActivityActorEvidence(userId, requesterDisplayName), requestId,
            request.RequestingUserId, requesterDisplayName));
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

        // Use NovaAdminDbContext instead of the tenant-filtered read context because
        // .Include(RequestingUser) would pull users outside the caller's tenant filter.
        // Access is gated by RequireClubAdmin policy and the above in-method guard.
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
        if (!currentUserProvider.IsClubAdmin
            || currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You are not a club administrator.");
        }

        await using var strategyDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        var approvalResult = await strategy.ExecuteAsync(async () =>
        {
            await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var request = await db.ClubJoinRequests
                .FirstOrDefaultAsync(
                    candidate => candidate.ClubJoinRequestId == requestId && candidate.ClubId == clubId,
                    cancellationToken);

            if (request is null)
            {
                return ServiceProblem.NotFound("The join request was not found.");
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceProblem.Conflict("Only pending join requests can be approved.");
            }

            var requestingUser = await db.Users.SingleOrDefaultAsync(
                user => user.Id == request.RequestingUserId,
                cancellationToken);
            if (requestingUser is null)
            {
                LogApproveRequestingUserMissing(request.RequestingUserId, requestId);
                return ServiceProblem.NotFound("The requesting user no longer exists.");
            }

            var actorDisplayName = await ReadDisplayNameAsync(db, actorUserId, cancellationToken);
            if (actorDisplayName is null)
            {
                return ServiceProblem.NotFound("The approving administrator no longer exists.");
            }

            var requesterDisplayName = $"{requestingUser.FirstName} {requestingUser.LastName}";

            request.Status = RequestStatus.Approved;
            requestingUser.ClubId = request.ClubId;
            activityEventWriter.AppendJoinRequest(db, new JoinRequestActivityEvidence(
                ClubActivityEventKind.JoinRequestApproved, request.ClubId,
                new ActivityActorEvidence(actorUserId, actorDisplayName), requestId,
                request.RequestingUserId, requesterDisplayName));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ServiceResult<long>(requestingUser.Id);
        });

        if (approvalResult.IsProblem)
        {
            return approvalResult.Problem;
        }

        var claimsResult = await clubMembershipClaimRefresher.MarkUserClaimsStaleAsync(approvalResult.Value);
        var claimsServiceResult = claimsResult.Match<ServiceResult<Success>>(
            _ => new Success(),
            error => ServiceProblem.ServerError(string.Join(", ", error.Value)));
        if (claimsServiceResult.IsProblem)
        {
            return claimsServiceResult.Problem;
        }

        LogJoinRequestApproved(actorUserId, requestId, approvalResult.Value, clubId);
        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RejectJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin
            || currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You are not a club administrator.");
        }

        await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(
                candidate => candidate.ClubJoinRequestId == requestId && candidate.ClubId == clubId,
                cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be rejected.");
        }

        var actorDisplayName = await ReadDisplayNameAsync(db, actorUserId, cancellationToken);
        var requesterDisplayName = await ReadDisplayNameAsync(db, request.RequestingUserId, cancellationToken);
        if (actorDisplayName is null || requesterDisplayName is null)
        {
            return ServiceProblem.NotFound("The activity participants no longer exist.");
        }

        request.Status = RequestStatus.Rejected;
        activityEventWriter.AppendJoinRequest(db, new JoinRequestActivityEvidence(
            ClubActivityEventKind.JoinRequestRejected, request.ClubId,
            new ActivityActorEvidence(actorUserId, actorDisplayName), requestId,
            request.RequestingUserId, requesterDisplayName));
        await db.SaveChangesAsync(cancellationToken);

        LogJoinRequestRejected(actorUserId, requestId, request.RequestingUserId);
        return new Success();
    }

    /// <summary>Reads a durable user display name from the explicitly authorized admin context.</summary>
    private static Task<string?> ReadDisplayNameAsync(
        NovaAdminDbContext db,
        long userId,
        CancellationToken cancellationToken)
        => db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.FirstName + " " + user.LastName)
            .SingleOrDefaultAsync(cancellationToken);

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
