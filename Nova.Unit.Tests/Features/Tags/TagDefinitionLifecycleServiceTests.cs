using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Covers tenant-safe tag-definition lifecycle transitions using the shared SQLite harness.
/// </summary>
public sealed class TagDefinitionLifecycleServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;
    private const long ClubBAdminId = 201;

    private const long ActiveTagId = 300;
    private const long ArchivedTagId = 301;

    private readonly TenancyTestHarness _harness = new();

    public TagDefinitionLifecycleServiceTests()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "A", LastName = "Admin", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "B", LastName = "Admin", ClubId = ClubBId });
        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                PlayerTagId = ActiveTagId,
                Name = "Forward",
                NormalizedName = "FORWARD",
                Color = "#FF0000",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ArchivedTagId,
                Name = "Goalkeeper",
                NormalizedName = "GOALKEEPER",
                Color = "#00FF00",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ArchivedById = ClubAAdminId
            });
        db.SaveChanges();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Archive_ArchivesTag_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().ArchiveAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        using var db = _harness.CreateAdminContext();
        var tag = db.PlayerTags.Single(t => t.PlayerTagId == ActiveTagId);
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        tag.ArchivedAt.ShouldNotBeNull();
        tag.ArchivedById.ShouldBe(ClubAAdminId);
    }

    [Fact]
    public async Task Archive_ReturnsConflict_WhenAlreadyArchived()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().ArchiveAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task Archive_ReturnsNotFound_ForCrossTenantTag()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().ArchiveAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task Archive_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().ArchiveAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task Restore_RestoresArchivedTag_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().RestoreAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        using var db = _harness.CreateAdminContext();
        var tag = db.PlayerTags.Single(t => t.PlayerTagId == ArchivedTagId);
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        tag.ArchivedAt.ShouldBeNull();
        tag.ArchivedById.ShouldBeNull();
    }

    [Fact]
    public async Task Restore_ReturnsConflict_WhenAlreadyActive()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().RestoreAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task Restore_ReturnsNotFound_ForCrossTenantTag()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().RestoreAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    private TagDefinitionLifecycleService CreateService()
        => new(
            new HarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

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
