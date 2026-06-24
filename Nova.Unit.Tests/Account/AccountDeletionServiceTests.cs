using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Account;
using Nova.Shared.Account;
using Nova.Shared.Security;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// Tests for <see cref="AccountDeletionService.GetDeletionPreviewAsync"/> and <see cref="AccountDeletionService.DeleteAccountAsync"/>.
/// </summary>
public class AccountDeletionServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long AdminUserId = 200;
    private const long NonAdminUserId = 201;
    private const long SecondAdminUserId = 202;
    private const long UnauthenticatedUserId = -1;

    private readonly TenancyTestHarness _harness = new();
    private readonly ILogger<AccountDeletionService> _mockLogger;
    private NovaUserEntity? _adminUser;
    private NovaUserEntity? _nonAdminUser;
    private NovaUserEntity? _secondAdminUser;

    public AccountDeletionServiceTests()
    {
        _mockLogger = Substitute.For<ILogger<AccountDeletionService>>();
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

        // Create users
        var adminUser = new NovaUserEntity
        {
            Id = AdminUserId,
            UserName = "admin@club.com",
            Email = "admin@club.com",
            FirstName = "Admin",
            LastName = "User",
            ClubId = ClubAId
        };

        var nonAdminUser = new NovaUserEntity
        {
            Id = NonAdminUserId,
            UserName = "member@club.com",
            Email = "member@club.com",
            FirstName = "Member",
            LastName = "User",
            ClubId = ClubAId
        };

        var secondAdminUser = new NovaUserEntity
        {
            Id = SecondAdminUserId,
            UserName = "secondadmin@club.com",
            Email = "secondadmin@club.com",
            FirstName = "SecondAdmin",
            LastName = "User",
            ClubId = ClubAId
        };

        context.Users.AddRange(adminUser, nonAdminUser, secondAdminUser);
        context.SaveChanges();

        _adminUser = adminUser;
        _nonAdminUser = nonAdminUser;
        _secondAdminUser = secondAdminUser;
    }

    private AccountDeletionService CreateService()
    {
        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var adminDbFactory = new TestDbContextFactory<NovaAdminDbContext>(() => _harness.CreateAdminContext());
        var userManagerMock = CreateUserManagerMock();

        return new AccountDeletionService(
            adminDbFactory,
            readDbFactory,
            userManagerMock,
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

        // Setup FindByIdAsync to return users
        userManager.FindByIdAsync(AdminUserId.ToString()).Returns(Task.FromResult(_adminUser)!);
        userManager.FindByIdAsync(NonAdminUserId.ToString()).Returns(Task.FromResult(_nonAdminUser)!);
        userManager.FindByIdAsync(SecondAdminUserId.ToString()).Returns(Task.FromResult(_secondAdminUser)!);

        // Configure role checks - use Arg.Is to match exact users
        if (_adminUser != null)
        {
            userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == AdminUserId), Roles.ClubAdmin)
                .Returns(Task.FromResult(true));
        }

        if (_nonAdminUser != null)
        {
            userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == NonAdminUserId), Roles.ClubAdmin)
                .Returns(Task.FromResult(false));
        }

        if (_secondAdminUser != null)
        {
            userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == SecondAdminUserId), Roles.ClubAdmin)
                .Returns(Task.FromResult(true));
        }

        // Setup GetUsersInRoleAsync to return all admins by default
        var defaultAdmins = new List<NovaUserEntity>();
        if (_adminUser != null)
            defaultAdmins.Add(_adminUser);
        if (_secondAdminUser != null)
            defaultAdmins.Add(_secondAdminUser);

        userManager.GetUsersInRoleAsync(Roles.ClubAdmin)
            .Returns(Task.FromResult((IList<NovaUserEntity>)defaultAdmins));

        // Setup DeleteAsync to succeed by default
        userManager.DeleteAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.FromResult(IdentityResult.Success));

        return userManager;
    }

    #region GetDeletionPreviewAsync Tests

    [Fact]
    public async Task GetDeletionPreviewAsync_ReturnsNoClubOrNonAdmin_WhenUserNotAuthenticated()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        // Act
        var result = await service.GetDeletionPreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
        result.ClubName.ShouldBeNull();
        result.OtherMemberCount.ShouldBeNull();
    }

    [Fact]
    public async Task GetDeletionPreviewAsync_ReturnsNoClubOrNonAdmin_WhenUserNotFound()
    {
        // Arrange
        _harness.CurrentUser.UserId = 999; // Non-existent user ID
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetDeletionPreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
        result.ClubName.ShouldBeNull();
        result.OtherMemberCount.ShouldBeNull();
    }

    [Fact]
    public async Task GetDeletionPreviewAsync_ReturnsNoClubOrNonAdmin_WhenUserNotClubAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = NonAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetDeletionPreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
        result.ClubName.ShouldBeNull();
        result.OtherMemberCount.ShouldBeNull();
    }

    [Fact]
    public async Task GetDeletionPreviewAsync_ReturnsOnlyClubMember_WhenUserIsOnlyMemberOfClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var adminDbFactory = new TestDbContextFactory<NovaAdminDbContext>(() => _harness.CreateAdminContext());

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

        userManager.FindByIdAsync(AdminUserId.ToString()).Returns(Task.FromResult(_adminUser)!);
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == AdminUserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(true));
        // Only admin user is a ClubAdmin in Club A
        userManager.GetUsersInRoleAsync(Roles.ClubAdmin)
            .Returns(Task.FromResult((IList<NovaUserEntity>)new List<NovaUserEntity> { _adminUser! }));

        // Need to seed Club A with only this admin user (no other members)
        using (var context = _harness.CreateAdminContext())
        {
            context.Users.RemoveRange(context.Users.Where(u => u.Id == NonAdminUserId || u.Id == SecondAdminUserId));
            context.SaveChanges();
        }

        var service = new AccountDeletionService(
            adminDbFactory,
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        var result = await service.GetDeletionPreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Scenario.ShouldBe(AccountDeletionScenario.OnlyClubMember);
        result.ClubName.ShouldNotBeNullOrEmpty();
        result.ClubName.ShouldBe("Club A");
        result.OtherMemberCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetDeletionPreviewAsync_ReturnsSoleClubAdmin_WhenUserIsOnlyAdminButOtherMembersExist()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var adminDbFactory = new TestDbContextFactory<NovaAdminDbContext>(() => _harness.CreateAdminContext());

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

        userManager.FindByIdAsync(AdminUserId.ToString()).Returns(Task.FromResult(_adminUser)!);
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == AdminUserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(true));
        // Only admin user is a ClubAdmin in Club A (other members are not admins)
        userManager.GetUsersInRoleAsync(Roles.ClubAdmin)
            .Returns(Task.FromResult((IList<NovaUserEntity>)new List<NovaUserEntity> { _adminUser! }));

        var service = new AccountDeletionService(
            adminDbFactory,
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        var result = await service.GetDeletionPreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Scenario.ShouldBe(AccountDeletionScenario.SoleClubAdmin);
        result.ClubName.ShouldNotBeNullOrEmpty();
        result.ClubName.ShouldBe("Club A");
        result.OtherMemberCount.ShouldBe(2); // nonAdminUser and secondAdminUser
    }

    [Fact]
    public async Task GetDeletionPreviewAsync_ReturnsNoClubOrNonAdmin_WhenAnotherAdminExistsInClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var adminDbFactory = new TestDbContextFactory<NovaAdminDbContext>(() => _harness.CreateAdminContext());

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

        userManager.FindByIdAsync(AdminUserId.ToString()).Returns(Task.FromResult(_adminUser)!);
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == AdminUserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(true));
        // Both admin and secondAdmin are ClubAdmins in Club A
        userManager.GetUsersInRoleAsync(Roles.ClubAdmin)
            .Returns(Task.FromResult((IList<NovaUserEntity>)new List<NovaUserEntity> { _adminUser!, _secondAdminUser! }));

        var service = new AccountDeletionService(
            adminDbFactory,
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        var result = await service.GetDeletionPreviewAsync(TestContext.Current.CancellationToken);

        // Assert
        result.Scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
        result.ClubName.ShouldBeNull();
        result.OtherMemberCount.ShouldBeNull();
    }

    #endregion

    #region DeleteAccountAsync Tests

    [Fact]
    public async Task DeleteAccountAsync_DeletesClubAndUser_WhenOnlyClubMember()
    {
        // Arrange
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var adminDbFactory = new TestDbContextFactory<NovaAdminDbContext>(() => _harness.CreateAdminContext());

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

        userManager.FindByIdAsync(AdminUserId.ToString()).Returns(Task.FromResult(_adminUser)!);
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == AdminUserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(true));
        // Only admin is a ClubAdmin
        userManager.GetUsersInRoleAsync(Roles.ClubAdmin)
            .Returns(Task.FromResult((IList<NovaUserEntity>)new List<NovaUserEntity> { _adminUser! }));
        // DeleteAsync succeeds
        userManager.DeleteAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.FromResult(IdentityResult.Success));

        // Remove other members so admin is the only one
        using (var context = _harness.CreateAdminContext())
        {
            context.Users.RemoveRange(context.Users.Where(u => u.Id == NonAdminUserId || u.Id == SecondAdminUserId));
            context.SaveChanges();
        }

        var service = new AccountDeletionService(
            adminDbFactory,
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act & Assert
        await service.DeleteAccountAsync(TestContext.Current.CancellationToken);

        // Verify DeleteAsync was called
        await userManager.Received(1).DeleteAsync(Arg.Is<NovaUserEntity>(u => u.Id == AdminUserId));

        // Verify club was removed
        using var finalContext = _harness.CreateAdminContext();
        var club = await finalContext.Clubs.FindAsync(new object[] { ClubAId }, cancellationToken: TestContext.Current.CancellationToken);
        club.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteAccountAsync_DeletesUserOnly_WhenNoClubOrNonAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = NonAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var readDbFactory = new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        var adminDbFactory = new TestDbContextFactory<NovaAdminDbContext>(() => _harness.CreateAdminContext());

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

        userManager.FindByIdAsync(NonAdminUserId.ToString()).Returns(Task.FromResult(_nonAdminUser)!);
        userManager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == NonAdminUserId), Roles.ClubAdmin)
            .Returns(Task.FromResult(false));
        userManager.GetUsersInRoleAsync(Roles.ClubAdmin)
            .Returns(Task.FromResult((IList<NovaUserEntity>)new List<NovaUserEntity> { _adminUser! }));
        // DeleteAsync succeeds
        userManager.DeleteAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var service = new AccountDeletionService(
            adminDbFactory,
            readDbFactory,
            userManager,
            _harness.CurrentUser,
            _mockLogger);

        // Act
        await service.DeleteAccountAsync(TestContext.Current.CancellationToken);

        // Assert
        // Verify DeleteAsync was called
        await userManager.Received(1).DeleteAsync(Arg.Is<NovaUserEntity>(u => u.Id == NonAdminUserId));

        // Verify club still exists (not deleted)
        using var finalContext = _harness.CreateAdminContext();
        var club = await finalContext.Clubs.FindAsync(new object[] { ClubAId }, cancellationToken: TestContext.Current.CancellationToken);
        club.ShouldNotBeNull();
    }

    #endregion
}
