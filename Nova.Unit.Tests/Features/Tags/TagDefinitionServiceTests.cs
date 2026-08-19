using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Covers tenant-safe tag-definition management behavior using the shared SQLite harness.
/// </summary>
public sealed class TagDefinitionServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;
    private const long ClubBAdminId = 201;

    private const long ClubATagId = 300;

    private readonly TenancyTestHarness _harness = new();

    public TagDefinitionServiceTests()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "A", LastName = "Admin", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "B", LastName = "Admin", ClubId = ClubBId });
        db.PlayerTags.Add(new PlayerTagEntity
        {
            PlayerTagId = ClubATagId,
            Name = "Forward",
            NormalizedName = "FORWARD",
            Color = "#FF0000",
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });
        db.SaveChanges();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Create_ReturnsActiveTag_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "Defender", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Defender");
        result.Value.Color.ShouldBe("#1A2B3C");
        result.Value.LifecycleStatus.ShouldBe(LifecycleStatus.Active);

        using var db = _harness.CreateAdminContext();
        var tag = db.PlayerTags.Single(t => t.PlayerTagId == result.Value.PlayerTagId);
        tag.NormalizedName.ShouldBe("DEFENDER");
    }

    [Fact]
    public async Task Create_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "Defender", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task Create_ReturnsForbidden_WhenCallerHasNoClub()
    {
        ActAs(ClubAAdminId, clubId: null, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "Defender", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("forward")]
    [InlineData("FORWARD")]
    [InlineData("Forward ")]
    public async Task Create_ReturnsConflict_ForDuplicateNameIgnoringCase(string name)
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = name, Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var db = _harness.CreateAdminContext();
        db.PlayerTags.Count(t => t.ClubId == ClubAId && t.NormalizedName == "FORWARD").ShouldBe(1);
    }

    [Fact]
    public async Task Create_Succeeds_ForSameNameInAnotherClub()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "Forward", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Forward");
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenActiveLimitReached()
    {
        using (var db = _harness.CreateAdminContext())
        {
            for (var i = 0; i < TagDefinitionLimits.MaxActiveTagDefinitions; i++)
            {
                db.PlayerTags.Add(new PlayerTagEntity
                {
                    Name = $"Limit Tag {i}",
                    NormalizedName = $"LIMIT TAG {i}",
                    Color = "#111111",
                    ClubId = ClubBId,
                    CreatedById = ClubBAdminId
                });
            }

            db.SaveChanges();
        }

        ActAs(ClubBAdminId, ClubBId, isAdmin: true);
        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "Overflow", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var verifyDb = _harness.CreateAdminContext();
        verifyDb.PlayerTags.Count(tag => tag.ClubId == ClubBId)
            .ShouldBe(TagDefinitionLimits.MaxActiveTagDefinitions);
    }

    [Fact]
    public async Task Create_IgnoresArchivedTags_WhenEnforcingActiveLimit()
    {
        using (var db = _harness.CreateAdminContext())
        {
            for (var i = 0; i < TagDefinitionLimits.MaxActiveTagDefinitions; i++)
            {
                db.PlayerTags.Add(new PlayerTagEntity
                {
                    Name = $"Archived Limit Tag {i}",
                    NormalizedName = $"ARCHIVED LIMIT TAG {i}",
                    Color = "#111111",
                    ClubId = ClubBId,
                    CreatedById = ClubBAdminId,
                    LifecycleStatus = LifecycleStatus.Archived,
                    ArchivedAt = DateTimeOffset.UtcNow,
                    ArchivedById = ClubBAdminId
                });
            }

            db.SaveChanges();
        }

        ActAs(ClubBAdminId, ClubBId, isAdmin: true);
        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "First Active", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Create_ReturnsValidation_ForInvalidColor()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTagDefinitionInput { Name = "Defender", Color = "red" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_ForCrossTenantTag()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTagDefinitionInput { TagId = ClubATagId, Name = "Changed", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task Update_Succeeds_ForActiveTag()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTagDefinitionInput { TagId = ClubATagId, Name = "Striker", Color = "#aabbcc" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Striker");
        result.Value.Color.ShouldBe("#AABBCC");
    }

    [Fact]
    public async Task Update_ReturnsConflict_ForArchivedTag()
    {
        using (var db = _harness.CreateAdminContext())
        {
            var tag = db.PlayerTags.Single(t => t.PlayerTagId == ClubATagId);
            tag.LifecycleStatus = LifecycleStatus.Archived;
            tag.ArchivedAt = DateTimeOffset.UtcNow;
            tag.ArchivedById = ClubAAdminId;
            db.SaveChanges();
        }

        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var result = await CreateService().UpdateAsync(
            new UpdateTagDefinitionInput { TagId = ClubATagId, Name = "Changed", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task Update_ReturnsConflict_WhenRenamingOntoExistingTag()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var service = CreateService();

        var created = await service.CreateAsync(
            new CreateTagDefinitionInput { Name = "NewTag", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue();

        var result = await service.UpdateAsync(
            new UpdateTagDefinitionInput { TagId = created.Value.PlayerTagId, Name = "Forward", Color = "#1a2b3c" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var db = _harness.CreateAdminContext();
        db.PlayerTags.Single(t => t.PlayerTagId == created.Value.PlayerTagId).Name.ShouldBe("NewTag");
    }

    private TagDefinitionService CreateService()
        => new(
            new HarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<TagDefinitionService>.Instance);

    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    private sealed class HarnessDbContextFactory(TenancyTestHarness harness)
        : IDbContextFactory<NovaDbContext>
    {
        public NovaDbContext CreateDbContext() => harness.CreateTenantContext();

        public Task<NovaDbContext> CreateDbContextAsync(CancellationToken _ = default)
            => Task.FromResult(harness.CreateTenantContext());
    }
}
