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

namespace Nova.Unit.Tests.Features.Teams;

/// <summary>
/// Tests for <see cref="TeamDetailQueryService"/> placement ordering, authorization,
/// and tenant isolation using the shared SQLite tenancy harness.
/// </summary>
public sealed class TeamDetailQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 101;
    private const long ClubBMemberId = 201;

    private const long ClubATeamId = 300;
    private const long ClubBTeamId = 301;

    // Active campaign intentionally has the OLDER start date to expose the in-memory sort bug.
    private const long ActiveCampaignId = 400;
    private const long ClosedCampaignId = 401;

    private const long ClubASeasonId = 500;
    private const long ClubBSeasonId = 501;

    private const long PlayerInActiveId = 600;
    private const long PlayerInClosedId = 601;

    private const long ActiveAssignmentId = 800;
    private const long ClosedAssignmentId = 801;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes the test class by seeding two-club data with an Active campaign whose
    /// <c>StartDate</c> is intentionally older than the Closed campaign's date.
    /// </summary>
    public TeamDetailQueryServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies that Active-campaign placements sort before non-Active placements even when the
    /// Active campaign has an older <c>StartDate</c> than the Closed one.
    /// Without the active-first leading key in the in-memory sort, the Closed campaign's newer
    /// date would push its rows to the top and contradict the SQL truncation order.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ActiveCampaignPlacementsFirst_WhenActiveHasOlderStartDate()
    {
        ActAs(ClubAMemberId, ClubAId);
        var result = await CreateService().GetTeamDetailAsync(ClubATeamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlacementHistory.Count.ShouldBe(2);
        result.Value.PlacementHistory[0].CampaignStatus.ShouldBe(CampaignStatus.Active,
            "Active-campaign placements must sort first, regardless of CampaignStartDate.");
        result.Value.PlacementHistory[0].CampaignId.ShouldBe(ActiveCampaignId);
    }

    /// <summary>
    /// Verifies that <c>ActivePlacementImpacts</c> contains only Active-campaign rows from
    /// the truncated page, and that <c>ActivePlacementImpactTotalCount</c> reflects the
    /// unbounded Active count independently of the page.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ActivePlacementImpacts_ContainsOnlyActiveCampaignRows()
    {
        ActAs(ClubAMemberId, ClubAId);
        var result = await CreateService().GetTeamDetailAsync(ClubATeamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActivePlacementImpacts.ShouldAllBe(p => p.CampaignStatus == CampaignStatus.Active);
        result.Value.ActivePlacementImpacts.Count.ShouldBe(1);
        result.Value.ActivePlacementImpactTotalCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies that callers without an approved club membership receive a non-disclosing
    /// forbidden result.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsForbidden_WhenCallerHasNoClub()
    {
        ActAs(userId: ClubAMemberId, clubId: null);
        var result = await CreateService().GetTeamDetailAsync(ClubATeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies that a team owned by another club returns a non-disclosing not-found result.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsNotFound_ForCrossTenantTeam()
    {
        ActAs(ClubBMemberId, ClubBId);
        var result = await CreateService().GetTeamDetailAsync(ClubATeamId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Creates the service under test wired to the SQLite tenancy harness.
    /// </summary>
    /// <returns>The configured <see cref="TeamDetailQueryService"/>.</returns>
    private TeamDetailQueryService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new TeamDetailQueryService(
            readDbFactory,
            _harness.CurrentUser,
            NullLogger<TeamDetailQueryService>.Instance);
    }

    /// <summary>
    /// Sets the simulated current user for the next tenant read context.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier, or <see langword="null"/> to simulate a non-member.</param>
    private void ActAs(long? userId, long? clubId)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
    }

    /// <summary>
    /// Seeds two clubs, a team for Club A with one Active-campaign placement and one
    /// Closed-campaign placement. The Active campaign has an older <c>StartDate</c> than
    /// the Closed one so that the in-memory sort key is observable.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Alpha", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Beta", LastName = "Member", ClubId = ClubBId });

        db.Teams.AddRange(
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = ClubATeamId, Name = "U16", GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = ClubBTeamId, Name = "U14", GraduationYear = 2030, ClubId = ClubBId, CreatedById = ClubBMemberId });

        db.Seasons.AddRange(
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = ClubASeasonId, Name = "Season A", StartDate = new DateOnly(2025, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId },
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = ClubBSeasonId, Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId });

        // The Active campaign has an OLDER start date (2025-01-01) than the Closed campaign
        // (2026-06-01). A sort that lacks the active-first leading key would put Closed first.
        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ActiveCampaignId,
                Name = "Active Tryouts",
                StartDate = new DateOnly(2025, 1, 1),
                Status = CampaignStatus.Active,
                SeasonId = ClubASeasonId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ClosedCampaignId,
                Name = "Closed Tryouts",
                StartDate = new DateOnly(2026, 6, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-30),
                ClosedById = ClubAMemberId,
                SeasonId = ClubASeasonId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            });

        db.Players.AddRange(
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = PlayerInActiveId,
                FirstName = "Active",
                LastName = "Player",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = 2028,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = PlayerInClosedId,
                FirstName = "Closed",
                LastName = "Player",
                DateOfBirth = new DateOnly(2010, 6, 1),
                GraduationYear = 2028,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            });

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerId = PlayerInActiveId,
                CampaignId = ActiveCampaignId,
                TeamId = ClubATeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                PlayerId = PlayerInClosedId,
                CampaignId = ClosedCampaignId,
                TeamId = ClubATeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            });

        db.SaveChanges();
    }
}
