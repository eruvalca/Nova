using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
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
    public async Task GetManagementListAsyncReturnsAllWhenNoFilterAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(tag => tag.Name).ShouldBe(["Defender", "Forward", "Goalkeeper"]);
        result.Value.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task GetManagementListAsyncReturnsOnlyActiveWhenActiveFilterAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput { LifecycleStatus = "active" }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(tag => tag.Name).ShouldBe(["Defender", "Forward"]);
    }

    [Fact]
    public async Task GetManagementListAsyncReturnsOnlyArchivedWhenArchivedFilterAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput { LifecycleStatus = "archived" }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(tag => tag.Name).ShouldBe(["Goalkeeper"]);
    }

    [Fact]
    public async Task GetManagementListAsyncFiltersCaseInsensitiveSearchAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput { Search = "WARD" }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(tag => tag.Name).ShouldBe(["Forward"]);
    }

    [Fact]
    public async Task GetManagementListAsyncReturnsForbiddenForNonAdminAsync()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetManagementListAsyncReturnsForbiddenWhenCallerHasNoClubAsync()
    {
        ActAs(ClubAAdminId, clubId: null, isAdmin: true);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetChoicesAsyncReturnsOnlyActiveForClubMemberAsync()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Defender", "Forward"]);
        result.Value.ShouldAllBe(tag => tag.LifecycleStatus == LifecycleStatus.Active);
    }

    [Fact]
    public async Task GetChoicesAsyncReturnsForbiddenWhenCallerHasNoClubAsync()
    {
        ActAs(ClubAMemberId, clubId: null, isAdmin: false);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetChoicesAsyncExcludesCrossTenantTagsAsync()
    {
        ActAs(ClubBMemberId, ClubBId, isAdmin: false);

        var result = await CreateService().GetChoicesAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(tag => tag.Name).ShouldBe(["Forward"]);
        result.Value.Single().PlayerTagId.ShouldBe(ClubBForwardTagId);
    }

    [Fact]
    public async Task GetManagementListAsyncReturnsAtMostHundredRowsWhenClubHasMoreAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        SeedExtraActiveTags(150);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(TagDefinitionLimits.MaxTagDefinitions);
        result.Value.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task GetManagementListAsyncHasMoreIsFalseWhenExactlyAtTheCapAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        // Club A is seeded with three tags; topping up to exactly the cap must not set HasMore.
        SeedExtraActiveTags(TagDefinitionLimits.MaxTagDefinitions - 3);

        var result = await CreateService().GetManagementListAsync(
            new GetTagDefinitionsInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(TagDefinitionLimits.MaxTagDefinitions);
        result.Value.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task GetChoicesAsyncReturnsAtMostHundredActiveRowsWhenClubHasMoreAsync()
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
                CreationOperationId = Guid.NewGuid(),
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
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "A", LastName = "Admin", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "B", LastName = "Member", ClubId = ClubBId });
        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ClubAForwardTagId,
                Name = "Forward",
                NormalizedName = "FORWARD",
                Color = "#FF0000",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ClubADefenderTagId,
                Name = "Defender",
                NormalizedName = "DEFENDER",
                Color = "#00FF00",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
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
                CreationOperationId = Guid.NewGuid(),
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
