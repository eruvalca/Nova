using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
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

#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    public TeamManagementServiceTests()
#pragma warning restore MA0051
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubAId,
                Name = "Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAAdminId
            },
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
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
            CreationOperationId = Guid.NewGuid(),
            Name = "Season",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Seasons.Add(season);
        db.SaveChanges();
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Active",
            StartDate = new DateOnly(2026, 1, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Player",
            LastName = "One",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 2028,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
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
    public async Task CreateReturnsActiveTeamForClubAdminAsync()
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
    public async Task CreateReturnsForbiddenForNonAdminAsync()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U18", GraduationYear = 2026 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task UpdateReturnsForbiddenForNonAdminAsync()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Changed", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task UpdateReturnsNotFoundForOtherClubTeamAsync()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Cross tenant", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task UpdateReturnsConflictWithBlockersAndWritesNothingWhenEligibilityWouldBreakAsync()
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
        var team = (await db.Teams.SingleAsync(t => t.TeamId == _teamId, TestContext.Current.CancellationToken));
        team.Name.ShouldBe("U16");
        team.GraduationYear.ShouldBe(2028);
        (await db.PlayerCampaignAssignments.AnyAsync(a =>
            a.PlayerCampaignAssignmentId > 0
            && a.CampaignId == _activeCampaignId
            && a.PlayerId == _playerId, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateSucceedsForActiveTeamAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateTeamInput { TeamId = _teamId, Name = "Changed", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Changed");
    }

    [Fact]
    public async Task UpdateReturnsConflictForArchivedTeamAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
            var team = (await db.Teams.SingleAsync(t => t.TeamId == _teamId, TestContext.Current.CancellationToken));
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = ClubAAdminId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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
    public async Task CreateReturnsConflictForDuplicateNameAndGraduationYearAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        using var db = _harness.CreateAdminContext();
        (await db.Teams.CountAsync(team => team.ClubId == ClubAId && team.Name == "U16", TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Verifies the same team name is allowed under a different graduation year.
    /// </summary>
    [Fact]
    public async Task CreateSucceedsForSameNameUnderDifferentGraduationYearAsync()
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
    public async Task CreateSucceedsForSameNameInAnotherClubAsync()
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
    public async Task UpdateReturnsConflictWhenRenamingOntoExistingTeamAsync()
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
        (await db.Teams.SingleAsync(team => team.TeamId == created.Value.TeamId, TestContext.Current.CancellationToken)).Name.ShouldBe("U18");
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
