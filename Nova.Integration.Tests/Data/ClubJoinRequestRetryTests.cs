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
    /// Creates the join-request service with the supplied retry-enabled tenant context factory.
    /// </summary>
    /// <param name="factory">The context factory used for execution attempts.</param>
    /// <returns>A join-request service.</returns>
    private ClubJoinRequestService CreateService(
        RetryingTenantDbContextFactory factory,
        long requesterUserId)
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

        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            userManager,
            Substitute.For<Microsoft.AspNetCore.Http.IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SignInManager<NovaUserEntity>>>(),
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());

        return new ClubJoinRequestService(
            factory,
            new PostgresReadContextFactory(fixture),
            new PostgresAdminContextFactory(fixture),
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
    /// Holds one test's seeded club and requester identifiers.
    /// </summary>
    /// <param name="RequesterUserId">The seeded requester user identifier.</param>
    /// <param name="ClubId">The seeded club identifier.</param>
    private sealed record Seed(long RequesterUserId, long ClubId);
}
