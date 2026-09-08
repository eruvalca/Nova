using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for Phase 2 enhancements to <see cref="ClubJoinRequestService"/>.
/// Covers the modified <see cref="ClubJoinRequestService.GetCurrentUserPendingRequestAsync"/>,
/// new <see cref="ClubJoinRequestService.GetClubJoinRequestsAsync"/>,
/// new <see cref="ClubJoinRequestService.ApproveJoinRequestAsync"/>,
/// and new <see cref="ClubJoinRequestService.RejectJoinRequestAsync"/>.
/// </summary>
public sealed class ClubJoinRequestServiceTests : IDisposable
{
    // Test data constants
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long AdminUserId = 200;
    private const long RequestingUserId = 201;
    private const long OtherClubAdminId = 202;
    private const string RequesterSecurityStamp = "requester-security-stamp";
    private const string RequesterConcurrencyStamp = "requester-concurrency-stamp";

    private readonly TenancyTestHarness _harness = new();
    private readonly UserManager<NovaUserEntity> _userManager;

    public ClubJoinRequestServiceTests()
    {
        _userManager = Substitute.For<UserManager<NovaUserEntity>>(
            Substitute.For<IUserStore<NovaUserEntity>>(),
            Substitute.For<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            Array.Empty<IUserValidator<NovaUserEntity>>(),
            Array.Empty<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<UserManager<NovaUserEntity>>>());
        Seed();
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _harness.Dispose();
    }

