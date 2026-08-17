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

    [Fact]
    public void Panel_ShowsTeamChoiceTruncationNotice_WithoutConflict()
    {
        RegisterServices(teams: Enumerable.Range(1, 200).Select(teamId => new TeamRosterItem
        {
            TeamId = teamId,
            Name = $"Team {teamId}",
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active,
            ActivePlacementCount = 0
        }).ToList());

        var cut = RenderPanel();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Showing the first 200 active teams.");
            cut.Markup.ShouldNotContain("The placement was changed by someone else.");
        });
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

        var teamRosterService = Substitute.For<ITeamRosterService>();
        teamRosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(
                (teams ?? CreateTeams()).ToList())));

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
        long assignmentId = 301,
        PlacementOutcome outcome = PlacementOutcome.Undecided,
        CampaignParticipantTeamSummaryDto? team = null,
        int graduationYear = 2032,
        Guid? token = null) => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: 7,
        DisplayName: displayName,
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
