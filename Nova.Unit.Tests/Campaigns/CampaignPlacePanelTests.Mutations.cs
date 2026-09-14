using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    public async Task TheSaveGateStaysClosedUntilTheAuthoritativeReconciliationCompletesAsync()
    {
        // A committed save still has to replace the dirty draft with the authoritative row. Releasing the gate
        // before that happens re-enables Save against the pre-save row while it carries the replacement token,
        // so a second click - or the browser's retry loop - could dispatch a duplicate mutation.
        var queries = RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        var (entered, release) = HoldNextQueueRead(queries);

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        SaveButton(cut).HasAttribute("disabled").ShouldBeFalse();
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());

        // The mutation committed while the authoritative reconciliation is still in flight. The draft is still
        // the pre-save one carrying the replacement token, so an open gate would offer a second mutation.
        _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
        SaveButton(cut).HasAttribute("disabled").ShouldBeTrue();

        await cut.InvokeAsync(release);
        await save;
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Placement saved."));
        _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADiscoveryChangeRaisedDuringASaveReachesTheUrlOwnerAsync()
    {
        // A callback already dispatched before the controls became disabled must still reach the URL owner.
        // Browser history can likewise supply a new state during settlement; disabling the controls does not
        // replace that existing deferral contract.
        var queries = RegisterServices();
        CampaignWorkspacePlacementState? raised = null;
        var cut = RenderPanel(selectedParticipantId: 301, onStateChanged: state => raised = state);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        var (entered, release) = HoldNextQueueRead(queries);

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());

        await cut.Find("#roster-outcome").ChangeAsync(new ChangeEventArgs { Value = "assigned" });

        raised.ShouldNotBeNull();
        raised.Outcome.ShouldBe("assigned");

        await cut.InvokeAsync(release);
        await save;
    }

    [Fact]
    public async Task DiscoveryControlsStayDisabledDuringMutationAndSettlementAsync()
    {
        var queries = RegisterServices();
        var mutation = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(mutation.Task);
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        var (entered, release) = HoldNextQueueRead(queries);

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        try
        {
            await cut.WaitForAssertionAsync(() => cut.Find("#roster-outcome").HasAttribute("disabled").ShouldBeTrue());
            cut.FindAll(".place-queue input, .place-queue select").ShouldAllBe(control => control.HasAttribute("disabled"));

            await cut.InvokeAsync(() => mutation.SetResult(new PlacementMutationSuccess(_replacementToken)));
            await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());
            cut.FindAll(".place-queue input, .place-queue select").ShouldAllBe(control => control.HasAttribute("disabled"));
        }
        finally
        {
            await cut.InvokeAsync(() => mutation.TrySetResult(new PlacementMutationSuccess(_replacementToken)));
            await cut.InvokeAsync(release);
            await save;
        }

        await cut.WaitForAssertionAsync(() => cut.Find("#roster-outcome").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#roster-search").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void CompatibleTeamChoicesShowObservedCountsWithoutInventingMissingTeamCounts()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                LocalDecision = CreateDecision(301, PlacementOutcome.Assigned, 99),
                LocalTeam = new CampaignParticipantTeamSummaryDto(99, "Outside search")
            }
        ]);
        _teams.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<IReadOnlyList<TeamRosterItem>>(new List<TeamRosterItem>
            {
                new TeamRosterItem
                {
                    TeamId = 21, Name = "Elite Silver", GraduationYear = 2032,
                    LifecycleStatus = LifecycleStatus.Active, ActivePlacementCount = 8,
                    EffectiveCurrentSeasonPlacementCount = 16, CurrentCampaignPlacementContribution = 3
                },
                new TeamRosterItem
                {
                    TeamId = 22, Name = "Empty team", GraduationYear = 2032,
                    LifecycleStatus = LifecycleStatus.Active, ActivePlacementCount = 0,
                    EffectiveCurrentSeasonPlacementCount = 0, CurrentCampaignPlacementContribution = 0
                }
            }));

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-team").Count.ShouldBe(1));
        Collapse(cut.Find("#place-team option[value='21']")).ShouldBe("Elite Silver · 16 this season · 3 from this campaign");
        Collapse(cut.Find("#place-team option[value='22']")).ShouldBe("Empty team · 0 this season · 0 from this campaign");
        Collapse(cut.Find("#place-team option[value='99']")).ShouldBe("Outside search");
        cut.FindAll("#place-team-counts").ShouldBeEmpty();
        cut.Find("#place-team").Change("21");
        Collapse(cut.Find("#place-team-counts")).ShouldBe("Observed placements: 16 this season · 3 from this campaign.");
    }

    [Fact]
    public async Task AnUnconfirmedSaveStillAppliesTheDiscoveryChangeItDeferredAsync()
    {
        // A lost transport leaves the outcome unknown, but the discovery change deferred while the save was in
        // flight still belongs to the URL. Skipping it would leave the controls describing a state the queue
        // was never read for.
        var queries = RegisterServices();
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<PlacementMutationSuccess>>>(_ => throw new HttpRequestException("lost"));

        var (entered, release) = HoldNextQueueRead(queries);

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());

        ReRender(cut, new CampaignWorkspacePlacementState { Outcome = "assigned" }, selectedParticipantId: 301);

        await cut.InvokeAsync(release);
        await save;

        await cut.WaitForAssertionAsync(() => cut.Find("#roster-outcome").GetAttribute("value").ShouldBe("assigned"));
    }

    [Fact]
    public async Task ALifecycleSwapDropsTheEvidenceItReplacedBeforeTheNewReadAnswersAsync()
    {
        // Active and Closed are different endpoints with different shapes, so the posture being left must not
        // keep rendering its rows and sheet while the replacement read is still in flight.
        var queries = RegisterServices(rows: [CreateRow(301)]);
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery"));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                    new PlacementCampaignIdentity(10, "Summer Tryouts", CampaignStatus.Closed, new PlacementSeasonIdentity(5, "2026")),
                    new PagedResult<ClosedCampaignRosterItem>([CreateClosedRow()], 1, 50, 1))
                {
                    ParticipantCount = 1
                });
            });

        ReRender(cut, new CampaignWorkspacePlacementState(), selectedParticipantId: 301, status: CampaignStatus.Closed);

        await cut.WaitForAssertionAsync(() => entered.Task.IsCompleted.ShouldBeTrue());
        cut.Markup.ShouldNotContain("Avery");

        await cut.InvokeAsync(() => release.SetResult());
    }

    [Fact]
    public async Task AnUnconfirmedSaveThatCouldNotBeRefreshedDoesNotClaimItWasAsync()
    {
        // The message is the only thing standing between an unknown outcome and the next decision, so it may
        // only claim a refresh when the authoritative regions were actually re-read.
        var queries = RegisterServices();
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<PlacementMutationSuccess>>>(_ => throw new HttpRequestException("lost"));
        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline")));

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        await SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("could not be refreshed"));
        cut.Markup.ShouldNotContain("This view was refreshed from the server");
    }

    [Fact]
    public async Task ALifecycleChangeDuringASaveIsReconciledOnceTheSaveSettlesAsync()
    {
        // Closure and authority changes are deferred with the discovery state rather than dropped with it: the
        // posture being left must not stay rendered behind a save that is still in flight.
        var queries = RegisterServices(rows: [CreateRow(301)]);
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery"));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                    new PlacementCampaignIdentity(10, "Summer Tryouts", CampaignStatus.Closed, new PlacementSeasonIdentity(5, "2026")),
                    new PagedResult<ClosedCampaignRosterItem>([CreateClosedRow() with { FirstName = "Closed", LastName = "Evidence" }], 1, 50, 1))
                {
                    ParticipantCount = 1
                });
            });

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());

        ReRender(cut, new CampaignWorkspacePlacementState(), selectedParticipantId: 301, status: CampaignStatus.Closed);
        cut.Markup.ShouldNotContain("Avery");

        await cut.InvokeAsync(() => release.SetResult());
        await save;

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Closed Evidence"));
    }

    [Fact]
    public async Task ASupersededReconciliationDoesNotClaimThePageWasCorrectedAsync()
    {
        // A superseded read and a corrected page both leave the settlement without a fresh snapshot, but only one
        // of them is a page correction. Reporting the wrong one tells the member their page moved when it did not.
        var queries = RegisterServices(rows: [CreateRow(301)]);
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery"));

        var (entered, release) = HoldNextQueueRead(queries);

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());

        // The posture changes while the reconciliation is still in flight, which supersedes it.
        ReRender(cut, new CampaignWorkspacePlacementState(), selectedParticipantId: 301, status: CampaignStatus.Closed);

        await cut.InvokeAsync(release);
        await save;

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("This view is reloading from the server"));
        cut.Markup.ShouldNotContain("page was corrected");
    }

    [Fact]
    public void ThePersistedOwnerCarriesTheLifecycleEvenWhenTheHostOmitsIt()
    {
        // Active and Closed read different endpoints with different shapes, so a snapshot keyed without the
        // lifecycle could restore one posture's rows as the other's and stamp them as current without refetching.
        RegisterServices();
        var cut = RenderPanel(owner: "101:42:10");

        cut.WaitForAssertion(() => cut.Instance.PersistedOwner.ShouldNotBeNull());
        cut.Instance.PersistedOwner.ShouldEndWith(":Active");
    }

    [Fact]
    public void ASavedTeamOutsideTheNarrowedChoicesStaysSelectableWhenNoCorrectionApplies()
    {
        // Absence from the choices can mean the current search or the 200-row cap excluded the team, so only the
        // authoritative correction reason may disable it or call it unavailable.
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                LocalDecision = CreateDecision(301, PlacementOutcome.Assigned, 99),
                LocalTeam = new CampaignParticipantTeamSummaryDto(99, "Elite Silver"),
                CorrectionReason = PlacementCorrectionReason.None
            }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Assigned));

        cut.WaitForAssertion(() => cut.FindAll("#place-team option").Count.ShouldBe(3));
        var saved = cut.FindAll("#place-team option")
            .Single(option => string.Equals(option.GetAttribute("value"), "99", StringComparison.Ordinal));

        saved.HasAttribute("disabled").ShouldBeFalse();
        saved.TextContent.ShouldNotContain("no longer available");
    }

    [Fact]
    public async Task NarrowingTheTeamSearchDropsADraftedTeamTheControlNoLongerOffersAsync()
    {
        // The read replaces the select while it runs, so a drafted team that is not the persisted one must not
        // stay submittable behind a control the member cannot see.
        RegisterServices();
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.Assigned) });
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-team option").Count.ShouldBe(2));
        await cut.Find("#place-team").ChangeAsync(new ChangeEventArgs { Value = "21" });
        SaveButton(cut).HasAttribute("disabled").ShouldBeFalse();

        await cut.Find("#place-team-search").ChangeAsync(new ChangeEventArgs { Value = "No such team" });

        await cut.WaitForAssertionAsync(() => SaveButton(cut).HasAttribute("disabled").ShouldBeTrue());
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
    public void CompatibleTeamChoicesUseThePolicyCutoffRatherThanAnExactYear()
    {
        // Placement policy refuses a player whose year precedes the team's, so a team is compatible when its
        // cutoff is at or below the player's year. Querying the player's year exactly would hide every valid
        // lower-cutoff team and mark a compatible saved team as no longer available.
        RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));

        _ = _teams.Received().GetRosterAsync(
            Arg.Is<GetTeamRosterInput>(input => input.LifecycleStatus == "active"
                && input.MaxGraduationYear == 2032
                && input.GraduationYear == null
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

    [Fact]
    public void APriorCampaignWithdrawalOffersNoOrdinaryDecisionControls()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                Eligibility = EffectivePlacementEligibility.Unavailable,
                EffectiveDecision = new PlacementDecisionSource(
                    CreateDecision(205, PlacementOutcome.Withdrawn, null) with { CampaignId = 9 },
                    "Earlier campaign", null)
            }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll(".place-name").Count.ShouldBe(1));
        cut.FindAll("#place-outcome, #place-team, .place-decision-actions").ShouldBeEmpty();
        cut.Find(".place-sheet").TextContent.ShouldContain("Administrator recovery of a prior-campaign withdrawal is not available here.");
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static AngleSharp.Dom.IElement SaveButton(IRenderedComponent<Nova.UI.Features.Campaigns.Components.CampaignPlacePanel> cut)
        => cut.FindAll("button").Single(button => button.TextContent.Contains("Save placement", StringComparison.Ordinal));
}
