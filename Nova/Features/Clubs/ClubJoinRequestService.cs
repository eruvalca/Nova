using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Clubs;
using Nova.Features.Activity;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Clubs;
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

        await using var probeDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Check for existing pending request
        var existingRequest = await probeDb.ClubJoinRequests
            .AnyAsync(e => e.RequestingUserId == userId && e.Status == RequestStatus.Pending, cancellationToken);

        if (existingRequest)
        {
            return ServiceProblem.Conflict("You already have a pending join request.");
        }

        // Check if club exists and capture its name for the response.
        var clubName = await probeDb.Clubs
            .Where(c => c.ClubId == clubId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(cancellationToken);

        if (clubName is null)
        {
            return ServiceProblem.NotFound("The specified club does not exist.");
        }

        // Resolve the requester's display name for the event snapshot. The requester is
        // club-less, so the tenant-filtered write context cannot see them; UserManager is
        // the established route for club-less users.
        var requesterName = (await userManager.FindByIdAsync(userId.ToString()))?.FullName ?? "Unknown user";

        // Create join request. The request id is identity-generated, so the durable event is
        // appended after the first save but inside the same transaction: the request and its
        // activity row commit atomically. With the Npgsql retrying execution strategy the
        // user-initiated transaction must run inside CreateExecutionStrategy().ExecuteAsync()
        // so the whole unit is retried as one on transient failures. A fresh write context is
        // created per attempt so tracked Added state from a failed attempt is never replayed.
        // Post-commit reads (and the DTO construction that triggered them) run via the verify
        // delegate instead of inside the retryable operation: a transient failure while reloading
        // must not replay the insert against the request's unique RequestingUserId key.
        var strategy = probeDb.Database.CreateExecutionStrategy();
        var state = (ClubId: clubId, UserId: userId, RequesterName: requesterName, ClubName: clubName);

        try
        {
            return await strategy.ExecuteAsync(
                state,
                async (operationState, token) =>
                {
                    await using var writeDb = await dbContextFactory.CreateDbContextAsync(token);
                    return await PersistJoinRequestAsync(writeDb, operationState, token);
                },
                async (operationState, token) =>
                {
                    await using var verifyDb = await dbContextFactory.CreateDbContextAsync(token);
                    return await VerifyJoinRequestCommittedAsync(verifyDb, operationState, token);
                },
                cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            LogJoinRequestCreationFailed(ex, userId, clubId);
            return ServiceProblem.ServerError("Failed to create the join request. Please try again.");
        }
    }

    /// <summary>
    /// Persists one join request and its join-request-submitted activity row in a single
    /// transaction using the provided fresh tenant context.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="state">The logical operation state captured before the strategy started.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The created join request DTO.</returns>
    private async Task<ServiceResult<ClubJoinRequestDto>> PersistJoinRequestAsync(
        NovaDbContext db,
        (long ClubId, long UserId, string RequesterName, string ClubName) state,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var joinRequest = new ClubJoinRequestEntity
        {
            ClubId = state.ClubId,
            RequestingUserId = state.UserId,
            Status = RequestStatus.Pending,
            CreatedById = state.UserId
        };

        db.ClubJoinRequests.Add(joinRequest);
        await db.SaveChangesAsync(cancellationToken);

        // JoinRequestSubmitted is administrator-only (unresolved join-request events
        // are admin-only per the attention brief).
        ActivityEventWriter.AppendJoinRequest(
            db,
            state.ClubId,
            ActivityEventKind.JoinRequestSubmitted,
            state.UserId,
            state.RequesterName,
            new JoinRequestContext
            {
                JoinRequestId = joinRequest.ClubJoinRequestId,
                RequesterDisplayName = state.RequesterName,
            });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogJoinRequestCreated(state.UserId, state.ClubId, joinRequest.ClubJoinRequestId);
        return new ClubJoinRequestDto(
            joinRequest.ClubJoinRequestId,
            state.ClubId,
            state.ClubName,
            state.UserId,
            state.RequesterName,
            RequestStatus.Pending,
            joinRequest.CreatedAt);
    }

    /// <summary>
    /// Checks whether a join-request creation transaction with an uncertain commit outcome was
    /// committed and reconstructs its successful service result without replaying the insert.
    /// </summary>
    /// <param name="db">The fresh tenant context used for commit verification.</param>
    /// <param name="state">The logical operation state captured before the strategy started.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed join request was found.</returns>
    private async Task<ExecutionResult<ServiceResult<ClubJoinRequestDto>>> VerifyJoinRequestCommittedAsync(
        NovaDbContext db,
        (long ClubId, long UserId, string RequesterName, string ClubName) state,
        CancellationToken cancellationToken)
    {
        var joinRequest = await db.ClubJoinRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.RequestingUserId == state.UserId && candidate.ClubId == state.ClubId,
                cancellationToken);

        if (joinRequest is null)
        {
            return new ExecutionResult<ServiceResult<ClubJoinRequestDto>>(successful: false, default!);
        }

        LogJoinRequestCreated(state.UserId, state.ClubId, joinRequest.ClubJoinRequestId);
        return new ExecutionResult<ServiceResult<ClubJoinRequestDto>>(
            successful: true,
            new ClubJoinRequestDto(
                joinRequest.ClubJoinRequestId,
                state.ClubId,
                state.ClubName,
                state.UserId,
                state.RequesterName,
                joinRequest.Status,
                joinRequest.CreatedAt));
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

        // Resolve the requester display name for the event snapshot. The requester is club-less
        // at cancel time, so UserManager is required (tenant filter excludes club-less users).
        var requesterName = (await userManager.FindByIdAsync(request.RequestingUserId.ToString()))?.FullName ?? "Unknown user";

        db.ClubJoinRequests.Remove(request);

        // JoinRequestCancelled is administrator-only (unresolved join-request events are
        // admin-only per the attention brief).
        ActivityEventWriter.AppendJoinRequest(
            db,
            request.ClubId,
            ActivityEventKind.JoinRequestCancelled,
            userId,
            requesterName,
            new JoinRequestContext
            {
                JoinRequestId = request.ClubJoinRequestId,
                RequesterDisplayName = requesterName,
            });

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

        // The member joined event is visible to all members (only the approval actor snapshot is
        // admin-shaped, and only while the requesting user remains a tenant-visible club member).
        // The requester is club-less, so UserManager resolves the member snapshot; the approving
        // admin is tenant-visible, so the write context resolves the actor snapshot.
        var adminUserId = currentUserProvider.UserId ?? 0;
        // The requester is club-less, so the tenant query filter hides the row. Load via the
        // write context with filters bypassed so the membership assignment and the approved
        // request + MemberJoined event land in ONE SaveChanges, atomically (the previous
        // UserManager-first path could commit ClubId without the request evidence and vice versa).
        var requestingUser = await db.Users
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(user => user.Id == request.RequestingUserId, cancellationToken);

        if (requestingUser is null)
        {
            LogApproveRequestingUserMissing(request.RequestingUserId, requestId);
            return ServiceProblem.ServerError("The requesting user account could not be found.");
        }

        var memberName = requestingUser.FullName;
        var adminName = (await db.Users
            .Where(user => user.Id == adminUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken)) ?? "Unknown user";

        requestingUser.ClubId = request.ClubId;

        ActivityEventWriter.AppendMembership(
            db,
            request.ClubId,
            ActivityEventKind.MemberJoined,
            adminUserId,
            adminName,
            new MembershipContext
            {
                MemberDisplayName = memberName,
                ApprovedByActorName = adminName,
            });

        await db.SaveChangesAsync(cancellationToken);

        // Best-effort security-stamp refresh so the newly assigned member's claims are regenerated;
        // failure to refresh is not a reason to fail the already-committed approval. Reload the
        // requester through UserManager so the instance belongs to the Identity store's context
        // (NovaAdminDbContext): passing the write-context instance would collide with the instance
        // already tracked there when UserStore.UpdateAsync calls Context.Attach(user).
        var identityUser = await userManager.FindByIdAsync(request.RequestingUserId.ToString());
        if (identityUser is not null)
        {
            await clubMembershipClaimRefresher.MarkUserClaimsStaleAsync(identityUser);
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

        // JoinRequestRejected is administrator-only (unresolved join-request events are admin-only
        // per the attention brief). The requester is club-less, so UserManager resolves the
        // snapshot; the rejecting admin is tenant-visible via the write context.
        var rejectAdminUserId = currentUserProvider.UserId ?? 0;
        var rejectRequesterName = (await userManager.FindByIdAsync(request.RequestingUserId.ToString()))?.FullName ?? "Unknown user";
        var rejectAdminName = (await db.Users
            .Where(user => user.Id == rejectAdminUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken)) ?? "Unknown user";

        ActivityEventWriter.AppendJoinRequest(
            db,
            request.ClubId,
            ActivityEventKind.JoinRequestRejected,
            rejectAdminUserId,
            rejectAdminName,
            new JoinRequestContext
            {
                JoinRequestId = request.ClubJoinRequestId,
                RequesterDisplayName = rejectRequesterName,
            });

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
