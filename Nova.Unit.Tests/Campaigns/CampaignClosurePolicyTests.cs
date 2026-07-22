using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests the deterministic campaign closure rule matrix without persistence dependencies.
/// </summary>
public sealed class CampaignClosurePolicyTests
{
    /// <summary>
    /// Verifies a campaign with no participation records may close.
    /// </summary>
    [Fact]
    public void Evaluate_AllowsClosure_WhenCampaignHasNoAssignments()
    {
        var result = CampaignClosurePolicy.Evaluate([]);

        result.Value.ShouldBeOfType<CampaignMayClose>();
    }

    /// <summary>
    /// Verifies every supported final outcome passes when assigned players remain eligible.
    /// </summary>
    [Fact]
    public void Evaluate_AllowsClosure_WhenEveryOutcomeIsFinalAndValid()
    {
        CampaignAssignmentClosureState[] states =
        [
            CreateState(1, PlacementOutcome.Assigned, playerYear: 2030, teamId: 10, teamYear: 2030),
            CreateState(2, PlacementOutcome.NotSelected),
            CreateState(3, PlacementOutcome.Withdrawn)
        ];

        var result = CampaignClosurePolicy.Evaluate(states);

        result.Value.ShouldBeOfType<CampaignMayClose>();
    }

    /// <summary>
    /// Verifies undecided participation records block closure.
    /// </summary>
    [Fact]
    public void Evaluate_BlocksClosure_WhenOutcomeIsUndecided()
    {
        CampaignAssignmentClosureState[] states =
        [
            CreateState(1, PlacementOutcome.Undecided),
            CreateState(2, PlacementOutcome.Undecided)
        ];

        var result = CampaignClosurePolicy.Evaluate(states);

        var blocked = result.Value.ShouldBeOfType<CampaignCloseBlocked>();
        blocked.Errors["outcomes"].ShouldBe(
        [
            "Every participant must have a final outcome before closing. Found 2 undecided participation record(s)."
        ]);
    }

    /// <summary>
    /// Verifies an assigned outcome without a team blocks closure.
    /// </summary>
    [Fact]
    public void Evaluate_BlocksClosure_WhenAssignedOutcomeHasNoTeam()
    {
        var result = CampaignClosurePolicy.Evaluate(
        [
            CreateState(7, PlacementOutcome.Assigned)
        ]);

        var blocked = result.Value.ShouldBeOfType<CampaignCloseBlocked>();
        blocked.Errors.ShouldContainKey("eligibility");
    }

    /// <summary>
    /// Verifies an assigned outcome without a team graduation year blocks closure.
    /// </summary>
    [Fact]
    public void Evaluate_BlocksClosure_WhenAssignedTeamHasNoGraduationYear()
    {
        var result = CampaignClosurePolicy.Evaluate(
        [
            CreateState(8, PlacementOutcome.Assigned, teamId: 10)
        ]);

        var blocked = result.Value.ShouldBeOfType<CampaignCloseBlocked>();
        blocked.Errors.ShouldContainKey("eligibility");
    }

    /// <summary>
    /// Verifies a player below the team's graduation-year cutoff blocks closure.
    /// </summary>
    [Fact]
    public void Evaluate_BlocksClosure_WhenAssignedPlayerIsIneligible()
    {
        var result = CampaignClosurePolicy.Evaluate(
        [
            CreateState(9, PlacementOutcome.Assigned, playerYear: 2029, teamId: 10, teamYear: 2030)
        ]);

        var blocked = result.Value.ShouldBeOfType<CampaignCloseBlocked>();
        blocked.Errors["eligibility"].ShouldBe(
        [
            "Every assigned participant must remain eligible for their team. Ineligible assignment ids: 9."
        ]);
    }

    /// <summary>
    /// Verifies an assigned participation referencing an archived team blocks closure.
    /// </summary>
    [Fact]
    public void Evaluate_BlocksClosure_WhenAssignedTeamIsArchived()
    {
        var result = CampaignClosurePolicy.Evaluate(
        [
            CreateState(
                10,
                PlacementOutcome.Assigned,
                playerYear: 2030,
                teamId: 10,
                teamYear: 2029,
                teamStatus: LifecycleStatus.Archived)
        ]);

        var blocked = result.Value.ShouldBeOfType<CampaignCloseBlocked>();
        blocked.Errors.ShouldContainKey("archivedTeams");
    }

    /// <summary>
    /// Verifies archived team facts do not affect a participation without an assigned outcome.
    /// </summary>
    [Fact]
    public void Evaluate_AllowsClosure_WhenArchivedTeamIsNotAssignedOutcome()
    {
        var result = CampaignClosurePolicy.Evaluate(
        [
            CreateState(
                11,
                PlacementOutcome.NotSelected,
                playerYear: 2030,
                teamId: 10,
                teamYear: 2029,
                teamStatus: LifecycleStatus.Archived)
        ]);

        result.Value.ShouldBeOfType<CampaignMayClose>();
    }

    /// <summary>
    /// Verifies closure returns every applicable blocker group in one decision.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsAllBlockerGroups_WhenMultipleRulesFail()
    {
        CampaignAssignmentClosureState[] states =
        [
            CreateState(20, PlacementOutcome.Undecided),
            CreateState(30, PlacementOutcome.Assigned, playerYear: 2029, teamId: 10, teamYear: 2030),
            CreateState(
                40,
                PlacementOutcome.Assigned,
                playerYear: 2030,
                teamId: 11,
                teamYear: 2029,
                teamStatus: LifecycleStatus.Archived)
        ];

        var result = CampaignClosurePolicy.Evaluate(states);

        var blocked = result.Value.ShouldBeOfType<CampaignCloseBlocked>();
        blocked.Errors.Keys.ShouldBe(["outcomes", "eligibility", "archivedTeams"]);
        blocked.UndecidedCount.ShouldBe(1);
        blocked.IneligibleCount.ShouldBe(1);
        blocked.ArchivedTeamCount.ShouldBe(1);
    }

    /// <summary>
    /// Creates one campaign assignment closure snapshot for a policy test.
    /// </summary>
    /// <param name="assignmentId">The participation identifier.</param>
    /// <param name="outcome">The placement outcome.</param>
    /// <param name="playerYear">The player graduation year.</param>
    /// <param name="teamId">The optional team identifier.</param>
    /// <param name="teamYear">The optional team graduation year.</param>
    /// <param name="teamStatus">The optional team lifecycle status.</param>
    /// <returns>The requested closure-state value.</returns>
    private static CampaignAssignmentClosureState CreateState(
        long assignmentId,
        PlacementOutcome outcome,
        int playerYear = 2030,
        long? teamId = null,
        int? teamYear = null,
        LifecycleStatus? teamStatus = null)
        => new(assignmentId, outcome, playerYear, teamId, teamYear, teamStatus);
}
