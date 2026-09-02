using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Account;
using Nova.Features.Activity;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Activity;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;
using OneOf.Types;

namespace Nova.Features.Account;

/// <summary>Lists club members and owns the transactional club-membership lifecycle.</summary>
public sealed partial class ClubMemberService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ClubMembershipClaimRefresher clubMembershipClaimRefresher,
    ILogger<ClubMemberService> logger) : IClubMemberService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubMemberDto>>> GetClubMembersAsync(CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You must be a club member to list members.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var members = await db.Users
            .Where(user => user.ClubId == clubId && user.Id != userId)
            .ToListAsync(cancellationToken);

        return members.Select(user => user.ToClubMemberDto()).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public Task<ServiceResult<Success>> PromoteMemberAsync(ClubMemberMutationInput input, CancellationToken cancellationToken = default)
        => ExecuteForAdminAsync(MutationKind.Promote, input, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<Success>> DemoteMemberAsync(ClubMemberMutationInput input, CancellationToken cancellationToken = default)
        => ExecuteForAdminAsync(MutationKind.Demote, input, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<Success>> RemoveMemberAsync(ClubMemberMutationInput input, CancellationToken cancellationToken = default)
        => ExecuteForAdminAsync(MutationKind.Remove, input, cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> LeaveClubAsync(CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long actorUserId || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You must be a club member to leave a club.");
        }

        return await ExecuteAndRefreshAsync(
            new MutationState(MutationKind.Leave, actorUserId, clubId, actorUserId, Guid.NewGuid(), NewSecurityStamp()),
            cancellationToken);
    }

    private async Task<ServiceResult<Success>> ExecuteForAdminAsync(
        MutationKind kind,
        ClubMemberMutationInput input,
        CancellationToken cancellationToken)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("You must be a club administrator to manage members.");
        }

        return await ExecuteAndRefreshAsync(
            new MutationState(kind, actorUserId, clubId, input.MemberUserId, Guid.NewGuid(), NewSecurityStamp()),
            cancellationToken);
    }

    private async Task<ServiceResult<Success>> ExecuteAndRefreshAsync(MutationState state, CancellationToken cancellationToken)
    {
        ServiceResult<MutationReceipt> result;
        try
        {
            await using var probeDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
            var strategy = probeDb.Database.CreateExecutionStrategy();
            var commitAttempted = new CommitAttemptTracker();
            result = await strategy.ExecuteAsync(
                (State: state, CommitAttempted: commitAttempted),
                async (operationState, token) =>
                {
                    operationState.CommitAttempted.Reset();
                    await using var db = await adminDbContextFactory.CreateDbContextAsync(token);
                    return await PersistMutationAsync(db, operationState.State, operationState.CommitAttempted, token);
                },
                async (operationState, token) =>
                {
                    if (!operationState.CommitAttempted.Attempted)
                    {
                        return new ExecutionResult<ServiceResult<MutationReceipt>>(successful: false, default!);
                    }

                    await using var db = await adminDbContextFactory.CreateDbContextAsync(token);
                    return await VerifyMutationCommittedAsync(db, operationState.State, token);
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogMutationFailed(exception, state.Kind, state.ActorUserId, state.TargetUserId, state.ClubId);
            return ServiceProblem.ServerError("The membership change could not be completed.");
        }

        if (result.IsProblem)
        {
            return result.Problem;
        }

        if (!result.Value.RefreshCurrentUser)
        {
            return new Success();
        }

        var currentUser = await userManager.FindByIdAsync(state.ActorUserId.ToString());
        if (currentUser is null)
        {
            return ServiceProblem.ServerError("The membership changed, but the current sign-in could not be refreshed.");
        }

        try
        {
            var refreshResult = await clubMembershipClaimRefresher.RefreshCurrentUserSignInAsync(currentUser);
            return refreshResult.Match<ServiceResult<Success>>(
                success => success,
                _ => ServiceProblem.ServerError("The membership changed, but the current sign-in could not be refreshed."));
        }
        catch (Exception exception)
        {
            LogSignInRefreshFailed(exception, state.ActorUserId);
            return ServiceProblem.ServerError("The membership changed, but the current sign-in could not be refreshed.");
        }
    }

    private async Task<ServiceResult<MutationReceipt>> PersistMutationAsync(
        NovaAdminDbContext db,
        MutationState state,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var userId in new[] { state.ActorUserId, state.TargetUserId }.Distinct().Order())
        {
            await db.AcquireUserMembershipLockAsync(userId, cancellationToken);
        }

        await db.AcquireClubMembershipLockAsync(state.ClubId, cancellationToken);

        var administratorRoleId = await db.Roles
            .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => (long?)role.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (administratorRoleId is null)
        {
            return ServiceProblem.ServerError("The club administrator role is not configured.");
        }

        var actor = await db.Users.SingleOrDefaultAsync(user => user.Id == state.ActorUserId, cancellationToken);
        if (actor is null)
        {
            return ServiceProblem.Forbidden("The current account is no longer available.");
        }

        var actorIsAdministrator = await db.UserRoles.AnyAsync(
            role => role.UserId == actor.Id && role.RoleId == administratorRoleId.Value,
            cancellationToken);

        if (state.Kind == MutationKind.Leave && actor.ClubId is null)
        {
            return new MutationReceipt(RefreshCurrentUser: true);
        }

        if (actor.ClubId != state.ClubId)
        {
            return ServiceProblem.Forbidden("Your club membership changed. Refresh and try again.");
        }

        if (state.Kind != MutationKind.Leave && !actorIsAdministrator)
        {
            if (state.Kind == MutationKind.Demote && state.ActorUserId == state.TargetUserId)
            {
                return new MutationReceipt(RefreshCurrentUser: true);
            }

            return ServiceProblem.Forbidden("You must be a club administrator to manage members.");
        }

        var target = state.Kind == MutationKind.Leave
            ? actor
            : await db.Users.SingleOrDefaultAsync(
                user => user.Id == state.TargetUserId && user.ClubId == state.ClubId,
                cancellationToken);
        if (target is null)
        {
            return ServiceProblem.NotFound("The specified member was not found.");
        }

        var targetRole = await db.UserRoles.SingleOrDefaultAsync(
            role => role.UserId == target.Id && role.RoleId == administratorRoleId.Value,
            cancellationToken);
        var targetIsAdministrator = targetRole is not null;
        var administratorCount = 0;
        var memberCount = 0;

        if (state.Kind is MutationKind.Demote or MutationKind.Leave)
        {
            administratorCount = await (from user in db.Users
                                        join role in db.UserRoles on user.Id equals role.UserId
                                        where user.ClubId == state.ClubId && role.RoleId == administratorRoleId.Value
                                        select user.Id).CountAsync(cancellationToken);
        }

        if (state.Kind == MutationKind.Leave)
        {
            memberCount = await db.Users.CountAsync(user => user.ClubId == state.ClubId, cancellationToken);
        }

        var outcome = state.Kind switch
        {
            MutationKind.Promote => ClubMembershipMutationPolicy.Promote(targetIsAdministrator),
            MutationKind.Demote => ClubMembershipMutationPolicy.Demote(targetIsAdministrator, administratorCount),
            MutationKind.Remove => ClubMembershipMutationPolicy.Remove(actor.Id, target.Id),
            MutationKind.Leave => ClubMembershipMutationPolicy.Leave(actorIsAdministrator, administratorCount, memberCount),
            _ => throw new UnreachableException(),
        };

        var disposition = outcome.Match<ServiceResult<MutationDisposition>>(
            _ => new MutationDisposition(Apply: true, RefreshCurrentUser: false),
            _ => new MutationDisposition(
                Apply: false,
                RefreshCurrentUser: state.Kind == MutationKind.Demote && state.ActorUserId == state.TargetUserId),
            _ => ServiceProblem.Conflict("The club must always have at least one administrator. Promote another member first."),
            _ => ServiceProblem.Conflict("The final club member cannot leave. Delete the club instead."),
            _ => ServiceProblem.Conflict("Use the leave-club action to remove yourself from the club."));
        if (disposition.IsProblem)
        {
            return disposition.Problem;
        }

        if (!disposition.Value.Apply)
        {
            return new MutationReceipt(disposition.Value.RefreshCurrentUser);
        }

        var actorDisplayName = actor.FullName;
        var targetDisplayName = target.FullName;
        switch (state.Kind)
        {
            case MutationKind.Promote:
                db.UserRoles.Add(new IdentityUserRole<long> { UserId = target.Id, RoleId = administratorRoleId.Value });
                ActivityEventWriter.AppendMemberRole(
                    db, state.ClubId, ActivityEventKind.MemberPromoted, actor.Id, actorDisplayName,
                    new MemberRoleContext
                    {
                        MemberUserId = target.Id,
                        MemberDisplayName = targetDisplayName,
                        Role = "club administrator",
                    });
                break;

            case MutationKind.Demote:
                db.UserRoles.Remove(targetRole!);
                ActivityEventWriter.AppendMemberRole(
                    db, state.ClubId, ActivityEventKind.MemberDemoted, actor.Id, actorDisplayName,
                    new MemberRoleContext
                    {
                        MemberUserId = target.Id,
                        MemberDisplayName = targetDisplayName,
                        Role = "club member",
                    });
                break;

            case MutationKind.Remove:
            case MutationKind.Leave:
                var resolvedJoinRequest = await db.ClubJoinRequests.SingleOrDefaultAsync(
                    request => request.RequestingUserId == target.Id && request.Status != RequestStatus.Pending,
                    cancellationToken);
                if (resolvedJoinRequest is not null)
                {
                    db.ClubJoinRequests.Remove(resolvedJoinRequest);
                }

                if (targetRole is not null)
                {
                    db.UserRoles.Remove(targetRole);
                }

                target.ClubId = null;
                ActivityEventWriter.AppendMembership(
                    db,
                    state.ClubId,
                    state.Kind == MutationKind.Remove ? ActivityEventKind.MemberRemoved : ActivityEventKind.MemberLeft,
                    actor.Id,
                    actorDisplayName,
                    new MembershipContext { MemberUserId = target.Id, MemberDisplayName = targetDisplayName });
                break;
        }

        target.SecurityStamp = state.SecurityStamp;
        await ClubMembershipMutationReceipts.PruneExpiredAsync(db, cancellationToken);
        db.ClubMembershipMutationReceipts.Add(new ClubMembershipMutationReceiptEntity
        {
            OperationId = state.OperationId,
            MemberUserId = target.Id,
            MutationKind = state.Kind.ToString(),
            ClubId = state.ClubId,
            CreatedById = actor.Id,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            commitAttempted.MarkAttempted();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ServiceProblem.Conflict("The club membership changed. Reload and try again.");
        }

        LogMutationCompleted(state.Kind, state.ActorUserId, state.TargetUserId, state.ClubId);
        return new MutationReceipt(RefreshCurrentUser: state.ActorUserId == state.TargetUserId);
    }

    private static async Task<ExecutionResult<ServiceResult<MutationReceipt>>> VerifyMutationCommittedAsync(
        NovaAdminDbContext db,
        MutationState state,
        CancellationToken cancellationToken)
    {
        var committed = await db.ClubMembershipMutationReceipts
            .AsNoTracking()
            .AnyAsync(
                receipt => receipt.OperationId == state.OperationId
                    && receipt.ClubId == state.ClubId
                    && receipt.MemberUserId == state.TargetUserId
                    && receipt.MutationKind == state.Kind.ToString(),
                cancellationToken);

        return committed
            ? new ExecutionResult<ServiceResult<MutationReceipt>>(
                successful: true,
                new MutationReceipt(RefreshCurrentUser: state.ActorUserId == state.TargetUserId))
            : new ExecutionResult<ServiceResult<MutationReceipt>>(successful: false, default!);
    }

    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    private enum MutationKind { Promote, Demote, Remove, Leave }

    private sealed record MutationState(
        MutationKind Kind,
        long ActorUserId,
        long ClubId,
        long TargetUserId,
        Guid OperationId,
        string SecurityStamp);

    private readonly record struct MutationReceipt(bool RefreshCurrentUser);

    /// <summary>Describes whether the service shell should apply effects for a policy outcome.</summary>
    /// <param name="Apply">Whether the mutation effects should be persisted.</param>
    /// <param name="RefreshCurrentUser">Whether the acting user's cookie should be refreshed.</param>
    private readonly record struct MutationDisposition(bool Apply, bool RefreshCurrentUser);

    private sealed class CommitAttemptTracker
    {
        private int _attempted;
        public bool Attempted => Volatile.Read(ref _attempted) == 1;
        public void Reset() => Volatile.Write(ref _attempted, 0);
        public void MarkAttempted() => Volatile.Write(ref _attempted, 1);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed {Kind} membership mutation by user {ActorUserId} for user {TargetUserId} in club {ClubId}.")]
    private partial void LogMutationCompleted(MutationKind kind, long actorUserId, long targetUserId, long clubId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {Kind} membership mutation by user {ActorUserId} for user {TargetUserId} in club {ClubId}.")]
    private partial void LogMutationFailed(Exception exception, MutationKind kind, long actorUserId, long targetUserId, long clubId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Membership changed but sign-in refresh failed for user {UserId}.")]
    private partial void LogSignInRefreshFailed(Exception exception, long userId);
}
