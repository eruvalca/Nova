using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Tests for <see cref="TagDefinitionQueryService"/> ordering, authorization, filtering,
/// and tenant isolation using the shared SQLite tenancy harness.
/// </summary>
public sealed class TagDefinitionQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;
    private const long ClubBMemberId = 201;

    private const long ClubAForwardTagId = 300;
    private const long ClubADefenderTagId = 301;
    private const long ClubAGoalkeeperTagId = 302;
    private const long ClubBForwardTagId = 400;

    private readonly TenancyTestHarness _harness = new();

    public TagDefinitionQueryServiceTests() => Seed();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetManagementListAsync_ReturnsAll_WhenNoFilter()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Defender", "Forward", "Goalkeeper"]);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsOnlyActive_WhenActiveFilter()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput { LifecycleStatus = "active" }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Defender", "Forward"]);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsOnlyArchived_WhenArchivedFilter()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput { LifecycleStatus = "archived" }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Goalkeeper"]);
    }

    [Fact]
    public async Task GetManagementListAsync_FiltersCaseInsensitiveSearch()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput { Search = "WARD" }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Forward"]);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsForbidden_WhenCallerHasNoClub()
    {
        ActAs(ClubAAdminId, clubId: null, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetChoicesAsync_ReturnsOnlyActive_ForClubMember()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Defender", "Forward"]);
        result.Value.ShouldAllBe(tag => tag.LifecycleStatus == LifecycleStatus.Active);
    }

    [Fact]
    public async Task GetChoicesAsync_ReturnsForbidden_WhenCallerHasNoClub()
    {
        ActAs(ClubAMemberId, clubId: null, isAdmin: false);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetChoicesAsync_ExcludesCrossTenantTags()
    {
        ActAs(ClubBMemberId, ClubBId, isAdmin: false);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Forward"]);
        result.Value.Single().PlayerTagId.ShouldBe(ClubBForwardTagId);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsAtMostHundredRows_WhenClubHasMore()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        SeedExtraActiveTags(150);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(TagDefinitionLimits.MaxTagDefinitions);
    }

    [Fact]
    public async Task GetChoicesAsync_ReturnsAtMostHundredActiveRows_WhenClubHasMore()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        SeedExtraActiveTags(150);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(TagDefinitionLimits.MaxTagDefinitions);
        result.Value.ShouldAllBe(tag => tag.LifecycleStatus == LifecycleStatus.Active);
    }

    private void SeedExtraActiveTags(int count)
    {
        using var db = _harness.CreateAdminContext();
        for (var i = 0; i < count; i++)
        {
            db.PlayerTags.Add(new PlayerTagEntity
            {
                PlayerTagId = 1000 + i,
                Name = $"Bound{i}",
                NormalizedName = $"BOUND{i}",
                Color = "#AABBCC",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });
        }

        db.SaveChanges();
    }

    private TagDefinitionQueryService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new TagDefinitionQueryService(
            readDbFactory,
            _harness.CurrentUser,
            NullLogger<TagDefinitionQueryService>.Instance);
    }

    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "A", LastName = "Admin", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "B", LastName = "Member", ClubId = ClubBId });
        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                PlayerTagId = ClubAForwardTagId,
                Name = "Forward",
                NormalizedName = "FORWARD",
                Color = "#FF0000",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ClubADefenderTagId,
                Name = "Defender",
                NormalizedName = "DEFENDER",
                Color = "#00FF00",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ClubAGoalkeeperTagId,
                Name = "Goalkeeper",
                NormalizedName = "GOALKEEPER",
                Color = "#0000FF",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ArchivedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ClubBForwardTagId,
                Name = "Forward",
                NormalizedName = "FORWARD",
                Color = "#FF00FF",
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.SaveChanges();
    }
}
