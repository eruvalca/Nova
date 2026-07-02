using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="ClubAdminService"/>, covering
/// <see cref="ClubAdminService.GetClubAdminSummaryAsync(long, CancellationToken)"/>,
/// <see cref="ClubAdminService.GetClubRosterAsync(long, CancellationToken)"/>, and
/// <see cref="ClubAdminService.DemoteClubAdminAsync(DemoteAdminInput, CancellationToken)"/>.
/// </summary>
public class ClubAdminServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long CurrentUserId = 200;
    private const long ClubAdminUserId = 201;
    private const long RegularMemberUserId = 202;
    private const long OtherClubAdminUserId = 203;

    private readonly TenancyTestHarness _harness = new();

    public ClubAdminServiceTests() => Seed();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetClubAdminSummaryAsync_ReturnsClubOverviewDataForCurrentClubAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var userManager = CreateUserManager([
            new NovaUserEntity { Id = CurrentUserId, FirstName = "Alice", LastName = "Alpha", ClubId = ClubAId }
        ]);
        var service = CreateService(userManager);

        // Act
        var result = await service.GetClubAdminSummaryAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value;
        summary.ClubId.ShouldBe(ClubAId);
        summary.Name.ShouldBe("Club A");
        summary.City.ShouldBe("Austin");
        summary.State.ShouldBe("TX");
        summary.MemberCount.ShouldBe(3);
        summary.AdminCount.ShouldBe(1);
        summary.PendingJoinRequestCount.ShouldBe(0);
        summary.PlayerCount.ShouldBe(0);
        summary.IsCurrentUserSoleAdmin.ShouldBeTrue();
    }

    [Fact]
    public async Task GetClubRosterAsync_ReturnsMembersWithComputedNamesAndAdminFlags()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var userManager = CreateUserManager([
            new NovaUserEntity { Id = ClubAdminUserId, FirstName = "Bob", LastName = "Beta", ClubId = ClubAId },
            new NovaUserEntity { Id = OtherClubAdminUserId, FirstName = "Darren", LastName = "Delta", ClubId = ClubBId }
        ]);
        var service = CreateService(userManager);

        // Act
        var result = await service.GetClubRosterAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var members = result.Value;
        members.Count.ShouldBe(3);
        members.Select(m => m.FullName).ShouldBe(["Alice Alpha", "Bob Beta", "Carol Gamma"]);
        members.Single(m => m.UserId == CurrentUserId).IsCurrentUser.ShouldBeTrue();
        members.Single(m => m.UserId == ClubAdminUserId).IsClubAdmin.ShouldBeTrue();
        members.Single(m => m.UserId == RegularMemberUserId).IsClubAdmin.ShouldBeFalse();
        members.Single(m => m.UserId == CurrentUserId).IsClubAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task GetClubRosterAsync_ReturnsForbidden_WhenUserIsNotClubAdminForRequestedClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var service = CreateService(CreateUserManager([]));

        // Act
        var result = await service.GetClubRosterAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsSuccessAndRemovesRole_WhenTargetIsAdminAndAnotherAdminRemains()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var target = new NovaUserEntity { Id = ClubAdminUserId, FirstName = "Bob", LastName = "Beta", ClubId = ClubAId };
        var otherAdmin = new NovaUserEntity { Id = 204, FirstName = "Dina", LastName = "Delta", ClubId = ClubAId };
        var userManager = CreateUserManager(
            [target, otherAdmin],
            new Dictionary<long, NovaUserEntity?> { [target.Id] = target },
            new Dictionary<long, bool> { [target.Id] = true });
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = target.Id }, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await userManager.Received().RemoveFromRoleAsync(target, Roles.ClubAdmin);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_AllowsSelfDemotion_WhenAnotherAdminRemains()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var target = new NovaUserEntity { Id = ClubAdminUserId, FirstName = "Bob", LastName = "Beta", ClubId = ClubAId };
        var otherAdmin = new NovaUserEntity { Id = 204, FirstName = "Dina", LastName = "Delta", ClubId = ClubAId };
        var userManager = CreateUserManager(
            [target, otherAdmin],
            new Dictionary<long, NovaUserEntity?> { [target.Id] = target },
            new Dictionary<long, bool> { [target.Id] = true });
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = target.Id }, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await userManager.Received().RemoveFromRoleAsync(target, Roles.ClubAdmin);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsConflictAndDoesNotRemoveRole_WhenTargetIsTheLastAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var target = new NovaUserEntity { Id = ClubAdminUserId, FirstName = "Bob", LastName = "Beta", ClubId = ClubAId };
        var userManager = CreateUserManager(
            [target],
            new Dictionary<long, NovaUserEntity?> { [target.Id] = target },
            new Dictionary<long, bool> { [target.Id] = true });
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = target.Id }, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await userManager.DidNotReceive().RemoveFromRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var service = CreateService(CreateUserManager([]));

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = ClubAdminUserId }, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsForbidden_WhenTargetBelongsToAnotherClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var target = new NovaUserEntity { Id = OtherClubAdminUserId, FirstName = "Darren", LastName = "Delta", ClubId = ClubBId };
        var userManager = CreateUserManager(
            [],
            new Dictionary<long, NovaUserEntity?> { [target.Id] = target });
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = target.Id }, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsNotFound_WhenTargetDoesNotExist()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var userManager = CreateUserManager([], new Dictionary<long, NovaUserEntity?> { [999] = null });
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = 999 }, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsSuccessAsNoOp_WhenTargetIsNotAnAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var target = new NovaUserEntity { Id = RegularMemberUserId, FirstName = "Carol", LastName = "Gamma", ClubId = ClubAId };
        var userManager = CreateUserManager(
            [],
            new Dictionary<long, NovaUserEntity?> { [target.Id] = target },
            new Dictionary<long, bool> { [target.Id] = false });
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = target.Id }, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        await userManager.DidNotReceive().RemoveFromRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin);
    }

    [Fact]
    public async Task DemoteClubAdminAsync_ReturnsValidation_WhenTargetUserIdIsZero()
    {
        // Arrange
        _harness.CurrentUser.UserId = CurrentUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var userManager = CreateUserManager([]);
        var service = CreateService(userManager);

        // Act
        var result = await service.DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = 0 }, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        await userManager.DidNotReceiveWithAnyArgs().FindByIdAsync(default!);
    }

    private ClubAdminService CreateService(UserManager<NovaUserEntity> userManager)
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());

        userManager.UpdateSecurityStampAsync(Arg.Any<NovaUserEntity>())
            .Returns(Task.FromResult(IdentityResult.Success));

        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            null,
            null,
            null,
            null);

        var claimRefresher = new ClubMembershipClaimRefresher(userManager, signInManager);
        return new ClubAdminService(readDbFactory, userManager, _harness.CurrentUser, claimRefresher, NullLogger<ClubAdminService>.Instance);
    }

    private static UserManager<NovaUserEntity> CreateUserManager(
        IList<NovaUserEntity> clubAdmins,
        IReadOnlyDictionary<long, NovaUserEntity?>? usersById = null,
        IReadOnlyDictionary<long, bool>? roleMembershipByUserId = null)
    {
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var manager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<NovaUserEntity>(),
            Array.Empty<IUserValidator<NovaUserEntity>>(),
            Array.Empty<IPasswordValidator<NovaUserEntity>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<NovaUserEntity>>.Instance);

        manager.GetUsersInRoleAsync(Roles.ClubAdmin).Returns(Task.FromResult<IList<NovaUserEntity>>(clubAdmins));
        manager.RemoveFromRoleAsync(Arg.Any<NovaUserEntity>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        if (usersById is not null)
        {
            foreach (var user in usersById)
            {
                manager.FindByIdAsync(user.Key.ToString()).Returns(Task.FromResult(user.Value));
            }
        }

        if (roleMembershipByUserId is not null)
        {
            foreach (var roleMembership in roleMembershipByUserId)
            {
                manager.IsInRoleAsync(Arg.Is<NovaUserEntity>(u => u.Id == roleMembership.Key), Roles.ClubAdmin)
                    .Returns(Task.FromResult(roleMembership.Value));
            }
        }

        return manager;
    }

    private void Seed()
    {
        using var context = _harness.CreateAdminContext();

        context.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = CurrentUserId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = OtherClubAdminUserId });

        context.Users.AddRange(
            new NovaUserEntity { Id = CurrentUserId, FirstName = "Alice", LastName = "Alpha", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAdminUserId, FirstName = "Bob", LastName = "Beta", ClubId = ClubAId },
            new NovaUserEntity { Id = RegularMemberUserId, FirstName = "Carol", LastName = "Gamma", ClubId = ClubAId },
            new NovaUserEntity { Id = OtherClubAdminUserId, FirstName = "Darren", LastName = "Delta", ClubId = ClubBId });

        context.SaveChanges();
    }
}
