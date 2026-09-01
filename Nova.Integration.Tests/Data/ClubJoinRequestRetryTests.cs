using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Enums;
using NSubstitute;
using Shouldly;

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
    public async Task CreateJoinRequest_RetriesFreshTransaction_AfterTransientSaveFailure()
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
    public async Task CreateJoinRequest_VerifiesCommittedRequest_AfterAmbiguousCommitFailure()
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
    public async Task ApproveJoinRequest_VerifiesCommittedApproval_AfterAmbiguousCommitFailure()
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

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.MemberJoined)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        events[0].ActorUserId.ShouldBe(seed.AdminUserId);
    }

    /// <summary>
    /// Verifies a rejection whose commit reached the database but surfaced a transient failure is
    /// verified as committed rather than replayed into a duplicate join-request-rejected event.
    /// </summary>
    [Fact]
    public async Task RejectJoinRequest_VerifiesCommittedRejection_AfterAmbiguousCommitFailure()
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
    public async Task CancelJoinRequest_VerifiesCommittedCancellation_AfterAmbiguousCommitFailure()
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
    /// Seeds one club and one identity user with a database-generated id, both fresh per test.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The seeded club and requester user identifiers.</returns>
    private async Task<Seed> SeedJoinRequestDataAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
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

        userManager.UpdateSecurityStampAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            userManager,
            Substitute.For<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SignInManager<NovaUserEntity>>>(),
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());

        return new ClubJoinRequestService(
            writeFactory,
            new PostgresReadContextFactory(fixture),
            adminFactory ?? new PostgresAdminContextFactory(fixture),
            fixture.CurrentUser,
            new ClubMembershipClaimRefresher(userManager, signInManager),
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

    /// <summary>
    /// Seeds one club, one club-less requester, one club-member administrator, and a pending join
    /// request, all fresh per test, so approval/rejection retry paths can be exercised.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The seeded club, administrator, requester, and pending request identifiers.</returns>
    private async Task<ApprovalSeed> SeedApprovalDataAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");

        var requester = new NovaUserEntity
        {
            FirstName = "Requester",
            LastName = "R",
            ClubId = null
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

        return new ApprovalSeed(club.ClubId, admin.Id, requester.Id, request.ClubJoinRequestId);
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
    private sealed record ApprovalSeed(long ClubId, long AdminUserId, long RequesterUserId, long RequestId);
}
