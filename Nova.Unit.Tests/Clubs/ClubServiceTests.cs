using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Direct SQLite shell tests for <see cref="ClubService"/>: club search projection and club creation
/// authorization, role assignment, and error mapping.
/// </summary>
public sealed class ClubServiceTests : IDisposable
{
    private const long NoClubUserId = 200;
    private const long ExistingClubUserId = 201;

    private readonly TenancyTestHarness _harness = new();
    private UserManager<NovaUserEntity> _userManager = null!;

    /// <summary>
    /// Initializes the mocked <see cref="UserManager{TUser}"/> and seeded club data.
    /// </summary>
    public ClubServiceTests()
    {
        _userManager = CreateUserManagerMock();
        Seed();
    }

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task SearchClubsAsync_ReturnsAllClubsOrderedByName_WhenQueryIsBlank()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SearchClubsAsync(null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(club => club.Name).ShouldBe(["Alpha Club", "Beta Club", "Gamma Club"]);
    }

    [Fact]
    public async Task SearchClubsAsync_MatchesCaseInsensitively_AcrossNameCityAndState()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SearchClubsAsync("AUSTIN", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(club => club.Name).ShouldBe(["Alpha Club"]);
    }

    [Fact]
    public async Task SearchClubsAsync_TreatsLikeMetacharactersAsLiterals()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;

        await using (var seed = _harness.CreateAdminContext())
        {
            seed.Clubs.AddRange(
                new ClubEntity { Name = "50% Wins", City = "Dallas", State = "TX", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "50 Losses", City = "Erie", State = "PA", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "a_b Squad", City = "Fargo", State = "ND", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "axb Squad", City = "Tulsa", State = "OK", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = @"Path\Team", City = "Boise", State = "ID", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "PathTeam", City = "Reno", State = "NV", CreatedById = ExistingClubUserId });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        var percent = await service.SearchClubsAsync("50%", TestContext.Current.CancellationToken);
        percent.Value.Select(club => club.Name).ShouldBe(["50% Wins"]);

        var underscore = await service.SearchClubsAsync("a_b", TestContext.Current.CancellationToken);
        underscore.Value.Select(club => club.Name).ShouldBe(["a_b Squad"]);

        var backslash = await service.SearchClubsAsync(@"Path\T", TestContext.Current.CancellationToken);
        backslash.Value.Select(club => club.Name).ShouldBe([@"Path\Team"]);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsValidation_WhenInputIsInvalid()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "   ", City = "Austin", State = "TX" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsConflict_WhenUserAlreadyBelongsToClub()
    {
        _harness.CurrentUser.UserId = ExistingClubUserId;
        _harness.CurrentUser.ClubId = 1;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "New Club", City = "Austin", State = "TX" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsForbidden_WhenNotAuthenticated()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "New Club", City = "Austin", State = "TX" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsServerError_WhenUserNotFound()
    {
        _harness.CurrentUser.UserId = 999_999;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "New Club", City = "Austin", State = "TX" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task CreateClubAsync_CreatesClub_AndAssignsMembership()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult((NovaUserEntity?)null));
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Created Club", City = "Austin", State = "TX" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Created Club");

        await using var verify = _harness.CreateAdminContext();
        var club = await verify.Clubs
            .SingleAsync(candidate => candidate.Name == "Created Club", TestContext.Current.CancellationToken);
        club.City.ShouldBe("Austin");
        club.State.ShouldBe("TX");

        var user = await verify.Users
            .SingleAsync(candidate => candidate.Id == NoClubUserId, TestContext.Current.CancellationToken);
        user.ClubId.ShouldBe(club.ClubId);
    }

    [Fact]
    public async Task CreateClubAsync_AssignsClubAdminRole_WhenRoleAssignmentSucceeds()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var user = await LoadUserAsync(NoClubUserId);
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult(user));
        _userManager.AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin)
            .Returns(Task.FromResult(IdentityResult.Success));
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Role Club", City = "Austin", State = "TX" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _userManager.Received().AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin);
    }

    private ClubService CreateService()
        => new(
            new TestDbContextFactory<NovaAdminDbContext>(_harness.CreateAdminContext),
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _userManager,
            _harness.CurrentUser,
            NullLogger<ClubService>.Instance);

    private async Task<NovaUserEntity?> LoadUserAsync(long userId)
    {
        await using var db = _harness.CreateAdminContext();
        return await db.Users.SingleAsync(
            candidate => candidate.Id == userId,
            TestContext.Current.CancellationToken);
    }

    private static UserManager<NovaUserEntity> CreateUserManagerMock()
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

        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult((NovaUserEntity?)null));
        userManager.AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin)
            .Returns(Task.FromResult(IdentityResult.Success));
        return userManager;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { ClubId = 1, Name = "Alpha Club", City = "Austin", State = "TX", CreatedById = ExistingClubUserId },
            new ClubEntity { ClubId = 2, Name = "Beta Club", City = "Boston", State = "MA", CreatedById = ExistingClubUserId },
            new ClubEntity { ClubId = 3, Name = "Gamma Club", City = "Denver", State = "CO", CreatedById = ExistingClubUserId });
        db.SaveChanges();

        db.Users.AddRange(
            new NovaUserEntity
            {
                Id = NoClubUserId,
                UserName = "noclub@example.com",
                Email = "noclub@example.com",
                FirstName = "No",
                LastName = "Club",
                ClubId = null
            },
            new NovaUserEntity
            {
                Id = ExistingClubUserId,
                UserName = "member@example.com",
                Email = "member@example.com",
                FirstName = "Existing",
                LastName = "Member",
                ClubId = 1
            });

        db.SaveChanges();
    }
}
