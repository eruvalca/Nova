using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign workspace shell covering the header, tab bar, detail-load
/// states, roster-load ordering, URL-backed roster filters and sorting, paging, empty states, and
/// persisted-state restoration.
/// </summary>
public sealed partial class CampaignWorkspaceTests : BunitContext
{
    private const string WorkspaceModulePath = "./_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js";

    // ── Render mode ───────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceRouteDeclaresInteractiveAutoRenderMode()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Campaigns", "Pages", "CampaignWorkspace.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode InteractiveAuto");
    }

    // ── Loading state ─────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceShowsLoadingStateWhileDetailRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(campaignQueryService: queryService);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.Markup.ShouldContain("Loading campaign...");

        pending.SetResult(new ServiceResult<CampaignDetailResult>(CreateDetail()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));
    }

    // ── Header fields ─────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceRendersHeaderFieldsWhenDetailLoads()
    {
        RegisterServices();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Summer Tryouts");
            cut.Markup.ShouldContain("Summer 2026");
            cut.FindAll(".campaign-facts > div").Single(fact => string.Equals(fact.QuerySelector("dt")!.TextContent, "Participants", StringComparison.Ordinal))
                .QuerySelector("dd")!.TextContent.ShouldBe("12 participants");
        });

        cut.Find(".campaign-lifecycle").TextContent.Trim().ShouldBe("Active");
        cut.Markup.ShouldContain($"{new DateOnly(2026, 6, 15):MMM d, yyyy} – {new DateOnly(2026, 6, 20):MMM d, yyyy}");
        cut.Markup.ShouldContain("Back to campaigns");
    }

    // ── Tab bar ───────────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceShowsAllRouteMarkersEnabledAndRosterActive()
    {
        RegisterServices();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Roster");

        var routeMarkers = cut.FindAll("ul.route-marker-list a.nav-link");
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        routeMarkers.Select(tab => tab.QuerySelector(".route-marker-label")!.TextContent.Trim()).ShouldBe(new[] { "Roster", "Evaluate", "Place", "Close" });
#pragma warning restore CA1861

        cut.FindAll("ul.nav-tabs .nav-link.disabled").ShouldBeEmpty();
    }

    [Fact]
    public void CampaignWorkspaceRouteMarkersAreAnchorsWithCanonicalDestinations()
    {
        RegisterServices();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var markers = cut.FindAll("ul.route-marker-list a.route-marker");
        markers.Count.ShouldBe(4);
        markers.Select(marker => marker.GetAttribute("href")).ShouldBe(
            ["/campaigns/10/roster?tab=roster&evaluation=true", "/campaigns/10?tab=evaluate&evaluation=true", "/campaigns/10?tab=place&evaluation=true", "/campaigns/10?tab=close&evaluation=true"]);
        cut.FindAll("ul.route-marker-list button").ShouldBeEmpty();
    }

    [Fact]
    public void CampaignWorkspaceKeepsEvaluateTabActiveWhenTabQueryIsEvaluate()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Evaluate");
    }

    [Fact]
    public void CampaignWorkspaceFallsBackToRosterWhenTabQueryIsUnknown()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=garbage");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Roster");
    }

    [Fact]
    public void CampaignWorkspaceRendersRosterPanelWhenNavigatedByUrl()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=place");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("placements-region-heading"));

        navigationManager.NavigateTo("/campaigns/10/roster?tab=roster");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("roster-region-heading"));
        cut.Markup.ShouldNotContain("placements-region-heading");
        cut.Markup.ShouldContain("Roster");
        cut.FindAll("ul.nav-tabs .nav-link.active").Single()
            .QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Roster");
    }

    [Fact]
    public void CampaignWorkspaceActivatesPlacementsTabWhenTabQueryIsPlacements()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=place");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].TextContent.Trim().ShouldContain("Place");

        cut.Markup.ShouldContain("placements-region-heading");
        cut.Markup.ShouldNotContain("roster-region-heading");
    }

    [Fact]
    public void CampaignWorkspacePushesPlacementsUrlAndRendersPlacementsRegionWhenPlacementsTabSelected()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        navigationManager.NavigateTo("/campaigns/10?tab=place");
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/campaigns/10?tab=place"));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("placements-region-heading"));
        cut.Markup.ShouldNotContain("roster-region-heading");
    }

    [Fact]
    public void CampaignWorkspaceTabClicksSwitchViewBackAndForth()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));
        cut.Markup.ShouldContain("roster-region-heading");

        // Roster → Place switches the rendered region.
        navigationManager.NavigateTo("/campaigns/10?tab=place");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("placements-region-heading"));
        cut.Markup.ShouldNotContain("roster-region-heading");

        // Place → Roster switches back.
        navigationManager.NavigateTo("/campaigns/10/roster?tab=roster");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("roster-region-heading"));
        cut.Markup.ShouldNotContain("placements-region-heading");
    }

    // ── Overview / Closeout tabs ──────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceEvaluateUrlRendersBlankEvaluationFinder()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/campaigns/10?tab=evaluate"));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("evaluation-finder-heading"));
        cut.Markup.ShouldNotContain("roster-region-heading");
    }

    [Fact]
    public void BlankEvaluateDefersRosterReadsUntilReturnWithPreservedQuery()
    {
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreatePagedRoster(2, 150, [301], pageSize: 50)));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10?tab=evaluate&evaluation=true&search=Avery&page=2&eligibility=NeedsPlacement");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Find("#evaluation-search").GetAttribute("value").ShouldBeEmpty());
        cut.Markup.ShouldContain("Summer Tryouts");
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        _ = reads.DidNotReceive().GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
        _ = reads.DidNotReceive().GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ICampaignParticipantQueryService>().DidNotReceive()
            .GetRosterGraduationYearsAsync(Arg.Any<GetCampaignParticipantGraduationYearsInput>(), Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ITagDefinitionQueryService>().DidNotReceive().GetChoicesAsync(Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ITeamRosterService>().DidNotReceive().GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ICampaignQueryService>().Received(1).GetCampaignDetailAsync(Arg.Is<GetCampaignDetailInput>(input => input.CampaignId == 10), Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ICampaignCloseoutQueryService>().Received(1).GetCloseoutReadinessAsync(Arg.Is<GetCampaignCloseoutReadinessInput>(input => input.CampaignId == 10), Arg.Any<CancellationToken>());
        var rosterUrl = cut.FindAll("ul.route-marker-list a.route-marker")
            .Single(marker => string.Equals(marker.QuerySelector(".route-marker-label")!.TextContent.Trim(), "Roster", StringComparison.Ordinal)).GetAttribute("href")!;
        navigation.NavigateTo(rosterUrl);
        cut.WaitForAssertion(() => cut.Find("#roster-search").GetAttribute("value").ShouldBe("Avery"));
        cut.WaitForAssertion(() =>
        {
            _ = reads.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.CampaignId == 10
                && input.Search == "Avery" && input.Page == 2 && input.Eligibility == "NeedsPlacement"), Arg.Any<CancellationToken>());
        });
        _ = Services.GetRequiredService<ITagDefinitionQueryService>().Received(1).GetChoicesAsync(Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ICampaignParticipantQueryService>().Received(1)
            .GetRosterGraduationYearsAsync(Arg.Any<GetCampaignParticipantGraduationYearsInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceCloseUrlRendersCloseoutRegion()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        navigationManager.NavigateTo("/campaigns/10?tab=close");
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/campaigns/10?tab=close"));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("closeout-region-heading"));
        cut.Markup.ShouldNotContain("roster-region-heading");
    }

    [Fact]
    public void CampaignWorkspaceActivatesEvaluateMarkerWhenTabQueryIsEvaluate()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.FindAll("ul.route-marker-list .nav-link.active")[0].QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Evaluate");
        cut.Markup.ShouldContain("evaluation-finder-heading");
    }

    [Fact]
    public void CampaignWorkspaceActivatesCloseMarkerWhenTabQueryIsClose()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=close");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.FindAll("ul.route-marker-list .nav-link.active")[0].QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Close");
        cut.Markup.ShouldContain("closeout-region-heading");
    }

    // ── Header campaign menu ───────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceHeaderRendersCampaignMenuForAdmin()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Markup.ShouldContain("Campaign menu");
    }

    [Fact]
    public void CampaignWorkspaceNonAdminDoesNotSeeMenuItems()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Markup.ShouldNotContain("Campaign menu");
        cut.Markup.ShouldNotContain("Edit metadata");
        cut.Markup.ShouldNotContain("Close campaign");
        cut.Markup.ShouldNotContain("Reopen");
    }

    /// <summary>Verifies the entry snapshot avoids a duplicate startup read while explicit metadata refresh reads current data.</summary>
    [Fact]
    public void CampaignWorkspaceUsesInitialSnapshotThenRefreshesAfterMetadataSave()
    {
        var queries = Substitute.For<ICampaignQueryService>();
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(CreateDetail("Fresh campaign")));
        queries.GetCreationSetupAsync(Arg.Any<CancellationToken>()).Returns(new ServiceResult<CampaignCreationSetupResult>(CreateSetup()));
        var metadata = Substitute.For<ICampaignMetadataService>();
        metadata.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<UpdateCampaignMetadataResult>(new UpdateCampaignMetadataResult(10, "Fresh campaign",
                new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 20), CampaignStatus.Active, 5, "Summer 2026")));
        RegisterServices(campaignQueryService: queries, campaignMetadataService: metadata, isClubAdmin: true);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, CreateDetail("Entry snapshot"))
            .Add(component => component.InitialDetailScope, "101:42:True"));

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("Entry snapshot"));
        _ = queries.DidNotReceive().GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
        cut.Find("button[aria-haspopup='menu']").Click();
        cut.FindAll("button[role='menuitem']").Single(button => string.Equals(button.TextContent.Trim(), "Edit metadata", StringComparison.Ordinal)).Click();
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("Fresh campaign"));
        _ = queries.Received(1).GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies startup never trusts a snapshot belonging to another campaign, identity, or Draft lifecycle.</summary>
    /// <param name="campaignId">The snapshot campaign.</param>
    /// <param name="scope">The snapshot owner.</param>
    /// <param name="status">The snapshot lifecycle.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(11, "101:42:False", CampaignStatus.Active)]
    [InlineData(10, "101:43:False", CampaignStatus.Active)]
    [InlineData(10, "101:42:False", CampaignStatus.Draft)]
    public void CampaignWorkspaceRejectsUnusableInitialSnapshot(long campaignId, string scope, CampaignStatus status)
    {
        var queries = Substitute.For<ICampaignQueryService>();
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(CreateDetail("Authoritative campaign")));
        RegisterServices(campaignQueryService: queries);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, CreateDetail("Unusable snapshot", status) with { CampaignId = campaignId })
            .Add(component => component.InitialDetailScope, scope));

        cut.WaitForAssertion(() => cut.Find("h1").TextContent.ShouldBe("Authoritative campaign"));
        cut.Markup.ShouldNotContain("Unusable snapshot");
        _ = queries.Received(1).GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies the opening receipt is acknowledged separately only after its actual count is displayed.</summary>
    /// <param name="count">The immutable number enrolled by the opening operation.</param>
    /// <param name="noun">The grammatically appropriate enrollment noun.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, "player")]
    [InlineData(7, "players")]
    public void CampaignWorkspaceAcknowledgesValidOpeningReceiptAfterApplyingCount(int count, string noun)
    {
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster");
        var operationId = Guid.NewGuid();
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", _ => true).SetResult(
            new OpenCampaignResult(operationId, 10, DateTimeOffset.UtcNow, 101, count, 0, [CampaignOpeningWarning.NoActiveTeams]));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain($"Campaign opened and enrolled {count} {noun}."));
        module.Invocations.Count(invocation => string.Equals(invocation.Identifier, "focus", StringComparison.Ordinal)).ShouldBe(1);
        module.Invocations.Count(invocation => string.Equals(invocation.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)).ShouldBe(1);
        var arguments = module.Invocations.Single(invocation => string.Equals(invocation.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)).Arguments;
        arguments[0].ShouldBe("101:42:False");
        arguments[1].ShouldBe(10L);
        arguments[2]!.ToString().ShouldBe(operationId.ToString());
    }

    [Fact]
    public async Task WorkspaceSharesStartupLocationReconciliationAcrossOverlappingRendersAsync()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster");
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        using var reconciliation = new ControlledReconciliationHandler();
        module.AddInvocationHandler(reconciliation);
        var operationId = Guid.NewGuid();
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", _ => true).SetResult(
            new OpenCampaignResult(operationId, 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => reconciliation.Invocations.Count.ShouldBe(1));
        var arguments = reconciliation.Invocations.Single().Arguments;
        arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldNotBeNullOrEmpty();
        arguments[1].ShouldBeOfType<string>().ShouldNotBeNullOrEmpty();
        arguments[2].ShouldBe(navigation.Uri);
        arguments[3].ShouldBe("/campaigns/10");

        cut.Render();
        cut.Render();
        reconciliation.Invocations.Count.ShouldBe(1);
        module.Invocations.ShouldNotContain(invocation => string.Equals(invocation.Identifier, "readOpeningReceipt", StringComparison.Ordinal));
        await cut.InvokeAsync(() => reconciliation.Complete(false));

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Campaign opened and enrolled 7 players."));
        cut.Render();
        reconciliation.Invocations.Count.ShouldBe(1);
        module.Invocations.Count(invocation => string.Equals(invocation.Identifier, "focus", StringComparison.Ordinal)).ShouldBe(1);
        var acknowledgement = module.Invocations.Single(invocation => string.Equals(invocation.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal));
        acknowledgement.Arguments[2].ShouldBe(operationId);
    }

    [Fact]
    public async Task WorkspaceWaitsForReconciledLocationDeliveryAndAllowsLaterReturnToOriginalUriAsync()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster");
        var originalUri = navigation.Uri;
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        using var reconciliation = new ControlledReconciliationHandler();
        module.AddInvocationHandler(reconciliation);
        var operationId = Guid.NewGuid();
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", _ => true).SetResult(
            new OpenCampaignResult(operationId, 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => reconciliation.Invocations.Count.ShouldBe(1));

        await cut.InvokeAsync(() => reconciliation.Complete(true));
        navigation.Uri.ShouldBe(originalUri);
        cut.Render();

        module.Invocations.ShouldNotContain(invocation => string.Equals(invocation.Identifier, "readOpeningReceipt", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "focus", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "restoreScroll", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "scrollToTop", StringComparison.Ordinal));
        cut.Markup.ShouldNotContain("Campaign opened and enrolled");
        navigation.NavigateTo("/campaigns/10?tab=place");
        await cut.WaitForAssertionAsync(() =>
        {
            cut.FindAll("a.route-marker").Single(link => string.Equals(link.GetAttribute("aria-current"), "page", StringComparison.Ordinal)).TextContent.ShouldContain("Place");
            module.Invocations.ShouldContain(invocation => string.Equals(invocation.Identifier, "revealActiveRouteMarker", StringComparison.Ordinal));
        });

        navigation.NavigateTo(originalUri);

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Campaign opened and enrolled 7 players."));
        navigation.Uri.ShouldBe(originalUri);
        reconciliation.Invocations.Count.ShouldBe(1);
        module.Invocations.Count(invocation => string.Equals(invocation.Identifier, "focus", StringComparison.Ordinal)).ShouldBe(1);
        module.Invocations.Single(invocation => string.Equals(invocation.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal))
            .Arguments[2].ShouldBe(operationId);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("place", true)]
    [InlineData("roster", false)]
    public async Task WorkspaceIgnoresOldReceiptAndScrollWhenNavigationOvertakesStartupReconciliationAsync(string tab, bool reconciled)
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster");
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        using var reconciliation = new ControlledReconciliationHandler();
        module.AddInvocationHandler(reconciliation);
        module.Setup<double?>("captureScroll", _ => true).SetResult(120);
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", _ => true).SetResult(
            new OpenCampaignResult(Guid.NewGuid(), 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => reconciliation.Invocations.Count.ShouldBe(1));

        await cut.Find("tbody tr").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => navigation.Uri.ShouldContain("participant=301"));
        module.Invocations.Count(invocation => string.Equals(invocation.Identifier, "captureScroll", StringComparison.Ordinal)).ShouldBe(1);
        navigation.NavigateTo($"/campaigns/10?tab={tab}&participant=302");
        cut.Render();
        reconciliation.Invocations.Count.ShouldBe(1);
        await cut.InvokeAsync(() => reconciliation.Complete(reconciled));

        await cut.WaitForAssertionAsync(() => module.Invocations.ShouldContain(invocation => string.Equals(invocation.Identifier, "revealActiveRouteMarker", StringComparison.Ordinal)));
        navigation.Uri.ShouldEndWith($"/campaigns/10?tab={tab}&participant=302");
        cut.FindAll("a.route-marker").Single(link => string.Equals(link.GetAttribute("aria-current"), "page", StringComparison.Ordinal))
            .TextContent.ShouldContain(string.Equals(tab, "place", StringComparison.Ordinal) ? "Place" : "Roster");
        module.Invocations.ShouldNotContain(invocation => string.Equals(invocation.Identifier, "focus", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "restoreScroll", StringComparison.Ordinal));
        cut.Markup.ShouldNotContain("Campaign opened and enrolled");
        reconciliation.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WorkspaceDisposalIgnoresLateStartupReconciliationCompletionAsync()
    {
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster");
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        using var reconciliation = new ControlledReconciliationHandler();
        module.AddInvocationHandler(reconciliation);
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", _ => true).SetResult(
            new OpenCampaignResult(Guid.NewGuid(), 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => reconciliation.Invocations.Count.ShouldBe(1));
        await cut.Instance.DisposeAsync();
        var callsAfterDisposal = module.Invocations.Select(invocation => invocation.Identifier).ToArray();

        await cut.InvokeAsync(() => reconciliation.Complete(true));

        module.Invocations.Select(invocation => invocation.Identifier).ShouldBe(callsAfterDisposal);
        module.Invocations.ShouldNotContain(invocation => string.Equals(invocation.Identifier, "readOpeningReceipt", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "focus", StringComparison.Ordinal)
            || string.Equals(invocation.Identifier, "restoreScroll", StringComparison.Ordinal));
        cut.Markup.ShouldNotContain("Campaign opened and enrolled");
    }

    /// <summary>Verifies unreadable or invalid receipts remain unacknowledged and never produce an opening claim.</summary>
    /// <param name="kind">The invalid receipt partition.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("read-failure")]
    [InlineData("json-failure")]
    [InlineData("unsupported-failure")]
    [InlineData("no-receipt")]
    [InlineData("wrong-campaign")]
    [InlineData("empty-operation")]
    [InlineData("zero-count")]
    public void CampaignWorkspaceDoesNotAcknowledgeUnusableReceipt(string kind)
    {
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster");
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var read = module.Setup<OpenCampaignResult?>("readOpeningReceipt", _ => true);
        if (string.Equals(kind, "read-failure", StringComparison.Ordinal))
        {
            read.SetException(new JSException("Storage unavailable"));
        }
        else if (string.Equals(kind, "json-failure", StringComparison.Ordinal))
        {
            read.SetException(new System.Text.Json.JsonException("Malformed receipt"));
        }
        else if (string.Equals(kind, "unsupported-failure", StringComparison.Ordinal))
        {
            read.SetException(new NotSupportedException("Unsupported receipt"));
        }
        else if (string.Equals(kind, "no-receipt", StringComparison.Ordinal))
        {
            read.SetResult(null);
        }
        else
        {
            read.SetResult(new OpenCampaignResult(string.Equals(kind, "empty-operation", StringComparison.Ordinal) ? Guid.Empty : Guid.NewGuid(),
string.Equals(kind, "wrong-campaign", StringComparison.Ordinal) ? 11 : 10, DateTimeOffset.UtcNow, 101, string.Equals(kind, "zero-count", StringComparison.Ordinal) ? 0 : 7, 0, []));
        }

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));

        cut.WaitForAssertion(() => module.Invocations.Count(invocation => string.Equals(invocation.Identifier, "readOpeningReceipt", StringComparison.Ordinal)).ShouldBe(1));
        cut.Markup.ShouldNotContain("Campaign opened and enrolled");
        module.Invocations.ShouldNotContain(invocation => invocation.Identifier == "acknowledgeOpeningReceipt");
        module.Invocations.ShouldNotContain(invocation => invocation.Identifier == "focus");
    }

    /// <summary>Verifies stale canonical-workspace tab values cannot replace the dedicated Roster panel.</summary>
    /// <param name="tab">A conflicting tab retained in the query string.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("place")]
    [InlineData("evaluate")]
    [InlineData("close")]
    public void CampaignWorkspaceRosterLandingIgnoresConflictingTab(string tab)
    {
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/campaigns/10/roster?tab={tab}");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));

        cut.WaitForAssertion(() =>
        {
            cut.Find("#roster-region-heading").TextContent.Trim().ShouldBe("Roster");
            cut.Find("#roster-search").ShouldNotBeNull();
            cut.Find(".roster-scroll-region").ShouldNotBeNull();
            cut.FindAll("tbody tr[id^='roster-row-']").ShouldNotBeEmpty();
        });
    }

    // ── Edit metadata ──────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceEditMetadataFlowRendersFormSavesAndUpdatesHeader()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignDetailResult>(CreateDetail())),
                Task.FromResult(new ServiceResult<CampaignDetailResult>(CreateDetail("Fall Tryouts"))));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCreationSetupResult>(CreateSetup())));

        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateCampaignMetadataResult>(
                new UpdateCampaignMetadataResult(10, "Fall Tryouts", new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 20), CampaignStatus.Active, 5, "Summer 2026"))));

        RegisterServices(campaignQueryService: queryService, campaignMetadataService: metadataService, isClubAdmin: true);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("button[aria-haspopup='menu']").Click();
        cut.FindAll("button[role='menuitem']").Single(button => string.Equals(button.TextContent.Trim(), "Edit metadata", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));
        cut.Find("#edit-campaign-name").GetAttribute("value").ShouldBe("Summer Tryouts");

        cut.Find("#edit-campaign-name").Change("Fall Tryouts");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign \"Fall Tryouts\" metadata updated."));
        cut.WaitForAssertion(() => cut.Find("h1").TextContent.Trim().ShouldBe("Fall Tryouts"));
    }

    [Fact]
    public void CampaignWorkspaceEditMetadataConflictShowsWarningAffordance()
    {
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateCampaignMetadataResult>(
                ServiceProblem.Conflict("The campaign is Closed. Reopen the campaign before editing its metadata."))));

        RegisterServices(campaignMetadataService: metadataService, isClubAdmin: true);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("button[aria-haspopup='menu']").Click();
        cut.FindAll("button[role='menuitem']").Single(button => string.Equals(button.TextContent.Trim(), "Edit metadata", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close and reload"));
    }

    // ── Not-found and forbidden ───────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceShowsNotFoundStateWhenServiceReturnsNotFound()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(
            participantQueryService: participantService,
            detailResult: new ServiceResult<CampaignDetailResult>(
                ServiceProblem.NotFound("Campaign not found.")));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 99));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign not found"));
        cut.Markup.ShouldContain("Return to campaigns");
        cut.Markup.ShouldNotContain("Loading campaign...");

        _ = participantService.DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceRedirectsToAccessDeniedWhenServiceReturnsForbidden()
    {
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(
            ServiceProblem.Forbidden("Access denied.")));

        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/Account/AccessDenied"));
    }

    // ── Recoverable error with retry ──────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceShowsErrorAndRetriesWhenDetailLoadFails()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignDetailResult>(ServiceProblem.ServerError("Service unavailable."))),
                Task.FromResult(new ServiceResult<CampaignDetailResult>(CreateDetail())));

        RegisterServices(campaignQueryService: queryService);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Service unavailable."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));
    }

    // ── Roster load ordering ──────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceLoadsRosterOnlyAfterDetailSucceeds()
    {
        var pendingDetail = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(pendingDetail.Task);

        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));

        RegisterServices(campaignQueryService: queryService, participantQueryService: participantService);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.Markup.ShouldContain("Loading campaign...");
        _ = participantService.DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());

        pendingDetail.SetResult(new ServiceResult<CampaignDetailResult>(CreateDetail()));
        cut.WaitForAssertion(() =>
        {
            _ = participantService.Received(1).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
        });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    [Fact]
    public void CampaignWorkspaceShowsRosterErrorAndRetriesWhenRosterLoadFails()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(ServiceProblem.ServerError("Roster service unavailable."))),
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));

        RegisterServices(participantQueryService: participantService);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Roster service unavailable."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    [Fact]
    public void CampaignWorkspaceShowsChoicesRetryAndRecoversWhenChoiceLoadFails()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));

        RegisterServices(participantQueryService: participantService);
        participantService.GetRosterGraduationYearsAsync(
                Arg.Any<GetCampaignParticipantGraduationYearsInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<IReadOnlyList<int>>(ServiceProblem.ServerError("Choice service unavailable."))),
                Task.FromResult(new ServiceResult<IReadOnlyList<int>>(CreateGraduationYearChoices().ToList())));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Couldn't load filter options."));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Couldn't load filter options."));
    }

    // ── Persisted state ───────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceDoesNotReloadWhenPersistedStateIsRestored()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(campaignQueryService: queryService, participantQueryService: participantService);

        var cut = Render<PersistedStateCampaignWorkspace>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.StartInitialized, true)
            .Add(component => component.PersistedCampaignDetail, CreateDetail()));

        cut.Markup.ShouldContain("Summer Tryouts");
        _ = queryService.DidNotReceive().GetCampaignDetailAsync(
            Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
        _ = participantService.DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    // ── Roster filters, sorting, and paging ────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceAppliesRosterStateFromQueryParametersOnLoad()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));

        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tab"] = "roster",
            ["search"] = "avery",
            ["graduationYears"] = "2032,2031",
            ["tagIds"] = "12,11",
            ["outcome"] = "undecided",
            ["teamId"] = 21L,
            ["sortBy"] = "displayName",
            ["sortDirection"] = "desc",
            ["page"] = 2,
        }));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        _ = participantService.Received(1).GetParticipantRosterAsync(
            Arg.Is<GetCampaignParticipantRosterInput>(input =>
                input.Search == "avery"
                && input.GraduationYears != null
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
                && input.GraduationYears.Order().SequenceEqual(new[] { 2031, 2032 })
#pragma warning restore CA1861
                && input.TagDefinitionIds != null
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
                && input.TagDefinitionIds.Order().SequenceEqual(new[] { 11L, 12L })
#pragma warning restore CA1861
                && input.Outcome == "undecided"
                && input.TeamId == 21
                && input.SortBy == "displayName"
                && input.SortDirection == "desc"
                && input.Page == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceSortHeaderClickCyclesAscendingThenDescendingAndPushesUrl()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?sortBy=displayName&sortDirection=desc");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.FindAll("button.roster-sort-header").Single(button => button.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldNotContain("sortBy="));
        navigationManager.Uri.ShouldNotContain("sortDirection=");
        cut.WaitForAssertion(() => cut.FindAll("button.roster-sort-header").Count.ShouldBe(2));

        cut.FindAll("button.roster-sort-header").Single(button => button.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("sortBy=displayName&sortDirection=desc"));
        cut.WaitForAssertion(() => cut.FindAll("button.roster-sort-header").Single(button => button.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal)).ParentElement!.GetAttribute("aria-sort").ShouldBe("descending"));
    }

    [Fact]
    public void CampaignWorkspaceDefaultNameSortIsAscendingAndFirstClickSwitchesToDescending()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var header = cut.FindAll("button.roster-sort-header").Single(button => button.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal));
        header.ParentElement!.GetAttribute("aria-sort").ShouldBe("ascending");

        header.Click();

        cut.WaitForAssertion(() => navigation.Uri.ShouldContain("sortBy=displayName&sortDirection=desc"));
        cut.FindAll("button.roster-sort-header").Single(button => button.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal))
            .ParentElement!.GetAttribute("aria-sort").ShouldBe("descending");
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.SortBy, "displayName", StringComparison.Ordinal)
                && string.Equals(input.SortDirection, "desc", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("displayName", "asc")]
    [InlineData("displayName", "desc")]
    [InlineData("graduationYear", "asc")]
    [InlineData("graduationYear", "desc")]
    [InlineData("tryoutNumber", "asc")]
    [InlineData("tryoutNumber", "desc")]
    [InlineData("outcome", "asc")]
    [InlineData("outcome", "desc")]
    [InlineData("teamName", "asc")]
    [InlineData("teamName", "desc")]
    public void CampaignWorkspaceOrderSelectorRetainsEverySortAndResetsPage(string sort, string direction)
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?page=2");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant 304"));
        cut.Find("#roster-order").Change($"{sort}:{direction}");
        if (string.Equals(sort, "displayName", StringComparison.Ordinal) && string.Equals(direction, "asc", StringComparison.Ordinal))
        {
            cut.WaitForAssertion(() => navigation.Uri.ShouldNotContain("sortBy="));
            navigation.Uri.ShouldNotContain("sortDirection=");
        }
        else
        {
            cut.WaitForAssertion(() => navigation.Uri.ShouldContain($"sortBy={sort}&sortDirection={direction}"));
        }
        navigation.Uri.ShouldNotContain("page=2");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant 301"));
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Page == 1
                && string.Equals(input.SortBy, sort, StringComparison.Ordinal) && string.Equals(input.SortDirection, direction, StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceDebouncesSearchToSingleRequestWithFinalTerm()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));

        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        var searchInput = cut.Find("#roster-search");
        searchInput.Input("a");
        searchInput.Input("av");
        searchInput.Input("ave");

        cut.WaitForAssertion(
            () =>
            {
                _ = participantService.Received(2).GetParticipantRosterAsync(
                Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
            },
            timeout: TimeSpan.FromSeconds(5));

        _ = participantService.Received(1).GetParticipantRosterAsync(
            Arg.Is<GetCampaignParticipantRosterInput>(input => input.Search == "ave"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CampaignWorkspaceDiscardsPendingSearchWhenHistorySupersedesDraftAsync()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?search=original");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        var pendingInput = cut.Find("#roster-search").InputAsync(new ChangeEventArgs { Value = "obsolete draft" });
        navigation.NavigateTo("/campaigns/10/roster?search=history");
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        await cut.WaitForAssertionAsync(() =>
        {
            _ = reads.Received(1).GetCampaignEffectivePlacementsAsync(
                Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.Search, "history", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        });
        await pendingInput;

        navigation.Uri.ShouldEndWith("/campaigns/10/roster?search=history");
        cut.Find("#roster-search").GetAttribute("value").ShouldBe("history");
        _ = reads.DidNotReceive().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.Search, "obsolete draft", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        cut.Markup.ShouldNotContain("Loading roster...");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("participant")]
    [InlineData("tab")]
    public async Task CampaignWorkspaceRestoresCanonicalSearchAfterSameQueryNavigationSupersedesDraftAsync(string destination)
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10?search=original&tab=roster");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var pendingInput = cut.Find("#roster-search").InputAsync(new ChangeEventArgs { Value = "abandoned draft" });
        navigation.NavigateTo(string.Equals(destination, "participant", StringComparison.Ordinal)
            ? "/campaigns/10?search=original&tab=roster&participant=301"
            : "/campaigns/10?search=original&tab=evaluate");
        await pendingInput;

        navigation.Uri.ShouldContain("search=original");
        navigation.Uri.ShouldNotContain("abandoned");
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        _ = reads.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
        var returnsFromEvaluate = string.Equals(destination, "tab", StringComparison.Ordinal);
        if (returnsFromEvaluate)
        {
            await cut.WaitForAssertionAsync(() => cut.Find("#evaluation-search").GetAttribute("value").ShouldBeEmpty());
            navigation.NavigateTo("/campaigns/10?search=original&tab=roster");
        }
        await cut.WaitForAssertionAsync(() => cut.Find("#roster-search").GetAttribute("value").ShouldBe("original"));
        _ = reads.Received(returnsFromEvaluate ? 2 : 1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.Search, "original", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        _ = reads.DidNotReceive().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.Search, "abandoned draft", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CampaignWorkspaceAbandonedSearchCompletionCannotClearNewerInputAfterParticipantNavigationAsync()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10?search=original&tab=roster");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var obsoleteInput = cut.Find("#roster-search").InputAsync(new ChangeEventArgs { Value = "abandoned draft" });
        navigation.NavigateTo("/campaigns/10?search=original&tab=roster&participant=301");
        var newestInput = cut.Find("#roster-search").InputAsync(new ChangeEventArgs { Value = "latest" });
        await obsoleteInput;
        cut.Find("#roster-search").GetAttribute("value").ShouldBe("latest");
        await newestInput;

        navigation.Uri.ShouldContain("search=latest");
        navigation.Uri.ShouldContain("participant=301");
        cut.Find("#roster-search").GetAttribute("value").ShouldBe("latest");
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        _ = reads.DidNotReceive().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.Search, "abandoned draft", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        _ = reads.Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => string.Equals(input.Search, "latest", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceDiscardsStaleRosterResponseWhenNewerRequestCompletesFirst()
    {
        var firstResponse = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var secondResponse = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())),
                firstResponse.Task,
                secondResponse.Task);

        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("#roster-outcome").Change("assigned");
        cut.WaitForAssertion(() =>
        {
            _ = participantService.Received(2).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
        });

        cut.Find("#roster-outcome").Change("withdrawn");
        cut.WaitForAssertion(() =>
        {
            _ = participantService.Received(3).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
        });

        secondResponse.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreateRoster(CreateRosterItem("Fresh Roster"))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Fresh Roster"));

        firstResponse.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreateRoster(CreateRosterItem("Stale Roster"))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Fresh Roster"));
        cut.Markup.ShouldNotContain("Stale Roster");
    }

    [Fact]
    public void CampaignWorkspacePagerReflectsPageMathAndBounds()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                    new PagedResult<CampaignParticipantRosterItem>(
                        Items: [CreateRosterItem()],
                        Page: input.Page ?? GetCampaignParticipantRosterInput.DefaultPage,
                        PageSize: GetCampaignParticipantRosterInput.DefaultPageSize,
                        TotalCount: 120)));
            });

        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Page 1 of 3"));

        var buttons = cut.FindAll("nav[aria-label='Roster pagination'] button");
        buttons[0].HasAttribute("disabled").ShouldBeTrue();
        buttons[1].HasAttribute("disabled").ShouldBeFalse();

        buttons[1].Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Page 2 of 3"));

        buttons = cut.FindAll("nav[aria-label='Roster pagination'] button");
        buttons[1].Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Page 3 of 3"));

        buttons = cut.FindAll("nav[aria-label='Roster pagination'] button");
        buttons[0].HasAttribute("disabled").ShouldBeFalse();
        buttons[1].HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void CampaignWorkspaceShowsEmptyCampaignMessageWhenRosterHasNoParticipantsAndNoFilters()
    {
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            new PagedResult<CampaignParticipantRosterItem>(
                Items: [],
                Page: 1,
                PageSize: GetCampaignParticipantRosterInput.DefaultPageSize,
                TotalCount: 0)));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No participants in this campaign yet."));
        cut.Markup.ShouldNotContain("No participants match the current filters.");
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Clear filters", StringComparison.Ordinal));
    }

    [Fact]
    public void CampaignWorkspaceEmptyRosterDetachesKeydownSuppressionInsteadOfAttaching()
    {
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            new PagedResult<CampaignParticipantRosterItem>(
                Items: [],
                Page: 1,
                PageSize: GetCampaignParticipantRosterInput.DefaultPageSize,
                TotalCount: 0)));

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var attach = workspaceModule.SetupVoid("attachRosterActivationSuppression", _ => true);
        attach.SetVoidResult();
        var detach = workspaceModule.SetupVoid("detachRosterActivationSuppression", _ => true);
        detach.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No participants in this campaign yet."));

        cut.WaitForAssertion(() => detach.Invocations.Count.ShouldBeGreaterThanOrEqualTo(1));
        attach.Invocations.ShouldBeEmpty();
    }

    [Fact]
