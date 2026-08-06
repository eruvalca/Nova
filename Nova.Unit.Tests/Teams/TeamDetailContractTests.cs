using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies team-detail routes and the shared placement-impact contract.
/// </summary>
public sealed class TeamDetailContractTests
{
    /// <summary>
    /// Verifies the canonical team-detail URL.
    /// </summary>
    [Fact]
    public void GetDetailUrl_BuildsCanonicalTeamDetailRoute()
    {
        TeamEndpoints.GetDetailUrl(123).ShouldBe("/api/teams/123");
    }

    /// <summary>
    /// Verifies the contract retains active and historical placement context.
    /// </summary>
    [Fact]
    public void TeamDetailDto_SeparatesActiveImpactsFromHistoricalPlacements()
    {
        var active = new TeamPlacementImpactDto(
            1,
            2,
            "Fall tryouts",
            CampaignStatus.Active,
            new DateOnly(2026, 9, 1),
            3,
            "Avery Athlete",
            2028,
            7,
            PlacementOutcome.Assigned);
        var closed = active with
        {
            PlayerCampaignAssignmentId = 4,
            CampaignId = 5,
            CampaignName = "Spring tryouts",
            CampaignStatus = CampaignStatus.Closed
        };

        var detail = new TeamDetailDto(10, 20, "U16 Blue", 2028, LifecycleStatus.Active, [active], [active, closed]);

        detail.ActivePlacementImpacts.Count.ShouldBe(1);
        detail.PlacementHistory.Select(item => item.CampaignStatus).ShouldBe([CampaignStatus.Active, CampaignStatus.Closed]);
    }
}
