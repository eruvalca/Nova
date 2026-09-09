using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Activity;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies join-request creation remains correct when Npgsql retries a failed transaction:
/// the request and its durable activity row are never duplicated, and an ambiguous commit is
/// verified rather than replayed against the request's unique requesting-user key.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubJoinRequestRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient failure before the commit rolls back and retries with a fresh context,
    /// leaving exactly one pending request and one join-request-submitted activity row for the
    /// operation.
    /// </summary>
    [Fact]
    public async Task CreateJoinRequestRetriesFreshTransactionAfterTransientSaveFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedJoinRequestDataAsync(cancellationToken);
        ActAs(seed.RequesterUserId, clubId: null);

        var failureInterceptor = new FailFirstTransactionCommitInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(factory, seed.RequesterUserId);

        var result = await service.CreateJoinRequestAsync(seed.ClubId, cancellationToken);

        result.IsSuccess.ShouldBeTrue(
            "a pre-commit transient failure must be retried to a successful join request");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var requests = await verify.ClubJoinRequests
            .Where(request => request.RequestingUserId == seed.RequesterUserId)
            .ToListAsync(cancellationToken);
        requests.Count.ShouldBe(1);
        requests[0].ClubId.ShouldBe(seed.ClubId);
        requests[0].Status.ShouldBe(RequestStatus.Pending);
        requests[0].ClubJoinRequestId.ShouldBe(result.Value.ClubJoinRequestId);

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.JoinRequestSubmitted)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        events[0].ActorUserId.ShouldBe(seed.RequesterUserId);
        events[0].ActorDisplayName.ShouldBe("Requester R");
        events[0].IsAdminOnly.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies a join request whose commit reached the database but surfaced a transient failure
    /// is verified as committed rather than replayed into a spurious insert conflict.
    /// </summary>
    [Fact]
    public async Task CreateJoinRequestVerifiesCommittedRequestAfterAmbiguousCommitFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedJoinRequestDataAsync(cancellationToken);
        ActAs(seed.RequesterUserId, clubId: null);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(factory, seed.RequesterUserId);

        var result = await service.CreateJoinRequestAsync(seed.ClubId, cancellationToken);

        result.IsSuccess.ShouldBeTrue(
            "an ambiguous commit must be verified rather than replayed into a conflict");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var requests = await verify.ClubJoinRequests
            .Where(request => request.RequestingUserId == seed.RequesterUserId)
            .ToListAsync(cancellationToken);
        requests.Count.ShouldBe(1);
        requests[0].ClubJoinRequestId.ShouldBe(result.Value.ClubJoinRequestId);

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.JoinRequestSubmitted)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies an approval whose commit reached the database but surfaced a transient failure is
    /// verified as committed rather than replayed into a duplicate member-joined event.
    /// </summary>
    [Fact]
    public async Task ApproveJoinRequestVerifiesCommittedApprovalAfterAmbiguousCommitFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        ActAsAdmin(seed.AdminUserId, seed.ClubId);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var writeFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var adminFactory = new RetryingAdminDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(writeFactory, seed.RequesterUserId, adminFactory);

        var result = await service.ApproveJoinRequestAsync(seed.RequestId, cancellationToken);

        result.IsSuccess.ShouldBeTrue(
            "an ambiguous approval commit must be verified rather than replayed into a duplicate event");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var request = await verify.ClubJoinRequests
            .SingleAsync(r => r.ClubJoinRequestId == seed.RequestId, cancellationToken);
        request.Status.ShouldBe(RequestStatus.Approved);

        var requester = await verify.Users
            .SingleAsync(u => u.Id == seed.RequesterUserId, cancellationToken);
        requester.ClubId.ShouldBe(seed.ClubId);
        requester.SecurityStamp.ShouldNotBe(seed.SecurityStamp, StringComparer.Ordinal);
        requester.ConcurrencyStamp.ShouldNotBe(seed.ConcurrencyStamp, StringComparer.Ordinal);

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.MemberJoined)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        events[0].ActorUserId.ShouldBe(seed.AdminUserId);

        var receipts = await verify.ClubMembershipMutationReceipts
            .Where(receipt => receipt.MemberUserId == seed.RequesterUserId
                && receipt.MutationKind == "JoinApproval")
            .ToListAsync(cancellationToken);
        receipts.Count.ShouldBe(1);
        receipts[0].OperationId.ShouldNotBe(Guid.Empty);
        receipts[0].ClubId.ShouldBe(seed.ClubId);
        receipts[0].CreatedById.ShouldBe(seed.AdminUserId);
    }

    /// <summary>
    /// Verifies approval recovery remains successful when the club aggregate is deleted after the
    /// commit but before ambiguous-commit verification executes. The immutable receipt deliberately
    /// survives that cascade so the committed approval is not replayed.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task ApproveJoinRequestAmbiguousCommitThenClubDeletionVerifiesIndependentReceiptAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        ActAsAdmin(seed.AdminUserId, seed.ClubId);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var gateInterceptor = new GateReceiptVerificationInterceptor("\"ClubMembershipMutationReceipts\"");
        var service = CreateService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                new NoOpInterceptor()),
            seed.RequesterUserId,
            new RetryingAdminDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                failureInterceptor,
                gateInterceptor));

        Task<ServiceResult<Success>> approvalTask;
        try
        {
            approvalTask = service.ApproveJoinRequestAsync(seed.RequestId, cancellationToken);
            await gateInterceptor.WaitForVerificationAttemptAsync(cancellationToken);

#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
            await using (var delete = fixture.CreateAdminContext())
#pragma warning restore MA0004
            {
                delete.Clubs.Remove(await delete.Clubs.SingleAsync(
                    club => club.ClubId == seed.ClubId,
                    cancellationToken));
                await delete.SaveChangesAsync(cancellationToken);
            }

            gateInterceptor.Release();
            var result = await approvalTask;
            result.IsSuccess.ShouldBeTrue(
                "approval must verify its independent receipt after the club aggregate is deleted");
        }
        finally
        {
            gateInterceptor.Release();
        }

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Users.SingleAsync(
            user => user.Id == seed.RequesterUserId,
            cancellationToken)).ClubId.ShouldBeNull();
        (await verify.ClubJoinRequests.AnyAsync(
            request => request.ClubJoinRequestId == seed.RequestId,
            cancellationToken)).ShouldBeFalse();
        (await verify.Clubs.AnyAsync(
            club => club.ClubId == seed.ClubId,
            cancellationToken)).ShouldBeFalse();
        var receipt = await verify.ClubMembershipMutationReceipts.SingleAsync(
            receipt => receipt.MemberUserId == seed.RequesterUserId
                && receipt.MutationKind == "JoinApproval",
            cancellationToken);
        receipt.OperationId.ShouldNotBe(Guid.Empty);
        receipt.ClubId.ShouldBe(seed.ClubId);
        receipt.CreatedById.ShouldBe(seed.AdminUserId);
    }

    /// <summary>
    /// Verifies a rejection whose commit reached the database but surfaced a transient failure is
    /// verified as committed rather than replayed into a duplicate join-request-rejected event.
    /// </summary>
    [Fact]
    public async Task RejectJoinRequestVerifiesCommittedRejectionAfterAmbiguousCommitFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        ActAsAdmin(seed.AdminUserId, seed.ClubId);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var writeFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(writeFactory, seed.RequesterUserId);

        var result = await service.RejectJoinRequestAsync(seed.RequestId, cancellationToken);

        result.IsSuccess.ShouldBeTrue(
            "an ambiguous rejection commit must be verified rather than replayed into a duplicate event");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var request = await verify.ClubJoinRequests
            .SingleAsync(r => r.ClubJoinRequestId == seed.RequestId, cancellationToken);
        request.Status.ShouldBe(RequestStatus.Rejected);

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.JoinRequestRejected)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        events[0].ActorUserId.ShouldBe(seed.AdminUserId);
    }

    /// <summary>
    /// Verifies a cancellation whose commit reached the database but surfaced a transient failure
    /// is verified as committed rather than replayed into a duplicate join-request-cancelled event.
    /// </summary>
    [Fact]
    public async Task CancelJoinRequestVerifiesCommittedCancellationAfterAmbiguousCommitFailureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        ActAs(seed.RequesterUserId, clubId: null);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var writeFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(writeFactory, seed.RequesterUserId);

        var result = await service.CancelJoinRequestAsync(seed.RequestId, cancellationToken);

        result.IsSuccess.ShouldBeTrue(
            "an ambiguous cancellation commit must be verified rather than replayed into a duplicate event");
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var request = await verify.ClubJoinRequests
            .SingleOrDefaultAsync(r => r.ClubJoinRequestId == seed.RequestId, cancellationToken);
        request.ShouldBeNull("the committed cancellation deletes the request row");

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.JoinRequestCancelled)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        events[0].ActorUserId.ShouldBe(seed.RequesterUserId);
    }

    /// <summary>
    /// Verifies the join-request advisory lock serializes competing terminal transitions: when a
    /// rejection commits while an approval waits for the request lock, the approval re-reads the
    /// committed rejection and returns Conflict instead of appending a contradictory member-joined
    /// event.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task ApproveJoinRequestReturnsConflictWhenRejectionWinsTheRequestLockAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        ActAsAdmin(seed.AdminUserId, seed.ClubId);

        var service = CreateService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                new NoOpInterceptor()),
            seed.RequesterUserId);

        // Hold the join-request advisory lock so the approval blocks inside its mutation attempt.
        var lockKey = (long.MinValue / 8) + seed.RequestId;
        await using var holdDb = fixture.CreateAdminContext();
        await using var holdTransaction = await holdDb.Database.BeginTransactionAsync(cancellationToken);
        await holdDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var approveTask = service.ApproveJoinRequestAsync(seed.RequestId, cancellationToken);

        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            holdDb,
            lockKey,
            cancellationToken);

        // The competing rejection wins while the approval waits: mark the request rejected and
        // append its durable event, then release the lock.
        var request = await holdDb.ClubJoinRequests
            .SingleAsync(candidate => candidate.ClubJoinRequestId == seed.RequestId, cancellationToken);
        request.Status = RequestStatus.Rejected;
        var rejectPayload = JsonSerializer.Serialize<ClubActivityContext>(new JoinRequestContext { JoinRequestId = seed.RequestId, RequesterDisplayName = "Requester R" });
        holdDb.ActivityEvents.Add(new ActivityEventEntity
        {
            ClubId = seed.ClubId,
            EventKind = ActivityEventKind.JoinRequestRejected,
            IsAdminOnly = true,
            ActorUserId = seed.AdminUserId,
            ActorDisplayName = "Admin A",
            PayloadJson = rejectPayload,
            CreatedById = seed.AdminUserId,
        });
        await holdDb.SaveChangesAsync(cancellationToken);
        await holdTransaction.CommitAsync(cancellationToken);

        var result = await approveTask;
        result.IsProblem.ShouldBeTrue(
            "the losing approval must observe the committed rejection and return Conflict");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.ClubJoinRequests
            .SingleAsync(candidate => candidate.ClubJoinRequestId == seed.RequestId, cancellationToken);
        persisted.Status.ShouldBe(RequestStatus.Rejected);

        var joinedEvents = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.MemberJoined)
            .CountAsync(cancellationToken);
        joinedEvents.ShouldBe(0, "the losing approval must not append a member-joined event");

        var rejectedEvents = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.JoinRequestRejected)
            .CountAsync(cancellationToken);
        rejectedEvents.ShouldBe(1);
    }

    /// <summary>
    /// Verifies approval shares the requester-scoped membership lock with competing membership
    /// assignments and re-reads the requester after waiting, preserving the competing club.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task ApproveJoinRequestReturnsConflictWhenRequesterJoinsAnotherClubWhileWaitingAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        ActAsAdmin(seed.AdminUserId, seed.ClubId);
        long otherClubId;
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var setup = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
            var otherClub = new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Competing Membership Club {Guid.NewGuid():N}",
                City = "Austin",
                State = "TX",
                CreatedById = seed.RequesterUserId,
            };
            setup.Clubs.Add(otherClub);
            await setup.SaveChangesAsync(cancellationToken);
            otherClubId = otherClub.ClubId;
        }

        var service = CreateService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                new NoOpInterceptor()),
            seed.RequesterUserId);
        var lockKey = (long.MinValue / 64) + seed.RequesterUserId;
        await using var holdDb = fixture.CreateAdminContext();
        await using var holdTransaction = await holdDb.Database.BeginTransactionAsync(cancellationToken);
        await holdDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var approveTask = service.ApproveJoinRequestAsync(seed.RequestId, cancellationToken);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            holdDb,
            lockKey,
            cancellationToken);
        var requester = await holdDb.Users.SingleAsync(
            user => user.Id == seed.RequesterUserId,
            cancellationToken);
        requester.ClubId = otherClubId;
        await holdDb.SaveChangesAsync(cancellationToken);
        await holdTransaction.CommitAsync(cancellationToken);

        var result = await approveTask;

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Users.SingleAsync(user => user.Id == seed.RequesterUserId, cancellationToken))
            .ClubId.ShouldBe(otherClubId);
        (await verify.ClubJoinRequests.SingleAsync(
            request => request.ClubJoinRequestId == seed.RequestId,
            cancellationToken)).Status.ShouldBe(RequestStatus.Pending);
        (await verify.ActivityEvents.CountAsync(
            activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.MemberJoined,
            cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// Verifies club creation and join approval serialize on the requester's user-membership lock:
    /// both operations visibly wait on the held PostgreSQL advisory lock, then exactly one assigns
    /// membership while the loser observes the committed state and returns Conflict.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task ClubCreationAndJoinApprovalSerializeOnUserMembershipLockAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedApprovalDataAsync(cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var input = new CreateClubInput
        {
            Name = $"Creation Approval Race {suffix}",
            City = "Austin",
            State = "TX",
            CrestContent = CreateJpeg(),
            CrestContentType = "image/jpeg",
        };
        var approvalService = CreateService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                new NoOpInterceptor()),
            seed.RequesterUserId,
            new RetryingAdminDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                new NoOpInterceptor()));
        var creationLock = new AdvisoryLockGateInterceptor();
        // This test holds the real lock below; use the interceptor only to observe arrival.
        creationLock.Release();
        var creationService = new ClubService(
            new RetryingAdminDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                creationLock),
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            fixture.ClubCrestsContainer,
            NullLogger<ClubService>.Instance);

        var lockKey = (long.MinValue / 64) + seed.RequesterUserId;
        await using var holdDb = fixture.CreateAdminContext();
        await using var holdTransaction = await holdDb.Database.BeginTransactionAsync(cancellationToken);
        await holdDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var creationTask = Task.Run(async () =>
        {
            using var actor = fixture.UseUser(seed.RequesterUserId, clubId: null, isClubAdmin: false);
            return await creationService.CreateClubAsync(input, cancellationToken);
        });
        // Crest encoding and four blob uploads precede the transaction. Start bounded lock
        // observation only once creation reaches SQL, and expose any pre-lock failure directly.
        var creationLockAttempt = creationLock.WaitForAttemptAsync(cancellationToken);
        if (await Task.WhenAny(creationLockAttempt, creationTask) == creationTask)
        {
            var earlyResult = await creationTask;
            Assert.Fail(earlyResult.Match(
                created => $"Club creation unexpectedly completed before waiting for the held user-membership lock (club {created.ClubId}).",
                problem => $"Club creation failed before attempting the user-membership lock: {problem.Kind}: {problem.Detail}"));
        }
        await creationLockAttempt;
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            holdDb,
            lockKey,
            cancellationToken);

        var approvalTask = Task.Run(async () =>
        {
            using var actor = fixture.UseUser(seed.AdminUserId, seed.ClubId, isClubAdmin: true);
            return await approvalService.ApproveJoinRequestAsync(seed.RequestId, cancellationToken);
        });

        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            holdDb,
            lockKey,
            expectedWaiterCount: 2,
            cancellationToken);
        await holdTransaction.CommitAsync(cancellationToken);

        var approval = await approvalTask;
        var creation = await creationTask;
        (approval.IsSuccess ^ creation.IsSuccess).ShouldBeTrue(
            "the shared user-membership lock must allow exactly one membership assignment");

        await using var verify = fixture.CreateAdminContext();
        var requester = await verify.Users.SingleAsync(
            user => user.Id == seed.RequesterUserId,
            cancellationToken);
        var request = await verify.ClubJoinRequests.SingleAsync(
            candidate => candidate.ClubJoinRequestId == seed.RequestId,
            cancellationToken);

        if (approval.IsSuccess)
        {
            creation.IsProblem.ShouldBeTrue();
            creation.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
            requester.ClubId.ShouldBe(seed.ClubId);
            request.Status.ShouldBe(RequestStatus.Approved);
            (await verify.Clubs.AnyAsync(club => club.Name == input.Name, cancellationToken)).ShouldBeFalse();
        }
        else
        {
            approval.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
            creation.IsSuccess.ShouldBeTrue();
            requester.ClubId.ShouldBe(creation.Value.ClubId);
            request.Status.ShouldBe(RequestStatus.Pending);
            (await verify.Clubs.AnyAsync(
                club => club.ClubId == creation.Value.ClubId && club.Name == input.Name,
                cancellationToken)).ShouldBeTrue();
        }
    }

    /// <summary>
    /// Verifies a join-request create whose insert violates the one-to-one RequestingUserId unique
    /// constraint (a concurrent submission won the probe/write race, or a non-pending request still
    /// occupies the slot) is classified as Conflict rather than a server error.
    /// </summary>
    [Fact]
    public async Task CreateJoinRequestReturnsConflictWhenUniqueViolationOccursAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedJoinRequestDataAsync(cancellationToken);

        // Seed a non-pending request for the requester: the preflight only checks for a Pending
        // request, so it passes, but the one-to-one RequestingUserId unique constraint rejects the
        // subsequent insert.
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var db = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
            db.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = seed.ClubId,
                RequestingUserId = seed.RequesterUserId,
                Status = RequestStatus.Rejected,
                CreatedById = seed.RequesterUserId
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        ActAs(seed.RequesterUserId, clubId: null);
        var service = CreateService(
            new RetryingTenantDbContextFactory(
                fixture.ConnectionString,
                fixture.CurrentUser,
                new NoOpInterceptor()),
            seed.RequesterUserId);

        var result = await service.CreateJoinRequestAsync(seed.ClubId, cancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Seeds one club and one identity user with a database-generated id, both fresh per test.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The seeded club and requester user identifiers.</returns>
    private async Task<Seed> SeedJoinRequestDataAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        var db = fixture.CreateAdminContext();
        await using (db)
        {
            var suffix = Guid.NewGuid().ToString("N");

            var requester = new NovaUserEntity
            {
                FirstName = "Requester",
                LastName = "R",
                ClubId = null
            };
            db.Users.Add(requester);
            await db.SaveChangesAsync(cancellationToken);

            var club = new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Join Request Retry Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = requester.Id
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync(cancellationToken);

            return new Seed(requester.Id, club.ClubId);
        }
    }

    /// <summary>
    /// Creates the join-request service with the supplied retry-enabled tenant context factory and
    /// an optional retry-enabled admin context factory (defaulting to the non-retrying fixture
    /// factory) so approval paths can exercise the admin-context execution strategy.
    /// </summary>
    /// <param name="writeFactory">The write context factory used for execution attempts.</param>
    /// <param name="requesterUserId">The requester identity returned by the substituted user manager.</param>
    /// <param name="adminFactory">The admin context factory, used by the approval path.</param>
    /// <returns>A join-request service.</returns>
    private ClubJoinRequestService CreateService(
        IDbContextFactory<NovaDbContext> writeFactory,
        long requesterUserId,
        IDbContextFactory<NovaAdminDbContext>? adminFactory = null)
    {
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            Substitute.For<IUserStore<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<UserManager<NovaUserEntity>>>());

        userManager.FindByIdAsync(Arg.Any<string>())
            .Returns(Task.FromResult<NovaUserEntity?>(
                new NovaUserEntity
                {
                    Id = requesterUserId,
                    FirstName = "Requester",
                    LastName = "R",
                    ClubId = null
                }));

        return new ClubJoinRequestService(
            writeFactory,
            new PostgresReadContextFactory(fixture),
            adminFactory ?? new PostgresAdminContextFactory(fixture),
            fixture.CurrentUser,
            userManager,
            NullLogger<ClubJoinRequestService>.Instance);
    }

    /// <summary>
    /// Sets the current tenant identity used by newly created contexts.
    /// </summary>
    /// <param name="userId">The acting user identifier.</param>
    /// <param name="clubId">The acting club identifier.</param>
    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    /// <summary>
    /// Sets the current tenant identity to a club administrator for approval/rejection paths.
    /// </summary>
    /// <param name="userId">The acting administrator user identifier.</param>
    /// <param name="clubId">The acting administrator club identifier.</param>
    private void ActAsAdmin(long userId, long clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
    }

    /// <summary>Creates a valid JPEG crest for club-creation contention tests.</summary>
    /// <returns>JPEG-encoded image bytes.</returns>
    private static byte[] CreateJpeg()
    {
        using var image = new Image<Rgba32>(128, 96, new Rgba32(120, 180, 240));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Seeds one club, one club-less requester, one club-member administrator, and a pending join
    /// request, all fresh per test, so approval/rejection retry paths can be exercised.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The seeded club, administrator, requester, and pending request identifiers.</returns>
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    private async Task<ApprovalSeed> SeedApprovalDataAsync(CancellationToken cancellationToken)
#pragma warning restore MA0051
    {
        ActAs(userId: null, clubId: null);
        var db = fixture.CreateAdminContext();
        await using (db)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var securityStamp = Guid.NewGuid().ToString("N");
            var concurrencyStamp = Guid.NewGuid().ToString("N");

            var requester = new NovaUserEntity
            {
                FirstName = "Requester",
                LastName = "R",
                ClubId = null,
                SecurityStamp = securityStamp,
                ConcurrencyStamp = concurrencyStamp,
            };
            db.Users.Add(requester);
            await db.SaveChangesAsync(cancellationToken);

            var admin = new NovaUserEntity
            {
                FirstName = "Admin",
                LastName = "A",
                ClubId = null
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync(cancellationToken);

            var club = new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Join Request Approve Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = admin.Id
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync(cancellationToken);

            admin.ClubId = club.ClubId;
            var administratorRoleId = await db.Roles
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
                .Where(role => role.NormalizedName == Nova.SharedKernel.Security.Roles.ClubAdmin.ToUpperInvariant())
#pragma warning restore CA1862
                .Select(role => role.Id)
                .SingleAsync(cancellationToken);
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = admin.Id, RoleId = administratorRoleId });
            await db.SaveChangesAsync(cancellationToken);

            var request = new ClubJoinRequestEntity
            {
                ClubId = club.ClubId,
                RequestingUserId = requester.Id,
                Status = RequestStatus.Pending,
                CreatedById = requester.Id
            };
            db.ClubJoinRequests.Add(request);
            await db.SaveChangesAsync(cancellationToken);

            return new ApprovalSeed(
                club.ClubId,
                admin.Id,
                requester.Id,
                request.ClubJoinRequestId,
                securityStamp,
                concurrencyStamp);
        }
    }

    /// <summary>
    /// Holds one test's seeded club and requester identifiers.
    /// </summary>
    /// <param name="RequesterUserId">The seeded requester user identifier.</param>
    /// <param name="ClubId">The seeded club identifier.</param>
    private sealed record Seed(long RequesterUserId, long ClubId);

    /// <summary>
    /// Holds one test's seeded approval identities and pending request identifier.
    /// </summary>
    /// <param name="ClubId">The seeded club identifier.</param>
    /// <param name="AdminUserId">The seeded club-member administrator identifier.</param>
    /// <param name="RequesterUserId">The seeded club-less requester identifier.</param>
    /// <param name="RequestId">The seeded pending join-request identifier.</param>
    /// <param name="SecurityStamp">The requester's security stamp before approval.</param>
    /// <param name="ConcurrencyStamp">The requester's concurrency stamp before approval.</param>
    private sealed record ApprovalSeed(
        long ClubId,
        long AdminUserId,
        long RequesterUserId,
        long RequestId,
        string SecurityStamp,
        string ConcurrencyStamp);
}
