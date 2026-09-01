using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Components.Account;
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
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = AdminUserId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = OtherClubAdminId });

        // Create users
        context.Users.AddRange(
            new NovaUserEntity { Id = AdminUserId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = RequestingUserId, FirstName = "Requester", LastName = "R", ClubId = null },
            new NovaUserEntity { Id = OtherClubAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });

        context.SaveChanges();
    }

    private ClubJoinRequestService CreateService()
    {
        var readDbFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<ClubJoinRequestService>>();

        // Setup factories to use the harness contexts
        readDbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(_harness.CreateReadContext()));

        var adminDbFactory = Substitute.For<IDbContextFactory<NovaAdminDbContext>>();
        adminDbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(_harness.CreateAdminContext()));

        // Create a minimal ClubMembershipClaimRefresher with mocked dependencies
        // Use _userManager so the test can verify UpdateSecurityStampAsync calls
        _userManager.UpdateSecurityStampAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.FromResult(IdentityResult.Success));
        _userManager.FindByIdAsync(Arg.Any<string>())
            .Returns(call => Task.FromResult<NovaUserEntity?>(new NovaUserEntity
            {
                Id = long.Parse(call.Arg<string>(), System.Globalization.CultureInfo.InvariantCulture),
                FirstName = "Requester",
                LastName = "R",
                ClubId = ClubAId
            }));

        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            _userManager, Substitute.For<IHttpContextAccessor>(), Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<SignInManager<NovaUserEntity>>>(),
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());

        // Mock RefreshSignInAsync to avoid null reference exceptions
        signInManager.RefreshSignInAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.CompletedTask);

        var realClaimRefresher = new ClubMembershipClaimRefresher(_userManager, signInManager);

        return new ClubJoinRequestService(
            readDbFactory,
            adminDbFactory,
            _harness.CurrentUser,
            realClaimRefresher,
            logger,
            new Nova.Features.ClubActivity.ClubActivityEventWriter());
    }

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

        // Verify the request was approved in the database
        using (var context = _harness.CreateAdminContext())
        {
            var updatedRequest = await context.ClubJoinRequests.FirstAsync(r => r.ClubJoinRequestId == requestId, TestContext.Current.CancellationToken);
            updatedRequest.Status.ShouldBe(RequestStatus.Approved);
            var updatedUser = await context.Users.SingleAsync(user => user.Id == RequestingUserId, TestContext.Current.CancellationToken);
            updatedUser.ClubId.ShouldBe(ClubAId);
        }

        // Verify UserManager.UpdateSecurityStampAsync was called (proxy for MarkUserClaimsStaleAsync)
        await _userManager.Received().UpdateSecurityStampAsync(Arg.Is<NovaUserEntity>(u => u != null && u.Id == RequestingUserId));
    }

    #endregion

    #region Durable activity and ownership tests

    /// <summary>Verifies request creation commits its durable activity snapshot with the mutation.</summary>
    [Fact]
    public async Task CreateJoinRequestAsync_PersistsRequestAndDurableActivity()
    {
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().CreateJoinRequestAsync(ClubAId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var context = _harness.CreateAdminContext();
        var activity = context.ClubActivityEvents.ShouldHaveSingleItem();
        activity.EventKind.ShouldBe(ClubActivityEventKind.JoinRequestSubmitted);
        activity.JoinRequestId.ShouldBe(result.Value.ClubJoinRequestId);
        activity.ActorDisplayName.ShouldBe("Requester R");
        activity.SubjectDisplayName.ShouldBe("Requester R");
    }

    /// <summary>Verifies cancellation does not reveal whether another user's request identifier exists.</summary>
    [Fact]
    public async Task CancelJoinRequestAsync_ReturnsNotFound_ForAnotherUsersRequest()
    {
        long requestId;
        using (var context = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = ClubAId,
                RequestingUserId = AdminUserId,
                Status = RequestStatus.Pending,
                CreatedById = AdminUserId
            };
            context.ClubJoinRequests.Add(request);
            context.SaveChanges();
            requestId = request.ClubJoinRequestId;
        }
        _harness.CurrentUser.UserId = RequestingUserId;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().CancelJoinRequestAsync(requestId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        using var verification = _harness.CreateAdminContext();
        verification.ClubJoinRequests.Any(request => request.ClubJoinRequestId == requestId).ShouldBeTrue();
        verification.ClubActivityEvents.ShouldBeEmpty();
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
