using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="ClubDetailService.GetClubDetailAsync(long, CancellationToken)"/>.
/// </summary>
public class ClubDetailServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAUserId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBUserId = 202;
    private const long MissingClubId = 999;

    private readonly TenancyTestHarness _harness = new();

    public ClubDetailServiceTests()
    {
        Seed();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetClubDetailAsync_ReturnsForbidden_WhenUserIsNotAuthenticated()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetClubDetailAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetClubDetailAsync_ReturnsForbidden_WhenAuthenticatedUserIsOutsideRequestedClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        // Act
        var result = await service.GetClubDetailAsync(ClubBId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetClubDetailAsync_ReturnsNotFound_WhenRequestedClubDoesNotExist()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = MissingClubId;
        var service = CreateService();

        // Act
        var result = await service.GetClubDetailAsync(MissingClubId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task GetClubDetailAsync_ReturnsClubDetailAndRoster_WhenUserBelongsToRequestedClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;
        var service = CreateService();

        // Act
        var result = await service.GetClubDetailAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(ClubAId);
        result.Value.Name.ShouldBe("Club A");
        result.Value.IsCurrentUserClubAdmin.ShouldBeTrue();

        result.Value.Members.Select(m => m.UserId).ToList().ShouldBe([ClubAUserId, ClubAMemberId]);
        result.Value.Members.Single(m => m.UserId == ClubAUserId).IsCurrentUser.ShouldBeTrue();
        result.Value.Members.Single(m => m.UserId == ClubAMemberId).IsCurrentUser.ShouldBeFalse();
        result.Value.Members.ShouldNotContain(m => m.UserId == ClubBUserId);
    }

    [Fact]
    public async Task GetClubDetailAsync_ReturnsNonAdminFlag_WhenCurrentUserIsNotClubAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;
        var service = CreateService();

        // Act
        var result = await service.GetClubDetailAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.IsCurrentUserClubAdmin.ShouldBeFalse();
    }

    private ClubDetailService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext());
        return new ClubDetailService(readDbFactory, _harness.CurrentUser, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClubDetailService>.Instance);
    }

    private void Seed()
    {
        using var context = _harness.CreateAdminContext();
        context.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAUserId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBUserId });

        context.Users.AddRange(
            new NovaUserEntity { Id = ClubAUserId, FirstName = "Alice", LastName = "Alpha", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Brenda", LastName = "Beta", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBUserId, FirstName = "Aaron", LastName = "Other", ClubId = ClubBId });

        context.SaveChanges();
    }
}
