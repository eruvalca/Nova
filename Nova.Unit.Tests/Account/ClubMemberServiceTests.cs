using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Extensions.Account;
using Nova.Features.Account;
using Nova.Shared.Account;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// Tests for <see cref="ClubMemberService.GetClubMembersAsync"/> and <see cref="ClubMemberService.AssignClubAdminAsync"/>.
/// </summary>
public class ClubMemberServiceTests : IDisposable
{
    private const long ClubAId = 200;
    private const long ClubBId = 201;
    private const long ClubAdminUserId = 300;
    private const long Member1UserId = 301;
    private const long Member2UserId = 302;
    private const long ClubBAdminUserId = 303;
    private const long NonExistentUserId = 999;

    private readonly TenancyTestHarness _harness = new();
    private readonly ILogger<ClubMemberService> _mockLogger;
    private NovaUserEntity? _clubAdminUser;
    private NovaUserEntity? _member1User;
    private NovaUserEntity? _member2User;
    private NovaUserEntity? _clubBAdminUser;

    public ClubMemberServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<ClubMemberService>>();
        Seed();
    }

    public void Dispose() => _harness.Dispose();

    private void Seed()
    {
        // Seed clubs and users via admin context
        using var context = _harness.CreateAdminContext();

        // Create two clubs
        context.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = 1 },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = 1 });

        // Create users for Club A
        var clubAdminUser = new NovaUserEntity
        {
            Id = ClubAdminUserId,
            UserName = "admin@cluba.com",
            Email = "admin@cluba.com",
            FirstName = "Club",
            LastName = "Admin",
            ClubId = ClubAId
        };

        var member1User = new NovaUserEntity
        {
            Id = Member1UserId,
            UserName = "member1@cluba.com",
            Email = "member1@cluba.com",
            FirstName = "Member",
            LastName = "One",
            ClubId = ClubAId
        };

        var member2User = new NovaUserEntity
        {
            Id = Member2UserId,
            UserName = "member2@cluba.com",
            Email = "member2@cluba.com",
            FirstName = "Member",
            LastName = "Two",
            ClubId = ClubAId
        };

        // Create user for Club B
        var clubBAdminUser = new NovaUserEntity
        {
            Id = ClubBAdminUserId,
            UserName = "admin@clubb.com",
            Email = "admin@clubb.com",
            FirstName = "ClubB",
            LastName = "Admin",
            ClubId = ClubBId
        };

        context.Users.AddRange(clubAdminUser, member1User, member2User, clubBAdminUser);
        context.SaveChanges();

        _clubAdminUser = clubAdminUser;
        _member1User = member1User;
        _member2User = member2User;
        _clubBAdminUser = clubBAdminUser;
    }

    private ClubMemberService CreateService()
    {
        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var userManager = CreateUserManagerMock();

        return new ClubMemberService(
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);
    }

    private UserManager<NovaUserEntity> CreateUserManagerMock()
    {
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());

        // Setup FindByIdAsync to return users - use Arg.Any to catch all calls
        userManager.FindByIdAsync(ClubAdminUserId.ToString()).Returns(Task.FromResult(_clubAdminUser)!);
        userManager.FindByIdAsync(Member1UserId.ToString()).Returns(Task.FromResult(_member1User)!);
        userManager.FindByIdAsync(Member2UserId.ToString()).Returns(Task.FromResult(_member2User)!);
        userManager.FindByIdAsync(ClubBAdminUserId.ToString()).Returns(Task.FromResult(_clubBAdminUser)!);
        userManager.FindByIdAsync(NonExistentUserId.ToString()).Returns(Task.FromResult((NovaUserEntity?)null));

        // Setup role checks - default all to not ClubAdmin
        userManager.IsInRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin).Returns(Task.FromResult(false));

        // Setup AddToRoleAsync to succeed for any user
        userManager.AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin)
            .Returns(Task.FromResult(IdentityResult.Success));

        return userManager;
    }

    #region GetClubMembersAsync Tests

    [Fact]
    public async Task GetClubMembersAsync_ReturnsForbidden_WhenUserNotAuthenticated()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetClubMembersAsync_ReturnsForbidden_WhenUserHasNoClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = 500; // Some user ID
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetClubMembersAsync_ReturnsOtherMembers_ExcludingCurrentUser()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var members = result.Value;
        members.Count.ShouldBe(2); // Member1 and Member2, but not the admin user
        members.ShouldContain(m => m.UserId == Member1UserId);
        members.ShouldContain(m => m.UserId == Member2UserId);
        members.ShouldNotContain(m => m.UserId == ClubAdminUserId);
    }

    [Fact]
    public async Task GetClubMembersAsync_ReturnsCorrectFullNames()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var members = result.Value;
        members.ShouldContain(m => m.FullName == "Member One");
        members.ShouldContain(m => m.FullName == "Member Two");
    }

    #endregion

    #region AssignClubAdminAsync Tests

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsForbidden_WhenActorNotAuthenticated()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.AssignClubAdminAsync(Member1UserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsForbidden_WhenActorHasNoClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        // Act
        var result = await service.AssignClubAdminAsync(Member1UserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsNotFound_WhenTargetUserNotFound()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.AssignClubAdminAsync(NonExistentUserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsForbidden_WhenTargetInDifferentClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.AssignClubAdminAsync(ClubBAdminUserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsTrue_WhenTargetAlreadyAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());

        userManager.FindByIdAsync(Member1UserId.ToString()).Returns(Task.FromResult(_member1User)!);
        // Member1 is already a ClubAdmin
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == Member1UserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(true));

        var service = new ClubMemberService(
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        var result = await service.AssignClubAdminAsync(Member1UserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsTrue_OnSuccessfulAssignment()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());

        userManager.FindByIdAsync(Member1UserId.ToString()).Returns(Task.FromResult(_member1User)!);
        // Member1 is not yet a ClubAdmin
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == Member1UserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(false));
        userManager.AddToRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == Member1UserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(IdentityResult.Success));

        var service = new ClubMemberService(
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        var result = await service.AssignClubAdminAsync(Member1UserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task AssignClubAdminAsync_ReturnsServerError_WhenAddToRoleAsyncFails()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());

        userManager.FindByIdAsync(Member1UserId.ToString()).Returns(Task.FromResult(_member1User)!);
        // Member1 is not yet a ClubAdmin
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == Member1UserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(false));
        // AddToRoleAsync fails
        var failedResult = IdentityResult.Failed(new IdentityError { Code = "RoleFailed", Description = "Role assignment failed." });
        userManager.AddToRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == Member1UserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(failedResult));

        var service = new ClubMemberService(
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        var result = await service.AssignClubAdminAsync(Member1UserId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldNotBeNullOrEmpty();
        result.Problem.Detail!.ShouldContain("Role assignment failed.");
    }

    #endregion
}
