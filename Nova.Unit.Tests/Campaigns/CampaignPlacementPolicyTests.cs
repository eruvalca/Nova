using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign placement lifecycle and eligibility decisions without persistence dependencies.
/// </summary>
public sealed class CampaignPlacementPolicyTests
{
    /// <summary>
    /// Verifies a closed campaign has the highest rejection precedence.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsCampaignClosed_WhenMultipleFactsReject()
    {
        var result = CampaignPlacementPolicy.Evaluate(
            CreateContext(
                campaignStatus: CampaignStatus.Closed,
                playerStatus: LifecycleStatus.Archived,
                teamRequested: true,
                teamFound: false));

        result.Value.ShouldBeOfType<PlacementCampaignClosed>();
    }

    /// <summary>
    /// Verifies an archived player is rejected before requested-team state.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsPlayerArchived_WhenPlayerIsArchived()
    {
        var result = CampaignPlacementPolicy.Evaluate(
            CreateContext(
                playerStatus: LifecycleStatus.Archived,
                teamRequested: true,
                teamFound: false));

        result.Value.ShouldBeOfType<PlacementPlayerArchived>();
    }

    /// <summary>
    /// Verifies a missing tenant-visible team is rejected.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsTeamUnavailable_WhenRequestedTeamWasNotFound()
    {
        var result = CampaignPlacementPolicy.Evaluate(
            CreateContext(teamRequested: true, teamFound: false));

        result.Value.ShouldBeOfType<PlacementTeamUnavailable>();
    }

    /// <summary>
    /// Verifies an archived requested team is rejected.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsTeamArchived_WhenRequestedTeamIsArchived()
    {
        var result = CampaignPlacementPolicy.Evaluate(
            CreateContext(
                teamRequested: true,
                teamFound: true,
                teamStatus: LifecycleStatus.Archived,
                teamYear: 2029));

        result.Value.ShouldBeOfType<PlacementTeamArchived>();
    }

    /// <summary>
    /// Verifies a player below the team's graduation-year cutoff is rejected.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsTeamIneligible_WhenPlayerYearIsBelowTeamYear()
    {
        var result = CampaignPlacementPolicy.Evaluate(
            CreateContext(
                playerYear: 2029,
                teamRequested: true,
                teamFound: true,
                teamYear: 2030));

        result.Value.ShouldBeOfType<PlacementTeamIneligible>();
    }

    /// <summary>
    /// Verifies graduation-year equality satisfies placement eligibility.
    /// </summary>
    [Fact]
    public void Evaluate_AllowsPlacement_WhenGraduationYearsAreEqual()
    {
        var result = CampaignPlacementPolicy.Evaluate(
            CreateContext(
                playerYear: 2030,
                teamRequested: true,
                teamFound: true,
                teamYear: 2030));

        result.Value.ShouldBeOfType<PlacementMayApply>();
    }

    /// <summary>
    /// Verifies a non-assigned outcome does not require team facts.
    /// </summary>
    [Fact]
    public void Evaluate_AllowsPlacement_WhenNoTeamWasRequested()
    {
        var result = CampaignPlacementPolicy.Evaluate(CreateContext());

        result.Value.ShouldBeOfType<PlacementMayApply>();
    }

    /// <summary>
    /// Creates placement decision facts with valid active defaults.
    /// </summary>
    /// <param name="campaignStatus">The campaign status.</param>
    /// <param name="playerStatus">The player lifecycle status.</param>
    /// <param name="playerYear">The player graduation year.</param>
    /// <param name="teamRequested">Whether a team was requested.</param>
    /// <param name="teamFound">Whether the requested team was found.</param>
    /// <param name="teamStatus">The requested team's lifecycle status.</param>
    /// <param name="teamYear">The requested team's graduation year.</param>
    /// <returns>The requested placement decision context.</returns>
    private static PlacementDecisionContext CreateContext(
        CampaignStatus campaignStatus = CampaignStatus.Active,
        LifecycleStatus playerStatus = LifecycleStatus.Active,
        int playerYear = 2030,
        bool teamRequested = false,
        bool teamFound = false,
        LifecycleStatus? teamStatus = null,
        int? teamYear = null)
        => new(
            campaignStatus,
            playerStatus,
            playerYear,
            teamRequested,
            teamFound,
            teamStatus,
            teamYear);
}
