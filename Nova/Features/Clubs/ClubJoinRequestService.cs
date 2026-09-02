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
        var commitAttempted = new CommitAttemptTracker();

        try
        {
            return await strategy.ExecuteAsync(
                (State: state, CommitAttempted: commitAttempted),
                async (operationState, token) =>
                {
                    operationState.CommitAttempted.Reset();
                    await using var writeDb = await dbContextFactory.CreateDbContextAsync(token);
                    return await PersistJoinRequestAsync(writeDb, operationState.State, operationState.CommitAttempted, token);
                },
                async (operationState, token) =>
                {
                    if (!operationState.CommitAttempted.Attempted)
                    {
                        return new ExecutionResult<ServiceResult<ClubJoinRequestDto>>(successful: false, default!);
                    }

                    await using var verifyDb = await dbContextFactory.CreateDbContextAsync(token);
                    return await VerifyJoinRequestCommittedAsync(verifyDb, operationState.State, token);
                },
                cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // The one-to-one RequestingUserId constraint is the final guard after the preflight:
            // two concurrent submissions can both pass the probe, and the loser reaches the unique
            // violation rather than the expected conflict.
            LogJoinRequestCreationConflict(userId, clubId);
            return ServiceProblem.Conflict("You already have a pending join request.");
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
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The created join request DTO.</returns>
    private async Task<ServiceResult<ClubJoinRequestDto>> PersistJoinRequestAsync(
        NovaDbContext db,
        (long ClubId, long UserId, string RequesterName, string ClubName) state,
        CommitAttemptTracker commitAttempted,
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
        commitAttempted.MarkAttempted();
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

        await using var probeDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await probeDb.ClubJoinRequests
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

        // The probe resolves the request identity, validates ownership/status up front, and
        // snapshots the names used by the durable event; the execution-strategy delegate re-runs
        // the delete + JoinRequestCancelled append on a fresh write context per attempt, acquires
        // the request lock to serialize concurrent terminal transitions, and verifies an ambiguous
        // commit (gated on this attempt reaching commit) instead of replaying the delete.
        var state = new CancellationState(requestId, userId, requesterName);
        var strategy = probeDb.Database.CreateExecutionStrategy();
        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (State: state, CommitAttempted: commitAttempted),
            async (operationState, token) =>
            {
                operationState.CommitAttempted.Reset();
                await using var writeDb = await dbContextFactory.CreateDbContextAsync(token);
                return await PersistCancellationAsync(writeDb, operationState.State, operationState.CommitAttempted, token);
            },
            async (operationState, token) =>
            {
                if (!operationState.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<ServiceResult<Success>>(successful: false, default!);
                }

                await using var verifyDb = await dbContextFactory.CreateDbContextAsync(token);
                return await VerifyCancellationCommittedAsync(verifyDb, operationState.State, token);
            },
            cancellationToken);
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

        // The requester is club-less, so the tenant query filters hide the row from the write
        // context. Approval runs on the admin context (filters bypassed) so the membership
        // assignment and the approved request + MemberJoined event land in ONE SaveChanges,
        // atomically (the previous UserManager-first path could commit ClubId without the request
        // evidence and vice versa); the club constraint is applied explicitly because the admin
        // context has no tenant filter. The probe resolves the request identity and validates it up
        // front; the execution-strategy delegate re-runs the whole mutation on a fresh admin context
        // per attempt, acquires the request lock to serialize concurrent terminal transitions, and
        // verifies an ambiguous commit (gated on this attempt reaching commit) instead of replaying
        // the event insert.
        await using var probeDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await probeDb.ClubJoinRequests
            .FirstOrDefaultAsync(
                e => e.ClubJoinRequestId == requestId && e.ClubId == currentUserProvider.ClubId,
                cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be approved.");
        }

        var state = new ApprovalState(
            requestId,
            request.ClubId,
            currentUserProvider.UserId ?? 0,
            request.RequestingUserId);
        var strategy = probeDb.Database.CreateExecutionStrategy();
        var commitAttempted = new CommitAttemptTracker();

        var result = await strategy.ExecuteAsync(
            (State: state, CommitAttempted: commitAttempted),
            async (operationState, token) =>
            {
                operationState.CommitAttempted.Reset();
                await using var writeDb = await adminDbContextFactory.CreateDbContextAsync(token);
                return await PersistApprovalAsync(writeDb, operationState.State, operationState.CommitAttempted, token);
            },
            async (operationState, token) =>
            {
                if (!operationState.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<ServiceResult<Success>>(successful: false, default!);
                }

                await using var verifyDb = await adminDbContextFactory.CreateDbContextAsync(token);
                return await VerifyApprovalCommittedAsync(verifyDb, operationState.State, token);
            },
            cancellationToken);

        // Best-effort security-stamp refresh so the newly assigned member's claims are regenerated
        // after the committed approval; a refresh failure must not fail an already-committed
        // approval. Reload the requester through UserManager so the instance belongs to the
        // Identity store's context, then Match the refresh result so a stale-mark failure is
        // diagnosed instead of silently leaving the member stuck behind the onboarding gate. The
        // whole block is exception-guarded so an Identity operation that throws (rather than
        // returning a failed result) also preserves the committed success.
        if (result.IsSuccess)
        {
            try
            {
                var identityUser = await userManager.FindByIdAsync(state.RequestingUserId.ToString());
                if (identityUser is null)
                {
                    LogApproveRequestingUserMissing(state.RequestingUserId, state.RequestId);
                }
                else
                {
                    var refreshResult = await clubMembershipClaimRefresher.MarkUserClaimsStaleAsync(identityUser);
                    refreshResult.Switch(
                        _ => { },
                        error => LogApproveClaimsStaleFailed(
                            state.RequestingUserId,
                            state.RequestId,
                            string.Join(", ", error.Value)));
                }
            }
            catch (Exception exception)
            {
                LogApproveClaimsStaleRefreshFailed(exception, state.RequestingUserId, state.RequestId);
            }
        }

        return result;
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

        // The probe resolves the request identity, validates it up front, and snapshots the names
        // used by the durable event; the execution-strategy delegate re-runs the mutation on a
        // fresh write context per attempt, acquires the request lock to serialize concurrent
        // terminal transitions, and verifies an ambiguous commit (gated on this attempt reaching
        // commit) instead of replaying the rejection event.
        await using var probeDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = await probeDb.ClubJoinRequests
            .FirstOrDefaultAsync(
                e => e.ClubJoinRequestId == requestId && e.ClubId == currentUserProvider.ClubId,
                cancellationToken);

        if (request is null)
        {
            return ServiceProblem.NotFound("The join request was not found.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be rejected.");
        }

        // JoinRequestRejected is administrator-only (unresolved join-request events are admin-only
        // per the attention brief). The requester is club-less, so UserManager resolves the
        // snapshot; the rejecting admin is tenant-visible via the write context.
        var adminUserId = currentUserProvider.UserId ?? 0;
        var requesterName = (await userManager.FindByIdAsync(request.RequestingUserId.ToString()))?.FullName ?? "Unknown user";
        var adminName = (await probeDb.Users
            .Where(user => user.Id == adminUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken)) ?? "Unknown user";

        var state = new RejectionState(requestId, request.ClubId, adminUserId, adminName, requesterName);
        var strategy = probeDb.Database.CreateExecutionStrategy();
        var commitAttempted = new CommitAttemptTracker();

        return await strategy.ExecuteAsync(
            (State: state, CommitAttempted: commitAttempted),
            async (operationState, token) =>
            {
                operationState.CommitAttempted.Reset();
                await using var writeDb = await dbContextFactory.CreateDbContextAsync(token);
                return await PersistRejectionAsync(writeDb, operationState.State, operationState.CommitAttempted, token);
            },
            async (operationState, token) =>
            {
                if (!operationState.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<ServiceResult<Success>>(successful: false, default!);
                }

                await using var verifyDb = await dbContextFactory.CreateDbContextAsync(token);
                return await VerifyRejectionCommittedAsync(verifyDb, operationState.State, token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Persists an approval (request status, requester membership, and the member-joined event) in
    /// one transaction on the provided fresh admin context, after acquiring the request lock so
    /// concurrent terminal transitions serialize and the loser observes the winner's status.
    /// </summary>
    /// <param name="db">The fresh admin context for this execution attempt.</param>
    /// <param name="state">The logical approval state captured before the strategy started.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The approval outcome.</returns>
    private async Task<ServiceResult<Success>> PersistApprovalAsync(
        NovaAdminDbContext db,
        ApprovalState state,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireClubMembershipLockAsync(state.ClubId, cancellationToken);
        await db.AcquireJoinRequestLockAsync(state.RequestId, cancellationToken);

        var administratorRoleId = await db.Roles
            .Where(role => role.NormalizedName == Nova.Shared.Security.Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => (long?)role.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var administratorIsCurrent = administratorRoleId is not null
            && await db.Users.AnyAsync(user => user.Id == state.AdminUserId && user.ClubId == state.ClubId, cancellationToken)
            && await db.UserRoles.AnyAsync(
                role => role.UserId == state.AdminUserId && role.RoleId == administratorRoleId.Value,
                cancellationToken);
        if (!administratorIsCurrent)
        {
            return ServiceProblem.Forbidden("You must be a club administrator to approve join requests.");
        }

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(
                e => e.ClubJoinRequestId == state.RequestId && e.ClubId == state.ClubId,
                cancellationToken);

        if (request is null)
        {
            // The preflight confirmed the request exists, so a vanished row means a concurrent
            // cancellation deleted it while this attempt waited for the request lock.
            return ServiceProblem.Conflict("The join request was already resolved.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be approved.");
        }

        var requestingUser = await db.Users
            .SingleOrDefaultAsync(user => user.Id == request.RequestingUserId, cancellationToken);

        if (requestingUser is null)
        {
            LogApproveRequestingUserMissing(request.RequestingUserId, request.ClubJoinRequestId);
            return ServiceProblem.ServerError("The requesting user account could not be found.");
        }

        var memberName = requestingUser.FullName;
        var adminName = (await db.Users
            .Where(user => user.Id == state.AdminUserId)
            .Select(user => user.FirstName + " " + user.LastName)
            .FirstOrDefaultAsync(cancellationToken)) ?? "Unknown user";

        request.Status = RequestStatus.Approved;
        requestingUser.ClubId = request.ClubId;

        ActivityEventWriter.AppendMembership(
            db,
            request.ClubId,
            ActivityEventKind.MemberJoined,
            state.AdminUserId,
            adminName,
            new MembershipContext
            {
                MemberUserId = request.RequestingUserId,
                MemberDisplayName = memberName,
                ApprovedByActorName = adminName,
            });

        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);

        LogJoinRequestApproved(state.AdminUserId, request.ClubJoinRequestId, request.RequestingUserId, request.ClubId);
        return new Success();
    }

    /// <summary>
    /// Verifies whether an approval with an uncertain commit outcome actually committed by
    /// re-reading the request status and the requester's membership without replaying the write.
    /// </summary>
    /// <param name="db">The fresh admin context used for commit verification.</param>
    /// <param name="state">The logical approval state captured before the strategy started.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed approval was found.</returns>
    private async Task<ExecutionResult<ServiceResult<Success>>> VerifyApprovalCommittedAsync(
        NovaAdminDbContext db,
        ApprovalState state,
        CancellationToken cancellationToken)
    {
        var request = await db.ClubJoinRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.ClubJoinRequestId == state.RequestId, cancellationToken);

        var requesterClubId = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == state.RequestingUserId)
            .Select(user => user.ClubId)
            .SingleOrDefaultAsync(cancellationToken);

        if (request is { Status: RequestStatus.Approved }
            && requesterClubId is long assignedClubId
            && assignedClubId == state.ClubId)
        {
            LogJoinRequestApproved(state.AdminUserId, state.RequestId, state.RequestingUserId, state.ClubId);
            return new ExecutionResult<ServiceResult<Success>>(successful: true, new Success());
        }

        return new ExecutionResult<ServiceResult<Success>>(successful: false, default!);
    }

    /// <summary>
    /// Persists a rejection (request status and the join-request-rejected event) in one transaction
    /// on the provided fresh write context, after acquiring the request lock so concurrent terminal
    /// transitions serialize and the loser observes the winner's status.
    /// </summary>
    /// <param name="db">The fresh write context for this execution attempt.</param>
    /// <param name="state">The logical rejection state captured before the strategy started.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The rejection outcome.</returns>
    private async Task<ServiceResult<Success>> PersistRejectionAsync(
        NovaDbContext db,
        RejectionState state,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireJoinRequestLockAsync(state.RequestId, cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(
                e => e.ClubJoinRequestId == state.RequestId && e.ClubId == state.ClubId,
                cancellationToken);

        if (request is null)
        {
            // The preflight confirmed the request exists, so a vanished row means a concurrent
            // cancellation deleted it while this attempt waited for the request lock.
            return ServiceProblem.Conflict("The join request was already resolved.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be rejected.");
        }

        request.Status = RequestStatus.Rejected;

        ActivityEventWriter.AppendJoinRequest(
            db,
            request.ClubId,
            ActivityEventKind.JoinRequestRejected,
            state.AdminUserId,
            state.AdminName,
            new JoinRequestContext
            {
                JoinRequestId = request.ClubJoinRequestId,
                RequesterDisplayName = state.RequesterName,
            });

        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);

        LogJoinRequestRejected(state.AdminUserId, state.RequestId, request.RequestingUserId);
        return new Success();
    }

    /// <summary>
    /// Verifies whether a rejection with an uncertain commit outcome actually committed by
    /// re-reading the request status without replaying the write.
    /// </summary>
    /// <param name="db">The fresh write context used for commit verification.</param>
    /// <param name="state">The logical rejection state captured before the strategy started.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed rejection was found.</returns>
    private async Task<ExecutionResult<ServiceResult<Success>>> VerifyRejectionCommittedAsync(
        NovaDbContext db,
        RejectionState state,
        CancellationToken cancellationToken)
    {
        var request = await db.ClubJoinRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.ClubJoinRequestId == state.RequestId, cancellationToken);

        if (request is { Status: RequestStatus.Rejected })
        {
            LogJoinRequestRejected(state.AdminUserId, state.RequestId, request.RequestingUserId);
            return new ExecutionResult<ServiceResult<Success>>(successful: true, new Success());
        }

        return new ExecutionResult<ServiceResult<Success>>(successful: false, default!);
    }

    /// <summary>
    /// Persists a cancellation (request deletion and the join-request-cancelled event) in one
    /// transaction on the provided fresh write context, after acquiring the request lock so
    /// concurrent terminal transitions serialize and the loser observes the winner's status.
    /// </summary>
    /// <param name="db">The fresh write context for this execution attempt.</param>
    /// <param name="state">The logical cancellation state captured before the strategy started.</param>
    /// <param name="commitAttempted">The tracker marked immediately before this attempt commits.</param>
    /// <param name="cancellationToken">A token that cancels the database work.</param>
    /// <returns>The cancellation outcome.</returns>
    private async Task<ServiceResult<Success>> PersistCancellationAsync(
        NovaDbContext db,
        CancellationState state,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireJoinRequestLockAsync(state.RequestId, cancellationToken);

        var request = await db.ClubJoinRequests
            .FirstOrDefaultAsync(e => e.ClubJoinRequestId == state.RequestId, cancellationToken);

        if (request is null)
        {
            // The preflight confirmed the request exists, so a vanished row means a concurrent
            // approval/rejection/cancellation resolved it while this attempt waited for the lock.
            return ServiceProblem.Conflict("The join request was already resolved.");
        }

        if (request.RequestingUserId != state.UserId)
        {
            return ServiceProblem.Forbidden("You do not own this join request.");
        }

        if (request.Status != RequestStatus.Pending)
        {
            return ServiceProblem.Conflict("Only pending join requests can be cancelled.");
        }

        db.ClubJoinRequests.Remove(request);

        // JoinRequestCancelled is administrator-only (unresolved join-request events are
        // admin-only per the attention brief).
        ActivityEventWriter.AppendJoinRequest(
            db,
            request.ClubId,
            ActivityEventKind.JoinRequestCancelled,
            state.UserId,
            state.RequesterName,
            new JoinRequestContext
            {
                JoinRequestId = request.ClubJoinRequestId,
                RequesterDisplayName = state.RequesterName,
            });

        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);

        LogJoinRequestCancelled(state.UserId, state.RequestId);
        return new Success();
    }

    /// <summary>
    /// Verifies whether a cancellation with an uncertain commit outcome actually committed by
    /// re-reading the request row (the committed cancellation deletes it) without replaying the
    /// delete.
    /// </summary>
    /// <param name="db">The fresh write context used for commit verification.</param>
    /// <param name="state">The logical cancellation state captured before the strategy started.</param>
    /// <param name="cancellationToken">A token that cancels the verification query.</param>
    /// <returns>An execution result indicating whether the committed cancellation was found.</returns>
    private async Task<ExecutionResult<ServiceResult<Success>>> VerifyCancellationCommittedAsync(
        NovaDbContext db,
        CancellationState state,
        CancellationToken cancellationToken)
    {
        var request = await db.ClubJoinRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.ClubJoinRequestId == state.RequestId, cancellationToken);

        if (request is null)
        {
            LogJoinRequestCancelled(state.UserId, state.RequestId);
            return new ExecutionResult<ServiceResult<Success>>(successful: true, new Success());
        }

        return new ExecutionResult<ServiceResult<Success>>(successful: false, default!);
    }

    /// <summary>Captured, immutable state for one cancellation attempt.</summary>
    private readonly record struct CancellationState(long RequestId, long UserId, string RequesterName);

    /// <summary>Captured, immutable state for one approval attempt.</summary>
    private readonly record struct ApprovalState(long RequestId, long ClubId, long AdminUserId, long RequestingUserId);

    /// <summary>Captured, immutable state for one rejection attempt.</summary>
    private readonly record struct RejectionState(long RequestId, long ClubId, long AdminUserId, string AdminName, string RequesterName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request approved: RequestId={RequestId} by AdminUserId={AdminUserId} for RequestingUserId={RequestingUserId} into ClubId={ClubId}.")]
    private partial void LogJoinRequestApproved(long adminUserId, long requestId, long requestingUserId, long clubId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request rejected: RequestId={RequestId} by AdminUserId={AdminUserId} for RequestingUserId={RequestingUserId}.")]
    private partial void LogJoinRequestRejected(long adminUserId, long requestId, long requestingUserId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approved join request but requesting user not found: RequestingUserId={RequestingUserId}, RequestId={RequestId}.")]
    private partial void LogApproveRequestingUserMissing(long requestingUserId, long requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approved join request but failed to mark the member's claims stale: RequestingUserId={RequestingUserId}, RequestId={RequestId}, Errors={Errors}.")]
    private partial void LogApproveClaimsStaleFailed(long requestingUserId, long requestId, string errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approved join request but the claims-stale refresh threw: RequestingUserId={RequestingUserId}, RequestId={RequestId}.")]
    private partial void LogApproveClaimsStaleRefreshFailed(Exception exception, long requestingUserId, long requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request created: RequestId={RequestId} for UserId={UserId} to ClubId={ClubId}.")]
    private partial void LogJoinRequestCreated(long userId, long clubId, long requestId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create join request for UserId={UserId} to ClubId={ClubId}.")]
    private partial void LogJoinRequestCreationFailed(Exception exception, long userId, long clubId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request creation conflicted with an existing request for UserId={UserId} to ClubId={ClubId}.")]
    private partial void LogJoinRequestCreationConflict(long userId, long clubId);

    /// <summary>
    /// Determines whether a persistence failure was caused by a unique-index violation. The check is
    /// text-based so it holds for both the Npgsql production provider (SQLSTATE 23505) and the SQLite
    /// provider used by the tenancy unit-test harness, without either provider being referenced here.
    /// </summary>
    /// <param name="exception">The persistence failure to classify.</param>
    /// <returns><see langword="true"/> when the failure was a unique-index violation.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message;
        return message is not null
            && (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Join request cancelled: RequestId={RequestId} by UserId={UserId}.")]
    private partial void LogJoinRequestCancelled(long userId, long requestId);
}
