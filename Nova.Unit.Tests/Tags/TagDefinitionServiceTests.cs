using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Tags;

/// <summary>
/// Covers club-scoped tag-definition management and archive visibility rules.
/// </summary>
public sealed class TagDefinitionServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;
    private const long ClubBAdminId = 201;

    private readonly TenancyTestHarness _harness = new();

    public TagDefinitionServiceTests()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Member", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });
        db.SaveChanges();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Create_ReturnsConflict_ForDuplicateNameIgnoringCase()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var service = CreateService();

        var first = await service.CreateAsync(new CreateTagDefinitionInput { Name = " Skills ", Color = "#123456" }, TestContext.Current.CancellationToken);
        var second = await service.CreateAsync(new CreateTagDefinitionInput { Name = "skills", Color = "#abcdef" }, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        second.IsProblem.ShouldBeTrue();
        second.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task GetActive_ReturnsOnlyActiveDefinitions_ForClubMember()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        using (var db = _harness.CreateAdminContext())
        {
            db.PlayerTags.AddRange(
                new PlayerTagEntity { Name = "Active", NormalizedName = "ACTIVE", Color = "#123456", ClubId = ClubAId, LifecycleStatus = LifecycleStatus.Active, CreatedById = ClubAAdminId },
                new PlayerTagEntity { Name = "Archived", NormalizedName = "ARCHIVED", Color = "#abcdef", ClubId = ClubAId, LifecycleStatus = LifecycleStatus.Archived, CreatedById = ClubAAdminId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAAdminId });
            db.SaveChanges();
        }

        var result = await CreateService().GetActiveAsync(new GetTagDefinitionsInput { Limit = 10 }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(item => item.Name).ShouldBe(["Active"]);
    }

    private void ActAs(long userId, long clubId, bool isAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    private TagDefinitionService CreateService()
    {
        var dbFactory = new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext);
        var lifecycleService = new TagDefinitionLifecycleService(dbFactory, _harness.CurrentUser, NullLogger<TagDefinitionLifecycleService>.Instance);
        return new TagDefinitionService(dbFactory, _harness.CurrentUser, lifecycleService, NullLogger<TagDefinitionService>.Instance);
    }
}
