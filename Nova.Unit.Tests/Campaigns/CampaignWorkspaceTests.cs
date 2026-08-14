using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Tags;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
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
    public void CampaignWorkspaceRoute_DeclaresInteractiveAutoRenderMode()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Campaigns", "Pages", "CampaignWorkspace.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode InteractiveAuto");
    }

    // ── Loading state ─────────────────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspace_ShowsLoadingState_WhileDetailRequestIsPending()
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
    public void CampaignWorkspace_RendersHeaderFields_WhenDetailLoads()
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
    public void CampaignWorkspace_ShowsEvaluateActive_AndOtherTabsDisabled()
    {
        RegisterServices();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].TextContent.Trim().ShouldBe("Evaluate");

        var disabledTabs = cut.FindAll("ul.nav-tabs .nav-link.disabled");
        disabledTabs.Count.ShouldBe(3);
        disabledTabs.Select(tab => tab.TextContent.Trim()).ShouldBe(new[] { "Overview", "Placements", "Closeout" });
        disabledTabs.ShouldAllBe(tab => tab.GetAttribute("title") == "Coming soon");
    }

    [Fact]
    public void CampaignWorkspace_KeepsEvaluateTabActive_WhenTabQueryIsEvaluate()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].TextContent.Trim().ShouldBe("Evaluate");
    }

    [Fact]
    public void CampaignWorkspace_FallsBackToEvaluateTab_WhenTabQueryIsUnknown()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=overview");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        var activeTabs = cut.FindAll("ul.nav-tabs .nav-link.active");
        activeTabs.Count.ShouldBe(1);
        activeTabs[0].TextContent.Trim().ShouldBe("Evaluate");
    }

    [Fact]
    public void CampaignWorkspace_PushesTabQuery_WhenEvaluateTabSelected()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("ul.nav-tabs button.nav-link").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/campaigns/10?tab=evaluate"));
    }

    // ── Not-found and forbidden ───────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspace_ShowsNotFoundState_WhenServiceReturnsNotFound()
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

        participantService.DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspace_RedirectsToAccessDenied_WhenServiceReturnsForbidden()
    {
        RegisterServices(detailResult: new ServiceResult<CampaignDetailResult>(
            ServiceProblem.Forbidden("Access denied.")));

        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/Account/AccessDenied"));
    }

    // ── Recoverable error with retry ──────────────────────────────────────────

    [Fact]
    public void CampaignWorkspace_ShowsErrorAndRetries_WhenDetailLoadFails()
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
    public void CampaignWorkspace_LoadsRoster_OnlyAfterDetailSucceeds()
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
        participantService.DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());

        pendingDetail.SetResult(new ServiceResult<CampaignDetailResult>(CreateDetail()));
        cut.WaitForAssertion(() => participantService.Received(1).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    [Fact]
    public void CampaignWorkspace_ShowsRosterErrorAndRetries_WhenRosterLoadFails()
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
    public void CampaignWorkspace_ShowsChoicesRetryAndRecovers_WhenChoiceLoadFails()
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
    public void CampaignWorkspace_DoesNotReload_WhenPersistedStateIsRestored()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(campaignQueryService: queryService, participantQueryService: participantService);

        var cut = Render<PersistedStateCampaignWorkspace>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.StartInitialized, true)
            .Add(component => component.PersistedCampaignDetail, CreateDetail()));

        cut.Markup.ShouldContain("Summer Tryouts");
        queryService.DidNotReceive().GetCampaignDetailAsync(
            Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
        participantService.DidNotReceive().GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    // ── Roster filters, sorting, and paging ────────────────────────────────────

    [Fact]
    public void CampaignWorkspace_AppliesRosterState_FromQueryParametersOnLoad()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));

        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["tab"] = "evaluate",
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

        participantService.Received(1).GetParticipantRosterAsync(
            Arg.Is<GetCampaignParticipantRosterInput>(input =>
                input.Search == "avery"
                && input.GraduationYears != null
                && input.GraduationYears.Order().SequenceEqual(new[] { 2031, 2032 })
                && input.TagDefinitionIds != null
                && input.TagDefinitionIds.Order().SequenceEqual(new[] { 11L, 12L })
                && input.Outcome == "undecided"
                && input.TeamId == 21
                && input.SortBy == "displayName"
                && input.SortDirection == "desc"
                && input.Page == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspace_SortHeaderClick_CyclesAscendingThenDescending_AndPushesUrl()
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
    public void CampaignWorkspace_DebouncesSearch_ToSingleRequestWithFinalTerm()
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
            () => participantService.Received(2).GetParticipantRosterAsync(
                Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>()),
            timeout: TimeSpan.FromSeconds(5));

        participantService.Received(1).GetParticipantRosterAsync(
            Arg.Is<GetCampaignParticipantRosterInput>(input => input.Search == "ave"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspace_DiscardsStaleRosterResponse_WhenNewerRequestCompletesFirst()
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
        cut.WaitForAssertion(() => participantService.Received(2).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>()));

        cut.Find("#roster-outcome").Change("withdrawn");
        cut.WaitForAssertion(() => participantService.Received(3).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>()));

        secondResponse.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreateRoster(CreateRosterItem("Fresh Roster"))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Fresh Roster"));

        firstResponse.SetResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(
            CreateRoster(CreateRosterItem("Stale Roster"))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Fresh Roster"));
        cut.Markup.ShouldNotContain("Stale Roster");
    }

    [Fact]
    public void CampaignWorkspace_Pager_ReflectsPageMathAndBounds()
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
    public void CampaignWorkspace_ShowsEmptyCampaignMessage_WhenRosterHasNoParticipantsAndNoFilters()
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
    public void CampaignWorkspace_EmptyRoster_DetachesKeydownSuppression_InsteadOfAttaching()
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
    public void CampaignWorkspace_DetachesKeydownSuppression_WhenRosterReloadFails()
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
    public async Task CampaignWorkspace_DisposeAsync_ToleratesDisconnectedCircuit()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

        var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
        var detach = workspaceModule.SetupVoid("detachRosterActivationSuppression", _ => true);
        detach.SetException(new JSDisconnectedException("Circuit has disconnected."));

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        var disposeTask = cut.InvokeAsync(cut.Instance.DisposeAsync);
        await disposeTask;

        detach.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public void CampaignWorkspace_ShowsNoMatchMessage_AndClearsFilters_WhenFiltersExcludeAllParticipants()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignParticipantRosterInput>();
                var empty = input.Outcome == "withdrawn";
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
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>
        {
            ["tab"] = "evaluate",
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
    public void CampaignWorkspace_ClickingRosterRow_OpensDrawer_PushesParticipant_AndHighlightsRow()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

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
    public void CampaignWorkspace_ClosingDrawer_RemovesParticipant_AndPreservesRosterParams()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&outcome=assigned&participant=301");

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
        navigationManager.Uri.ShouldEndWith("/campaigns/10?outcome=assigned&tab=evaluate");
        cut.Markup.ShouldContain("Avery Johnson");

        cut.WaitForAssertion(() =>
        {
            restoreScroll.Invocations.Count.ShouldBe(1);
            restoreScroll.Invocations.Single().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
            restoreScroll.Invocations.Single().Arguments[1].ShouldBe(120.0);
        });
    }

    [Fact]
    public void CampaignWorkspace_Escape_ClosesDrawer()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=301");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("aside.participant-drawer").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("participant-drawer"));
        navigationManager.Uri.ShouldNotContain("participant=");
    }

    [Fact]
    public void CampaignWorkspace_KeyboardEnter_OnRosterRow_SelectsParticipant()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("tbody tr").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=301"));
        cut.Markup.ShouldContain("participant-drawer");
    }

    [Fact]
    public void CampaignWorkspace_SortChange_ScrollsRosterToTop_WithoutCapturingScroll()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");

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
    public void CampaignWorkspace_UnknownParticipantParam_OpensDrawerWithErrorAndFallbackHeading()
    {
        var participantService = Substitute.For<ICampaignParticipantQueryService>();
        participantService.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster())));
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                ServiceProblem.NotFound("Participant not found."))));
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=999");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Participant");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant not found"));
        cut.Find("#participant-drawer-retry").ShouldNotBeNull();
    }

    [Fact]
    public void CampaignWorkspace_InvalidParticipantParam_IsDropped()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=abc");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Markup.ShouldNotContain("participant-drawer");
    }

    // ── Drawer sequence navigation ──────────────────────────────────────────────

    [Fact]
    public void CampaignWorkspace_ShowsParticipantPositionAndEnabledNavigation_WhenDrawerOpen()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("3 of 142");
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeFalse();
    }

    [Theory]
    [InlineData(301, true, false)]
    [InlineData(302, false, false)]
    [InlineData(303, false, true)]
    public void CampaignWorkspace_DisablesSequenceButtons_AccordingToPosition(
        long participantId, bool previousDisabled, bool nextDisabled)
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService(totalCount: 3));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"/campaigns/10?tab=evaluate&participant={participantId}");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBe(previousDisabled);
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBe(nextDisabled);
    }

    [Fact]
    public void CampaignWorkspace_NextWithinPage_ChangesOnlyParticipant_WithoutReloadingRoster()
    {
        var participantService = CreatePagedParticipantService();
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=301");

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
        navigationManager.Uri.ShouldContain("tab=evaluate");
        participantService.Received(1).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());

        var history = ((BunitNavigationManager)navigationManager).History;
        history.Count.ShouldBe(historyCountBefore + 1);
        history.First().Options.ReplaceHistoryEntry.ShouldBeFalse();
        captureScroll.Invocations.Last().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
    }

    [Fact]
    public void CampaignWorkspace_PreviousWithinPage_MovesBackward_WithoutReloadingRoster()
    {
        var participantService = CreatePagedParticipantService();
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-previous").Click();

        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("participant=302"));
        cut.WaitForAssertion(() => cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("2 of 142"));

        navigationManager.Uri.ShouldNotContain("page=");
        participantService.Received(1).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspace_NextAcrossPageBoundary_SelectsFirstOfNextPage_CorrectingUrlInPlace()
    {
        var participantService = CreatePagedParticipantService(totalCount: 6);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

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

        participantService.Received(2).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() =>
        {
            scrollToTop.Invocations.Count.ShouldBeGreaterThanOrEqualTo(1);
            scrollToTop.Invocations.Last().Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(rosterRefId);
        });
    }

    [Fact]
    public void CampaignWorkspace_PreviousAcrossPageBoundary_SelectsLastOfPreviousPage_CorrectingUrlInPlace()
    {
        var participantService = CreatePagedParticipantService(totalCount: 6);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?page=2&tab=evaluate&participant=304");

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

        participantService.Received(2).GetParticipantRosterAsync(
            Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CampaignWorkspace_SequenceMoves_PreserveFilterAndSortParameters()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            "/campaigns/10?tab=evaluate&search=lee&sortBy=displayName&sortDirection=asc&participant=302");

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
    public void CampaignWorkspace_OffPageParticipant_HidesPositionAndDisablesNavigation_ButRendersDetail()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=999");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.FindAll("#participant-drawer-position").ShouldBeEmpty();
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeTrue();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));
    }

    [Fact]
    public void CampaignWorkspace_BoundaryMove_ToEmptyPage_LeavesDrawerOffPage_WithoutUrlCorrection()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService(totalCount: 6, page2Items: []));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

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
    public void CampaignWorkspace_BoundaryMove_ClosedBeforeTargetPageLoads_DoesNotReopenDrawer()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = CreatePagedParticipantServiceWithDelayedPage2(page2Completion);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

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
    public void CampaignWorkspace_BoundaryMove_CloseThenBackBeforeTargetPageLoads_DoesNotConsumeMove()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = CreatePagedParticipantServiceWithDelayedPage2(page2Completion);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

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
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&page=2&participant=303");
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
    public void CampaignWorkspace_BoundaryMove_BackBeforeTargetPageLoads_DoesNotReopenDrawer()
    {
        var page2Completion = new TaskCompletionSource<ServiceResult<PagedResult<CampaignParticipantRosterItem>>>();
        var participantService = CreatePagedParticipantServiceWithDelayedPage2(page2Completion);
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));

        // Browser Back returns to the workspace URL without the participant query parameter.
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate");
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
    public void CampaignWorkspace_BoundaryMove_FilterChangeBeforeTargetPageLoads_DoesNotConsumeIntent()
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

                var roster = input.Search == "jones"
                    ? CreatePagedRoster(page: 1, 2, [901, 902])
                    : CreatePagedRoster(page: 1, 6, [301, 302, 303]);
                return Task.FromResult(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(roster));
            });
        participantService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail())));
        RegisterServices(participantQueryService: participantService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("participant-drawer"));

        cut.Find("#participant-drawer-next").Click();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldContain("page=2"));

        // Back/Forward lands on the same participant with different filters; the newer request
        // supersedes the page-2 load the move was issued against, so the intent must be cleared.
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&search=jones&participant=303");
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
    public void CampaignWorkspace_OpenNavigateClose_RestoresScroll_AndPreservesState()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService());
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo(
            "/campaigns/10?tab=evaluate&search=lee&sortBy=displayName&sortDirection=asc&participant=301");

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
    public void CampaignWorkspace_RapidNavigation_EndsOnFinalParticipant_WithoutStaleDetail()
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
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=301");

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
    public void CampaignWorkspace_BoundaryMoves_DisableButtons_AtTrueSequenceEnds()
    {
        RegisterServices(participantQueryService: CreatePagedParticipantService(totalCount: 4, page2Items: [304]));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=303");

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
    public void CampaignWorkspace_BoundaryMove_LandsOnFirstItem_OfFilteredNextPage()
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
            "/campaigns/10?tab=evaluate&search=jones&sortBy=displayName&sortDirection=asc&participant=303");

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
    public void CampaignWorkspace_RendersResponsiveRosterLayout_WithDrawerOutsideResponsiveContainers()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns/10?tab=evaluate&participant=301");

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

    private void RegisterServices(
        ICampaignQueryService? campaignQueryService = null,
        ICampaignParticipantQueryService? participantQueryService = null,
        ServiceResult<CampaignDetailResult>? detailResult = null,
        ServiceResult<PagedResult<CampaignParticipantRosterItem>>? rosterResult = null,
        IReadOnlyList<int>? graduationYearChoices = null,
        IReadOnlyList<TagDefinitionDto>? tagChoices = null,
        IReadOnlyList<TeamRosterItem>? teamChoices = null)
    {
        if (campaignQueryService is null)
        {
            campaignQueryService = Substitute.For<ICampaignQueryService>();
            campaignQueryService.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(detailResult ?? new ServiceResult<CampaignDetailResult>(CreateDetail())));
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

        var teamRosterService = Substitute.For<ITeamRosterService>();
        teamRosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(
                (teamChoices ?? CreateTeamChoices()).ToList())));

        Services.AddSingleton(campaignQueryService);
        Services.AddSingleton(participantQueryService);
        Services.AddSingleton(tagDefinitionQueryService);
        Services.AddSingleton(teamRosterService);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal()));
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

    private static CampaignDetailResult CreateDetail() => new()
    {
        CampaignId = 10,
        Name = "Summer Tryouts",
        Status = CampaignStatus.Active,
        StartDate = new DateOnly(2026, 6, 15),
        PlannedEndDate = new DateOnly(2026, 6, 20),
        ParticipantCount = 12,
        SeasonId = 5,
        SeasonName = "Summer 2026"
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

    private static ClaimsPrincipal CreatePrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101"),
            new(NovaClaimTypes.ClubId, "42")
        };

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
    private sealed class PersistedStateCampaignWorkspace(
        ICampaignQueryService campaignQueryService,
        ICampaignParticipantQueryService participantQueryService,
        ITagDefinitionQueryService tagDefinitionQueryService,
        ITeamRosterService teamRosterService,
        NavigationManager navigationManager,
        IJSRuntime jsRuntime)
        : CampaignWorkspacePage(campaignQueryService, participantQueryService, tagDefinitionQueryService, teamRosterService, navigationManager, jsRuntime)
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
