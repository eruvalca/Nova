using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
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
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "A", LastName = "Admin", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "B", LastName = "Admin", ClubId = ClubBId });
        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ActiveTagId,
                Name = "Forward",
                NormalizedName = "FORWARD",
                Color = "#FF0000",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
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
    public async Task ArchiveArchivesTagForClubAdminAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().ArchiveAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        using var db = _harness.CreateAdminContext();
        var tag = (await db.PlayerTags.SingleAsync(t => t.PlayerTagId == ActiveTagId, TestContext.Current.CancellationToken));
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        tag.ArchivedAt.ShouldNotBeNull();
        tag.ArchivedById.ShouldBe(ClubAAdminId);
    }

    [Fact]
    public async Task ArchiveReturnsConflictWhenAlreadyArchivedAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().ArchiveAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ArchiveReturnsNotFoundForCrossTenantTagAsync()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().ArchiveAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task ArchiveReturnsForbiddenForNonAdminAsync()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().ArchiveAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task RestoreRestoresArchivedTagForClubAdminAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().RestoreAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        using var db = _harness.CreateAdminContext();
        var tag = (await db.PlayerTags.SingleAsync(t => t.PlayerTagId == ArchivedTagId, TestContext.Current.CancellationToken));
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        tag.ArchivedAt.ShouldBeNull();
        tag.ArchivedById.ShouldBeNull();
    }

    [Fact]
    public async Task RestoreReturnsConflictWhenAlreadyActiveAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().RestoreAsync(ActiveTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task RestoreReturnsConflictWhenActiveLimitReachedAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        using (var seed = _harness.CreateAdminContext())
        {
            // The constructor seeds one active tag; fill the remainder of the active-definition cap.
            for (var i = 0; i < TagDefinitionLimits.MaxActiveTagDefinitions - 1; i++)
            {
                seed.PlayerTags.Add(new PlayerTagEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    PlayerTagId = 1000 + i,
                    Name = $"Cap {i}",
                    NormalizedName = $"CAP {i}",
                    Color = "#000000",
                    ClubId = ClubAId,
                    CreatedById = ClubAAdminId
                });
            }
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().RestoreAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var db = _harness.CreateAdminContext();
        var tag = (await db.PlayerTags.SingleAsync(t => t.PlayerTagId == ArchivedTagId, TestContext.Current.CancellationToken));
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
    }

    [Fact]
    public async Task RestoreReturnsForbiddenForNonAdminAsync()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().RestoreAsync(ArchivedTagId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task RestoreReturnsNotFoundForCrossTenantTagAsync()
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

        public Task<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(harness.CreateTenantContext());
    }
}
