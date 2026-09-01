using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
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
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([BuildCampaignEvent(DashboardActivityEventKind.CampaignOpened, 1)])));

        RegisterServices(isClubAdmin: false, dashboardService);

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
        cut.Markup.ShouldContain("opened Campaign A");
    }

    /// <summary>Verifies an administrator sees the attention card with both counts and links.</summary>
    [Fact]
    public void ClubDashboard_ShowsAdminAttention_ForClubAdmin()
    {
        var attention = new AdminAttentionDto
        {
            PendingJoinRequestCount = 3,
            UnresolvedPlacementCount = 5,
            FirstUnresolvedCampaignId = 77
        };
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary(attention))));
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService);

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
        reviewPlacementsLink.GetAttribute("href").ShouldBe("/campaigns/77");
    }

    /// <summary>Verifies an evaluator does not see the administrator attention card.</summary>
    [Fact]
    public void ClubDashboard_HidesAdminAttention_ForEvaluator()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary(attention: null))));
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: false, dashboardService);

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
        var attention = new AdminAttentionDto
        {
            PendingJoinRequestCount = 1,
            UnresolvedPlacementCount = 0,
            FirstUnresolvedCampaignId = null
        };
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreatePopulatedSummary(attention))));
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService);

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
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService);

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
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: false, dashboardService);

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
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: false, dashboardService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recent activity"));

        cut.Markup.ShouldContain("No recent activity.");
    }

    /// <summary>Verifies every activity kind renders its expected verb phrase with kind-specific context.</summary>
    [Fact]
    public void ClubDashboard_RendersEachActivityKind_WithVerb()
    {
        var events = new List<DashboardActivityItemDto>
        {
            BuildCampaignEvent(DashboardActivityEventKind.CampaignDraftCreated, eventId: 1),
            BuildJoinRequestEvent(),
            BuildPlacementEvent(),
            BuildMembershipEvent(),
            BuildCampaignEvent(DashboardActivityEventKind.CampaignReopened, eventId: 5)
        };
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDashboard(CreateEmptySummary())));
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity(events)));

        RegisterServices(isClubAdmin: false, dashboardService);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recent activity"));

        cut.FindAll("ul li").Count.ShouldBe(5);
        cut.Markup.ShouldContain(ActivityAt.ToString("MMM d, yyyy"));
        cut.Markup.ShouldContain("created Draft Campaign A");
        cut.Markup.ShouldContain("Requester submitted a request");
        cut.Markup.ShouldContain("placed Placer on U14 Teal");
        cut.Markup.ShouldContain("Placer");
        cut.Markup.ShouldContain("promoted Member A");
        cut.Markup.ShouldContain("reopened Campaign A");
    }

    /// <summary>Verifies the dashboard shows an accessible loading state while the request is pending.</summary>
    [Fact]
    public void ClubDashboard_ShowsLoadingState_WhileRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<ClubDashboardResult>>();
        var dashboardService = Substitute.For<IDashboardQueryService>();
        dashboardService.GetDashboardAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService);

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
        dashboardService.GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessActivity([])));

        RegisterServices(isClubAdmin: true, dashboardService);

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

        RegisterServices(isClubAdmin: false, dashboardService, clubId: null);

        var cut = Render<ClubDashboardPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You must join a club before viewing the dashboard."));

        cut.Markup.ShouldContain("role=\"alert\"");
        cut.Markup.ShouldNotContain("Active campaigns");
        cut.Markup.ShouldNotContain("Admin attention");
        dashboardService.DidNotReceive().GetDashboardAsync(Arg.Any<CancellationToken>());
        dashboardService.DidNotReceive().GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies seeded persisted state is restored without re-fetching either dashboard payload.</summary>
    [Fact]
    public void ClubDashboard_RestoresPersistedState_WithoutRefetching()
    {
        var dashboardService = Substitute.For<IDashboardQueryService>();
        var persistedSummary = CreatePopulatedSummary();
        var persistedActivity = new DashboardActivityResult([BuildCampaignEvent(DashboardActivityEventKind.CampaignOpened, 1)]);

        RegisterServices(isClubAdmin: true, dashboardService);

        var cut = Render<PersistedStateClubDashboard>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedSummaryValue, persistedSummary)
            .Add(p => p.PersistedActivityValue, persistedActivity));

        cut.Markup.ShouldContain("Campaign A");
        cut.Markup.ShouldContain("opened Campaign A");

        dashboardService.DidNotReceive().GetDashboardAsync(Arg.Any<CancellationToken>());
        dashboardService.DidNotReceive().GetActivityAsync(Arg.Any<GetDashboardActivityInput>(), Arg.Any<CancellationToken>());
    }

    private void RegisterServices(bool isClubAdmin, IDashboardQueryService dashboardService, string? clubId = "42")
    {
        Services.AddSingleton(dashboardService);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin, clubId)));
    }

    private static ClubDashboardResult CreatePopulatedSummary(AdminAttentionDto? attention = null) => new()
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
        Teams = new TeamCountsDto { ActiveTeams = 8, ArchivedTeams = 1 },
        AdminAttention = attention
    };

    private static ClubDashboardResult CreateEmptySummary() => new()
    {
        ActiveCampaigns = [],
        Roster = new RosterCountsDto { ActivePlayers = 0, ArchivedPlayers = 0 },
        Teams = new TeamCountsDto { ActiveTeams = 0, ArchivedTeams = 0 },
        AdminAttention = null
    };

    private static ServiceResult<ClubDashboardResult> SuccessDashboard(ClubDashboardResult summary)
        => new(summary);

    private static ServiceResult<DashboardActivityResult> SuccessActivity(IReadOnlyList<DashboardActivityItemDto> events)
        => new(new DashboardActivityResult(events));

    private static DashboardActivityItemDto BuildPlacementEvent()
        => new()
        {
            Kind = DashboardActivityEventKind.PlacementAssigned,
            EventId = 3,
            EventAt = ActivityAt,
            Context = new PlacementActivityContextDto
            {
                ActorDisplayName = "Admin A",
                PlayerId = 8,
                PlayerDisplayName = "Placer",
                PlayerCampaignAssignmentId = 7,
                CampaignId = 42,
                CampaignName = "Campaign A",
                Previous = new PlacementSnapshotDto { Outcome = PlacementOutcome.Undecided },
                Current = new PlacementSnapshotDto { Outcome = PlacementOutcome.Assigned, TeamId = 9, TeamName = "U14 Teal" }
            }
        };

    private static DashboardActivityItemDto BuildJoinRequestEvent()
        => new()
        {
            Kind = DashboardActivityEventKind.JoinRequestSubmitted,
            EventId = 2,
            EventAt = ActivityAt,
            Context = new JoinRequestActivityContextDto
            {
                ActorDisplayName = "Requester",
                RequesterUserId = 10,
                RequesterDisplayName = "Requester",
                ActionableRequestId = 11
            }
        };

    private static DashboardActivityItemDto BuildMembershipEvent()
        => new()
        {
            Kind = DashboardActivityEventKind.MemberPromoted,
            EventId = 4,
            EventAt = ActivityAt,
            Context = new MembershipActivityContextDto
            {
                MemberUserId = 12,
                MemberDisplayName = "Member A",
                ActorDisplayName = "Admin A"
            }
        };

    private static DashboardActivityItemDto BuildCampaignEvent(DashboardActivityEventKind kind, long eventId)
        => new()
        {
            Kind = kind,
            EventId = eventId,
            EventAt = ActivityAt,
            Context = new CampaignActivityContextDto
            {
                ActorDisplayName = "Admin A",
                CampaignId = 42,
                CampaignName = "Campaign A",
                SeasonName = "Fall"
            }
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
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : ClubDashboardPage(dashboardQueryService, authenticationStateProvider, navigationManager)
    {
        /// <summary>Gets or sets whether the persisted-state initialization path should be seeded.</summary>
        [Parameter]
        public bool StartInitialized { get; set; }

        /// <summary>Gets or sets the persisted summary payload.</summary>
        [Parameter]
        public ClubDashboardResult? PersistedSummaryValue { get; set; }

        /// <summary>Gets or sets the persisted activity payload.</summary>
        [Parameter]
        public DashboardActivityResult? PersistedActivityValue { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedSummary = PersistedSummaryValue;
                PersistedActivity = PersistedActivityValue;
                PersistedPageError = null;
            }

            return base.OnInitializedAsync();
        }
    }
}