    private void Seed()
    {
        using var context = _harness.CreateAdminContext();

        // Create clubs
        context.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = AdminUserId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = OtherClubAdminId });

        // Create users
        context.Users.AddRange(
            new NovaUserEntity { Id = AdminUserId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity
            {
                Id = RequestingUserId,
                FirstName = "Requester",
                LastName = "R",
                ClubId = null,
                SecurityStamp = RequesterSecurityStamp,
                ConcurrencyStamp = RequesterConcurrencyStamp,
            },
            new NovaUserEntity { Id = OtherClubAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });

        var administratorRole = new IdentityRole<long>(Nova.SharedKernel.Security.Roles.ClubAdmin)
        {
            Id = 10,
            NormalizedName = Nova.SharedKernel.Security.Roles.ClubAdmin.ToUpperInvariant(),
        };
        context.Roles.Add(administratorRole);
        context.UserRoles.AddRange(
            new IdentityUserRole<long> { UserId = AdminUserId, RoleId = administratorRole.Id },
            new IdentityUserRole<long> { UserId = OtherClubAdminId, RoleId = administratorRole.Id });

        context.SaveChanges();
    }

    private ClubJoinRequestService CreateService()
    {
        var dbFactory = Substitute.For<IDbContextFactory<NovaDbContext>>();
        var readDbFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<ClubJoinRequestService>>();

        // Setup factories to use the harness contexts
        dbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(_harness.CreateTenantContext()));

        readDbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(_harness.CreateReadContext()));

        var adminDbFactory = Substitute.For<IDbContextFactory<NovaAdminDbContext>>();
        adminDbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(_harness.CreateAdminContext()));

        return new ClubJoinRequestService(
            dbFactory,
            readDbFactory,
            adminDbFactory,
            _harness.CurrentUser,
            _userManager,
            logger);
    }

    #region CreateJoinRequestAsync Tests

    [Fact]
    public async Task CreateJoinRequestAsyncReturnsForbiddenWhenNoSignedInUserAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        // Act
        var result = await service.CreateJoinRequestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task CreateJoinRequestAsyncReturnsConflictWhenUserAlreadyHasClubAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.CreateJoinRequestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task CreateJoinRequestAsyncReturnsConflictWhenPendingRequestAlreadyExistsAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        using (var context = _harness.CreateAdminContext())
        {
            context.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        // Act
        var result = await service.CreateJoinRequestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task CreateJoinRequestAsyncReturnsNotFoundWhenClubDoesNotExistAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        // Act
        var result = await service.CreateJoinRequestAsync(999, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task CreateJoinRequestAsyncCreatesRequestAndEmitsJoinRequestSubmittedEventAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        _userManager.FindByIdAsync(RequestingUserId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Returns(Task.FromResult<NovaUserEntity?>(new NovaUserEntity
            {
                Id = RequestingUserId,
                FirstName = "Requester",
                LastName = "R",
                ClubId = null
            }));

        var service = CreateService();

        // Act
        var result = await service.CreateJoinRequestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(ClubAId);
        result.Value.RequestingUserId.ShouldBe(RequestingUserId);
        result.Value.Status.ShouldBe(RequestStatus.Pending);

        using (var context = _harness.CreateAdminContext())
        {
            var request = await context.ClubJoinRequests.SingleAsync(
                r => r.RequestingUserId == RequestingUserId,
                TestContext.Current.CancellationToken);
            request.ClubId.ShouldBe(ClubAId);
            request.Status.ShouldBe(RequestStatus.Pending);

            var activityEvent = await context.ActivityEvents.SingleAsync(
                e => e.EventKind == ActivityEventKind.JoinRequestSubmitted,
                TestContext.Current.CancellationToken);
            activityEvent.ClubId.ShouldBe(ClubAId);
            activityEvent.ActorUserId.ShouldBe(RequestingUserId);
            activityEvent.ActorDisplayName.ShouldBe("Requester R");
            activityEvent.IsAdminOnly.ShouldBeTrue();
            activityEvent.CampaignId.ShouldBeNull();
        }
    }

    #endregion

    #region GetCurrentUserPendingRequestAsync Tests (Modified Behavior)

    [Fact]
    public async Task GetCurrentUserPendingRequestAsyncReturnsApprovedRequestWhenUserHasApprovedRequestAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        using (var context = _harness.CreateAdminContext())
        {
            context.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Approved,
                CreatedById = RequestingUserId
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(RequestStatus.Approved);
        result.Value.RequestingUserId.ShouldBe(RequestingUserId);
    }

    [Fact]
    public async Task GetCurrentUserPendingRequestAsyncReturnsRejectedRequestWhenUserHasRejectedRequestAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        using (var context = _harness.CreateAdminContext())
        {
            context.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Rejected,
                CreatedById = RequestingUserId
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(RequestStatus.Rejected);
    }

    [Fact]
    public async Task GetCurrentUserPendingRequestAsyncReturnsNotFoundWhenUserHasNoRequestsAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        var service = CreateService();

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }


    [Fact]
    public async Task GetCurrentUserPendingRequestAsyncReturnsNotFoundWhenNotAuthenticatedAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        var service = CreateService();

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    #endregion

    #region GetClubJoinRequestsAsync Tests

    [Fact]
    public async Task GetClubJoinRequestsAsyncReturnsPendingRequestsWhenCallerIsClubAdminAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        using (var context = _harness.CreateAdminContext())
        {
            context.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        // Act
        var result = await service.GetClubJoinRequestsAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].RequestingUserId.ShouldBe(RequestingUserId);
        result.Value[0].Status.ShouldBe(RequestStatus.Pending);
    }

    [Fact]
    public async Task GetClubJoinRequestsAsyncReturnsEmptyListWhenClubHasNoPendingRequestsAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var service = CreateService();

        // Act
        var result = await service.GetClubJoinRequestsAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetClubJoinRequestsAsyncReturnsForbiddenWhenCallerIsNotClubAdminAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        var service = CreateService();

        // Act
        var result = await service.GetClubJoinRequestsAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetClubJoinRequestsAsyncReturnsForbiddenWhenCallerIsClubAdminOfDifferentClubAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = OtherClubAdminId;
        _harness.CurrentUser.ClubId = ClubBId;
        _harness.CurrentUser.IsClubAdmin = true;

        var service = CreateService();

        // Act
        var result = await service.GetClubJoinRequestsAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetClubJoinRequestsAsyncOnlyReturnsPendingRequestsNotApprovedOrRejectedAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        using (var context = _harness.CreateAdminContext())
        {
            context.ClubJoinRequests.AddRange(
                new ClubJoinRequestEntity
                {
                    ClubId = ClubAId,
                    RequestingUserId = RequestingUserId,
                    Status = RequestStatus.Pending,
                    CreatedById = RequestingUserId
                },
                new ClubJoinRequestEntity
                {
                    ClubId = ClubAId,
                    RequestingUserId = OtherClubAdminId,
                    Status = RequestStatus.Approved,
                    CreatedById = RequestingUserId
                },
                new ClubJoinRequestEntity
                {
                    ClubId = ClubAId,
                    RequestingUserId = AdminUserId,
                    Status = RequestStatus.Rejected,
                    CreatedById = RequestingUserId
                });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        // Act
        var result = await service.GetClubJoinRequestsAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].Status.ShouldBe(RequestStatus.Pending);
        result.Value[0].RequestingUserId.ShouldBe(RequestingUserId);
    }

    [Fact]
    public async Task GetClubJoinRequestsAsyncReturnsRequestsOrderedOldestFirstAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId1 = 0;
        long requestId2 = 0;

        using (var context = _harness.CreateAdminContext())
        {
            var request1 = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request1);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId1 = request1.ClubJoinRequestId;

            // Give the first request an explicit earlier timestamp instead of depending on timer resolution.
            await context.ClubJoinRequests.Where(request => request.ClubJoinRequestId == requestId1)
                .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.CreatedAt, DateTimeOffset.UtcNow.AddDays(-1)), TestContext.Current.CancellationToken)
                ;

            var request2 = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = OtherClubAdminId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request2);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId2 = request2.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.GetClubJoinRequestsAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        // Oldest first means the first item was created earlier
        result.Value[0].ClubJoinRequestId.ShouldBe(requestId1);
        result.Value[1].ClubJoinRequestId.ShouldBe(requestId2);
    }

    #endregion

    #region ApproveJoinRequestAsync Tests

    [Fact]
    public async Task ApproveJoinRequestAsyncReturnsForbiddenWhenCallerIsNotClubAdminAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ApproveJoinRequestAsyncReturnsNotFoundWhenRequestDoesNotExistAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var service = CreateService();

        // Act
        var result = await service.ApproveJoinRequestAsync(999, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task ApproveJoinRequestAsyncReturnsNotFoundWhenRequestBelongsToDifferentClubAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubBId, // Different club
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task ApproveJoinRequestAsyncReturnsConflictWhenRequestIsAlreadyApprovedAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Approved,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ApproveJoinRequestAsyncReturnsConflictWhenRequestIsAlreadyRejectedAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Rejected,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ApproveJoinRequestAsyncReturnsConflictWhenRequesterAlreadyJoinedAnotherClubAsync()
    {
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId,
            };
            context.ClubJoinRequests.Add(request);
            (await context.Users.SingleAsync(user => user.Id == RequestingUserId, TestContext.Current.CancellationToken)).ClubId = ClubBId;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var result = await CreateService().ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        using var verify = _harness.CreateAdminContext();
        (await verify.Users.SingleAsync(user => user.Id == RequestingUserId, TestContext.Current.CancellationToken)).ClubId.ShouldBe(ClubBId);
        (await verify.ClubJoinRequests.SingleAsync(request => request.ClubJoinRequestId == requestId, TestContext.Current.CancellationToken)).Status.ShouldBe(RequestStatus.Pending);
        verify.ActivityEvents.ShouldNotContain(activity => activity.EventKind == ActivityEventKind.MemberJoined);
    }

    [Fact]
    public async Task ApproveJoinRequestAsyncApprovesRequestWhenRequestIsPendingAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        // Verify the request was approved and the requester's membership was assigned atomically
        using (var context = _harness.CreateAdminContext())
        {
            var updatedRequest = await context.ClubJoinRequests.FirstAsync(r => r.ClubJoinRequestId == requestId, TestContext.Current.CancellationToken);
            updatedRequest.Status.ShouldBe(RequestStatus.Approved);

            var updatedUser = await context.Users.FirstAsync(u => u.Id == RequestingUserId, TestContext.Current.CancellationToken);
            updatedUser.ClubId.ShouldBe(ClubAId);
            updatedUser.SecurityStamp.ShouldNotBe(RequesterSecurityStamp, StringComparer.Ordinal);
            updatedUser.ConcurrencyStamp.ShouldNotBe(RequesterConcurrencyStamp, StringComparer.Ordinal);

            var receipt = await context.ClubMembershipMutationReceipts.SingleAsync(
                candidate => candidate.MemberUserId == RequestingUserId,
                TestContext.Current.CancellationToken);
            receipt.OperationId.ShouldNotBe(Guid.Empty);
            receipt.ClubId.ShouldBe(ClubAId);
            receipt.MutationKind.ShouldBe("JoinApproval");
            receipt.CreatedById.ShouldBe(AdminUserId);
        }

        await _userManager.DidNotReceive().UpdateSecurityStampAsync(Arg.Any<NovaUserEntity>());
    }

    #endregion

    #region RejectJoinRequestAsync Tests

    [Fact]
    public async Task RejectJoinRequestAsyncReturnsForbiddenWhenCallerIsNotClubAdminAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.RejectJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task RejectJoinRequestAsyncReturnsNotFoundWhenRequestDoesNotExistAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var service = CreateService();

        // Act
        var result = await service.RejectJoinRequestAsync(999, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task RejectJoinRequestAsyncReturnsNotFoundWhenRequestBelongsToDifferentClubAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubBId, // Different club
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.RejectJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task RejectJoinRequestAsyncReturnsConflictWhenRequestIsNotPendingAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Approved,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.RejectJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task RejectJoinRequestAsyncRejectsRequestWhenRequestIsPendingAsync()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        long requestId = 0;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = RequestingUserId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            requestId = request.ClubJoinRequestId;
        }

        var service = CreateService();

        // Act
        var result = await service.RejectJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        // Verify the request was rejected in the database
        using (var context = _harness.CreateAdminContext())
        {
            var updatedRequest = await context.ClubJoinRequests.FirstAsync(r => r.ClubJoinRequestId == requestId, TestContext.Current.CancellationToken);
            updatedRequest.Status.ShouldBe(RequestStatus.Rejected);
        }
    }

    #endregion
}
