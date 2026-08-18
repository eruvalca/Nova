using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;
using CampaignPlacementsPanel = Nova.UI.Features.Campaigns.Components.CampaignPlacementsPanel;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign placements panel: loading/empty/error states, summary
/// rendering, read-only views, the per-row edit state machine, validation, token adoption,
/// conflict recovery, and the Closed transition.
/// </summary>
public sealed class CampaignPlacementsPanelTests : BunitContext
{
    // ── Loading, empty, error, retry ──────────────────────────────────────────

    [Fact]
    public void Panel_ShowsLoadingState_WhileRosterRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService);

        var cut = RenderPanel();
        cut.Markup.ShouldContain("Loading placements...");

        pending.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    [Fact]
    public void Panel_ShowsEmptyMessage_WhenNoPlacements()
    {
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(
            new PagedResult<CampaignPlacementRosterItem>([], 1, GetCampaignPlacementRosterInput.DefaultPageSize, 0)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No placements in this campaign yet."));
    }

    [Fact]
    public void Panel_ShowsErrorAndRetries_WhenRosterLoadFails()
    {
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(
                    ServiceProblem.ServerError("Service unavailable."))),
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Service unavailable."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    // ── Summary footer ────────────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersSummaryCounts_FromSummaryDto()
    {
        RegisterServices(summaryResult: new ServiceResult<CampaignPlacementSummaryDto>(
            CreateSummary(assigned: 2, notSelected: 3, withdrawn: 4, undecided: 5)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("div[role=status]").TextContent.ShouldContain("2 assigned");
        cut.Markup.ShouldContain("3 not selected");
        cut.Markup.ShouldContain("4 withdrawn");
        cut.Markup.ShouldContain("5 undecided");
    }

    // ── Read-only views ───────────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersStaticRows_AndFrozenBanner_WhenCampaignClosed()
    {
        RegisterServices();

        var cut = RenderPanel(status: CampaignStatus.Closed, canEdit: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Markup.ShouldContain("Placements are frozen.");
        cut.FindAll("select[aria-label^=\"Outcome for\"]").ShouldBeEmpty();
        cut.FindAll("select[aria-label^=\"Team for\"]").ShouldBeEmpty();
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
    }

    [Fact]
    public void Panel_RendersStaticRows_AndReadOnlyNote_ForNonAdminActiveCampaign()
    {
        RegisterServices();

        var cut = RenderPanel(status: CampaignStatus.Active, canEdit: false);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Markup.ShouldContain("Read-only");
        cut.FindAll("select[aria-label^=\"Outcome for\"]").ShouldBeEmpty();
        cut.FindAll("select[aria-label^=\"Team for\"]").ShouldBeEmpty();
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
    }

    [Fact]
    public void Panel_PlayerLink_CarriesPlacementsReturnUrl()
    {
        RegisterServices();

        var cut = RenderPanel(state: new CampaignWorkspacePlacementState { GraduationYear = 2032, UnresolvedOnly = true });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        var href = cut.Find("a[href*=\"/players/7\"]").GetAttribute("href")!;
        href.ShouldContain("returnUrl=");
        var returnUrl = Uri.UnescapeDataString(href[(href.IndexOf("returnUrl=", StringComparison.Ordinal) + "returnUrl=".Length)..]);
        returnUrl.ShouldBe("/campaigns/10?placementGraduationYear=2032&unresolvedOnly=true&tab=placements");
    }

    [Fact]
    public void Panel_RendersCardEquivalent_MarkingNarrowLayout()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.FindAll("div.d-md-none[aria-label=\"Campaign placements\"]").Count.ShouldBe(1);
        cut.FindAll("#placement-card-301").Count.ShouldBe(1);
        cut.FindAll("div.table-responsive").Count.ShouldBe(1);
    }

    // ── Per-row edit state machine ────────────────────────────────────────────

    [Fact]
    public void Save_SetsSavedState_AndAdoptsReturnedToken()
    {
        var token1 = Guid.NewGuid();
        var token2 = Guid.NewGuid();
        var item = CreateRosterItem(token: token1);
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(token2))));

        RegisterServices(placementService: placementService, rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster(item)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("1");
        cut.Find("select[aria-label=\"Team for Avery Johnson\"]").Change("21");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saved"));

        placementService.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input =>
                input.PlayerCampaignAssignmentId == item.PlayerCampaignAssignmentId
                && input.Outcome == PlacementOutcome.Assigned
                && input.TeamId == 21
                && input.ExpectedConcurrencyToken == token1),
            Arg.Any<CancellationToken>());

        // A second edit adopts the token returned by the first save.
        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => placementService.Received(2).UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>()));

        placementService.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input => input.ExpectedConcurrencyToken == token2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Save_ShowsValidationError_WhenAssignedWithoutTeam_AndBlocksSubmit()
    {
        var placementService = Substitute.For<ICampaignPlacementService>();
        RegisterServices(placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("1");
        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A team is required for an assigned outcome."));
        placementService.DidNotReceive().UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void OutcomeChange_ClearsTeam_AndDisablesTeamSelect_WhenLeavingAssigned()
    {
        var assignedTeam = new CampaignParticipantTeamSummaryDto(21, "Blue");
        var item = CreateRosterItem(outcome: PlacementOutcome.Assigned, team: assignedTeam);
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster(item)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Team for Avery Johnson\"]").HasAttribute("disabled").ShouldBeFalse();

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("0");
        cut.Find("select[aria-label=\"Team for Avery Johnson\"]").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("select[aria-label=\"Team for Avery Johnson\"]").GetAttribute("value").ShouldBeEmpty();
    }

    [Fact]
    public void TeamSelect_DisablesIneligibleTeams_WithIneligibleLabel()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        var goldOption = cut.FindAll("select[aria-label=\"Team for Avery Johnson\"] option")
            .Single(option => option.GetAttribute("value") == "22");
        goldOption.HasAttribute("disabled").ShouldBeTrue();
        goldOption.TextContent.ShouldContain("ineligible");

        var blueOption = cut.FindAll("select[aria-label=\"Team for Avery Johnson\"] option")
            .Single(option => option.GetAttribute("value") == "21");
        blueOption.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void TeamSelect_RendersDisabledCurrentTeamOption_WhenAssignedTeamIsMissingFromActiveChoices()
    {
        var archivedTeam = new CampaignParticipantTeamSummaryDto(99, "Legacy");
        var item = CreateRosterItem(outcome: PlacementOutcome.Assigned, team: archivedTeam);
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster(item)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        var currentOption = cut.FindAll("select[aria-label=\"Team for Avery Johnson\"] option")
            .Single(option => option.GetAttribute("value") == "99");
        currentOption.HasAttribute("disabled").ShouldBeTrue();
        currentOption.TextContent.ShouldContain("Legacy");
        currentOption.TextContent.ShouldContain("current team");
    }

    [Fact]
    public void Save_PreventsDuplicateSubmission_WhileSaving()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        placementService.Received(1).UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());

        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saved"));
    }

    [Fact]
    public void Save_RemovesRowFromUnresolvedView_WhenOutcomeLeavesUndecided()
    {
        var item = CreateRosterItem(outcome: PlacementOutcome.Undecided);
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster(item)));

        var cut = RenderPanel(state: new CampaignWorkspacePlacementState { UnresolvedOnly = true });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No placements match the current filters."));
    }

    // ── Filter/save race (finding 1) ────────────────────────────────────────

    [Fact]
    public void Panel_DisablesFiltersAndPager_WhileSaveIsInFlight_AndReEnablesAfter()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        // Two pages so the pager renders while the save is in flight.
        var roster = new PagedResult<CampaignPlacementRosterItem>(
            Items: [CreateRosterItem()],
            Page: 1,
            PageSize: GetCampaignPlacementRosterInput.DefaultPageSize,
            TotalCount: GetCampaignPlacementRosterInput.DefaultPageSize + 1);

        RegisterServices(
            placementService: placementService,
            rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(roster));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // The filter bar and pager are disabled while any row save is in flight.
        cut.Find("#placement-graduation-year").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#placement-unresolved-only").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("nav[aria-label=\"Roster pagination\"] button")
            .ShouldAllBe(button => button.HasAttribute("disabled"));

        // Completing the save re-enables the controls. The Previous button stays disabled on page 1.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Saving"));
        cut.Find("#placement-graduation-year").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#placement-unresolved-only").HasAttribute("disabled").ShouldBeFalse();
        var nextButton = cut.FindAll("nav[aria-label=\"Roster pagination\"] button")
            .Single(button => button.TextContent.Trim() == "Next");
        nextButton.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void SummaryRetryDuringSave_IsDisabledAndCannotRebuildDrafts_WhenSaveInFlight()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(
                    ServiceProblem.ServerError("Summary unavailable."))),
                Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Markup.ShouldContain("Couldn't load the placement summary.");

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // The summary-failure banner Retry is disabled while a save is in flight.
        var retry = cut.FindAll("button.btn-outline-warning")
            .Single(button => button.TextContent == "Retry");
        retry.HasAttribute("disabled").ShouldBeTrue();

        // Clicking it must not rebuild drafts out from under the in-flight save: the roster is
        // never requested again, and completing the save still lands on the saved row.
        retry.Click();
        queryService.Received(1).GetPlacementRosterAsync(
            Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());

        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saved"));
        cut.Markup.ShouldContain("Avery Johnson");
    }

    [Fact]
    public void ChoicesRetryDuringSave_IsDisabledAndCannotReloadRoster_WhenSaveInFlight()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        var teamRosterService = Substitute.For<ITeamRosterService>();
        teamRosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(
                ServiceProblem.ServerError("Teams unavailable."))));

        RegisterServices(
            placementQueryService: queryService,
            placementService: placementService,
            teamRosterService: teamRosterService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Couldn't load filter options."));
        cut.Markup.ShouldContain("Avery Johnson");

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // The choices-failure banner Retry is disabled while a save is in flight.
        var retry = cut.FindAll("button.btn-outline-warning")
            .Single(button => button.TextContent == "Retry");
        retry.HasAttribute("disabled").ShouldBeTrue();

        // Clicking it must not trigger a roster reload (which would rebuild drafts mid-save).
        retry.Click();
        queryService.Received(1).GetPlacementRosterAsync(
            Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());

        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saved"));
    }

    [Fact]
    public void Panel_DefersRosterReload_WhenStateChangesDuringSave_AndAppliesAfter()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Simulate a URL/navigation-driven state change while the save is in flight.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));

        // The roster reload must be deferred until the save completes.
        queryService.DidNotReceive().GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // Completing the save applies the pending state and reloads the roster for it.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => queryService.Received(1).GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Save_DeferredReloadNetworkFailure_SurfacesPanelError_AndDoesNotThrowFromFinally()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())),
                Task.FromException<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>(
                    new HttpRequestException("Network down.")));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Navigate to another placement state while the save is in flight; the reload is deferred.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));

        // The deferred reload fails on the network. The finally block must not throw (which would
        // replace the save's outcome or take the panel down); the failure surfaces as the panel
        // error instead, and the per-row save gate is still released.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Failed to reload placements. Please retry."));
    }

    [Fact]
    public void Save_DeferredReload_ShowsLoadingState_AndHidesRowControls_WhileReloadPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        // Two rows so a second row's Save button exists before the deferred reload begins.
        var roster = new PagedResult<CampaignPlacementRosterItem>(
            Items:
            [
                CreateRosterItem(),
                CreateRosterItem(displayName: "Blake Miller", firstName: "Blake", lastName: "Miller", assignmentId: 302, playerId: 8)
            ],
            Page: 1,
            PageSize: GetCampaignPlacementRosterInput.DefaultPageSize,
            TotalCount: 2);

        var deferredReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(roster)),
                deferredReload.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Blake Miller"));

        // Dirty both rows so both Save buttons are rendered before the deferred reload begins.
        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("select[aria-label=\"Outcome for Blake Miller\"]").Change("3");
        cut.FindAll("button.btn-primary").Count.ShouldBe(4);

        cut.FindAll("button.btn-primary")[0].Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Navigate to another placement state while the save is in flight; the reload is deferred.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));
        queryService.DidNotReceive().GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // Completing the save starts the deferred reload for the pending state.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => queryService.Received(1).GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>()));

        // While the deferred reload is pending, the loading state is shown and every row control
        // (including the second row's Save button) is hidden, so a second save cannot be dispatched
        // into the reload window and have its draft replaced when the reload rebuilds drafts.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Loading placements..."));
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Avery Johnson");
        cut.Markup.ShouldNotContain("Blake Miller");

        // Completing the reload renders the pending state's roster and clears the loading state.
        deferredReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(
            CreateRoster(CreateRosterItem(
                displayName: "Zoe Carter", firstName: "Zoe", lastName: "Carter", assignmentId: 303, graduationYear: 2033, playerId: 9))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Zoe Carter"));
        cut.Markup.ShouldNotContain("Loading placements...");
    }

    [Fact]
    public async Task Save_SecondRowSaveDispatchedDuringDeferredReload_IsNoOp()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        // Two rows so a second row's Save button exists before the deferred reload begins.
        var roster = new PagedResult<CampaignPlacementRosterItem>(
            Items:
            [
                CreateRosterItem(),
                CreateRosterItem(displayName: "Blake Miller", firstName: "Blake", lastName: "Miller", assignmentId: 302, playerId: 8)
            ],
            Page: 1,
            PageSize: GetCampaignPlacementRosterInput.DefaultPageSize,
            TotalCount: 2);

        var deferredReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(roster)),
                deferredReload.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Blake Miller"));

        // Dirty both rows so both Save buttons render before the deferred reload begins.
        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("select[aria-label=\"Outcome for Blake Miller\"]").Change("3");
        cut.FindAll("button.btn-primary").Count.ShouldBe(4);

        cut.FindAll("button.btn-primary")[0].Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Navigate to another placement state while the save is in flight; the reload is deferred.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));

        // Completing the save starts the deferred reload; the loading state hides the row controls.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Loading placements..."));

        // Dispatch a second row save into the loading window — the queued-click case the
        // _isLoading guard is the authoritative backstop for. The second row's Save button is
        // already gone from the DOM (exactly as in a real browser), so invoke the handler
        // directly: it must no-op, the placement service is never called a second time, and the
        // deferred reload's RebuildDrafts cannot detach a save that never started.
        await cut.InvokeAsync(() => cut.Instance.SaveRowAsync(roster.Items[1]));
        _ = placementService.Received(1).UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());

        // Completing the deferred reload renders the pending state's roster.
        deferredReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(
            CreateRoster(CreateRosterItem(
                displayName: "Zoe Carter", firstName: "Zoe", lastName: "Carter", assignmentId: 303, graduationYear: 2033, playerId: 9))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Zoe Carter"));
    }

    [Fact]
    public async Task Save_SupersedingNavigationDuringDeferredReload_KeepsLoadingUntilNewestReloadCompletes()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var deferredReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var supersedingReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())),
                deferredReload.Task,
                supersedingReload.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Navigate to another placement state while the save is in flight; the reload is deferred.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));
        _ = queryService.DidNotReceive().GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // Completing the save starts the deferred reload; the loading state hides the row controls.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Loading placements..."));
        _ = queryService.Received(1).GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // While the deferred reload is pending, a filter change supersedes it with a newer
        // direct-path reload. SetParametersAsync is invoked without awaiting it because the
        // newer reload is deliberately left pending so both requests are in flight at once.
        await cut.InvokeAsync(() =>
        {
            _ = cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(CampaignPlacementsPanel.State)] = new CampaignWorkspacePlacementState { GraduationYear = 2034 }
                }));
        });

        _ = queryService.Received(1).GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2034),
            Arg.Any<CancellationToken>());

        // The superseded deferred reload completes first. It must not release the loading guard —
        // the newest reload owns it now — so the post-save render still shows the loading state,
        // no row controls re-appear, and the stale roster is never applied.
        var rendersBeforeStaleCompletion = cut.RenderCount;
        deferredReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster()));
        cut.WaitForAssertion(() => cut.RenderCount.ShouldBeGreaterThan(rendersBeforeStaleCompletion));
        cut.Markup.ShouldContain("Loading placements...");
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Avery Johnson");

        // Completing the newest reload renders its roster and releases the loading state.
        supersedingReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(
            CreateRoster(CreateRosterItem(
                displayName: "Zoe Carter", firstName: "Zoe", lastName: "Carter", assignmentId: 303, graduationYear: 2034, playerId: 9))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Zoe Carter"));
        cut.Markup.ShouldNotContain("Loading placements...");
    }

    [Fact]
    public async Task Save_DispatchedDuringFilterChangeReload_IsNoOp()
    {
        var pendingReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())),
                pendingReload.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        // Make the row dirty so a save could be dispatched, then change the filter outside a
        // save. The direct-path reload holds the loading guard for its whole duration even
        // though the old roster (and its row controls) stays rendered until the reload completes.
        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.FindAll("button.btn-primary").Count.ShouldBe(2);

        await cut.InvokeAsync(() =>
        {
            _ = cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(CampaignPlacementsPanel.State)] = new CampaignWorkspacePlacementState { GraduationYear = 2033 }
                }));
        });

        _ = queryService.Received(1).GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // A save queued into the direct-reload window no-ops under the held loading guard and
        // never reaches the placement service (its draft would be replaced when the reload
        // rebuilds drafts).
        await cut.InvokeAsync(() => cut.Instance.SaveRowAsync(CreateRosterItem()));
        _ = placementService.DidNotReceive().UpdatePlacementAsync(
            Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());

        pendingReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(
            CreateRoster(CreateRosterItem(
                displayName: "Zoe Carter", firstName: "Zoe", lastName: "Carter", assignmentId: 303, graduationYear: 2033, playerId: 9))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Zoe Carter"));
    }

    [Fact]
    public void Panel_ClearsPendingState_WhenNavigationReturnsToAppliedStateDuringSave()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Navigate away while the save is in flight; the roster reload is deferred.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));
        queryService.DidNotReceive().GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // Navigate back to the state that produced the loaded roster while still saving.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState()));

        // Completing the save must not apply the stale deferred state: the roster stays on the
        // applied state and is never reloaded for the abandoned GraduationYear 2033.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saved"));
        queryService.DidNotReceive().GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClosedTransitionDuringSave_StillDefersStateChange_UntilSaveCompletes()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel(status: CampaignStatus.Active, canEdit: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // The campaign closes while a save is in flight and the parameter update also carries a
        // state change. ResetAllDrafts() replaces every draft with a fresh non-saving draft, so
        // the deferral must use the pre-reset in-flight flag.
        cut.Render(parameters => parameters
            .Add(panel => panel.CampaignStatus, CampaignStatus.Closed)
            .Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));

        // The roster reload is still deferred until the in-flight save completes.
        queryService.DidNotReceive().GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>());

        // Completing the save applies the deferred state and reloads the roster for it.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(new PlacementMutationSuccess(Guid.NewGuid())));
        cut.WaitForAssertion(() => queryService.Received(1).GetPlacementRosterAsync(
            Arg.Is<GetCampaignPlacementRosterInput>(input => input.GraduationYear == 2033),
            Arg.Any<CancellationToken>()));
    }

    // ── Summary failure surfacing (finding 2) ───────────────────────────────

    [Fact]
    public void Panel_ShowsSummaryWarning_AndRetryRecovers_WhenInitialSummaryLoadFails()
    {
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(
                    ServiceProblem.ServerError("Summary unavailable."))),
                Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        // A distinct warning replaces the summary footer, and the roster error alert is absent.
        cut.Markup.ShouldContain("Couldn't load the placement summary.");
        cut.FindAll("div.placement-summary[role=status]").ShouldBeEmpty();
        cut.FindAll("div.alert-danger").ShouldBeEmpty();

        // Retry reloads the summary along with roster and choices.
        cut.FindAll("button.btn-outline-warning")
            .Single(button => button.TextContent == "Retry")
            .Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("1 undecided"));
        cut.Markup.ShouldNotContain("Couldn't load the placement summary.");
    }

    [Fact]
    public void Save_ShowsSummaryRefreshWarning_WhenSummaryRefreshFailsAfterSave()
    {
        var item = CreateRosterItem(outcome: PlacementOutcome.Undecided);
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlacementMutationSuccess>(
                new PlacementMutationSuccess(Guid.NewGuid()))));

        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster(item))));
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary(undecided: 1))),
                Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(
                    ServiceProblem.ServerError("Summary unavailable."))));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();

        // The success banner is suppressed and a refresh warning with retry is shown instead.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved, but the summary could not be refreshed."));
        cut.FindAll("div.alert-success[role=status]").ShouldBeEmpty();
        cut.FindAll("div.placement-summary[role=status]").ShouldBeEmpty();
        cut.FindAll("button.btn-outline-warning")
            .Single(button => button.TextContent == "Retry")
            .ShouldNotBeNull();
    }

    // ── Bounded team choices (finding 3) ────────────────────────────────────

    [Fact]
    public void Panel_RequestsBoundedTeamChoices_WithDocumentedCap()
    {
        var teamRosterService = Substitute.For<ITeamRosterService>();
        teamRosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(CreateTeams().ToList())));

        RegisterServices(teamRosterService: teamRosterService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        teamRosterService.Received(1).GetRosterAsync(
            Arg.Is<GetTeamRosterInput>(input => input.LifecycleStatus == "active" && input.Limit == 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Panel_RendersTruncationNotice_WhenTeamChoicesReachCap()
    {
        var teams = Enumerable.Range(1, 200)
            .Select(index => new TeamRosterItem
            {
                TeamId = index,
                Name = $"Team {index}",
                GraduationYear = 2030,
                LifecycleStatus = LifecycleStatus.Active,
                ActivePlacementCount = 0
            })
            .ToList();

        RegisterServices(teams: teams);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Markup.ShouldContain("Showing the first 200 active teams.");
        cut.Markup.ShouldContain("refine via Team management");
    }

    [Fact]
    public void Panel_OmitsTruncationNotice_WhenTeamChoicesBelowCap()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Markup.ShouldNotContain("Showing the first 200 active teams.");
    }

    // ── Conflict recovery ─────────────────────────────────────────────────────

    [Fact]
    public void Conflict_ShowsWarning_BlocksSaves_AndReloadDiscardsDrafts()
    {
        var item = CreateRosterItem(outcome: PlacementOutcome.Undecided);
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlacementMutationSuccess>(
                ServiceProblem.Conflict("The placement was changed by another user."))));

        RegisterServices(
            placementService: placementService,
            rosterResult: new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster(item)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close and reload"));

        var warning = cut.Find("div.alert-warning[role=alert]");
        warning.GetAttribute("tabindex").ShouldBe("-1");
        cut.Markup.ShouldContain("The placement was changed by another user.");

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Close and reload"));
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
    }

    [Fact]
    public void Conflict_CloseAndReloadSupersedingDeferredReload_ReleasesLoadingGuard()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>();
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        var deferredReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var conflictReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())),
                deferredReload.Task,
                conflictReload.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Saving"));

        // Navigate to another placement state while the save is in flight; the reload is deferred.
        cut.Render(parameters => parameters.Add(panel => panel.State, new CampaignWorkspacePlacementState { GraduationYear = 2033 }));

        // The save fails with a conflict. The deferred reload for the pending state begins and
        // holds the loading guard; the conflict banner renders independently of the loading state.
        pending.SetResult(new ServiceResult<PlacementMutationSuccess>(
            ServiceProblem.Conflict("The placement was changed by another user.")));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close and reload"));
        cut.Markup.ShouldContain("Loading placements...");
        _ = queryService.Received(2).GetPlacementRosterAsync(
            Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());

        // While the deferred reload is pending, "Close and reload" supersedes it: the conflict
        // recovery reload advances the request sequence, so the deferred reload's conditional
        // release must not fire. Without the ownership fix this leaves the panel stuck on the
        // loading spinner, because the recovery never cleared _isLoading either.
        cut.Find("button.btn-outline-warning").Click();
        _ = queryService.Received(3).GetPlacementRosterAsync(
            Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());

        // The superseded deferred reload completes first. It must not release the loading guard —
        // the newest reload owns it now — so the loading state stays up and the stale roster is
        // never applied.
        var rendersBeforeStaleCompletion = cut.RenderCount;
        deferredReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster()));
        cut.WaitForAssertion(() => cut.RenderCount.ShouldBeGreaterThan(rendersBeforeStaleCompletion));
        cut.Markup.ShouldContain("Loading placements...");
        cut.Markup.ShouldNotContain("Avery Johnson");

        // The conflict reload completes as the newest request: it applies its roster and releases
        // the loading guard, so the panel is not stuck on the spinner and the conflict clears.
        conflictReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Markup.ShouldNotContain("Loading placements...");
        cut.Markup.ShouldNotContain("Close and reload");
    }

    [Fact]
    public async Task Conflict_CloseAndReloadSupersedingFilterChangeReload_ReleasesLoadingGuard()
    {
        var placementService = Substitute.For<ICampaignPlacementService>();
        placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlacementMutationSuccess>(
                ServiceProblem.Conflict("The placement was changed by another user."))));

        var filterReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var conflictReload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>();
        var queryService = Substitute.For<ICampaignPlacementQueryService>();
        queryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())),
                filterReload.Task,
                conflictReload.Task);
        queryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));

        RegisterServices(placementQueryService: queryService, placementService: placementService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close and reload"));

        // While the conflict is active, a filter change starts a direct-path reload that holds the
        // loading guard for its whole duration. It is deliberately left pending so the conflict
        // recovery can supersede it (the "Close and reload" button stays rendered in the browser
        // because no render happens until the reload completes).
        await cut.InvokeAsync(() =>
        {
            _ = cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(CampaignPlacementsPanel.State)] = new CampaignWorkspacePlacementState { GraduationYear = 2033 }
                }));
        });

        _ = queryService.Received(2).GetPlacementRosterAsync(
            Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());

        // "Close and reload" supersedes the in-flight filter reload, advancing the request
        // sequence past it. Without the ownership fix the filter reload's conditional release
        // never fires and the recovery never clears the guard, leaving the spinner stuck.
        cut.Find("button.btn-outline-warning").Click();
        _ = queryService.Received(3).GetPlacementRosterAsync(
            Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());

        // The superseded filter reload completes first; it must not release the loading guard, so
        // the loading state stays up and the stale roster is never applied.
        var rendersBeforeStaleCompletion = cut.RenderCount;
        filterReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster()));
        cut.WaitForAssertion(() => cut.RenderCount.ShouldBeGreaterThan(rendersBeforeStaleCompletion));
        cut.Markup.ShouldContain("Loading placements...");
        cut.Markup.ShouldNotContain("Avery Johnson");

        // The conflict reload completes as the newest request: it applies its roster and releases
        // the loading guard, so the panel is not stuck on the spinner and the conflict clears.
        conflictReload.SetResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Markup.ShouldNotContain("Loading placements...");
        cut.Markup.ShouldNotContain("Close and reload");
    }

    [Fact]
    public void ClosedTransition_ClearsDrafts_AndRendersReadOnly()
    {
        RegisterServices();

        var cut = RenderPanel(status: CampaignStatus.Active, canEdit: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").Change("2");
        cut.FindAll("button.btn-primary").ShouldNotBeEmpty();

        cut.Render(parameters => parameters.Add(panel => panel.CampaignStatus, CampaignStatus.Closed));
        cut.Markup.ShouldContain("Placements are frozen.");
        cut.FindAll("button.btn-primary").ShouldBeEmpty();
        cut.FindAll("select[aria-label^=\"Outcome for\"]").ShouldBeEmpty();
        cut.FindAll("select[aria-label^=\"Team for\"]").ShouldBeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RegisterServices(
        ICampaignPlacementQueryService? placementQueryService = null,
        ICampaignPlacementService? placementService = null,
        ITeamRosterService? teamRosterService = null,
        ServiceResult<PagedResult<CampaignPlacementRosterItem>>? rosterResult = null,
        ServiceResult<CampaignPlacementSummaryDto>? summaryResult = null,
        IReadOnlyList<TeamRosterItem>? teams = null,
        IReadOnlyList<int>? graduationYears = null)
    {
        if (placementQueryService is null)
        {
            placementQueryService = Substitute.For<ICampaignPlacementQueryService>();
            placementQueryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(rosterResult ?? new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())));
            placementQueryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(summaryResult ?? new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));
        }

        if (placementService is null)
        {
            placementService = Substitute.For<ICampaignPlacementService>();
            placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<PlacementMutationSuccess>(
                    new PlacementMutationSuccess(Guid.NewGuid()))));
        }

        if (teamRosterService is null)
        {
            teamRosterService = Substitute.For<ITeamRosterService>();
            teamRosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(
                    (teams ?? CreateTeams()).ToList())));
        }

        var participantQueryService = Substitute.For<ICampaignParticipantQueryService>();
        participantQueryService.GetRosterGraduationYearsAsync(
                Arg.Any<GetCampaignParticipantGraduationYearsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<int>>(
                (graduationYears ?? [2032, 2033]).ToList())));

        Services.AddSingleton(placementQueryService);
        Services.AddSingleton(placementService);
        Services.AddSingleton(teamRosterService);
        Services.AddSingleton(participantQueryService);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<CampaignPlacementsPanel> RenderPanel(
        CampaignStatus status = CampaignStatus.Active,
        bool canEdit = true,
        CampaignWorkspacePlacementState? state = null)
        => Render<CampaignPlacementsPanel>(parameters =>
        {
            parameters.Add(panel => panel.CampaignId, 10);
            parameters.Add(panel => panel.CampaignStatus, status);
            parameters.Add(panel => panel.CanEditPlacements, canEdit);
            parameters.Add(panel => panel.State, state ?? new CampaignWorkspacePlacementState());
        });

    private static PagedResult<CampaignPlacementRosterItem> CreateRoster() => CreateRoster(CreateRosterItem());

    private static PagedResult<CampaignPlacementRosterItem> CreateRoster(CampaignPlacementRosterItem item) => new(
        Items: [item],
        Page: 1,
        PageSize: GetCampaignPlacementRosterInput.DefaultPageSize,
        TotalCount: 1);

    private static CampaignPlacementRosterItem CreateRosterItem(
        string displayName = "Avery Johnson",
        string firstName = "Avery",
        string lastName = "Johnson",
        long assignmentId = 301,
        PlacementOutcome outcome = PlacementOutcome.Undecided,
        CampaignParticipantTeamSummaryDto? team = null,
        int graduationYear = 2032,
        Guid? token = null,
        long playerId = 7) => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: playerId,
        DisplayName: displayName,
        FirstName: firstName,
        LastName: lastName,
        GraduationYear: graduationYear,
        PlacementOutcome: outcome,
        Team: team,
        ConcurrencyToken: token ?? Guid.NewGuid());

    private static CampaignPlacementSummaryDto CreateSummary(
        int assigned = 0,
        int notSelected = 0,
        int withdrawn = 0,
        int undecided = 1) => new(
        AssignedCount: assigned,
        NotSelectedCount: notSelected,
        WithdrawnCount: withdrawn,
        UndecidedCount: undecided,
        TotalCount: assigned + notSelected + withdrawn + undecided);

    private static IReadOnlyList<TeamRosterItem> CreateTeams() =>
    [
        new()
        {
            TeamId = 21,
            Name = "Blue",
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active,
            ActivePlacementCount = 0
        },
        new()
        {
            TeamId = 22,
            Name = "Gold",
            GraduationYear = 2033,
            LifecycleStatus = LifecycleStatus.Active,
            ActivePlacementCount = 0
        }
    ];
}
