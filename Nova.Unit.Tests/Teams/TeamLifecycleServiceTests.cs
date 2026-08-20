using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Direct SQLite shell tests for <see cref="TeamLifecycleService"/>: administrator authorization,
/// tenant isolation, lifecycle mutation effects, and archive blockers.
/// </summary>
public sealed class TeamLifecycleServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long ActiveTeamId = 300;
    private const long BlockedTeamId = 301;
    private const long ArchivedTeamId = 302;
    private const long ClubBTeamId = 303;
    private const long PlacedPlayerId = 400;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded team lifecycle data for two clubs.
    /// </summary>
    public TeamLifecycleServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ArchiveAsync_ReturnsForbidden_WhenActorIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);
        var service = CreateService();

        var result = await service.ArchiveAsync(ActiveTeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsForbidden_WhenActorHasNoClub()
    {
        ActAs(ClubAAdminId, clubId: null, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ActiveTeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsNotFound_WhenTeamBelongsToOtherClub()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ClubBTeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task ArchiveAsync_ArchivesTeam_AndSetsProvenance()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ActiveTeamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var team = await verify.Teams
            .SingleAsync(candidate => candidate.TeamId == ActiveTeamId, TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        team.ArchivedAt.ShouldNotBeNull();
        team.ArchivedById.ShouldBe(ClubAAdminId);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsConflict_WhenTeamAlreadyArchived()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(ArchivedTeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsConflict_WhenActivePlacementRemains()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ArchiveAsync(BlockedTeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail!.ShouldContain("Resolve every active-campaign placement");

        await using var verify = _harness.CreateAdminContext();
        var team = await verify.Teams
            .SingleAsync(candidate => candidate.TeamId == BlockedTeamId, TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    [Fact]
    public async Task RestoreAsync_RestoresTeam_AndClearsProvenance()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RestoreAsync(ArchivedTeamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var team = await verify.Teams
            .SingleAsync(candidate => candidate.TeamId == ArchivedTeamId, TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        team.ArchivedAt.ShouldBeNull();
        team.ArchivedById.ShouldBeNull();
    }

    [Fact]
    public async Task RestoreAsync_ReturnsConflict_WhenTeamAlreadyActive()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RestoreAsync(ActiveTeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    private TeamLifecycleService CreateService()
        => new(
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext),
            _harness.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

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
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Member", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });

        db.Players.Add(new PlayerEntity
        {
            PlayerId = PlacedPlayerId,
            FirstName = "Placed",
            LastName = "Player",
            DateOfBirth = new DateOnly(2011, 1, 1),
            GraduationYear = 2029,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });

        db.Teams.AddRange(
            new TeamEntity
            {
                TeamId = ActiveTeamId,
                Name = "Active Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = BlockedTeamId,
                Name = "Blocked Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = ArchivedTeamId,
                Name = "Archived Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ArchivedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = ClubBTeamId,
                Name = "Club B Team",
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.SaveChanges();

        var season = new SeasonEntity
        {
            Name = "Lifecycle Season",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Seasons.Add(season);
        db.SaveChanges();

        var campaign = new CampaignEntity
        {
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
            PlayerId = PlacedPlayerId,
            CampaignId = campaign.CampaignId,
            TeamId = BlockedTeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });
        db.SaveChanges();
    }
}