#pragma warning disable MA0051 // Keep the capture, same-query failure, keyboard action, and recovery together as one ownership regression.
    public void CampaignWorkspaceRetainsKeyboardOwnershipOnReloadFailureThenDetachesWhenRetryRemovesRows()
#pragma warning restore MA0051
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())),
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                    ServiceProblem.ServerError("Roster service unavailable."))),
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                    new PagedResult<CampaignParticipantRosterItem>([], 1, 50, 0))));
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail() with
            { Capabilities = new(false, true, false, false) })));

        RegisterServices(participantQueryService: participantService);
        Services.GetRequiredService<ICampaignEvaluationNoteService>().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(99, Guid.NewGuid(), new(Guid.CreateVersion7(), 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))))));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=roster&participant=301");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var attach = workspaceModule.SetupVoid("attachRosterActivationSuppression", _ => true);
        attach.SetVoidResult();
        var detach = workspaceModule.SetupVoid("detachRosterActivationSuppression", _ => true);
        detach.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        attach.Invocations.Count.ShouldBeGreaterThanOrEqualTo(1);
        var initialListenerOwner = attach.Invocations.Last().Arguments[1];

        cut.FindAll("aside.participant-drawer button").Single(button => string.Equals(button.TextContent.Trim(), "Add note", StringComparison.Ordinal)).Click();
        cut.Find("aside.participant-drawer textarea").Input("New observation");
        cut.FindAll("aside.participant-drawer button").Single(button => string.Equals(button.TextContent.Trim(), "Save note", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Roster service unavailable."));
        cut.Find("#roster-row-301").TextContent.ShouldContain("Avery Johnson");
        attach.Invocations.Last().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldNotBeNullOrEmpty();
        var listenerOwner = attach.Invocations.Last().Arguments[1];
        listenerOwner.ShouldNotBeNull();
        listenerOwner.ShouldBe(initialListenerOwner);
        detach.Invocations.ShouldBeEmpty();

        cut.Find("#participant-drawer-close").Click();
        cut.WaitForAssertion(() => cut.FindAll("aside.participant-drawer").ShouldBeEmpty());
        cut.Find("#roster-row-301").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => cut.Find("aside.participant-drawer h2").TextContent.Trim().ShouldBe("Avery Johnson"));
        cut.Find("#participant-drawer-close").Click();
        cut.WaitForAssertion(() => cut.FindAll("aside.participant-drawer").ShouldBeEmpty());
        cut.Find(".workspace-board .alert-danger button").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No participants in this campaign yet."));
        cut.FindAll(".roster-scroll-region").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Roster service unavailable.");
        cut.WaitForAssertion(() => detach.Invocations.Last().Arguments[0].ShouldBe(listenerOwner));
    }

    [Fact]
    public async Task CampaignWorkspaceDisposeAsyncToleratesDisconnectedCircuitAsync()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var detach = workspaceModule.SetupVoid("detachRosterActivationSuppression", _ => true);
        detach.SetException(new JSDisconnectedException("Circuit has disconnected."));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        var disposeTask = cut.InvokeAsync(cut.Instance.DisposeAsync);
        await disposeTask;

        detach.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CampaignWorkspaceDisposeCompletesWhenPendingModuleImportIsCanceledAsync()
    {
        RegisterServices();
        var runtime = new PendingModuleRuntime(JSInterop.JSRuntime, WorkspaceModulePath);
        Services.AddSingleton<IJSRuntime>(runtime);
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        runtime.ImportedPaths.ShouldBe([WorkspaceModulePath]);

        var disposal = cut.Instance.DisposeAsync().AsTask();
        disposal.IsCompleted.ShouldBeFalse();
        runtime.CancelImport(Xunit.TestContext.Current.CancellationToken);
        await disposal;
        await cut.Instance.DisposeAsync();

        runtime.ImportedPaths.ShouldBe([WorkspaceModulePath]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CampaignWorkspaceDisposesKeyboardOwnershipOnceWhenCleanupCompletesOrIsCanceledAsync(bool canceled)
    {
        RegisterServices();
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var attach = module.SetupVoid("attachRosterActivationSuppression", _ => true);
        attach.SetVoidResult();
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => attach.Invocations.Count.ShouldBe(1));
        var owner = attach.Invocations.Single().Arguments[1];
        var detach = module.SetupVoid("detachRosterActivationSuppression", _ => true);
        if (canceled)
        {
            detach.SetCanceled();
        }
        else
        {
            detach.SetVoidResult();
        }

        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();

        detach.Invocations.Count.ShouldBe(1);
        detach.Invocations.Single().Arguments[0].ShouldBe(owner);
    }

    [Fact]
    public async Task CampaignWorkspaceDisposalDoesNotSwallowUnrelatedJavascriptFailureAsync()
    {
        RegisterServices();
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var detach = module.SetupVoid("detachRosterActivationSuppression", _ => true);
        detach.SetException(new JSException("Unexpected keyboard cleanup failure"));

        var error = await Should.ThrowAsync<JSException>(() => cut.Instance.DisposeAsync().AsTask());

        error.Message.ShouldBe("Unexpected keyboard cleanup failure");
        detach.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public void CampaignWorkspaceDiscardsPreviousMatchesWhenChangedFilterReadFails()
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var fixture = Substitute.For<ICampaignParticipantQueryService>();
        fixture.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())), pending.Task);
        RegisterServices(participantQueryService: fixture);
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Find("#roster-row-301").TextContent.ShouldContain("Avery Johnson"));

        cut.Find("#roster-outcome").Change("assigned");
        cut.WaitForAssertion(() => cut.FindAll("#roster-row-301").ShouldBeEmpty());
        pending.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(ServiceProblem.ServerError("Assigned matches unavailable")));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Assigned matches unavailable"));
        cut.FindAll("#roster-row-301").ShouldBeEmpty();
        cut.FindAll(".roster-scroll-region").ShouldBeEmpty();
        cut.Markup.ShouldContain("Summer Tryouts");
        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("outcome=assigned");
    }

    [Fact]
    public async Task CampaignWorkspaceDiscardsActiveRowsWhenClosedHistoryReadFailsAsync()
    {
        RegisterServices();
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Find("#roster-row-301").TextContent.ShouldContain("No campaign decision"));
        Services.GetRequiredService<ICampaignQueryService>().GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignDetailResult>(CreateDetail(status: CampaignStatus.Closed))));
        var effective = Services.GetRequiredService<IEffectivePlacementQueryService>();
        effective.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.ServerError("Closed history unavailable"))));

        await cut.InvokeAsync(() => cut.FindComponent<Nova.UI.Features.Campaigns.Components.CampaignWorkspaceReadiness>().Instance.OnLifecycleChanged.InvokeAsync());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Closed history unavailable"));
        cut.Markup.ShouldContain("Campaign record is read-only");
        cut.FindAll("#roster-row-301").ShouldBeEmpty();
        cut.FindAll(".roster-scroll-region").ShouldBeEmpty();
        _ = effective.Received(1).GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceShowsNoMatchMessageAndClearsFiltersWhenFiltersExcludeAllParticipants()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                var empty = string.Equals(input.Outcome, "withdrawn", StringComparison.Ordinal);
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                    empty
                        ? new PagedResult<CampaignParticipantRosterItem>(
                            Items: [],
                            Page: 1,
                            PageSize: GetCampaignParticipantRosterInput.DefaultPageSize,
                            TotalCount: 0)
                        : CreateRoster()));
            });

        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tab"] = "roster",
            ["outcome"] = "withdrawn",
        }));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No participants match the current filters."));

        cut.FindAll("button").First(button => string.Equals(button.TextContent.Trim(), "Clear filters", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Markup.ShouldNotContain("No participants match the current filters.");
    }

    // ── Phase 5: participant selection and drawer ──────────────────────────────

    [Fact]
    public void CampaignWorkspaceClickingRosterRowOpensDrawerPushesParticipantAndHighlightsRow()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        workspaceModule.Setup<double?>("captureScroll", _ => true).SetResult(120);
        var restoreScroll = workspaceModule.SetupVoid("restoreScroll", _ => true);
        restoreScroll.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        cut.Find("tbody tr").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=301"));
        cut.Markup.ShouldContain("participant-drawer");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Strong defensive player."));
        cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Avery Johnson");
        cut.Find("tbody tr").GetAttribute("aria-current").ShouldBe("true");
        cut.Find("tbody tr.roster-row-selected").ShouldNotBeNull();

        cut.WaitForAssertion(() =>
        {
            restoreScroll.Invocations.Count.ShouldBe(1);
            restoreScroll.Invocations.Single().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
            restoreScroll.Invocations.Single().Arguments[1].ShouldBe(120.0);
        });
    }

    [Fact]
    public void CampaignWorkspaceClosingDrawerRemovesParticipantAndPreservesRosterParams()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&outcome=assigned&participant=301");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        workspaceModule.Setup<double?>("captureScroll", _ => true).SetResult(120);
        var restoreScroll = workspaceModule.SetupVoid("restoreScroll", _ => true);
        restoreScroll.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));
        cut.Markup.ShouldContain("Avery Johnson");
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        cut.Find("aside.participant-drawer .btn-close").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));
        navigationManager.Uri.ShouldEndWith("/campaigns/10?outcome=assigned&tab=roster&evaluation=true");
        cut.Markup.ShouldContain("Avery Johnson");

        cut.WaitForAssertion(() =>
        {
            restoreScroll.Invocations.Count.ShouldBe(1);
            restoreScroll.Invocations.Single().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
            restoreScroll.Invocations.Single().Arguments[1].ShouldBe(120.0);
        });
    }

    [Fact]
    public void CampaignWorkspaceEscapeClosesDrawer()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=301");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("aside.participant-drawer").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));
        navigationManager.Uri.ShouldNotContain("participant=");
    }

    [Fact]
    public void CampaignWorkspaceKeyboardEnterOnRosterRowSelectsParticipant()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("tbody tr").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=301"));
        cut.Markup.ShouldContain("participant-drawer");
    }

    [Fact]
    public void CampaignWorkspaceSortChangeScrollsRosterToTopWithoutCapturingScroll()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&sortBy=displayName&sortDirection=desc");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var captureScroll = workspaceModule.Setup<double?>("captureScroll", _ => true);
        captureScroll.SetResult(120);
        var scrollToTop = workspaceModule.SetupVoid("scrollToTop", _ => true);
        scrollToTop.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        cut.FindAll("button.roster-sort-header").Single(button => button.TextContent.Trim().StartsWith("Name", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldNotContain("sortBy="));
        navigationManager.Uri.ShouldNotContain("sortDirection=");
        cut.WaitForAssertion(() =>
        {
            scrollToTop.Invocations.Count.ShouldBe(1);
            scrollToTop.Invocations.Single().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
            captureScroll.Invocations.ShouldBeEmpty();
        });
    }

    [Fact]
    public void CampaignWorkspaceUnknownParticipantParamOpensDrawerWithErrorAndFallbackHeading()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                ServiceProblem.NotFound("Participant not found."))));
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=999");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Participant");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant not found"));
        cut.Find("#participant-drawer-retry").ShouldNotBeNull();
    }

    [Fact]
    public void CampaignWorkspaceInvalidParticipantParamIsDropped()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=abc");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Markup.ShouldNotContain("participant-drawer");
    }

    // ── Drawer sequence navigation ──────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceShowsParticipantPositionAndEnabledNavigationWhenDrawerOpen()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("3 of 142");
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(301, true, false)]
    [InlineData(302, false, false)]
    [InlineData(303, false, true)]
    public void CampaignWorkspaceDisablesSequenceButtonsAccordingToPosition(
        long participantId, bool previousDisabled, bool nextDisabled)
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService(totalCount: 3));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"/campaigns/10?tab=roster&participant={participantId}");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBe(previousDisabled);
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBe(nextDisabled);
    }

    [Fact]
    public void CampaignWorkspaceNextWithinPageChangesOnlyParticipantWithoutReloadingRoster()
    {
        var participantService = CreatePagedParticipantService();
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=301");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var captureScroll = workspaceModule.Setup<double?>("captureScroll", _ => true);
        captureScroll.SetResult(60);

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        var historyCountBefore = ((BunitNavigationManager)navigationManager).History.Count;
        cut.Find("#participant-drawer-next").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=302"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("2 of 142"));

        navigationManager.Uri.ShouldNotContain("page=");
        navigationManager.Uri.ShouldContain("tab=roster");
        _ = participantService.Received(1).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());

        var history = ((BunitNavigationManager)navigationManager).History;
        history.Count.ShouldBe(historyCountBefore + 1);
        history.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
        captureScroll.Invocations.Last().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
    }

    [Fact]
    public void CampaignWorkspacePreviousWithinPageMovesBackwardWithoutReloadingRoster()
    {
        var participantService = CreatePagedParticipantService();
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-previous").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=302"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("2 of 142"));

        navigationManager.Uri.ShouldNotContain("page=");
        _ = participantService.Received(1).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceNextAcrossPageBoundarySelectsFirstOfNextPageCorrectingUrlInPlace()
    {
        var participantService = CreatePagedParticipantService(totalCount: 6);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var scrollToTop = workspaceModule.SetupVoid("scrollToTop", _ => true);
        scrollToTop.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        var historyCountBefore = ((BunitNavigationManager)navigationManager).History.Count;
        cut.Find("#participant-drawer-next").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=304"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("4 of 6"));
        navigationManager.Uri.ShouldContain("page=2");
        new Uri(navigationManager.Uri).AbsolutePath.ShouldBe("/campaigns/10");

        var entries = ((BunitNavigationManager)navigationManager).History.ToList();
        entries.Count.ShouldBe(historyCountBefore + 1);
        var latest = entries[0];
        latest.Options.ReplaceHistoryEntry.ShouldBeTrue();
        latest.Uri.ShouldContain("page=2");
        latest.Uri.ShouldContain("participant=304");

        _ = participantService.Received(2).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() =>
        {
            scrollToTop.Invocations.Count.ShouldBeGreaterThanOrEqualTo(1);
            scrollToTop.Invocations.Last().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
        });
    }

    [Fact]
    public void CampaignWorkspacePreviousAcrossPageBoundarySelectsLastOfPreviousPageCorrectingUrlInPlace()
    {
        var participantService = CreatePagedParticipantService(totalCount: 6);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?page=2&tab=roster&participant=304");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        var historyCountBefore = ((BunitNavigationManager)navigationManager).History.Count;
        cut.Find("#participant-drawer-previous").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=303"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("3 of 6"));
        navigationManager.Uri.ShouldNotContain("page=");

        var entries = ((BunitNavigationManager)navigationManager).History.ToList();
        entries.Count.ShouldBe(historyCountBefore + 1);
        var latest = entries[0];
        latest.Options.ReplaceHistoryEntry.ShouldBeTrue();
        latest.Uri.ShouldContain("participant=303");
        latest.Uri.ShouldNotContain("page=");

        _ = participantService.Received(2).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceSequenceMovesPreserveFilterAndSortParameters()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            "/campaigns/10?tab=roster&search=lee&sortBy=displayName&sortDirection=desc&participant=302");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=303"));

        navigationManager.Uri.ShouldContain("search=lee");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=desc");

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=304"));

        navigationManager.Uri.ShouldContain("page=2");
        navigationManager.Uri.ShouldContain("search=lee");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=desc");
    }

    [Fact]
    public void CampaignWorkspaceOffPageParticipantHidesPositionAndDisablesNavigationButRendersDetail()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=999");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.FindAll("#participant-drawer-position").ShouldBeEmpty();
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeTrue();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMoveToEmptyPageLeavesDrawerOffPageWithoutUrlCorrection()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService(totalCount: 6, page2Items: []));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        var historyCountBefore = ((BunitNavigationManager)navigationManager).History.Count;
        cut.Find("#participant-drawer-next").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));
        cut.WaitForAssertion(() => cut.FindAll("#participant-drawer-position").ShouldBeEmpty());

        navigationManager.Uri.ShouldContain("participant=303");
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeTrue();

        var history = ((BunitNavigationManager)navigationManager).History;
        history.Count.ShouldBe(historyCountBefore + 1);
        history.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMoveClosedBeforeTargetPageLoadsDoesNotReopenDrawer()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = CreatePagedParticipantServiceWithDelayedPage2(page2Completion);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        var historyCountBefore = ((BunitNavigationManager)navigationManager).History.Count;
        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));

        // Close the drawer while the target page is still loading.
        cut.Find("#participant-drawer-close").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));

        page2Completion.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreatePagedRoster(page: 2, 6, [304, 305, 306])));

        // The delayed response must update the roster without resurrecting the drawer.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant 304"));
        cut.Markup.ShouldNotContain("participant-drawer");
        navigationManager.Uri.ShouldNotContain("participant=");
        ((BunitNavigationManager)navigationManager).History.Count.ShouldBe(historyCountBefore + 2);
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMoveCloseThenBackBeforeTargetPageLoadsDoesNotConsumeMove()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = CreatePagedParticipantServiceWithDelayedPage2(page2Completion);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        var historyCountBefore = ((BunitNavigationManager)navigationManager).History.Count;
        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));

        // Close the drawer, then Back to the initiating participant, while page 2 is still
        // loading. The close must cancel the move immediately, so the delayed response cannot
        // consume it when the transient selection round trip (303 → null → 303) restores the
        // initiating participant before the response arrives.
        cut.Find("#participant-drawer-close").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));
        navigationManager.NavigateTo("/campaigns/10?tab=roster&page=2&participant=303");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        page2Completion.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreatePagedRoster(page: 2, 6, [304, 305, 306])));

        // The roster updates to page 2, but the move is gone: the selection stays on the
        // initiating participant (off-page) instead of jumping to 304, and no history entry
        // is replaced.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant 304"));
        cut.Markup.ShouldContain("participant-drawer");
        navigationManager.Uri.ShouldContain("participant=303");
        navigationManager.Uri.ShouldNotContain("participant=304");
        ((BunitNavigationManager)navigationManager).History.Count.ShouldBe(historyCountBefore + 3);
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMoveBackBeforeTargetPageLoadsDoesNotReopenDrawer()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = CreatePagedParticipantServiceWithDelayedPage2(page2Completion);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));

        // Browser Back returns to the workspace URL without the participant query parameter.
        navigationManager.NavigateTo("/campaigns/10?tab=roster");
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));

        page2Completion.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreatePagedRoster(page: 2, 6, [304, 305, 306])));

        // The superseded page-2 response is discarded and the roster stays on page 1; the drawer
        // stays closed and the participant parameter stays off the URL.
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));
        cut.Markup.ShouldContain("Participant 301");
        cut.Markup.ShouldNotContain("Participant 304");
        navigationManager.Uri.ShouldNotContain("participant=");
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMoveFilterChangeBeforeTargetPageLoadsDoesNotConsumeIntent()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                if (input.Page == 2)
                {
                    return page2Completion.Task;
                }

                var roster = string.Equals(input.Search, "jones"
, StringComparison.Ordinal) ? CreatePagedRoster(page: 1, 2, [901, 902])
                    : CreatePagedRoster(page: 1, 6, [301, 302, 303]);
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(roster));
            });
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail())));
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));

        // Back/Forward lands on the same participant with different filters; the newer request
        // supersedes the page-2 load the move was issued against, so the intent must be cleared.
        navigationManager.NavigateTo("/campaigns/10?tab=roster&search=jones&participant=303");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant 901"));

        cut.Markup.ShouldContain("participant-drawer");
        navigationManager.Uri.ShouldContain("participant=303");
        navigationManager.Uri.ShouldNotContain("participant=901");
        navigationManager.Uri.ShouldNotContain("participant=304");

        page2Completion.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreatePagedRoster(page: 2, 6, [304, 305, 306])));

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Participant 304"));
        navigationManager.Uri.ShouldContain("participant=303");
        navigationManager.Uri.ShouldNotContain("participant=304");
    }

    // ── Sequence hardening (Phase 4) ───────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceOpenNavigateCloseRestoresScrollAndPreservesState()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            "/campaigns/10?tab=roster&search=lee&sortBy=displayName&sortDirection=desc&participant=301");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var captureScroll = workspaceModule.Setup<double?>("captureScroll", _ => true);
        captureScroll.SetResult(60);
        var restoreScroll = workspaceModule.SetupVoid("restoreScroll", _ => true);
        restoreScroll.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=302"));

        cut.Find("#participant-drawer-close").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));

        navigationManager.Uri.ShouldNotContain("participant=");
        navigationManager.Uri.ShouldContain("search=lee");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=desc");
        cut.WaitForAssertion(() =>
        {
            restoreScroll.Invocations.Count.ShouldBe(2);
            restoreScroll.Invocations.Last().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
            restoreScroll.Invocations.Last().Arguments[1].ShouldBe(60.0);
        });
    }

    [Fact]
    public void CampaignWorkspaceRapidNavigationEndsOnFinalParticipantWithoutStaleDetail()
    {
        var detailCompletions = new Dictionary<long, TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>>();
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                var roster = input.Page switch
                {
                    1 => CreatePagedRoster(page: 1, 6, [301, 302, 303]),
                    2 => CreatePagedRoster(page: 2, 6, [304, 305, 306]),
                    _ => CreatePagedRoster(page: input.Page ?? 1, 6, [])
                };
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(roster));
            });
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantDetailInput>();
                var tcs = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>();
                detailCompletions[input.PlayerCampaignAssignmentId] = tcs;
                return tcs.Task;
            });
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=301");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.Find("#participant-drawer-next").Click();
        cut.Find("#participant-drawer-next").Click();

        cut.WaitForAssertion(() => detailCompletions.ContainsKey(304).ShouldBeTrue());

        detailCompletions[304].SetResult(new ServiceResult<CampaignParticipantDetailDto>(
            CreateParticipantDetail(assignmentId: 304, displayName: "Detail 304")));
        detailCompletions[301].SetResult(new ServiceResult<CampaignParticipantDetailDto>(
            CreateParticipantDetail(assignmentId: 301, displayName: "Detail 301")));
        detailCompletions[303].SetResult(new ServiceResult<CampaignParticipantDetailDto>(
            CreateParticipantDetail(assignmentId: 303, displayName: "Detail 303")));
        detailCompletions[302].SetResult(new ServiceResult<CampaignParticipantDetailDto>(
            CreateParticipantDetail(assignmentId: 302, displayName: "Detail 302")));

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=304"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("4 of 6"));
        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Detail 304"));
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMovesDisableButtonsAtTrueSequenceEnds()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService(totalCount: 4, page2Items: [304]));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("4 of 4"));
        navigationManager.Uri.ShouldContain("page=2");
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeFalse();

        cut.Find("#participant-drawer-previous").Click();
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("3 of 4"));
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeFalse();

        cut.Find("#participant-drawer-previous").Click();
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("2 of 4"));

        cut.Find("#participant-drawer-previous").Click();
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("1 of 4"));
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void CampaignWorkspaceBoundaryMoveLandsOnFirstItemOfFilteredNextPage()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                input.Search.ShouldBe("jones");
                input.SortBy.ShouldBe("displayName");
                var roster = input.Page switch
                {
                    1 => CreatePagedRoster(page: 1, 5, [301, 302, 303]),
                    2 => CreatePagedRoster(page: 2, 5, [801]),
                    _ => CreatePagedRoster(page: input.Page ?? 1, 5, [])
                };
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(roster));
            });
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail())));
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            "/campaigns/10?tab=roster&search=jones&sortBy=displayName&sortDirection=desc&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=801"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("4 of 5"));
        navigationManager.Uri.ShouldContain("page=2");
        navigationManager.Uri.ShouldContain("search=jones");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=desc");
    }

    [Fact]
    public void CampaignWorkspaceRendersResponsiveRosterLayoutWithDrawerOutsideResponsiveContainers()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=roster&participant=301");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.FindAll("div.table-responsive.d-none.d-md-block").Count.ShouldBe(1);
        cut.FindAll("div.table-responsive.d-none.d-md-block tbody tr").Count.ShouldBe(1);
        cut.FindAll("div.d-md-none").Count.ShouldBe(1);
        cut.FindAll("div.d-md-none li").Count.ShouldBe(1);

        cut.FindAll("aside.participant-drawer").Count.ShouldBe(1);
        cut.FindAll("div.table-responsive .participant-drawer").ShouldBeEmpty();
        cut.FindAll("div.d-md-none .participant-drawer").ShouldBeEmpty();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceUsesEffectiveReadWithExplicitLocalFiltersAndDefaultNameOrder()
    {
        RegisterServices(rosterResult: new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            new PagedResult<CampaignParticipantRosterItem>([], 1, 50, 0)));
        Services.GetRequiredService<NavigationManager>().NavigateTo(
            "/campaigns/10/roster?outcome=undecided&teamId=21&eligibility=needsPlacement");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No participants match the current filters."));

        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.CampaignId == 10 && input.LocalOutcome == "undecided"
                && input.LocalTeamId == 21 && input.TeamId == null && input.Eligibility == "NeedsPlacement"
                && input.SortBy == "displayName" && input.SortDirection == "asc" && input.PageSize == 50),
            Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ICampaignParticipantQueryService>().DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspaceUsesClosedLocalHistoryAndUnfilteredParticipantCount()
    {
        var reads = Substitute.For<IEffectivePlacementQueryService>();
        var local = CreateRosterItem("Archived Participant") with { PlacementOutcome = PlacementOutcome.Assigned, Team = new(21, "Archived Blue") };
        var source = new PlacementDecisionSource(CreateSavedDecision(local, PlacementOutcome.Assigned), "Summer Tryouts", local.Team);
        reads.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                new(10, "Summer Tryouts", CampaignStatus.Closed, new(5, "Summer 2026")),
                new([new(301, 7, "Archived", "Participant", 2032, 14, source)], 1, 50, 1))
            { ParticipantCount = 143 })));
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(CreateDetail(status: CampaignStatus.Closed)), effectivePlacementQueryService: reads);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster?search=Archived&outcome=assigned&teamId=21");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archived Participant"));
        cut.FindAll(".campaign-facts > div").Single(fact => string.Equals(fact.QuerySelector("dt")!.TextContent, "Participants", StringComparison.Ordinal))
            .QuerySelector("dd")!.TextContent.ShouldBe("143 participants");
        cut.Markup.ShouldContain("Campaign record is read-only");
        cut.Markup.ShouldContain("Archived Blue");
        _ = reads.Received(1).GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input =>
            input.Search == "Archived" && input.LocalOutcome == "assigned" && input.LocalTeamId == 21 && input.SortBy == "displayName"), Arg.Any<CancellationToken>());
        _ = reads.DidNotReceive().GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/campaigns/10", "place", false)]
    [InlineData("/campaigns/10", "place", true)]
    [InlineData("/campaigns/10", "close", false)]
    [InlineData("/campaigns/10", "close", true)]
    [InlineData("/campaigns/10/roster", "evaluate", false)]
    [InlineData("/campaigns/10/roster", "evaluate", true)]
    public void ClosedWorkspaceClearsEligibilityBeforeFirstReadAndPreservesReturnContext(string path, string tab, bool initialDetail)
    {
        var detail = CreateDetail(status: CampaignStatus.Closed);
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(detail));
        Services.GetRequiredService<ICampaignParticipantQueryService>()
            .GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail() with { CampaignStatus = CampaignStatus.Closed }));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"{path}?tab={tab}&search=Avery&graduationYears=2031,2032&tagIds=11,12&outcome=notselected&teamId=21&sortBy=tryoutNumber&sortDirection=desc&participant=301&placementSearch=Avery&placementEligibility=Resolved&placementPage=2&eligibility=NeedsPlacement&page=3");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, initialDetail ? detail : null)
            .Add(component => component.InitialDetailScope, initialDetail ? "101:42:False" : null));

        cut.WaitForAssertion(() => AssertClosedEligibilityReturnContext(navigation, path, tab));
        cut.FindAll("#roster-eligibility").ShouldBeEmpty();
        cut.FindAll("a.route-marker").ShouldAllBe(link => !link.GetAttribute("href")!.Contains("eligibility", StringComparison.OrdinalIgnoreCase));
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        _ = reads.Received().GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input => input.ParticipantId == null
            && input.Page == 1 && input.PageSize == 50 && input.Search == "Avery" && input.LocalOutcome == "notselected"
            && input.LocalTeamId == 21 && input.SortBy == "tryoutNumber" && input.SortDirection == "desc"
            && input.GraduationYears != null && input.GraduationYears.Length == 2
            && input.TagDefinitionIds != null && input.TagDefinitionIds.Length == 2), Arg.Any<CancellationToken>());
        _ = reads.DidNotReceive().GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input => (input.Page ?? 1) != 1), Arg.Any<CancellationToken>());
        _ = reads.DidNotReceive().GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/campaigns/10", "place", false)]
    [InlineData("/campaigns/10", "place", true)]
    [InlineData("/campaigns/10/roster", "evaluate", false)]
    [InlineData("/campaigns/10/roster", "evaluate", true)]
    public void ClosedWorkspaceClearsAPlaceSectionThatNoRosterFilterFlagged(string path, string tab, bool initialDetail)
    {
        // A Closed campaign has no placement-eligibility axis and its Place read ignores the section. The
        // replacement is normally flagged by the roster eligibility filter, which this URL does not carry, so
        // the Place section and its stale page must be repaired on their own.
        var detail = CreateDetail(status: CampaignStatus.Closed);
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(detail));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"{path}?tab={tab}&search=Avery&placementSearch=Avery&placementEligibility=Resolved&placementPage=2&placementTeamId=21");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, initialDetail ? detail : null)
            .Add(component => component.InitialDetailScope, initialDetail ? "101:42:False" : null));

        cut.WaitForAssertion(() =>
        {
            var uri = new Uri(navigation.Uri);
            uri.AbsolutePath.ShouldBe(path);
            var query = QueryHelpers.ParseQuery(uri.Query);
            query.ShouldNotContainKey("placementEligibility");
            query.ShouldNotContainKey("placementPage");
            query["tab"].ToString().ShouldBe(tab);
            query["search"].ToString().ShouldBe("Avery");
            // Every other return parameter survives the repair.
            query["placementSearch"].ToString().ShouldBe("Avery");
            query["placementTeamId"].ToString().ShouldBe("21");
            ((BunitNavigationManager)navigation).History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
        });
        cut.FindAll("#roster-eligibility").ShouldBeEmpty();
    }

    [Fact]
    public void ClosedPlaceRepairKeepsALegitimateRosterPage()
    {
        // The destinations own separate pages, so dropping the Place section must not reset the Roster page a
        // Closed link legitimately carried. The evaluate destination loads no roster, so nothing else can
        // rewrite that page while the repair runs.
        var detail = CreateDetail(status: CampaignStatus.Closed);
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(detail));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?tab=evaluate&search=Avery&placementEligibility=Resolved&placementPage=2&page=3");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, detail)
            .Add(component => component.InitialDetailScope, "101:42:False"));

        cut.WaitForAssertion(() =>
        {
            var query = QueryHelpers.ParseQuery(new Uri(navigation.Uri).Query);
            query.ShouldNotContainKey("placementEligibility");
            query.ShouldNotContainKey("placementPage");
            query["page"].ToString().ShouldBe("3");
        });
    }

    [Fact]
    public void ClosedRosterRepairKeepsALegitimatePlacePage()
    {
        // The mirror of the above: dropping the Roster eligibility filter must not reset the Place page.
        var detail = CreateDetail(status: CampaignStatus.Closed);
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(detail));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?tab=evaluate&search=Avery&eligibility=NeedsPlacement&page=3&placementPage=2");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, detail)
            .Add(component => component.InitialDetailScope, "101:42:False"));

        cut.WaitForAssertion(() =>
        {
            var query = QueryHelpers.ParseQuery(new Uri(navigation.Uri).Query);
            query.ShouldNotContainKey("eligibility");
            query.ShouldNotContainKey("page");
            query["placementPage"].ToString().ShouldBe("2");
        });
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedWorkspaceRenormalizesEligibilityReintroducedByHistoryOrParameterRefreshAsync(bool refresh)
    {
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(CreateDetail(status: CampaignStatus.Closed)));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?search=original");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        reads.ClearReceivedCalls();

        navigation.NavigateTo("/campaigns/10/roster?search=history&eligibility=Unavailable&page=3");
        if (refresh)
        {
            cut.Render();
        }

        await cut.WaitForAssertionAsync(() =>
        {
            navigation.Uri.ShouldNotContain("eligibility=");
            navigation.Uri.ShouldNotContain("page=");
            cut.Find("#roster-search").GetAttribute("value").ShouldBe("history");
            _ = reads.Received().GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input => input.Search == "history" && input.Page == 1), Arg.Any<CancellationToken>());
        });
        _ = reads.DidNotReceive().GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input => (input.Page ?? 1) != 1), Arg.Any<CancellationToken>());
        ((BunitNavigationManager)navigation).History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
        cut.FindAll("#roster-eligibility").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(10, "101:43:False")]
    [InlineData(11, "101:42:False")]
    public void RejectedClosedEntrySnapshotCannotClearAuthorizedActiveEligibility(long campaignId, string scope)
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?eligibility=NeedsPlacement&page=3");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.InitialDetail, CreateDetail(status: CampaignStatus.Closed) with { CampaignId = campaignId })
            .Add(component => component.InitialDetailScope, scope));

        cut.WaitForAssertion(() => cut.Find("#roster-eligibility").GetAttribute("value").ShouldBe("NeedsPlacement"));
        navigation.Uri.ShouldContain("eligibility=NeedsPlacement&page=3");
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received()
            .GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Eligibility == "NeedsPlacement" && input.Page == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActiveEligibilitySurvivesUntilConflictRefreshClosesCampaignAndDoesNotReturnOnReopenAsync()
    {
        var fixture = Substitute.For<ICampaignParticipantQueryService>();
        fixture.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                CreatePagedRoster(call.Arg<GetCampaignParticipantRosterInput>().Page ?? 1, 150, [301], pageSize: 50))));
        RegisterServices(participantQueryService: fixture);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10/roster?eligibility=NeedsPlacement&page=3");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        var reads = Services.GetRequiredService<IEffectivePlacementQueryService>();
        await cut.WaitForAssertionAsync(() => cut.Find("#roster-eligibility").GetAttribute("value").ShouldBe("NeedsPlacement"));
        _ = reads.Received().GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Eligibility == "NeedsPlacement" && input.Page == 3), Arg.Any<CancellationToken>());
        navigation.Uri.ShouldContain("eligibility=NeedsPlacement&page=3");
        var queries = Services.GetRequiredService<ICampaignQueryService>();
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(CreateDetail(status: CampaignStatus.Closed)));
        reads.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.Conflict("Campaign closed elsewhere")));

        navigation.NavigateTo("/campaigns/10/roster?search=updated&eligibility=NeedsPlacement&page=3");

        await cut.WaitForAssertionAsync(() =>
        {
            cut.Markup.ShouldContain("Campaign record is read-only");
            navigation.Uri.ShouldNotContain("eligibility=");
            navigation.Uri.ShouldNotContain("page=");
            _ = reads.Received().GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input => input.Search == "updated" && input.Page == 1), Arg.Any<CancellationToken>());
        });
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(CreateDetail()));
        reads.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ToEffectiveRoster(CreateRoster())));
        reads.ClearReceivedCalls();
        await cut.InvokeAsync(() => cut.FindComponent<Nova.UI.Features.Campaigns.Components.CampaignWorkspaceReadiness>().Instance.OnLifecycleChanged.InvokeAsync());

        await cut.WaitForAssertionAsync(() => ((AngleSharp.Html.Dom.IHtmlSelectElement)cut.Find("#roster-eligibility")).Value.ShouldBeNullOrEmpty());
        _ = reads.Received().GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Eligibility == null && input.Page == 1 && input.Search == "updated"), Arg.Any<CancellationToken>());
        navigation.Uri.ShouldNotContain("eligibility=");
    }

    [Fact]
    public void ClosedWorkspaceRejectsPersistedRosterOwnedByUnnormalizedEligibilityQuery()
    {
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(CreateDetail(status: CampaignStatus.Closed)));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var query = CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState { Eligibility = "NeedsPlacement", Page = 3 });
        navigation.NavigateTo($"/campaigns/10/roster?{query}");

        var cut = Render<PersistedStateCampaignWorkspace>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.StartInitialized, true)
            .Add(component => component.PersistedCampaignDetail, CreateDetail(status: CampaignStatus.Closed))
            .Add(component => component.SeedOwner, $"101:42:False:10:Closed:{query}")
            .Add(component => component.SeedRoster, CreatePagedRoster(3, 150, [999], pageSize: 50)));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Markup.ShouldNotContain("Participant 999");
        navigation.Uri.ShouldNotContain("eligibility=");
        navigation.Uri.ShouldNotContain("page=");
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received()
            .GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(input => input.Page == 1), Arg.Any<CancellationToken>());
        cut.Instance.PersistedOwner.ShouldNotBeNull();
        cut.Instance.PersistedOwner.ShouldNotContain("eligibility=");
    }

    private static void AssertClosedEligibilityReturnContext(NavigationManager navigation, string path, string tab)
    {
        var uri = new Uri(navigation.Uri);
        uri.AbsolutePath.ShouldBe(path);
        var query = QueryHelpers.ParseQuery(uri.Query);
        query.ShouldNotContainKey("eligibility");
        query.ShouldNotContainKey("page");
        query["tab"].ToString().ShouldBe(tab);
        query["search"].ToString().ShouldBe("Avery");
        query["graduationYears"].ToString().ShouldBe("2031,2032");
        query["tagIds"].ToString().ShouldBe("11,12");
        query["outcome"].ToString().ShouldBe("notselected");
        query["teamId"].ToString().ShouldBe("21");
        query["sortBy"].ToString().ShouldBe("tryoutNumber");
        query["sortDirection"].ToString().ShouldBe("desc");
        query["participant"].ToString().ShouldBe("301");
        // A Closed campaign has no eligibility axis, so the Place section filter is cleared with the roster
        // one, while the literal search and every other return parameter survive.
        query.ShouldNotContainKey("placementEligibility");
        query.ShouldNotContainKey("placementPage");
        query["placementSearch"].ToString().ShouldBe("Avery");
        ((BunitNavigationManager)navigation).History.First().Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Fact]
    public void CampaignWorkspaceSeparatesMissingLocalDecisionFromInheritedEffectiveAssignment()
    {
        var inheritedTeam = new CampaignParticipantTeamSummaryDto(21, "Inherited Blue");
        var source = new PlacementDecisionSource(new CampaignSavedPlacementDecision(201, 7, 9, 5, 1,
            PlacementOutcome.Assigned, 21, new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), 101,
            "Coach Rivera", Guid.NewGuid()), "Spring campaign", inheritedTeam);
        var row = new CampaignEffectivePlacementItem(301, 7, "Avery", "Johnson", 2032, 14,
            LifecycleStatus.Active, Guid.NewGuid(), null, source, inheritedTeam,
            EffectivePlacementEligibility.OptionalReassignment, PlacementCorrectionReason.None);
        var reads = Substitute.For<IEffectivePlacementQueryService>();
        reads.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(new CampaignEffectivePlacementsResult(
                new(10, "Summer Tryouts", CampaignStatus.Active, new(5, "Summer 2026")), new(12, 130, 1, 0), new([row], 1, 50, 1)))));
        RegisterServices(effectivePlacementQueryService: reads);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster?search=Avery");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var rendered = cut.Find("#roster-row-301");
        rendered.TextContent.ShouldContain("No campaign decision");
        rendered.TextContent.ShouldContain("Inherited Blue");
        rendered.TextContent.ShouldContain("Spring campaign");
        rendered.TextContent.ShouldContain("Optional reassignment");
        cut.FindAll(".campaign-facts > div").Single(fact => string.Equals(fact.QuerySelector("dt")!.TextContent, "Participants", StringComparison.Ordinal))
            .QuerySelector("dd")!.TextContent.ShouldBe("143 participants");
    }

    [Fact]
    public void CampaignWorkspaceRetriesReadinessWithoutReloadingSuccessfulRoster()
    {
        var readiness = Substitute.For<ICampaignCloseoutQueryService>();
        readiness.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(ServiceProblem.ServerError("Unavailable"))),
                Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness() with { IsReady = false, NeedsPlacementCount = 0 })));
        RegisterServices(readinessQueryService: readiness);
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close readiness unavailable"));
        cut.Markup.ShouldContain("Avery Johnson");
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Retry readiness", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Not ready to close"));
        cut.Markup.ShouldContain("0 need placement");
        cut.Markup.ShouldContain("Avery Johnson");
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("102:42:False:10:Active:")]
    [InlineData("101:43:False:10:Active:")]
    [InlineData("101:42:True:10:Active:")]
    [InlineData("101:42:False:11:Active:")]
    [InlineData("101:42:False:10:Closed:")]
    [InlineData("101:42:False:10:Active:search=other")]
    public void CampaignWorkspaceRejectsPersistedSnapshotFromDifferentOwner(string owner)
    {
        RegisterServices();
        var cut = Render<PersistedStateCampaignWorkspace>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.StartInitialized, true)
            .Add(component => component.PersistedCampaignDetail, CreateDetail("Obsolete campaign"))
            .Add(component => component.SeedOwner, owner));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));
        cut.Markup.ShouldNotContain("Obsolete campaign");
        _ = Services.GetRequiredService<ICampaignQueryService>().Received(1).GetCampaignDetailAsync(
            Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReadinessIgnoresObsoleteFailureAfterClosedLifecycleReplacesActiveRequest()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignCloseoutReadinessDto>>();
        var query = Substitute.For<ICampaignCloseoutQueryService>();
        query.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        Services.AddSingleton(query);
        var cut = Render<Nova.UI.Features.Campaigns.Components.CampaignWorkspaceReadiness>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.Status, CampaignStatus.Active).Add(component => component.Owner, "101:42"));
        cut.Markup.ShouldContain("Checking Close readiness");
        cut.Render(parameters => parameters.Add(component => component.Status, CampaignStatus.Closed));
        pending.SetResult(new ServiceResult<CampaignCloseoutReadinessDto>(ServiceProblem.ServerError("Late error")));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign record is read-only"));
        cut.Markup.ShouldNotContain("unavailable");
        cut.FindAll("button").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadinessRestoresMatchingSnapshotWithoutDuplicateRead(bool failed)
    {
        var query = Substitute.For<ICampaignCloseoutQueryService>();
        Services.AddSingleton(query);
        var cut = Render<RestoredReadiness>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.Status, CampaignStatus.Active).Add(component => component.Owner, "101:42:False:1")
            .Add(component => component.SeedError, failed));
        cut.Markup.ShouldContain(failed ? "Close readiness unavailable" : "Ready to close");
        _ = query.DidNotReceive().GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("102:42:False:1:10:Active")]
    [InlineData("101:43:False:1:10:Active")]
    [InlineData("101:42:True:1:10:Active")]
    [InlineData("101:42:False:2:10:Active")]
    [InlineData("101:42:False:1:11:Active")]
    [InlineData("101:42:False:1:10:Closed")]
    public void ReadinessReloadsSnapshotOwnedByAnotherAuthorityCampaignLifecycleOrRevision(string owner)
    {
        var query = Substitute.For<ICampaignCloseoutQueryService>();
        query.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness())));
        Services.AddSingleton(query);
        var cut = Render<RestoredReadiness>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.Status, CampaignStatus.Active).Add(component => component.Owner, "101:42:False:1")
            .Add(component => component.SeedOwner, owner).Add(component => component.SeedError, true));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Ready to close"));
        cut.Markup.ShouldNotContain("Close readiness unavailable");
        _ = query.Received(1).GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
    }

