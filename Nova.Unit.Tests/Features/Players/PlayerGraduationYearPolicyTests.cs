using Nova.Features.Players;
using Nova.Shared.Players;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>
/// Tests for <see cref="PlayerGraduationYearPolicy"/>: deterministic eligibility classification
/// over constructed placement facts with no database, DI, or service setup.
/// </summary>
public sealed class PlayerGraduationYearPolicyTests
{
    [Fact]
    public void Evaluate_WithEmptyPlacements_ReturnsMayChange()
    {
        var result = PlayerGraduationYearPolicy.Evaluate(2028, []);
        result.IsT0.ShouldBeTrue(); // GraduationYearMayChange
    }

    [Fact]
    public void Evaluate_WithAllEligiblePlacements_ReturnsMayChange()
    {
        var placements = new[]
        {
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 1, CampaignId: 10, TeamId: 100, TeamGraduationYear: 2027),
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 2, CampaignId: 10, TeamId: 101, TeamGraduationYear: 2028),
        };

        var result = PlayerGraduationYearPolicy.Evaluate(2028, placements);

        result.IsT0.ShouldBeTrue(); // GraduationYearMayChange
    }

    [Fact]
    public void Evaluate_WithExactlyMatchingGraduationYear_ReturnsMayChange()
    {
        var placements = new[]
        {
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 1, CampaignId: 10, TeamId: 100, TeamGraduationYear: 2028),
        };

        // Player grad year == team grad year is eligible (>= rule).
        var result = PlayerGraduationYearPolicy.Evaluate(2028, placements);

        result.IsT0.ShouldBeTrue(); // GraduationYearMayChange
    }

    [Fact]
    public void Evaluate_WithOnePlacementTooYoung_ReturnsBlockedWithThatItem()
    {
        var placements = new[]
        {
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 5, CampaignId: 20, TeamId: 200, TeamGraduationYear: 2029),
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 6, CampaignId: 20, TeamId: 201, TeamGraduationYear: 2027),
        };

        var result = PlayerGraduationYearPolicy.Evaluate(2028, placements);

        result.IsT1.ShouldBeTrue(); // GraduationYearEditBlocked
        var blocked = result.AsT1;
        blocked.Blockers.Count.ShouldBe(1);
        blocked.Blockers[0].PlayerCampaignAssignmentId.ShouldBe(5);
        blocked.Blockers[0].CampaignId.ShouldBe(20);
        blocked.Blockers[0].TeamId.ShouldBe(200);
        blocked.Blockers[0].TeamGraduationYear.ShouldBe(2029);
    }

    [Fact]
    public void Evaluate_WithAllPlacementsBlocked_ReturnsAllBlockers()
    {
        var placements = new[]
        {
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 10, CampaignId: 30, TeamId: 300, TeamGraduationYear: 2030),
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 11, CampaignId: 30, TeamId: 301, TeamGraduationYear: 2031),
        };

        var result = PlayerGraduationYearPolicy.Evaluate(2028, placements);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Blockers.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(2028, 2027, false)] // player year > team year — eligible
    [InlineData(2028, 2028, false)] // equal — eligible
    [InlineData(2028, 2029, true)]  // player year < team year — blocked
    [InlineData(2025, 2026, true)]  // blocked
    [InlineData(2026, 2026, false)] // eligible
    public void Evaluate_GraduationYearEligibilityMatrix(
        int proposedPlayerYear,
        int teamGraduationYear,
        bool expectBlocked)
    {
        var placements = new[]
        {
            new AssignedPlacementFacts(PlayerCampaignAssignmentId: 99, CampaignId: 1, TeamId: 1, TeamGraduationYear: teamGraduationYear),
        };

        var result = PlayerGraduationYearPolicy.Evaluate(proposedPlayerYear, placements);

        result.IsT1.ShouldBe(expectBlocked,
            $"proposedYear={proposedPlayerYear}, teamYear={teamGraduationYear} — expected blocked={expectBlocked}");
    }
}
