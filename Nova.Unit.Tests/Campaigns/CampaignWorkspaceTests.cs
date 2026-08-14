using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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

    private static CampaignParticipantRosterItem CreateRosterItem(string displayName = "Avery Johnson") => new(
        PlayerCampaignAssignmentId: 301,
        PlayerId: 7,
        DisplayName: displayName,
        GraduationYear: 2032,
        TryoutNumber: 14,
        PlacementOutcome: PlacementOutcome.Undecided,
        Team: null,
        AppliedTags: []);

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
