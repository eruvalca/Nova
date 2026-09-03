using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Results;
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
public class ClubJoinRequestServiceTests : IDisposable
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

    public void Dispose() => _harness.Dispose();

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

        var administratorRole = new IdentityRole<long>(Nova.Shared.Security.Roles.ClubAdmin)
        {
            Id = 10,
            NormalizedName = Nova.Shared.Security.Roles.ClubAdmin.ToUpperInvariant(),
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
    public async Task CreateJoinRequestAsync_ReturnsForbidden_WhenNoSignedInUser()
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
    public async Task CreateJoinRequestAsync_ReturnsConflict_WhenUserAlreadyHasClub()
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
    public async Task CreateJoinRequestAsync_ReturnsConflict_WhenPendingRequestAlreadyExists()
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
            context.SaveChanges();
        }

        var service = CreateService();

        // Act
        var result = await service.CreateJoinRequestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task CreateJoinRequestAsync_ReturnsNotFound_WhenClubDoesNotExist()
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
    public async Task CreateJoinRequestAsync_CreatesRequestAndEmitsJoinRequestSubmittedEvent()
    {
        // Arrange
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        _userManager.FindByIdAsync(RequestingUserId.ToString())
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
    public async Task GetCurrentUserPendingRequestAsync_ReturnsApprovedRequest_WhenUserHasApprovedRequest()
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
            context.SaveChanges();
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
    public async Task GetCurrentUserPendingRequestAsync_ReturnsRejectedRequest_WhenUserHasRejectedRequest()
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
            context.SaveChanges();
        }

        var service = CreateService();

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(RequestStatus.Rejected);
    }

    [Fact]
    public async Task GetCurrentUserPendingRequestAsync_ReturnsNotFound_WhenUserHasNoRequests()
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
    public async Task GetCurrentUserPendingRequestAsync_ReturnsNotFound_WhenNotAuthenticated()
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
    public async Task GetClubJoinRequestsAsync_ReturnsPendingRequests_WhenCallerIsClubAdmin()
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
            context.SaveChanges();
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
    public async Task GetClubJoinRequestsAsync_ReturnsEmptyList_WhenClubHasNoPendingRequests()
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
    public async Task GetClubJoinRequestsAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
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
    public async Task GetClubJoinRequestsAsync_ReturnsForbidden_WhenCallerIsClubAdminOfDifferentClub()
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
    public async Task GetClubJoinRequestsAsync_OnlyReturnsPendingRequests_NotApprovedOrRejected()
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
            context.SaveChanges();
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
    public async Task GetClubJoinRequestsAsync_ReturnsRequestsOrderedOldestFirst()
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
            context.SaveChanges();
            requestId1 = request1.ClubJoinRequestId;

            System.Threading.Thread.Sleep(10);

            var request2 = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = OtherClubAdminId,
                Status = RequestStatus.Pending,
                CreatedById = RequestingUserId
            };
            context.ClubJoinRequests.Add(request2);
            context.SaveChanges();
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
    public async Task ApproveJoinRequestAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
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
            context.SaveChanges();
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
    public async Task ApproveJoinRequestAsync_ReturnsNotFound_WhenRequestDoesNotExist()
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
    public async Task ApproveJoinRequestAsync_ReturnsNotFound_WhenRequestBelongsToDifferentClub()
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
            context.SaveChanges();
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
    public async Task ApproveJoinRequestAsync_ReturnsConflict_WhenRequestIsAlreadyApproved()
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
            context.SaveChanges();
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
    public async Task ApproveJoinRequestAsync_ReturnsConflict_WhenRequestIsAlreadyRejected()
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
            context.SaveChanges();
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
    public async Task ApproveJoinRequestAsync_ReturnsConflict_WhenRequesterAlreadyJoinedAnotherClub()
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
            context.Users.Single(user => user.Id == RequestingUserId).ClubId = ClubBId;
            context.SaveChanges();
            requestId = request.ClubJoinRequestId;
        }

        var result = await CreateService().ApproveJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        using var verify = _harness.CreateAdminContext();
        verify.Users.Single(user => user.Id == RequestingUserId).ClubId.ShouldBe(ClubBId);
        verify.ClubJoinRequests.Single(request => request.ClubJoinRequestId == requestId).Status.ShouldBe(RequestStatus.Pending);
        verify.ActivityEvents.ShouldNotContain(activity => activity.EventKind == ActivityEventKind.MemberJoined);
    }

    [Fact]
    public async Task ApproveJoinRequestAsync_ApprovesRequest_WhenRequestIsPending()
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
            context.SaveChanges();
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
            updatedUser.SecurityStamp.ShouldNotBe(RequesterSecurityStamp);
            updatedUser.ConcurrencyStamp.ShouldNotBe(RequesterConcurrencyStamp);
        }

        await _userManager.DidNotReceive().UpdateSecurityStampAsync(Arg.Any<NovaUserEntity>());
    }

    #endregion

    #region RejectJoinRequestAsync Tests

    [Fact]
    public async Task RejectJoinRequestAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
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
            context.SaveChanges();
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
    public async Task RejectJoinRequestAsync_ReturnsNotFound_WhenRequestDoesNotExist()
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
    public async Task RejectJoinRequestAsync_ReturnsNotFound_WhenRequestBelongsToDifferentClub()
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
            context.SaveChanges();
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
    public async Task RejectJoinRequestAsync_ReturnsConflict_WhenRequestIsNotPending()
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
            context.SaveChanges();
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
    public async Task RejectJoinRequestAsync_RejectsRequest_WhenRequestIsPending()
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
            context.SaveChanges();
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
