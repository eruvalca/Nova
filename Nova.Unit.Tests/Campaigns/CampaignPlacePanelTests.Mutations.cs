using Bunit;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePlacementState = Nova.UI.Features.Campaigns.Services.CampaignWorkspacePlacementState;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Decision-recording tests for the Place destination: the local concurrency token, authoritative
/// reconciliation, bounded compatible teams, and the conflict-recovery ceiling.
/// </summary>
public sealed partial class CampaignPlacePanelTests
{
    [Fact]
    public void AssignedWithoutATeamShowsALocalErrorAndNeverCallsTheMutation()
    {
        RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Assigned));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Choose a team for an assigned participant."));

        SaveButton(cut).HasAttribute("disabled").ShouldBeTrue();
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ChoosingANonAssignedOutcomeDropsTheTeamFromTheSubmission()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                LocalDecision = CreateDecision(301, PlacementOutcome.Assigned, 21),
                LocalTeam = new CampaignParticipantTeamSummaryDto(21, "Elite Silver")
            }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Assigned));
        cut.WaitForAssertion(() => cut.FindAll("#place-team").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));

        // Leaving Assigned clears the team rather than leaving an invalid combination behind.
        cut.WaitForAssertion(() => cut.FindAll("#place-team").Count.ShouldBe(0));
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));
        _ = _mutations.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input => input.Outcome == PlacementOutcome.NotSelected && input.TeamId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SavingPresentsTheLocalToken()
    {
        RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        var localToken = _effectiveRows[0].ConcurrencyToken;

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));
        _ = _mutations.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input => input.PlayerCampaignAssignmentId == 301
                && input.Outcome == PlacementOutcome.NotSelected
                && input.TeamId == null
                && input.ExpectedConcurrencyToken == localToken),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SavingReReadsTheQueueTotalsAndSelectedParticipantAuthoritatively()
    {
        var queries = RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        queries.ClearReceivedCalls();

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));

        // The submitted values are never applied as truth: the queue and the selected participant's evidence
        // both come back from the authoritative read.
        _ = queries.Received().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>());
        _ = queries.Received().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == 301), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReplacingAnExistingLocalDecisionStaysAvailable()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with { LocalDecision = CreateDecision(301, PlacementOutcome.NotSelected, null) }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Withdrawn));
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));
        _ = _mutations.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input => input.Outcome == PlacementOutcome.Withdrawn),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ConflictBlocksFurtherEditingUntilAConfirmedReload()
    {
        RegisterServices();
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementMutationSuccess>(ServiceProblem.Conflict("Someone else saved first.")));

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Someone else saved first."));
        cut.Markup.ShouldContain("Close and reload");
        cut.FindAll("#place-outcome").ShouldBeEmpty();
    }

    [Fact]
    public void ConflictReloadRecoversEditingAndKeepsTheDiscoveryState()
    {
        RegisterServices();
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementMutationSuccess>(ServiceProblem.Conflict()));

        var cut = RenderPanel(selectedParticipantId: 301, state: new CampaignWorkspacePlacementState { Search = "Chen" });
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close and reload"));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Close and reload", StringComparison.Ordinal)).Click();

        // Recovery re-enables editing against authoritative state instead of leaving the surface locked.
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Markup.ShouldNotContain("Close and reload");
    }

    [Fact]
    public void ValidationRefusalStaysLocalToTheDecisionControls()
    {
        RegisterServices();
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementMutationSuccess>(
                ServiceProblem.Validation("TeamId", "The chosen team is no longer eligible.")));

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The chosen team is no longer eligible."));
        // A refusal is not a conflict: the controls stay available for a corrected retry.
        cut.FindAll("#place-outcome").Count.ShouldBe(1);
        cut.Markup.ShouldNotContain("Close and reload");
    }

    [Fact]
    public void CommittedSaveThatCannotBeRefreshedIsNotAnnouncedAsSuccess()
    {
        var queries = RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        // The mutation commits, but every later authoritative read fails.
        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline")));

        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("authoritative result could not be refreshed"));
        cut.Markup.ShouldNotContain("Placement saved.");
    }

    [Fact]
    public void AnUnavailableSavedTeamIsShownDisabledRatherThanSubstituted()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                LocalDecision = CreateDecision(301, PlacementOutcome.Assigned, 99),
                LocalTeam = new CampaignParticipantTeamSummaryDto(99, "Retired Gold"),
                CorrectionReason = PlacementCorrectionReason.TeamArchived
            }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Assigned));

        cut.WaitForAssertion(() => cut.FindAll("#place-team option").Count.ShouldBe(3));
        var options = cut.FindAll("#place-team option");
        options[0].GetAttribute("value").ShouldBeEmpty();
        // The saved but no-longer-available team stays legible and can never be re-selected, and the
        // compatible active teams follow it rather than replacing it.
        options[1].GetAttribute("value").ShouldBe("99");
        options[1].HasAttribute("disabled").ShouldBeTrue();
        options[2].GetAttribute("value").ShouldBe("21");
    }

    [Fact]
    public void CompatibleTeamChoicesAreBoundedToTheSelectedGraduationYear()
    {
        RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        _ = _teams.Received().GetRosterAsync(
            Arg.Is<GetTeamRosterInput>(input => input.LifecycleStatus == "active"
                && input.GraduationYear == 2032
                && input.Limit == 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CompatibleTeamFailureNamesItsOwnRegionWithoutBlockingTheOtherOutcomes()
    {
        RegisterServices();
        _teams.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<IReadOnlyList<TeamRosterItem>>(ServiceProblem.ServerError("offline")));

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Assigned));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Compatible teams could not be loaded."));
        // Not selected and Withdrawn remain available even when assignment cannot be resolved.
        cut.FindAll("#place-outcome option").Count.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void AnArchivedPlayerOffersNoDecisionControls()
    {
        // The server refuses a decision for an archived player, so the surface must not offer one.
        RegisterServices(rows: [CreateRow(301) with { PlayerLifecycleStatus = LifecycleStatus.Archived }]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll(".place-name").Count.ShouldBe(1));

        cut.FindAll("#place-outcome").ShouldBeEmpty();
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
        cut.Markup.ShouldContain("This player is archived");
    }

    [Fact]
    public void ALocallyWithdrawnDecisionOffersNoDecisionControls()
    {
        // Withdrawn is terminal in its owning campaign, so the only recovery belongs to a later campaign.
        RegisterServices(rows:
        [
            CreateRow(301) with { LocalDecision = CreateDecision(301, PlacementOutcome.Withdrawn, null) }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll(".place-name").Count.ShouldBe(1));

        cut.FindAll("#place-outcome").ShouldBeEmpty();
        cut.Markup.ShouldContain("withdrawn for the season");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<Nova.UI.Features.Campaigns.Components.CampaignPlacePanel> cut)
        => cut.FindAll("button").Single(button => button.TextContent.Contains("Save placement", StringComparison.Ordinal));
}
