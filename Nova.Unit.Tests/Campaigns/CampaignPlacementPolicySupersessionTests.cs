using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementPolicyTests
{
    /// <summary>Checks all accepted outcomes against ordinary saved decisions in owning and later campaigns.</summary>
    /// <param name="prior">The existing outcome.</param>
    /// <param name="local">Whether the decision belongs to the target campaign.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Undecided, false)]
    [InlineData(PlacementOutcome.Assigned, false)]
    [InlineData(PlacementOutcome.Assigned, true)]
    [InlineData(PlacementOutcome.NotSelected, false)]
    [InlineData(PlacementOutcome.NotSelected, true)]
    public void Evaluate_AllowsEverySavedOutcome_ForEligibleDecisionHistory(PlacementOutcome prior, bool local)
    {
        foreach (var requested in new[] { PlacementOutcome.Assigned, PlacementOutcome.NotSelected, PlacementOutcome.Withdrawn })
        {
            var facts = DecisionFacts(prior, local, requested);
            var result = CampaignPlacementPolicy.Evaluate(facts).Value.ShouldBeOfType<PlacementMayApply>();
            result.IsNoOp.ShouldBe(local && prior == requested);
            result.IsSupersession.ShouldBe(!local && prior != PlacementOutcome.Undecided);
        }
    }

    /// <summary>Checks ownership and administrator authority distinguish withdrawal replacement from supersession.</summary>
    /// <param name="local">Whether withdrawal belongs to the target.</param>
    /// <param name="admin">Whether the actor is an administrator.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Evaluate_EnforcesWithdrawalMatrix_ForEveryRequestedOutcome(bool local, bool admin)
    {
        foreach (var requested in new[] { PlacementOutcome.Assigned, PlacementOutcome.NotSelected, PlacementOutcome.Withdrawn })
        {
            var facts = DecisionFacts(PlacementOutcome.Withdrawn, local, requested) with { IsClubAdmin = admin };
            var value = CampaignPlacementPolicy.Evaluate(facts).Value;
            if (local && requested == PlacementOutcome.Withdrawn)
            {
                value.ShouldBeOfType<PlacementMayApply>().IsNoOp.ShouldBeTrue();
            }
            else if (local)
            {
                value.ShouldBeOfType<PlacementWithdrawalTerminal>();
            }
            else if (!admin)
            {
                value.ShouldBeOfType<PlacementWithdrawalRequiresAdmin>();
            }
            else
            {
                value.ShouldBeOfType<PlacementMayApply>().IsSupersession.ShouldBeTrue();
            }
        }
    }

    /// <summary>Checks classification is independent of technical enrollment and invalid prior assignments stay unresolved.</summary>
    /// <param name="outcome">The latest saved outcome.</param>
    /// <param name="local">Whether the source owns the target campaign.</param>
    /// <param name="validTeam">Whether the selected effective team is valid.</param>
    /// <param name="expected">The expected ordinary-placement eligibility.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Undecided, false, false, (int)PlacementEligibility.NeedsDecision)]
    [InlineData(PlacementOutcome.Assigned, false, true, (int)PlacementEligibility.OptionalReassignment)]
    [InlineData(PlacementOutcome.Assigned, true, true, (int)PlacementEligibility.OptionalReassignment)]
    [InlineData(PlacementOutcome.Assigned, false, false, (int)PlacementEligibility.NeedsDecision)]
    [InlineData(PlacementOutcome.Assigned, true, false, (int)PlacementEligibility.NeedsDecision)]
    [InlineData(PlacementOutcome.NotSelected, true, false, (int)PlacementEligibility.Resolved)]
    [InlineData(PlacementOutcome.NotSelected, false, false, (int)PlacementEligibility.NeedsDecision)]
    [InlineData(PlacementOutcome.Withdrawn, true, false, (int)PlacementEligibility.Unavailable)]
    [InlineData(PlacementOutcome.Withdrawn, false, false, (int)PlacementEligibility.Unavailable)]
    public void GetEligibility_ClassifiesLatestDecision(PlacementOutcome outcome, bool local, bool validTeam, int expected)
    {
        var facts = DecisionFacts(outcome, local, PlacementOutcome.NotSelected) with { EffectiveTeamIsValid = validTeam };
        CampaignPlacementPolicy.GetEligibility(facts).ShouldBe((PlacementEligibility)expected);
    }

    /// <summary>Checks prior season history cannot impose withdrawal restrictions.</summary>
    [Fact]
    public void Evaluate_IgnoresPreviousSeasonWithdrawal()
    {
        var facts = DecisionFacts(PlacementOutcome.Withdrawn, false, PlacementOutcome.Assigned);
        facts = facts with { LatestDecision = facts.LatestDecision! with { SeasonId = 99 } };
        var result = CampaignPlacementPolicy.Evaluate(facts).Value.ShouldBeOfType<PlacementMayApply>();
        result.IsSupersession.ShouldBeFalse();
        result.IsNoOp.ShouldBeFalse();
        CampaignPlacementPolicy.GetEligibility(facts).ShouldBe(PlacementEligibility.NeedsDecision);
    }

    /// <summary>Checks neither a later decision nor equal opening order can be overwritten from another campaign.</summary>
    /// <param name="sourceSequence">The existing source opening sequence.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(10L)]
    [InlineData(11L)]
    public void Evaluate_RejectsNonLaterSupersession(long sourceSequence)
    {
        var facts = DecisionFacts(PlacementOutcome.Assigned, false, PlacementOutcome.NotSelected);
        facts = facts with { LatestDecision = facts.LatestDecision! with { SeasonOpeningSequence = sourceSequence } };
        CampaignPlacementPolicy.Evaluate(facts).Value.ShouldBeOfType<PlacementSeasonConflict>();
    }

    /// <summary>Checks a non-current season rejects mutation and ordinary placement eligibility.</summary>
    [Fact]
    public void Evaluate_RejectsNonCurrentSeason()
    {
        var facts = DecisionFacts(PlacementOutcome.Undecided, false, PlacementOutcome.Assigned) with { IsCurrentSeason = false };
        CampaignPlacementPolicy.Evaluate(facts).Value.ShouldBeOfType<PlacementSeasonConflict>();
        CampaignPlacementPolicy.GetEligibility(facts).ShouldBe(PlacementEligibility.Unavailable);
    }

    /// <summary>Builds immutable decision facts with valid requested team state.</summary>
    /// <param name="prior">The existing decision outcome, or Undecided for no saved decision.</param>
    /// <param name="local">Whether the decision belongs to the target.</param>
    /// <param name="requested">The new outcome.</param>
    /// <returns>A self-contained policy context.</returns>
    private static PlacementDecisionContext DecisionFacts(PlacementOutcome prior, bool local, PlacementOutcome requested)
        => new(CampaignStatus.Active, LifecycleStatus.Active, 2030, requested == PlacementOutcome.Assigned,
            true, LifecycleStatus.Active, 2029)
        {
            CampaignId = 100,
            SeasonId = 200,
            SeasonOpeningSequence = 10,
            RequestedOutcome = requested,
            RequestedTeamId = requested == PlacementOutcome.Assigned ? 400 : null,
            LatestDecision = prior == PlacementOutcome.Undecided ? null : new CampaignSavedPlacementDecision(
                300, 500, local ? 100 : 101, 200, local ? 10 : 9, prior,
                prior == PlacementOutcome.Assigned ? 400 : null,
                DateTimeOffset.UnixEpoch, 600, "Member", Guid.Parse("2ba6aefa-c6e8-4892-bd4f-85fbd0c54122"))
        };
}
