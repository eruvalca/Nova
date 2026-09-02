using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

public sealed class ClubAdminServiceTests : IDisposable
{
    private readonly TenancyTestHarness _harness = new();

    public ClubAdminServiceTests()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.Add(new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = 1,
            Name = "Club",
            City = "Austin",
            State = "TX",
            CreatedById = 10,
        });
        db.Users.AddRange(
            new NovaUserEntity { Id = 10, FirstName = "Alice", LastName = "Admin", ClubId = 1 },
            new NovaUserEntity { Id = 11, FirstName = "Morgan", LastName = "Member", ClubId = 1 });
        db.SaveChanges();
        _harness.CurrentUser.UserId = 10;
        _harness.CurrentUser.ClubId = 1;
        _harness.CurrentUser.IsClubAdmin = true;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetClubAdminSummaryAsync_ReturnsCountsMetadataAndSoleAdminProjection()
    {
        var service = CreateService([new NovaUserEntity { Id = 10, FirstName = "Alice", LastName = "Admin", ClubId = 1 }]);

        var result = await service.GetClubAdminSummaryAsync(1, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(1);
        result.Value.Name.ShouldBe("Club");
        result.Value.City.ShouldBe("Austin");
        result.Value.State.ShouldBe("TX");
        result.Value.MemberCount.ShouldBe(2);
        result.Value.AdminCount.ShouldBe(1);
        result.Value.PendingJoinRequestCount.ShouldBe(0);
        result.Value.PlayerCount.ShouldBe(0);
        result.Value.IsCurrentUserSoleAdmin.ShouldBeTrue();
        result.Value.HasCrest.ShouldBeFalse();
    }

    [Fact]
    public async Task GetClubRosterAsync_ReturnsClubMembers()
    {
        var service = CreateService([new NovaUserEntity { Id = 10, FirstName = "Alice", LastName = "Admin", ClubId = 1 }]);

        var result = await service.GetClubRosterAsync(1, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Single(member => member.UserId == 10).IsClubAdmin.ShouldBeTrue();
    }

    [Fact]
    public async Task GetClubRosterAsync_ReturnsForbiddenForNonAdministrator()
    {
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService([]).GetClubRosterAsync(1, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    private ClubAdminService CreateService(IList<NovaUserEntity> administrators)
    {
        IDbContextFactory<NovaReadDbContext> factory = new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
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
        manager.GetUsersInRoleAsync(Roles.ClubAdmin).Returns(administrators);
        return new ClubAdminService(factory, manager, _harness.CurrentUser, NullLogger<ClubAdminService>.Instance);
    }
}
