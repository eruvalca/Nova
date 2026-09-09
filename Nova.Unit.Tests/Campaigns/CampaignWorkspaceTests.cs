using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign workspace shell covering the header, tab bar, detail-load
/// states, roster-load ordering, URL-backed roster filters and sorting, paging, empty states, and
/// persisted-state restoration.
/// </summary>
public sealed class CampaignWorkspaceTests : BunitContext
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
            cut.Markup.ShouldContain("12 participants");
        });

        cut.Find("span.badge.text-bg-success").TextContent.Trim().ShouldBe("Active");
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
            ["/campaigns/10/roster?tab=roster", "/campaigns/10?tab=evaluate", "/campaigns/10?tab=place", "/campaigns/10?tab=close"]);
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
    public void CampaignWorkspaceFallsBackToEvaluateTabWhenTabQueryIsUnknown()
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
    public void CampaignWorkspacePushesTabQueryWhenEvaluateTabSelected()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        navigationManager.NavigateTo("/campaigns/10/roster?tab=roster");
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/campaigns/10/roster?tab=roster"));
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

        // Evaluate → Placements switches the rendered region.
        navigationManager.NavigateTo("/campaigns/10?tab=place");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("placements-region-heading"));
        cut.Markup.ShouldNotContain("roster-region-heading");

        // Placements → Evaluate switches back.
        navigationManager.NavigateTo("/campaigns/10/roster?tab=roster");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("roster-region-heading"));
        cut.Markup.ShouldNotContain("placements-region-heading");
    }

    // ── Overview / Closeout tabs ──────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspaceOverviewTabClickPushesOverviewUrlAndRendersOverviewRegion()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/campaigns/10?tab=evaluate"));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("overview-region-heading"));
        cut.Markup.ShouldNotContain("roster-region-heading");
    }

    [Fact]
    public void CampaignWorkspaceCloseoutTabClickPushesCloseoutUrlAndRendersCloseoutRegion()
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
    public void CampaignWorkspaceActivatesOverviewTabWhenTabQueryIsOverview()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.FindAll("ul.route-marker-list .nav-link.active")[0].QuerySelector(".route-marker-label")!.TextContent.Trim().ShouldBe("Evaluate");
        cut.Markup.ShouldContain("overview-region-heading");
    }

    [Fact]
    public void CampaignWorkspaceActivatesCloseoutTabWhenTabQueryIsCloseout()
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
    [InlineData("placements")]
    [InlineData("overview")]
    [InlineData("closeout")]
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
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.FindAll("button.roster-sort-header")[1].Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("sortBy=displayName&sortDirection=asc"));
        cut.WaitForAssertion(() => cut.FindAll("button.roster-sort-header").Count.ShouldBe(5));

        cut.FindAll("button.roster-sort-header")[1].Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("sortBy=displayName&sortDirection=desc"));
        cut.WaitForAssertion(() => cut.FindAll("th[aria-sort]")[1].GetAttribute("aria-sort").ShouldBe("descending"));
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
        cut.FindAll("button.btn-outline-secondary").ShouldBeEmpty();
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
    public void CampaignWorkspaceDetachesKeydownSuppressionWhenRosterReloadFails()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())),
                Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
                    ServiceProblem.ServerError("Roster service unavailable."))));

        RegisterServices(participantQueryService: participantService);

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var attach = workspaceModule.SetupVoid("attachRosterActivationSuppression", _ => true);
        attach.SetVoidResult();
        var detach = workspaceModule.SetupVoid("detachRosterActivationSuppression", _ => true);
        detach.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        attach.Invocations.Count.ShouldBeGreaterThanOrEqualTo(1);

        cut.Find("#roster-outcome").Change("assigned");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Roster service unavailable."));

        detach.Invocations.Count.ShouldBeGreaterThanOrEqualTo(1);
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

        var clearButtons = cut.FindAll("button.btn-outline-secondary");
        clearButtons.ShouldNotBeEmpty();
        clearButtons[^1].Click();

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
        navigationManager.Uri.ShouldEndWith("/campaigns/10/roster?outcome=assigned&tab=roster");
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
        navigationManager.NavigateTo("/campaigns/10?tab=roster");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var captureScroll = workspaceModule.Setup<double?>("captureScroll", _ => true);
        captureScroll.SetResult(120);
        var scrollToTop = workspaceModule.SetupVoid("scrollToTop", _ => true);
        scrollToTop.SetVoidResult();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var rosterRefId = cut.Find(".roster-scroll-region").GetAttribute("blazor:elementreference");
        rosterRefId.ShouldNotBeNullOrEmpty();

        cut.FindAll("button.roster-sort-header")[1].Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("sortBy=displayName&sortDirection=asc"));
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
            "/campaigns/10?tab=roster&search=lee&sortBy=displayName&sortDirection=asc&participant=302");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=303"));

        navigationManager.Uri.ShouldContain("search=lee");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=asc");

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=304"));

        navigationManager.Uri.ShouldContain("page=2");
        navigationManager.Uri.ShouldContain("search=lee");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=asc");
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
            "/campaigns/10?tab=roster&search=lee&sortBy=displayName&sortDirection=asc&participant=301");

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
        navigationManager.Uri.ShouldContain("sortDirection=asc");
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
            "/campaigns/10?tab=roster&search=jones&sortBy=displayName&sortDirection=asc&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=801"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("4 of 5"));
        navigationManager.Uri.ShouldContain("page=2");
        navigationManager.Uri.ShouldContain("search=jones");
        navigationManager.Uri.ShouldContain("sortBy=displayName");
        navigationManager.Uri.ShouldContain("sortDirection=asc");
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

#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
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
        bool isClubAdmin = false)
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
        Services.AddSingleton(participantQueryService);
        Services.AddSingleton(tagDefinitionQueryService);
        Services.AddSingleton(evaluationNoteService);
        Services.AddSingleton(tagApplicationService);
        Services.AddSingleton(teamRosterService);
        Services.AddSingleton(placementQueryService);
        Services.AddSingleton(placementService);
        Services.AddSingleton(campaignMetadataService);
        Services.AddSingleton(closeoutQueryService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin)));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static IReadOnlyList<int> CreateGraduationYearChoices() => [2031, 2032];

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
        Notes:
        [
            new CampaignParticipantNoteDto(
                NoteId: 1,
                Content: "Strong defensive player.",
                AuthorDisplayName: "Coach Rivera",
                CreatedAt: new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero),
                ModifiedAt: null,
                CanEdit: false,
                CanDelete: false)
        ],
        AppliedTags: [],
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

    /// <summary>
    /// A test-only <see cref="CampaignWorkspacePage"/> subclass that seeds persisted prerender state.
    /// </summary>
#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class PersistedStateCampaignWorkspace(
#pragma warning restore CA1812
        ICampaignQueryService campaignQueryService,
        ICampaignParticipantQueryService participantQueryService,
        ITagDefinitionQueryService tagDefinitionQueryService,
        ITeamRosterService teamRosterService,
        ICampaignMetadataService campaignMetadataService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager,
        IJSRuntime jsRuntime)
        : CampaignWorkspacePage(campaignQueryService, participantQueryService, tagDefinitionQueryService, teamRosterService, campaignMetadataService, authenticationStateProvider, navigationManager, jsRuntime)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public CampaignDetailResult? PersistedCampaignDetail { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedDetail = PersistedCampaignDetail;
            }

            return base.OnInitializedAsync();
        }
    }
}
