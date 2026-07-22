using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="PlayerService.GetPlayerRosterAsync(GetPlayerRosterInput, CancellationToken)"/>.
/// </summary>
public sealed class PlayerServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAUserId = 200;
    private const long ClubBUserId = 201;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded test data.
    /// </summary>
    public PlayerServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsForbidden_WhenCurrentUserDoesNotBelongToRequestedClub()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = CreateService();
        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = ClubBId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsOnlyActiveClubPlayers_OrderedByDisplayNameByDefault()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = CreateService();
        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = ClubAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(3);
        result.Value.Items.Select(player => player.DisplayName).ToList()
            .ShouldBe(["Amy Adams", "Bobby Brown", "Casey Clark"]);
    }

    [Fact]
    public async Task GetPlayerRosterAsync_AppliesCaseInsensitiveContainsSearch()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = CreateService();
        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = ClubAId, Search = "bRoW" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().DisplayName.ShouldBe("Bobby Brown");
    }

    [Fact]
    public async Task GetPlayerRosterAsync_SortsByJoinedAtDescending_WhenRequested()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = CreateService();
        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = ClubAId, SortBy = "joinedAt", SortDirection = "desc" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(player => player.DisplayName).ToList()
            .ShouldBe(["Casey Clark", "Bobby Brown", "Amy Adams"]);
    }

    [Fact]
    public async Task GetPlayerRosterAsync_AppliesPagination()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = CreateService();
        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = ClubAId, Page = 2, PageSize = 1 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(2);
        result.Value.PageSize.ShouldBe(1);
        result.Value.TotalCount.ShouldBe(3);
        result.Value.Items.Count.ShouldBe(1);
        result.Value.Items.Single().DisplayName.ShouldBe("Bobby Brown");
    }

    private PlayerService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new PlayerService(readDbFactory, _harness.CurrentUser, NullLogger<PlayerService>.Instance);
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAUserId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBUserId });

        db.Players.AddRange(
            new PlayerEntity
            {
                ClubId = ClubAId,
                FirstName = "Amy",
                LastName = "Adams",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = 2028,
                CreatedById = ClubAUserId
            },
            new PlayerEntity
            {
                ClubId = ClubAId,
                FirstName = "Bobby",
                LastName = "Brown",
                DateOfBirth = new DateOnly(2011, 1, 1),
                GraduationYear = 2029,
                CreatedById = ClubAUserId
            },
            new PlayerEntity
            {
                ClubId = ClubAId,
                FirstName = "Casey",
                LastName = "Clark",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                CreatedById = ClubAUserId
            },
            new PlayerEntity
            {
                ClubId = ClubAId,
                FirstName = "Archie",
                LastName = "Archived",
                DateOfBirth = new DateOnly(2009, 1, 1),
                GraduationYear = 2027,
                CreatedById = ClubAUserId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero),
                ArchivedById = ClubAUserId
            },
            new PlayerEntity
            {
                ClubId = ClubBId,
                FirstName = "Blake",
                LastName = "Bishop",
                DateOfBirth = new DateOnly(2010, 5, 5),
                GraduationYear = 2028,
                CreatedById = ClubBUserId
            });

        db.SaveChanges();

        var amy = db.Players.Single(player => player.ClubId == ClubAId && player.FirstName == "Amy");
        var bobby = db.Players.Single(player => player.ClubId == ClubAId && player.FirstName == "Bobby");
        var casey = db.Players.Single(player => player.ClubId == ClubAId && player.FirstName == "Casey");
        amy.CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        bobby.CreatedAt = new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
        casey.CreatedAt = new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
        db.SaveChanges();
    }
}
