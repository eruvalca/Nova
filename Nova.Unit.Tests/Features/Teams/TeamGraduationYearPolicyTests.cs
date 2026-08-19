using Nova.Features.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Features.Teams;

/// <summary>
/// Covers the pure team graduation-year eligibility policy. A team's graduation year is the
/// minimum eligible player graduation year, so only players graduating earlier are blockers.
/// </summary>
public sealed class TeamGraduationYearPolicyTests
{
    /// <summary>
    /// Verifies an empty placement set never blocks a proposed graduation year.
    /// </summary>
    [Fact]
    public void Evaluate_Allows_WhenNoPlacementsExist()
    {
        var decision = TeamGraduationYearPolicy.Evaluate(2030, []);

        decision.IsT0.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies placements whose players graduate on or after the proposed year stay eligible.
    /// </summary>
    /// <param name="playerGraduationYear">The placed player's graduation year.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(2030)]
    [InlineData(2031)]
    public void Evaluate_Allows_WhenEveryPlayerGraduatesOnOrAfterProposedYear(int playerGraduationYear)
    {
        var decision = TeamGraduationYearPolicy.Evaluate(
            2030,
            [new TeamAssignedPlacementFacts(1, 10, 100, playerGraduationYear)]);

        decision.IsT0.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies a player graduating before the proposed year blocks the change and is reported.
    /// </summary>
    [Fact]
    public void Evaluate_Blocks_WhenPlayerGraduatesBeforeProposedYear()
    {
        var decision = TeamGraduationYearPolicy.Evaluate(
            2030,
            [new TeamAssignedPlacementFacts(1, 10, 100, 2029)]);

        decision.IsT1.ShouldBeTrue();
        var blockers = decision.AsT1.Blockers;
        blockers.Count.ShouldBe(1);
        blockers[0].PlayerCampaignAssignmentId.ShouldBe(1);
        blockers[0].CampaignId.ShouldBe(10);
        blockers[0].PlayerId.ShouldBe(100);
        blockers[0].PlayerGraduationYear.ShouldBe(2029);
    }

    /// <summary>
    /// Verifies only the ineligible placements are reported when the set is mixed, ordered by
    /// placement identifier so the payload is deterministic.
    /// </summary>
    [Fact]
    public void Evaluate_ReportsOnlyIneligiblePlacements_InPlacementIdOrder()
    {
        var decision = TeamGraduationYearPolicy.Evaluate(
            2030,
            [
                new TeamAssignedPlacementFacts(3, 10, 100, 2028),
                new TeamAssignedPlacementFacts(2, 10, 101, 2031),
                new TeamAssignedPlacementFacts(1, 11, 102, 2029)
            ]);

        decision.IsT1.ShouldBeTrue();
        decision.AsT1.Blockers
            .Select(blocker => blocker.PlayerCampaignAssignmentId)
            .ShouldBe([1L, 3L]);
    }

    /// <summary>
    /// Verifies lowering the team's graduation year never blocks, because a lower minimum can only
    /// widen eligibility.
    /// </summary>
    [Fact]
    public void Evaluate_Allows_WhenProposedYearIsLoweredBelowEveryPlayer()
    {
        var decision = TeamGraduationYearPolicy.Evaluate(
            2025,
            [
                new TeamAssignedPlacementFacts(1, 10, 100, 2028),
                new TeamAssignedPlacementFacts(2, 10, 101, 2029)
            ]);

        decision.IsT0.ShouldBeTrue();
    }
}