#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    [Fact]
    public void ApprovedMemberEvaluationHandoffCanSavePlacementThroughWorkspaceAdapter()
    {
        RegisterServices(isClubAdmin: false);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=place&evaluation=true&evalParticipant=301&placementParticipant=301&returnToEvaluation=true");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(c => c.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var placement = cut.FindComponent<Nova.UI.Features.Campaigns.Components.CampaignPlacePanel>();
        placement.Instance.CanEditPlacements.ShouldBeTrue();
        placement.Find("#place-outcome").Change("NotSelected");
        placement.Find("button.btn-primary").Click();
        placement.WaitForAssertion(() => placement.Markup.ShouldContain("Placement saved."));
        _ = Services.GetRequiredService<ICampaignPlacementService>().Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(i => i.PlayerCampaignAssignmentId == 301 && i.Outcome == PlacementOutcome.NotSelected), Arg.Any<CancellationToken>());
        placement.FindAll("a").Single(a => string.Equals(a.TextContent, "Return to evaluation", StringComparison.Ordinal))
            .GetAttribute("href")!.ShouldContain("evalParticipant=301");
    }

    private void RegisterServices(
#pragma warning restore MA0051
        ICampaignQueryService? campaignQueryService = null,
        ICampaignParticipantQueryService? participantQueryService = null,
        ServiceResult<CampaignDetailResult>? detailResult = null,
        ServiceResult<PagedResult<CampaignParticipantRosterItem>>? rosterResult = null,
        IReadOnlyList<int>? graduationYearChoices = null,
        IReadOnlyList<TagDefinitionDto>? tagChoices = null,
        IReadOnlyList<TeamRosterItem>? teamChoices = null,
        ICampaignPlacementQueryService? placementQueryService = null,
        ICampaignPlacementService? placementService = null,
        ICampaignMetadataService? campaignMetadataService = null,
        ServiceResult<CampaignCreationSetupResult>? setupResult = null,
        bool isClubAdmin = false,
        IEffectivePlacementQueryService? effectivePlacementQueryService = null,
        ICampaignCloseoutQueryService? readinessQueryService = null,
        AuthenticationStateProvider? authenticationStateProvider = null)
    {
        if (campaignQueryService is null)
        {
            campaignQueryService = Substitute.For<ICampaignQueryService>();
            campaignQueryService.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(detailResult ?? new ServiceResult<CampaignDetailResult>(CreateDetail())));
            campaignQueryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(setupResult ?? new ServiceResult<CampaignCreationSetupResult>(CreateSetup())));
        }

        if (participantQueryService is null)
        {
            participantQueryService = Substitute.For<ICampaignParticipantQueryService>();
            participantQueryService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(rosterResult ?? new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));
            participantQueryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail())));
        }

        participantQueryService.GetRosterGraduationYearsAsync(
                Arg.Any<GetCampaignParticipantGraduationYearsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<int>>(
                (graduationYearChoices ?? CreateGraduationYearChoices()).ToList())));

        // Retain the established paging/race fixtures while exercising the new authoritative reads.
        // The service injected into the UI has no legacy roster response configured, so a production
        // fallback to GetParticipantRosterAsync cannot make these scenarios pass.
        effectivePlacementQueryService ??= CreateEffectiveFixture(participantQueryService);
        var participantDetails = Substitute.For<ICampaignParticipantQueryService>();
        participantDetails.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call => participantQueryService.GetParticipantDetailAsync(
                call.Arg<GetCampaignParticipantDetailInput>(), call.Arg<CancellationToken>()));
        participantDetails.GetRosterGraduationYearsAsync(Arg.Any<GetCampaignParticipantGraduationYearsInput>(), Arg.Any<CancellationToken>())
            .Returns(call => participantQueryService.GetRosterGraduationYearsAsync(
                call.Arg<GetCampaignParticipantGraduationYearsInput>(), call.Arg<CancellationToken>()));

        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(
                (tagChoices ?? CreateTagChoices()).ToList())));

        var evaluationNoteService = Substitute.For<ICampaignEvaluationNoteService>();
        var tagApplicationService = Substitute.For<ICampaignTagApplicationService>();

        var teamRosterService = Substitute.For<ITeamRosterService>();
        teamRosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(
                (teamChoices ?? CreateTeamChoices()).ToList())));

        if (placementQueryService is null)
        {
            placementQueryService = Substitute.For<ICampaignPlacementQueryService>();
            placementQueryService.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreatePlacementRoster())));
            placementQueryService.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreatePlacementSummary())));
        }

        if (placementService is null)
        {
            placementService = Substitute.For<ICampaignPlacementService>();
            placementService.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<PlacementMutationSuccess>(
                    new PlacementMutationSuccess(Guid.NewGuid()))));
        }

        campaignMetadataService ??= Substitute.For<ICampaignMetadataService>();

        var closeoutQueryService = Substitute.For<ICampaignCloseoutQueryService>();
        closeoutQueryService.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness())));
        closeoutQueryService.GetActivityAsync(Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignActivityResult>(new CampaignActivityResult([]))));
        var lifecycleService = Substitute.For<ICampaignLifecycleService>();

        Services.AddSingleton(campaignQueryService);
        Services.AddSingleton(participantDetails);
        Services.AddSingleton(effectivePlacementQueryService);
        Services.AddSingleton(tagDefinitionQueryService);
        var evidence = Substitute.For<ICampaignEvaluationQueryService>();
        evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>(
                [new(1, "Strong defensive player.", "Coach Rivera", new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero), null, false, false, Guid.NewGuid())], null))));
        evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>([], null))));
        evidence.GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<EvaluationTagChoice>>(Array.Empty<EvaluationTagChoice>())));
        Services.AddSingleton(evidence);
        Services.AddSingleton(evaluationNoteService);
        Services.AddSingleton(tagApplicationService);
        Services.AddSingleton(teamRosterService);
        Services.AddSingleton(placementQueryService);
        Services.AddSingleton(placementService);
        Services.AddSingleton(campaignMetadataService);
        Services.AddSingleton(readinessQueryService ?? closeoutQueryService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton(authenticationStateProvider ?? new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin)));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static IReadOnlyList<int> CreateGraduationYearChoices() => [2031, 2032];

    private static IEffectivePlacementQueryService CreateEffectiveFixture(ICampaignParticipantQueryService fixture)
    {
        var service = Substitute.For<IEffectivePlacementQueryService>();
        service.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.Arg<GetCampaignEffectivePlacementsInput>();
                if (input.ParticipantId is { } linkedId)
                {
                    return new ServiceResult<CampaignEffectivePlacementsResult>(ToEffectiveRoster(
                        new([CreateRosterItem("Linked participant", linkedId)], 1, 1, 1)));
                }
                var result = await fixture.GetParticipantRosterAsync(ToFixtureInput(input), call.Arg<CancellationToken>());
                return result.IsProblem
                    ? new ServiceResult<CampaignEffectivePlacementsResult>(result.Problem)
                    : new ServiceResult<CampaignEffectivePlacementsResult>(ToEffectiveRoster(result.Value));
            });
        service.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.Arg<GetClosedCampaignRosterInput>();
                var result = input.ParticipantId is { } linkedId
                    ? new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(new PagedResult<CampaignParticipantRosterItem>(
                        [CreateRosterItem("Linked participant", linkedId)], 1, 1, 1))
                    : await fixture.GetParticipantRosterAsync(ToFixtureInput(input), call.Arg<CancellationToken>());
                if (result.IsProblem)
                {
                    return new ServiceResult<ClosedCampaignRosterResult>(result.Problem);
                }
                var rows = result.Value.Items.Select(item => new ClosedCampaignRosterItem(
                    item.PlayerCampaignAssignmentId, item.PlayerId, item.DisplayName, string.Empty,
                    item.GraduationYear, item.TryoutNumber,
                    new PlacementDecisionSource(CreateSavedDecision(item, PlacementOutcome.NotSelected), "Summer Tryouts", item.Team))
                { AppliedTags = item.AppliedTags }).ToList();
                return new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                    new(10, "Summer Tryouts", CampaignStatus.Closed, new(5, "Summer 2026")),
                    new(rows, result.Value.Page, result.Value.PageSize, result.Value.TotalCount))
                { ParticipantCount = result.Value.TotalCount });
            });
        return service;
    }

    private static GetCampaignParticipantRosterInput ToFixtureInput(CampaignRosterDiscoveryInput input) => new()
    {
        CampaignId = input.CampaignId,
        Search = input.Search,
        GraduationYears = input.GraduationYears,
        TagDefinitionIds = input.TagDefinitionIds,
        Outcome = input.LocalOutcome,
        TeamId = input.LocalTeamId,
        SortBy = input.SortBy,
        SortDirection = input.SortDirection,
        Page = input.Page,
        PageSize = input.PageSize
    };

    private static CampaignEffectivePlacementsResult ToEffectiveRoster(PagedResult<CampaignParticipantRosterItem> roster)
        => new(new(10, "Summer Tryouts", CampaignStatus.Active, new(5, "Summer 2026")),
            new(roster.TotalCount, 0, 0, 0),
            new(roster.Items.Select(item => new CampaignEffectivePlacementItem(
                item.PlayerCampaignAssignmentId, item.PlayerId, item.DisplayName, string.Empty,
                item.GraduationYear, item.TryoutNumber, LifecycleStatus.Active, Guid.NewGuid(),
                item.PlacementOutcome == PlacementOutcome.Undecided ? null : CreateSavedDecision(item, item.PlacementOutcome),
                null, null, EffectivePlacementEligibility.NeedsPlacement, PlacementCorrectionReason.None)
            { LocalTeam = item.Team, AppliedTags = item.AppliedTags }).ToList(), roster.Page, roster.PageSize, roster.TotalCount));

    private static CampaignSavedPlacementDecision CreateSavedDecision(CampaignParticipantRosterItem item, PlacementOutcome outcome)
        => new(item.PlayerCampaignAssignmentId, item.PlayerId, 10, 5, 1, outcome, item.Team?.TeamId,
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero), 101, "Coach Rivera", Guid.NewGuid());

    private static IReadOnlyList<TagDefinitionDto> CreateTagChoices() =>
    [
        new() { PlayerTagId = 11, Name = "Lefty", Color = "#0D6EFD", LifecycleStatus = LifecycleStatus.Active },
        new() { PlayerTagId = 12, Name = "Captain", Color = "#FD7E14", LifecycleStatus = LifecycleStatus.Active }
    ];

    private static IReadOnlyList<TeamRosterItem> CreateTeamChoices() =>
    [
        new()
        {
            TeamId = 21,
            Name = "Blue",
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active,
            ActivePlacementCount = 4
        },
        new()
        {
            TeamId = 22,
            Name = "Gold",
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active,
            ActivePlacementCount = 3
        }
    ];

    private static CampaignDetailResult CreateDetail(
        string name = "Summer Tryouts",
        CampaignStatus status = CampaignStatus.Active) => new()
        {
            CampaignId = 10,
            Name = name,
            Status = status,
            StartDate = new DateOnly(2026, 6, 15),
            PlannedEndDate = new DateOnly(2026, 6, 20),
            ParticipantCount = 12,
            SeasonId = 5,
            SeasonName = "Summer 2026"
        };

    private static CampaignCreationSetupResult CreateSetup() => new()
    {
        CurrentSeason = new CampaignSeasonChoice
        {
            SeasonId = 5,
            Name = "Summer 2026",
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 31)
        },
        ActivePlayerCount = 20,
        ActiveTeamCount = 4
    };

    private static CampaignParticipantDetailDto CreateParticipantDetail(
        long assignmentId = 301,
        string displayName = "Avery Johnson") => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: 7,
        DisplayName: displayName,
        GraduationYear: 2032,
        TryoutNumber: 14,
        PlacementOutcome: PlacementOutcome.Undecided,
        Team: null,
        CreatedAt: new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
        ModifiedAt: new DateTimeOffset(2026, 5, 3, 14, 30, 0, TimeSpan.Zero),
        CampaignStatus: CampaignStatus.Active,
        ConcurrencyToken: Guid.NewGuid(),
        Capabilities: new CampaignParticipantCapabilitiesDto(
            CanEditPlacement: false,
            CanAddNote: false,
            CanApplyTag: false,
            CanArchiveTagDefinitions: false));

    private static PagedResult<CampaignParticipantRosterItem> CreateRoster() => new(
        Items: [CreateRosterItem()],
        Page: 1,
        PageSize: GetCampaignParticipantRosterInput.DefaultPageSize,
        TotalCount: 12);

    private static PagedResult<CampaignParticipantRosterItem> CreateRoster(CampaignParticipantRosterItem item) => new(
        Items: [item],
        Page: 1,
        PageSize: GetCampaignParticipantRosterInput.DefaultPageSize,
        TotalCount: 1);

    private static CampaignParticipantRosterItem CreateRosterItem(string displayName = "Avery Johnson", long assignmentId = 301) => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: 7,
        DisplayName: displayName,
        GraduationYear: 2032,
        TryoutNumber: 14,
        PlacementOutcome: PlacementOutcome.Undecided,
        Team: null,
        AppliedTags: []);

    private static PagedResult<CampaignPlacementRosterItem> CreatePlacementRoster() => new(
        Items: [CreatePlacementRosterItem()],
        Page: 1,
        PageSize: GetCampaignPlacementRosterInput.DefaultPageSize,
        TotalCount: 1);

    private static CampaignPlacementRosterItem CreatePlacementRosterItem(
        string displayName = "Avery Johnson",
        string firstName = "Avery",
        string lastName = "Johnson",
        long assignmentId = 301,
        PlacementOutcome outcome = PlacementOutcome.Undecided,
        CampaignParticipantTeamSummaryDto? team = null) => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: 7,
        DisplayName: displayName,
        FirstName: firstName,
        LastName: lastName,
        GraduationYear: 2032,
        outcome,
        team,
        Guid.NewGuid());

    private static CampaignPlacementSummaryDto CreatePlacementSummary(
        int assigned = 0,
        int notSelected = 0,
        int withdrawn = 0,
        int undecided = 1) => new(
        AssignedCount: assigned,
        NotSelectedCount: notSelected,
        WithdrawnCount: withdrawn,
        UndecidedCount: undecided,
        TotalCount: assigned + notSelected + withdrawn + undecided);

    private static CampaignCloseoutReadinessDto CreateReadiness() => new(
        CampaignId: 10,
        Status: CampaignStatus.Active,
        IsReady: true,
        Summary: CreatePlacementSummary(),
        Blockers: []);

    /// <summary>
    /// Creates a participant query-service fake whose roster returns page 1 with assignments 301–303
    /// and page 2 with assignments 304–306 (or the supplied override), so sequence moves can be exercised.
    /// </summary>
    private static ICampaignParticipantQueryService CreatePagedParticipantService(
        int totalCount = 142,
        long[]? page2Items = null)
    {
        var service = Substitute.For<ICampaignParticipantQueryService>();
        service.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                var roster = input.Page switch
                {
                    1 => CreatePagedRoster(page: 1, totalCount, [301, 302, 303]),
                    2 => CreatePagedRoster(page: 2, totalCount, page2Items ?? [304, 305, 306]),
                    _ => CreatePagedRoster(page: input.Page ?? 1, totalCount, [])
                };
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(roster));
            });
        service.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail())));
        return service;
    }

    /// <summary>
    /// Creates a participant query-service fake whose page 1 returns assignments 301–303 and whose
    /// page 2 response is held back until the supplied completion source is set, so cross-page
    /// moves can be interrupted before the target page finishes loading.
    /// </summary>
    private static ICampaignParticipantQueryService CreatePagedParticipantServiceWithDelayedPage2(
        TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>> page2Completion)
    {
        var service = Substitute.For<ICampaignParticipantQueryService>();
        service.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                return input.Page == 2
                    ? page2Completion.Task
                    : Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                        CreatePagedRoster(page: 1, 6, [301, 302, 303])));
            });
        service.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail())));
        return service;
    }

    private static PagedResult<CampaignParticipantRosterItem> CreatePagedRoster(
        int page, int totalCount, long[] assignmentIds, int pageSize = 3) => new(
        Items: assignmentIds.Select(id => CreateRosterItem($"Participant {id}", id)).ToList(),
        Page: page,
        PageSize: pageSize,
        TotalCount: totalCount);

    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101"),
            new(NovaClaimTypes.ClubId, "42")
        };

        if (isClubAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.ClubAdmin));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectoryPath = Path.Join(directory.FullName, ".git");
            if (Directory.Exists(gitDirectoryPath) || File.Exists(gitDirectoryPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for campaign workspace route assertion.");
    }

    /// <summary>
    /// Provides a fixed authentication state for bUnit component tests.
    /// </summary>
    /// <param name="principal">The principal to return from <see cref="GetAuthenticationStateAsync"/>.</param>
    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    private sealed class ControlledReconciliationHandler : JSRuntimeInvocationHandler<bool>
    {
        // Completion is released on the renderer dispatcher so the awaiting render finishes before assertions.
        private readonly TaskCompletionSource<bool> _completion = new();

        public ControlledReconciliationHandler() : base(
            invocation => string.Equals(invocation.Identifier, "reconcileWorkspaceLocation", StringComparison.Ordinal), isCatchAllHandler: false)
        {
            SetResult(false);
        }

        public void Complete(bool reconciled) => _completion.SetResult(reconciled);

        protected override async Task<bool> HandleAsync(JSRuntimeInvocation invocation)
        {
            await base.HandleAsync(invocation);
            return await _completion.Task;
        }
    }

#pragma warning disable CA1812 // bUnit constructs the test-only restored component through reflection.
    private sealed class RestoredReadiness(ICampaignCloseoutQueryService queries)
        : Nova.UI.Features.Campaigns.Components.CampaignWorkspaceReadiness(queries)
#pragma warning restore CA1812
    {
        [Parameter] public string? SeedOwner { get; set; }
        [Parameter] public bool SeedError { get; set; }

        protected override void OnInitialized()
        {
            Initialized = true;
            PersistedOwner = SeedOwner ?? $"{Owner}:{CampaignId}:{Status}";
            PersistedError = SeedError;
            PersistedReadiness = SeedError ? null : CreateReadiness();
            base.OnInitialized();
        }
    }

    /// <summary>
    /// A test-only <see cref="CampaignWorkspacePage"/> subclass that seeds persisted prerender state.
    /// </summary>
#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class PersistedStateCampaignWorkspace(
#pragma warning restore CA1812
        ICampaignQueryService campaignQueryService,
        ICampaignParticipantQueryService participantQueryService,
        IEffectivePlacementQueryService effectivePlacementQueryService,
        ITagDefinitionQueryService tagDefinitionQueryService,
        ITeamRosterService teamRosterService,
        ICampaignMetadataService campaignMetadataService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager,
        IJSRuntime jsRuntime)
        : CampaignWorkspacePage(campaignQueryService, participantQueryService, effectivePlacementQueryService, tagDefinitionQueryService, teamRosterService, campaignMetadataService, authenticationStateProvider, navigationManager, jsRuntime)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public CampaignDetailResult? PersistedCampaignDetail { get; set; }

        [Parameter]
        public string? SeedOwner { get; set; }

        [Parameter]
        public PagedResult<CampaignParticipantRosterItem>? SeedRoster { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedDetail = PersistedCampaignDetail;
                PersistedRoster = SeedRoster;
                PersistedOwner = SeedOwner ?? $"101:42:False:{CampaignId}:{PersistedCampaignDetail?.Status}:";
            }

            return base.OnInitializedAsync();
        }
    }
}
