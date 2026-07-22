using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>
/// A minimal <see cref="IDbContextFactory{TContext}"/> that creates contexts from the shared
/// in-memory SQLite connection in <see cref="TenancyTestHarness"/>.
/// </summary>
file sealed class HarnessDbContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaDbContext>
{
    public NovaDbContext CreateDbContext() => harness.CreateTenantContext();
    public Task<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(harness.CreateTenantContext());
}

/// <summary>
/// Unit tests for <see cref="PlayerManagementService"/> covering create/update authorization,
/// campaign enrollment, graduation-year blocking, tenancy, and validation short-circuit.
/// </summary>
public sealed class PlayerManagementServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;
    private const long ClubBAdminId = 201;

    private readonly TenancyTestHarness _harness = new();

    private long _activeCampaignId;
    private long _closedCampaignId;
    private long _existingPlayerId;

    public PlayerManagementServiceTests() => Seed();

    public void Dispose() => _harness.Dispose();

    // ── Create ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Succeeds_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerId.ShouldBeGreaterThan(0);
        result.Value.ClubId.ShouldBe(ClubAId);
        result.Value.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    [Fact]
    public async Task Create_EnrollsPlayerInEveryActiveCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var playerId = result.Value.PlayerId;

        using var db = _harness.CreateAdminContext();
        var assignments = db.PlayerCampaignAssignments
            .Where(a => a.PlayerId == playerId)
            .ToList();

        assignments.Count.ShouldBe(1, "only the single Active campaign should get an enrollment");
        assignments[0].CampaignId.ShouldBe(_activeCampaignId);
        assignments[0].PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
    }

    [Fact]
    public async Task Create_DoesNotEnrollInClosedCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        db.PlayerCampaignAssignments
            .Any(a => a.PlayerId == result.Value.PlayerId && a.CampaignId == _closedCampaignId)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Create_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var sut = CreateService();

        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task Create_ReturnsForbidden_ForUnauthenticated()
    {
        ActAs(userId: null, clubId: null, isAdmin: false);
        var sut = CreateService();

        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task Create_ReturnsValidation_BeforeAccessingDatabase_WhenInputInvalid()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var invalid = new CreatePlayerInput
        {
            FirstName = "",
            LastName = "",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 1800
        };

        var result = await sut.CreateAsync(invalid, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task Create_CreatedPlayerIsVisibleToSameClub()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateTenantContext(); // already scoped to ClubA via harness
        var player = await db.Players.FindAsync([result.Value.PlayerId], TestContext.Current.CancellationToken);
        player.ShouldNotBeNull();
    }

    [Fact]
    public async Task Create_CreatedPlayerIsNotVisibleToOtherClub()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();
        var result = await sut.CreateAsync(ValidCreateInput(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        var newPlayerId = result.Value.PlayerId;

        // Switch to Club B — query filter should hide Club A's player.
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);
        using var db = _harness.CreateTenantContext();
        var player = await db.Players.FindAsync([newPlayerId], TestContext.Current.CancellationToken);
        player.ShouldBeNull();
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Succeeds_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.UpdateAsync(ValidUpdateInput(_existingPlayerId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FirstName.ShouldBe("Updated");
    }

    [Fact]
    public async Task Update_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);
        var sut = CreateService();

        var result = await sut.UpdateAsync(ValidUpdateInput(_existingPlayerId), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_ForCrossTenantPlayer()
    {
        ActAs(ClubBAdminId, ClubBId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.UpdateAsync(ValidUpdateInput(_existingPlayerId), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task Update_ReturnsConflict_ForArchivedPlayer()
    {
        // Archive the player first with the required metadata.
        using (var db = _harness.CreateAdminContext())
        {
            var player = db.Players.Find([_existingPlayerId]);
            player!.LifecycleStatus = LifecycleStatus.Archived;
            player.ArchivedAt = DateTimeOffset.UtcNow;
            player.ArchivedById = ClubAAdminId;
            db.SaveChanges();
        }

        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var result = await sut.UpdateAsync(ValidUpdateInput(_existingPlayerId), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task Update_ReturnsValidation_WhenInputInvalid()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        var invalid = new UpdatePlayerInput
        {
            PlayerId = _existingPlayerId,
            FirstName = "   ",
            LastName = "Smith",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 2028
        };

        var result = await sut.UpdateAsync(invalid, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task Update_GraduationYearChange_Blocked_WhenIneligibleActivePlacement()
    {
        // Seed a team and an Assigned placement in the active campaign.
        long teamId;
        using (var db = _harness.CreateAdminContext())
        {
            var team = new TeamEntity
            {
                Name = "Team 2030",
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            };
            db.Teams.Add(team);
            db.SaveChanges();
            teamId = team.TeamId;

            // Enroll existing player in the active campaign with Assigned placement.
            var assignment = new PlayerCampaignAssignmentEntity
            {
                PlayerId = _existingPlayerId,
                CampaignId = _activeCampaignId,
                TeamId = teamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            };
            db.PlayerCampaignAssignments.Add(assignment);
            db.SaveChanges();
        }

        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        // Propose graduation year 2028 < team's 2030 — should be blocked.
        var input = ValidUpdateInput(_existingPlayerId) with { GraduationYear = 2028 };
        var result = await sut.UpdateAsync(input, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors!.Keys.ShouldContain(k => k.StartsWith("blockers[", StringComparison.Ordinal));

        // Verify nothing was written.
        using var dbCheck = _harness.CreateAdminContext();
        dbCheck.Players.Find([_existingPlayerId])!.GraduationYear.ShouldBe(2030);
    }

    [Fact]
    public async Task Update_GraduationYearChange_Succeeds_WhenStillEligible()
    {
        // Seed a team with year 2025 and Assigned placement.
        using (var db = _harness.CreateAdminContext())
        {
            var team = new TeamEntity
            {
                Name = "Team 2025",
                GraduationYear = 2025,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            };
            db.Teams.Add(team);
            db.SaveChanges();

            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = _existingPlayerId,
                CampaignId = _activeCampaignId,
                TeamId = team.TeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });
            db.SaveChanges();
        }

        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var sut = CreateService();

        // 2030 >= 2025 — eligible
        var input = ValidUpdateInput(_existingPlayerId) with { GraduationYear = 2030 };
        var result = await sut.UpdateAsync(input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.GraduationYear.ShouldBe(2030);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    private PlayerManagementService CreateService() =>
        new(new HarnessDbContextFactory(_harness), _harness.CurrentUser, NullLogger<PlayerManagementService>.Instance);

    private static CreatePlayerInput ValidCreateInput() => new()
    {
        FirstName = "Alex",
        LastName = "Test",
        DateOfBirth = new DateOnly(2010, 3, 20),
        GraduationYear = 2028
    };

    private static UpdatePlayerInput ValidUpdateInput(long playerId) => new()
    {
        PlayerId = playerId,
        FirstName = "Updated",
        LastName = "Player",
        DateOfBirth = new DateOnly(2010, 3, 20),
        GraduationYear = 2030
    };

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminId });

        var seasonA = new SeasonEntity
        {
            Name = "Season 2026",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Seasons.Add(seasonA);

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Member", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });

        db.SaveChanges();

        var activeCampaign = new CampaignEntity
        {
            Name = "Active Campaign",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var closedCampaign = new CampaignEntity
        {
            Name = "Closed Campaign",
            StartDate = new DateOnly(2026, 1, 1),
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-30),
            ClosedById = ClubAAdminId,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Campaigns.AddRange(activeCampaign, closedCampaign);

        var existingPlayer = new PlayerEntity
        {
            FirstName = "Existing",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 4, 10),
            GraduationYear = 2030,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        db.Players.Add(existingPlayer);
        db.SaveChanges();

        _activeCampaignId = activeCampaign.CampaignId;
        _closedCampaignId = closedCampaign.CampaignId;
        _existingPlayerId = existingPlayer.PlayerId;
    }
}
