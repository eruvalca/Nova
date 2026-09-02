using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Attention;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Nova.Shared.Security;
using NSubstitute;
using Shouldly;
using ClubDashboardPage = Nova.UI.Features.Dashboard.Pages.ClubDashboard;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>
/// Component-level tests for the role-aware club dashboard: composition, role-aware attention
/// rendering, attention-link fallback, empty states, activity-feed rendering, loading/error/retry,
/// persisted-state restore, and route/render-mode declarations.
/// </summary>
public sealed class ClubDashboardComponentTests : BunitContext
{
    private static readonly DateTimeOffset ActivityAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies the dashboard page declares an interactive-auto render mode.</summary>
    [Fact]
    public void ClubDashboard_DeclaresInteractiveAutoRenderMode()
    {
        var attribute = typeof(ClubDashboardPage)
            .GetCustomAttributes(inherit: false)
            .OfType<RenderModeAttribute>()
            .SingleOrDefault();

        attribute.ShouldNotBeNull();
        attribute.Mode.ShouldBeOfType<InteractiveAutoRenderMode>();
    }

    /// <summary>Verifies the dashboard page is routed at the authenticated dashboard path.</summary>
    [Fact]
    public void ClubDashboard_DeclaresDashboardRoute()
    {
        var attribute = typeof(ClubDashboardPage)
            .GetCustomAttributes(inherit: false)
            .OfType<RouteAttribute>()
            .SingleOrDefault(route => route.Template == "/dashboard");

        attribute.ShouldNotBeNull();
    }

