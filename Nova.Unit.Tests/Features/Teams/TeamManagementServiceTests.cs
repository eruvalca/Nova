using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Features.Teams;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Teams;

/// <summary>
/// Covers tenant-safe team management behavior using the shared SQLite harness.
/// </summary>
public sealed class TeamManagementServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;
    private const long ClubBAdminId = 201;

    private readonly TenancyTestHarness _harness = new();
    private readonly long _teamId;
    private readonly long _activeCampaignId;
    private readonly long _playerId;

    public TeamManagementServiceTests()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity
            {
                ClubId = ClubAId,
                Name = "Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAAdminId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                Name = "Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBAdminId
            });
        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "A", LastName = "Admin", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "B", LastName = "Admin", ClubId = ClubBId });
        var season = new SeasonEntity
        {
            Name = "Season",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Seasons.Add(season);
        db.SaveChanges();
        var campaign = new CampaignEntity
        {
            Name = "Active",
            StartDate = new DateOnly(2026, 1, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var player = new PlayerEntity
        {
            FirstName = "Player",
            LastName = "One",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 2028,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var team = new TeamEntity
        {
            Name = "U16",
            GraduationYear = 2028,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Campaigns.Add(campaign);
        db.Players.Add(player);
        db.Teams.Add(team);
        db.SaveChanges();
        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            TeamId = team.TeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });
        db.SaveChanges();

        _teamId = team.TeamId;
        _activeCampaignId = campaign.CampaignId;
        _playerId = player.PlayerId;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Create_ReturnsActiveTeam_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U18", GraduationYear = 2026 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(ClubAId);
        result.Value.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    [Fact]
    public async Task Create_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U18", GraduationYear = 2026 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_ForOtherClubTeam()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Cross tenant", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task Update_ReturnsConflictWithBlockers_AndWritesNothing_WhenEligibilityWouldBreak()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Changed", GraduationYear = 2029 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.TryGetGraduationYearBlockers(out var blockers).ShouldBeTrue();
        blockers.Count.ShouldBe(1);
        blockers[0].PlayerId.ShouldBe(_playerId);
        blockers[0].PlayerGraduationYear.ShouldBe(2028);

        using var db = _harness.CreateAdminContext();
        var team = db.Teams.Single(t => t.TeamId == _teamId);
        team.Name.ShouldBe("U16");
        team.GraduationYear.ShouldBe(2028);
        db.PlayerCampaignAssignments.Any(a =>
            a.PlayerCampaignAssignmentId > 0
            && a.CampaignId == _activeCampaignId
            && a.PlayerId == _playerId).ShouldBeTrue();
    }

    [Fact]
    public async Task Update_Succeeds_ForActiveTeam()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Changed", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Changed");
    }

    [Fact]
    public async Task Update_ReturnsConflict_ForArchivedTeam()    {
        using (var db = _harness.CreateAdminContext())
        {
            var team = db.Teams.Single(t => t.TeamId == _teamId);
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = ClubAAdminId;
            db.SaveChanges();
        }

        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Changed", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies a club cannot own two teams sharing a name and graduation year.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsConflict_ForDuplicateNameAndGraduationYear()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var db = _harness.CreateAdminContext();
        db.Teams.Count(team => team.ClubId == ClubAId && team.Name == "U16").ShouldBe(1);
    }

    /// <summary>
    /// Verifies the same team name is allowed under a different graduation year.
    /// </summary>
    [Fact]
    public async Task Create_Succeeds_ForSameNameUnderDifferentGraduationYear()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2029 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.GraduationYear.ShouldBe(2029);
    }

    /// <summary>
    /// Verifies team-name uniqueness is scoped to the owning club rather than global.
    /// </summary>
    [Fact]
    public async Task Create_Succeeds_ForSameNameInAnotherClub()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(ClubBId);
    }

    /// <summary>
    /// Verifies renaming a team onto an existing name and graduation year is rejected.
    /// </summary>
    [Fact]
    public async Task Update_ReturnsConflict_WhenRenamingOntoExistingTeam()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var service = CreateService();

        var created = await service.CreateAsync(
            new CreateTeamInput { Name = "U18", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);
        created.IsSuccess.ShouldBeTrue();

        var result = await service.UpdateAsync(
            new UpdateTeamInput { TeamId = created.Value.TeamId, Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var db = _harness.CreateAdminContext();
        db.Teams.Single(team => team.TeamId == created.Value.TeamId).Name.ShouldBe("U18");
    }

    private TeamManagementService CreateService()
        => new(
            new HarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

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
