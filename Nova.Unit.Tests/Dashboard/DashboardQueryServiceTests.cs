using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Dashboard;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>
/// Verifies the tenant-safe club dashboard summary service: active-only cards with workspace
/// links and active/archived roster and team counts.
/// </summary>
public sealed class DashboardQueryServiceTests : IDisposable
{
    private const long ClubAId = 1000;
    private const long ClubBId = 2000;
    private const long ClubAMemberId = 1001;
    private const long ClubAAdminId = 1002;
    private const long ClubBMemberId = 2001;

    private readonly TenancyTestHarness _harness = new();
    private long _campaignAId;
    private long _campaignBId;

    /// <summary>Initializes a test instance with cross-tenant campaign, roster, and team data.</summary>
    public DashboardQueryServiceTests() => Seed();

    /// <summary>Releases the tenancy harness.</summary>
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies callers without approved membership are rejected before any composition.</summary>
    [Fact]
    public async Task GetDashboard_ReturnsForbidden_WhenNotMember()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies active-only cards, workspace links, and roster/team counts for an evaluator.
    /// </summary>
    [Fact]
    public async Task GetDashboard_ReturnsActiveCardsCounts_ForEvaluator()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var dashboard = result.Value;

        dashboard.ActiveCampaigns.Count.ShouldBe(1);
        var card = dashboard.ActiveCampaigns[0];
        card.CampaignId.ShouldBe(_campaignAId);
        card.Name.ShouldBe("Campaign A");
        card.SeasonName.ShouldBe("Season A");
        card.WorkspaceUrl.ShouldBe($"/campaigns/{_campaignAId}");
        card.ParticipantCount.ShouldBe(2);
        card.UnresolvedCount.ShouldBe(2);

        dashboard.Roster.ActivePlayers.ShouldBe(2);
        dashboard.Roster.ArchivedPlayers.ShouldBe(1);
        dashboard.Teams.ActiveTeams.ShouldBe(1);
        dashboard.Teams.ArchivedTeams.ShouldBe(1);
    }

    /// <summary>Verifies tenant isolation: a different club sees only its own campaigns and counts.</summary>
    [Fact]
    public async Task GetDashboard_IsTenantIsolated()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var dashboard = result.Value;
        dashboard.ActiveCampaigns.Count.ShouldBe(1);
        dashboard.ActiveCampaigns[0].CampaignId.ShouldBe(_campaignBId);
        dashboard.ActiveCampaigns[0].Name.ShouldBe("Campaign B");
        dashboard.Roster.ActivePlayers.ShouldBe(1);
        dashboard.Roster.ArchivedPlayers.ShouldBe(0);
        dashboard.Teams.ActiveTeams.ShouldBe(1);
        dashboard.Teams.ArchivedTeams.ShouldBe(0);
    }

    /// <summary>Verifies the active campaign cards are bounded to the dashboard maximum.</summary>
    [Fact]
    public async Task GetDashboard_CapsActiveCampaignsAtMaxCount()
    {
        using (var admin = _harness.CreateAdminContext())
        {
            var season = admin.Seasons.Single(season => season.ClubId == ClubAId);
            for (var index = 0; index < 25; index++)
            {
                admin.Campaigns.Add(new CampaignEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = $"Extra {index:D2}",
                    StartDate = new DateOnly(2026, 7, 1),
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                });
            }

            admin.SaveChanges();
        }

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveCampaigns.Count.ShouldBe(ClubDashboardResult.ActiveCampaignMaxCount);
    }

    /// <summary>Seeds clubs, users, seasons, campaigns, players, teams, and assignments.</summary>
    private void Seed()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });

        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });

        var seasonA = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Seasons.AddRange(seasonA, seasonB);
        admin.SaveChanges();

        var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonA.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignAClosed = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign A Closed", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAMemberId, SeasonId = seasonA.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign B", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Campaigns.AddRange(campaignA, campaignAClosed, campaignB);
        admin.SaveChanges();

        _campaignAId = campaignA.CampaignId;
        _campaignBId = campaignB.CampaignId;

        var playerActive1 = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P1", LastName = "Active", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerActive2 = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P2", LastName = "Active", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerArchived = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P3", LastName = "Archived", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId };
        var playerB = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "B", LastName = "Active", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Players.AddRange(playerActive1, playerActive2, playerArchived, playerB);

        admin.Teams.AddRange(
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Active Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Archived Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "B Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.SaveChanges();

        admin.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity { PlayerId = playerActive1.PlayerId, CampaignId = _campaignAId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Undecided },
            new PlayerCampaignAssignmentEntity { PlayerId = playerActive2.PlayerId, CampaignId = _campaignAId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Undecided });
        admin.SaveChanges();
    }

    /// <summary>
    /// Creates the dashboard query service over the shared SQLite tenancy harness, composing the real
    /// campaign list service.
    /// </summary>
    /// <returns>A service instance.</returns>
    private DashboardQueryService CreateService()
    {
        var readFactory = new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        var campaignQueryService = new CampaignQueryService(readFactory, _harness.CurrentUser, NullLogger<CampaignQueryService>.Instance);

        return new DashboardQueryService(
            campaignQueryService,
            readFactory,
            _harness.CurrentUser,
            NullLogger<DashboardQueryService>.Instance);
    }
}