    /// <summary>Verifies the dashboard razor source declares an interactive-auto render mode.</summary>
    [Fact]
    public void ClubDashboardRazor_DeclaresInteractiveAutoRenderMode()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Dashboard", "Pages", "ClubDashboard.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode InteractiveAuto");
    }

    /// <summary>Verifies the dashboard razor source is routed at the dashboard path.</summary>
    [Fact]
    public void ClubDashboardRazor_DeclaresDashboardRoute()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Dashboard", "Pages", "ClubDashboard.razor");
        File.ReadAllText(razorPath).ShouldContain("@page \"/dashboard\"");
    }

    /// <summary>Verifies a populated summary renders all regions: campaigns, counts, and activity.</summary>
    [Fact]
    public void ClubDashboard_RendersAllRegions_WhenPopulated()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([BuildNoteEvent()])));

        RegisterServices(isClubAdmin: false, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Active campaigns"));

        cut.Markup.ShouldContain("Campaign A");
        cut.Find("tbody a").GetAttribute("href").ShouldBe("/campaigns/42");
        cut.Markup.ShouldContain("5 active");
        cut.Markup.ShouldContain("2 archived");
        cut.Markup.ShouldContain("8 active");
        cut.Markup.ShouldContain("1 archived");
        cut.Markup.ShouldContain("href=\"players\"");
        cut.Markup.ShouldContain("href=\"teams\"");
        cut.Markup.ShouldContain("Admin A requested to join the club");
    }

    /// <summary>Verifies an administrator sees the attention card with both counts and links.</summary>
    [Fact]
    public void ClubDashboard_ShowsAdminAttention_ForClubAdmin()
    {
        var attention = new ClubAttentionResult
        {
            PendingJoinRequests = new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 3,
                OldestRequestAt = null
            },
            NeedsPlacement = new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 5,
                CampaignId = 77,
                CampaignName = "Campaign 77"
            }
        };
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));
        var attentionService = Substitute.For<IClubAttentionQueryService>();
        attentionService.GetClubAttentionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessAttention(attention)));

        RegisterServices(isClubAdmin: true, dashboardService, activityService, attentionService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Admin attention"));

        var attentionItems = cut.FindAll(".attention-panel p")
            .Select(item => item.TextContent.Trim())
            .ToArray();
        attentionItems.ShouldContain("3 pending join requests");
        attentionItems.ShouldContain("5 unresolved placements");

        var reviewRequestsLink = cut.FindAll("a")
            .Single(a => a.TextContent.Contains("Review requests", StringComparison.Ordinal));
        reviewRequestsLink.GetAttribute("href").ShouldBe("/Clubs/42/admin");

        var reviewPlacementsLink = cut.FindAll("a")
            .Single(a => a.TextContent.Contains("Review placements", StringComparison.Ordinal));
        reviewPlacementsLink.GetAttribute("href").ShouldBe("/campaigns/77?unresolvedOnly=true&tab=placements");
    }

    /// <summary>Verifies an evaluator does not see the administrator attention card.</summary>
    [Fact]
    public void ClubDashboard_HidesAdminAttention_ForEvaluator()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: false, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Active campaigns"));

        cut.Markup.ShouldNotContain("Admin attention");
        cut.Markup.ShouldNotContain("Review requests");
        cut.Markup.ShouldNotContain("Review placements");
    }

    /// <summary>Verifies the placement review link falls back to the campaign list when no campaign has unresolved placements.</summary>
    [Fact]
    public void ClubDashboard_FallsBackToCampaignList_WhenNoUnresolvedCampaign()
    {
        var attention = new ClubAttentionResult
        {
            PendingJoinRequests = new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 1,
                OldestRequestAt = null
            },
            NeedsPlacement = new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 0,
                CampaignId = null,
                CampaignName = null
            }
        };
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));
        var attentionService = Substitute.For<IClubAttentionQueryService>();
        attentionService.GetClubAttentionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessAttention(attention)));

        RegisterServices(isClubAdmin: true, dashboardService, activityService, attentionService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Review placements"));

        var reviewPlacementsLink = cut.FindAll("a")
            .Single(a => a.TextContent.Contains("Review placements", StringComparison.Ordinal));
        reviewPlacementsLink.GetAttribute("href").ShouldBe("/campaigns");
    }

    /// <summary>Verifies an administrator with no active campaigns sees the create-campaign call to action.</summary>
    [Fact]
    public void ClubDashboard_ShowsAdminEmptyState_WhenNoActiveCampaigns()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreateEmptySummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Create campaign"));

        cut.Markup.ShouldContain("Create your first campaign");
        var createLink = cut.FindAll("a").Single(a => a.TextContent.Trim() == "Create campaign");
        createLink.GetAttribute("href").ShouldBe("campaigns/new");
    }

    /// <summary>Verifies an evaluator with no active campaigns sees the neutral empty message and no call to action.</summary>
    [Fact]
    public void ClubDashboard_ShowsNeutralEmptyState_ForEvaluator()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreateEmptySummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: false, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No active campaigns right now"));

        cut.Markup.ShouldNotContain("Create campaign");
    }

    /// <summary>Verifies an empty activity feed renders the muted empty message.</summary>
    [Fact]
    public void ClubDashboard_ShowsNoActivity_WhenFeedEmpty()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: false, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recent activity"));

        cut.Markup.ShouldContain("No recent activity.");
    }

    /// <summary>Verifies every activity kind renders its expected verb phrase with kind-specific context.</summary>
    [Fact]
    public void ClubDashboard_RendersEachActivityKind_WithVerb()
    {
        var events = new List<ClubActivityItemDto>
        {
            BuildCampaignLifecycleEvent(ActivityEventKind.CampaignClosed, "Campaign A", eventId: 1),
            BuildCampaignLifecycleEvent(ActivityEventKind.CampaignReopened, "Campaign A", eventId: 2),
            BuildJoinRequestEvent(eventId: 3),
            BuildMembershipEvent(eventId: 4),
            BuildMemberRoleEvent(eventId: 5)
        };
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreateEmptySummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity(events)));

        RegisterServices(isClubAdmin: false, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recent activity"));

        cut.FindAll("ul li").Count.ShouldBe(5);
        cut.Markup.ShouldContain(ActivityAt.ToString("MMM d, yyyy"));
        cut.Markup.ShouldContain("closed Campaign A");
        cut.Markup.ShouldContain("reopened Campaign A");
        cut.Markup.ShouldContain("requested to join the club");
        cut.Markup.ShouldContain("joined the club");
        cut.Markup.ShouldContain("promoted");
    }

    /// <summary>Verifies the dashboard shows an accessible loading state while the request is pending.</summary>
    [Fact]
    public void ClubDashboard_ShowsLoadingState_WhileRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<ClubDashboardResult>>();
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.Markup.ShouldContain("Loading dashboard...");
        cut.Markup.ShouldContain("role=\"status\"");
        cut.Markup.ShouldContain("aria-live=\"polite\"");

        pending.SetResult(SuccessDashboard(CreatePopulatedSummary()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Active campaigns"));
    }

    /// <summary>Verifies a load error surfaces a role=alert message and Retry re-invokes the service.</summary>
    [Fact]
    public void ClubDashboard_ShowsErrorAndRetries_WhenInitialLoadFails()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<ClubDashboardResult>(ServiceProblem.ServerError("Dashboard transport failed."))),
                Task.FromResult(SuccessDashboard(CreatePopulatedSummary())));
        var activityService = Substitute.For<IClubActivityQueryService>();
        activityService.GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService, activityService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Dashboard transport failed."));

        cut.Markup.ShouldContain("role=\"alert\"");
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Active campaigns"));
    }

    /// <summary>Verifies a user without a club id claim sees the friendly error and no dashboard payload is fetched.</summary>
    [Fact]
    public void ClubDashboard_ShowsFriendlyError_WhenClubIdClaimMissing()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        var activityService = Substitute.For<IClubActivityQueryService>();

        RegisterServices(isClubAdmin: false, dashboardService, activityService, clubId: null);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You must join a club before viewing the dashboard."));

        cut.Markup.ShouldContain("role=\"alert\"");
        cut.Markup.ShouldNotContain("Active campaigns");
        cut.Markup.ShouldNotContain("Admin attention");
        dashboardService.DidNotReceive().GetDashboardAsync(Arg.Any<CancellationToken>());
        activityService.DidNotReceive().GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies seeded persisted state is restored without re-fetching either dashboard payload.</summary>
    [Fact]
    public void ClubDashboard_RestoresPersistedState_WithoutRefetching()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        var activityService = Substitute.For<IClubActivityQueryService>();
        var attentionService = Substitute.For<IClubAttentionQueryService>();
        var persistedSummary = CreatePopulatedSummary();
        var persistedActivity = new ClubActivityResult([BuildNoteEvent()], HasMore: false, NextCursor: null);
        var persistedAttention = new ClubAttentionResult
        {
            PendingJoinRequests = new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 2,
                OldestRequestAt = new DateTimeOffset(2026, 6, 14, 12, 0, 0, TimeSpan.Zero)
            },
            NeedsPlacement = new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 3,
                CampaignId = 42,
                CampaignName = "Campaign A"
            }
        };
        attentionService.GetClubAttentionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessAttention(persistedAttention)));
        RegisterServices(isClubAdmin: true, dashboardService, activityService, attentionService);

        var cut = Render<PersistedStateClubDashboard>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedSummaryValue, persistedSummary)
            .Add(p => p.PersistedActivityValue, persistedActivity)
            .Add(p => p.PersistedAttentionValue, persistedAttention));

        cut.Markup.ShouldContain("Campaign A");
        cut.Markup.ShouldContain("Admin A requested to join the club");
        cut.Markup.ShouldContain("Admin attention");
        var attentionItems = cut.FindAll(".attention-panel p")
            .Select(item => item.TextContent.Trim())
            .ToArray();
        attentionItems.ShouldContain("2 pending join requests");
        attentionItems.ShouldContain("3 unresolved placements");

        dashboardService.DidNotReceive().GetDashboardAsync(Arg.Any<CancellationToken>());
        activityService.DidNotReceive().GetClubActivityAsync(Arg.Any<GetClubActivityInput>(), Arg.Any<CancellationToken>());
        attentionService.DidNotReceive().GetClubAttentionAsync(Arg.Any<CancellationToken>());
    }

    private void RegisterServices(
        bool isClubAdmin,
        IDashboardQueryService dashboardService,
        IClubActivityQueryService activityService,
        IClubAttentionQueryService? attentionService = null,
        string? clubId = "42")
    {
        Services.AddSingleton(dashboardService);
        Services.AddSingleton(activityService);
        Services.AddSingleton(attentionService ?? EmptyAttentionService());

        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin, clubId)));
    }

    private static IClubAttentionQueryService EmptyAttentionService()
    {
        var service = Substitute.For<IClubAttentionQueryService>();
        service.GetClubAttentionAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessAttention(EmptyAttention())));
        return service;
    }

    private static ClubAttentionResult EmptyAttention() => new()
    {
        PendingJoinRequests = new PendingJoinRequestsRegion
        {
            Status = AttentionRegionStatus.Loaded,
            Count = 0,
            OldestRequestAt = null
        },
        NeedsPlacement = new NeedsPlacementRegion
        {
            Status = AttentionRegionStatus.Loaded,
            Count = 0,
            CampaignId = null,
            CampaignName = null
        }
    };

    private static ClubDashboardResult CreatePopulatedSummary() => new()
    {
        ActiveCampaigns =
        [
            new ActiveCampaignCardDto
            {
                CampaignId = 42,
                Name = "Campaign A",
                SeasonName = "Season 1",
                StartDate = new DateOnly(2026, 6, 1),
                PlannedEndDate = null,
                Status = CampaignStatus.Active,
                ParticipantCount = 12,
                UnresolvedCount = 3,
                WorkspaceUrl = "/campaigns/42"
            }
        ],
        Roster = new RosterCountsDto { ActivePlayers = 5, ArchivedPlayers = 2 },
        Teams = new TeamCountsDto { ActiveTeams = 8, ArchivedTeams = 1 }
    };

    private static ClubDashboardResult CreateEmptySummary() => new()
    {
        ActiveCampaigns = [],
        Roster = new RosterCountsDto { ActivePlayers = 0, ArchivedPlayers = 0 },
        Teams = new TeamCountsDto { ActiveTeams = 0, ArchivedTeams = 0 }
    };

    private static ServiceResult<ClubDashboardResult> SuccessDashboard(ClubDashboardResult summary)
        => new(summary);

    private static ServiceResult<ClubActivityResult> SuccessActivity(IReadOnlyList<ClubActivityItemDto> events)
        => new(new ClubActivityResult(events, HasMore: false, NextCursor: null));

    private static ServiceResult<ClubAttentionResult> SuccessAttention(ClubAttentionResult attention)
        => new(attention);

    private static ClubActivityItemDto BuildNoteEvent()
        => BuildJoinRequestEvent(eventId: 1);

    private static ClubActivityItemDto BuildCampaignLifecycleEvent(ActivityEventKind kind, string campaignName, long eventId)
        => new()
        {
            Kind = kind,
            ActivityEventId = eventId,
            OccurredAt = ActivityAt,
            ActorUserId = 300,
            ActorDisplayName = "Admin A",
            Context = new CampaignLifecycleContext { CampaignId = 42, CampaignName = campaignName }
        };

    private static ClubActivityItemDto BuildJoinRequestEvent(long eventId)
        => new()
        {
            Kind = ActivityEventKind.JoinRequestSubmitted,
            ActivityEventId = eventId,
            OccurredAt = ActivityAt,
            ActorUserId = 300,
            ActorDisplayName = "Admin A",
            Context = new JoinRequestContext { JoinRequestId = 7, RequesterDisplayName = "Noter" }
        };

    private static ClubActivityItemDto BuildMembershipEvent(long eventId)
        => new()
        {
            Kind = ActivityEventKind.MemberJoined,
            ActivityEventId = eventId,
            OccurredAt = ActivityAt,
            ActorUserId = 300,
            ActorDisplayName = "Admin A",
            Context = new MembershipContext { MemberUserId = 99, MemberDisplayName = "Noter", ApprovedByActorName = null }
        };

    private static ClubActivityItemDto BuildMemberRoleEvent(long eventId)
        => new()
        {
            Kind = ActivityEventKind.MemberPromoted,
            ActivityEventId = eventId,
            OccurredAt = ActivityAt,
            ActorUserId = 300,
            ActorDisplayName = "Admin A",
            Context = new MemberRoleContext { MemberUserId = 1, MemberDisplayName = "Noter", Role = "Team Lead" }
        };

    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin, string? clubId = "42")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101")
        };

        if (clubId is not null)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubId, clubId));
        }

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

        throw new InvalidOperationException("Could not locate repository root for dashboard route assertion.");
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
    /// A test-only <see cref="ClubDashboardPage"/> subclass that seeds persisted prerender state.
    /// </summary>
    private sealed class PersistedStateClubDashboard(
        IDashboardQueryService dashboardQueryService,
        IClubActivityQueryService activityQueryService,
        IClubAttentionQueryService attentionQueryService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : ClubDashboardPage(dashboardQueryService, activityQueryService, attentionQueryService, authenticationStateProvider, navigationManager)
    {
        /// <summary>Gets or sets whether the persisted-state initialization path should be seeded.</summary>
        [Parameter]
        public bool StartInitialized { get; set; }

        /// <summary>Gets or sets the persisted summary payload.</summary>
        [Parameter]
        public ClubDashboardResult? PersistedSummaryValue { get; set; }

        /// <summary>Gets or sets the persisted activity payload.</summary>
        [Parameter]
        public ClubActivityResult? PersistedActivityValue { get; set; }

        /// <summary>Gets or sets the persisted attention payload.</summary>
        [Parameter]
        public ClubAttentionResult? PersistedAttentionValue { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedSummary = PersistedSummaryValue;
                PersistedActivity = PersistedActivityValue;
                PersistedAttention = PersistedAttentionValue;
                PersistedPageError = null;
            }

            return base.OnInitializedAsync();
        }
    }
}
