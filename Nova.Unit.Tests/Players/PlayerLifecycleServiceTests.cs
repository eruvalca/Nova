using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Direct SQLite shell tests for <see cref="PlayerLifecycleService"/>: administrator authorization,
/// tenant isolation, lifecycle mutation effects, and archive blockers.
/// </summary>
public sealed class PlayerLifecycleServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long ActivePlayerId = 300;
    private const long BlockedPlayerId = 301;
    private const long ArchivedPlayerId = 302;
    private const long ClubBPlayerId = 303;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded player lifecycle data for two clubs.
    /// </summary>
    public PlayerLifecycleServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ArchiveAsync_ReturnsForbidden_WhenActorIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);
        var service = CreateService();

        var result = await service.ArchiveAsync(ActivePlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsForbidden_WhenActorHasNoClub()
    {
        ActAs(ClubAAdminId, clubId: null, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ActivePlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsNotFound_WhenPlayerBelongsToOtherClub()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ClubBPlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task ArchiveAsync_ArchivesPlayer_AndSetsProvenance()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ActivePlayerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var player = await verify.Players
            .SingleAsync(candidate => candidate.PlayerId == ActivePlayerId, TestContext.Current.CancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        player.ArchivedAt.ShouldNotBeNull();
        player.ArchivedById.ShouldBe(ClubAAdminId);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsConflict_WhenPlayerAlreadyArchived()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ArchivedPlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsConflict_WhenUndecidedParticipationRemains()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(BlockedPlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail!.ShouldContain("Resolve every undecided active-campaign participation");

        await using var verify = _harness.CreateAdminContext();
        var player = await verify.Players
            .SingleAsync(candidate => candidate.PlayerId == BlockedPlayerId, TestContext.Current.CancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    [Fact]
    public async Task RestoreAsync_RestoresPlayer_AndClearsProvenance()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RestoreAsync(ArchivedPlayerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var player = await verify.Players
            .SingleAsync(candidate => candidate.PlayerId == ArchivedPlayerId, TestContext.Current.CancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        player.ArchivedAt.ShouldBeNull();
        player.ArchivedById.ShouldBeNull();
    }

    [Fact]
    public async Task RestoreAsync_ReturnsConflict_WhenPlayerAlreadyActive()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RestoreAsync(ActivePlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    private PlayerLifecycleService CreateService()
        => new(
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext),
            _harness.CurrentUser,
            NullLogger<PlayerLifecycleService>.Instance);

    private void ActAs(long userId, long? clubId, bool isClubAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Member", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });

        db.Players.AddRange(
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = ActivePlayerId,
                FirstName = "Active",
                LastName = "Player",
                DateOfBirth = new DateOnly(2011, 1, 1),
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = BlockedPlayerId,
                FirstName = "Blocked",
                LastName = "Player",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = 2028,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = ArchivedPlayerId,
                FirstName = "Archived",
                LastName = "Player",
                DateOfBirth = new DateOnly(2009, 1, 1),
                GraduationYear = 2027,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ArchivedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = ClubBPlayerId,
                FirstName = "ClubB",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.SaveChanges();

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Lifecycle Season",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Seasons.Add(season);
        db.SaveChanges();

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Lifecycle Campaign",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Campaigns.Add(campaign);
        db.SaveChanges();

        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = BlockedPlayerId,
            CampaignId = campaign.CampaignId,
            PlacementOutcome = PlacementOutcome.Undecided,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });
        db.SaveChanges();
    }
}
