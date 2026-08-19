using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using NSubstitute;
using Shouldly;
using CampaignOverviewPanel = Nova.UI.Features.Campaigns.Components.CampaignOverviewPanel;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign overview panel: snapshot fields, outcome summary counts,
/// the closeout-readiness link gating, the activity feed, empty/error states, persisted-state
/// restoration, and the open-closeout callback.
/// </summary>
public sealed class CampaignOverviewPanelTests : BunitContext
{
    // ── Snapshot and summary ───────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersSnapshotFields_FromDetail()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer 2026"));

        cut.Markup.ShouldContain("Season");
        cut.Markup.ShouldContain("Dates");
        cut.Markup.ShouldContain("Enrolled");
        cut.Markup.ShouldContain($"{new DateOnly(2026, 6, 15):MMM d, yyyy} – {new DateOnly(2026, 6, 20):MMM d, yyyy}");
        cut.Markup.ShouldContain("12 participants");
    }

    [Fact]
    public void Panel_RendersSummaryCounts_ExactlyFromReadiness()
    {
        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(
            CreateReadiness(CreateSummary(assigned: 2, notSelected: 3, withdrawn: 4, undecided: 5))));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Assigned"));

        var counts = cut.FindAll("div[aria-label=\"Campaign outcome summary\"] .fs-4.fw-semibold")
            .Select(element => element.TextContent.Trim());
        counts.ShouldBe(["2", "3", "4", "5"]);
    }

    // ── Closeout-readiness line and link gating ────────────────────────────────

    [Fact]
    public void Panel_ShowsReadyLine_WhenReadinessIsReady()
    {
        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(
            CreateReadiness(CreateSummary(undecided: 0))));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Ready to close"));

        cut.Markup.ShouldNotContain("Closeout blocked");
    }

    [Fact]
    public void Panel_ShowsBlockedLine_WhenReadinessIsNotReady()
    {
        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(
            CreateReadiness(CreateSummary(undecided: 3), isReady: false)));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Closeout blocked"));

        cut.Markup.ShouldNotContain("Ready to close");
    }

    [Fact]
    public void Panel_ShowsOpenCloseoutLink_OnlyForAdminActiveCampaign()
    {
        RegisterServices();

        var adminActive = RenderPanel(isClubAdmin: true);
        adminActive.WaitForAssertion(() => adminActive.Markup.ShouldContain("Ready to close"));
        adminActive.FindAll("button.btn-link").Any(button => button.TextContent.Trim() == "Open closeout").ShouldBeTrue();

        var memberActive = RenderPanel(isClubAdmin: false);
        memberActive.WaitForAssertion(() => memberActive.Markup.ShouldContain("Ready to close"));
        memberActive.FindAll("button.btn-link").Any(button => button.TextContent.Trim() == "Open closeout").ShouldBeFalse();

        var adminClosed = RenderPanel(isClubAdmin: true, status: CampaignStatus.Closed);
        adminClosed.WaitForAssertion(() => adminClosed.Markup.ShouldContain("Ready to close"));
        adminClosed.FindAll("button.btn-link").Any(button => button.TextContent.Trim() == "Open closeout").ShouldBeFalse();
    }

    [Fact]
    public void Panel_OpenCloseoutLink_InvokesCallback()
    {
        RegisterServices();
        var opened = false;
        var onOpenCloseout = EventCallback.Factory.Create(this, () => opened = true);

        var cut = RenderPanel(isClubAdmin: true, onOpenCloseout: onOpenCloseout);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Ready to close"));

        cut.FindAll("button.btn-link")
            .Single(button => button.TextContent.Trim() == "Open closeout")
            .Click();

        opened.ShouldBeTrue();
    }

    // ── Activity feed ──────────────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersActivityRows_NewestFirst_WithVerbDateActor()
    {
        var closed = CreateActivityItem(1, CampaignLifecycleEventType.Closed, "Coach Rivera",
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero));
        var reopened = CreateActivityItem(2, CampaignLifecycleEventType.Reopened, "Coach Avery",
            new DateTimeOffset(2026, 6, 11, 9, 0, 0, TimeSpan.Zero));

        RegisterServices(activityResult: new ServiceResult<CampaignActivityResult>(
            new CampaignActivityResult([reopened, closed])));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("reopened the campaign"));

        var rows = cut.FindAll("ul.list-unstyled li").Select(element => element.TextContent.Trim());
        rows.ShouldBe(
        [
            $"{reopened.CreatedAt:MMM d, yyyy} Coach Avery reopened the campaign",
            $"{closed.CreatedAt:MMM d, yyyy} Coach Rivera closed the campaign"
        ]);
    }

    [Fact]
    public void Panel_ShowsEmptyActivityState_WhenNoEvents()
    {
        RegisterServices(activityResult: new ServiceResult<CampaignActivityResult>(
            new CampaignActivityResult([])));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No recent activity."));
    }

    // ── Error and retry ────────────────────────────────────────────────────────

    [Fact]
    public void Panel_ShowsErrorAndRetries_WhenLoadFails()
    {
        var queryService = Substitute.For<ICampaignCloseoutQueryService>();
        queryService.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(
                    ServiceProblem.ServerError("Service unavailable."))),
                Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness())));
        queryService.GetActivityAsync(Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignActivityResult>(CreateActivity())));

        RegisterServices(closeoutQueryService: queryService);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Service unavailable."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Ready to close"));
    }

    // ── Persisted-state restoration ────────────────────────────────────────────

    [Fact]
    public void Panel_DoesNotRefetch_WhenPersistedStateIsRestored()
    {
        var queryService = Substitute.For<ICampaignCloseoutQueryService>();
        RegisterServices(closeoutQueryService: queryService);

        var cut = Render<PersistedStateCampaignOverviewPanel>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.Detail, CreateDetail())
            .Add(component => component.StartInitialized, true)
            .Add(component => component.PersistedCampaignReadiness, CreateReadiness())
            .Add(component => component.PersistedCampaignActivity, CreateActivity()));

        cut.Markup.ShouldContain("Ready to close");
        cut.Markup.ShouldContain("Coach Rivera closed the campaign");
        queryService.DidNotReceive().GetCloseoutReadinessAsync(
            Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
        queryService.DidNotReceive().GetActivityAsync(
            Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void RegisterServices(
        ICampaignCloseoutQueryService? closeoutQueryService = null,
        ServiceResult<CampaignCloseoutReadinessDto>? readinessResult = null,
        ServiceResult<CampaignActivityResult>? activityResult = null)
    {
        if (closeoutQueryService is null)
        {
            closeoutQueryService = Substitute.For<ICampaignCloseoutQueryService>();
            closeoutQueryService.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(readinessResult ?? new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness())));
            closeoutQueryService.GetActivityAsync(Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(activityResult ?? new ServiceResult<CampaignActivityResult>(CreateActivity())));
        }

        Services.AddSingleton(closeoutQueryService);
    }

    private IRenderedComponent<CampaignOverviewPanel> RenderPanel(
        bool isClubAdmin = true,
        CampaignStatus status = CampaignStatus.Active,
        EventCallback? onOpenCloseout = null)
        => Render<CampaignOverviewPanel>(parameters =>
        {
            parameters.Add(component => component.CampaignId, 10);
            parameters.Add(component => component.Detail, CreateDetail(status));
            parameters.Add(component => component.IsClubAdmin, isClubAdmin);
            if (onOpenCloseout is not null)
            {
                parameters.Add(component => component.OnOpenCloseout, onOpenCloseout.Value);
            }
        });

    private static CampaignDetailResult CreateDetail(CampaignStatus status = CampaignStatus.Active) => new()
    {
        CampaignId = 10,
        Name = "Summer Tryouts",
        Status = status,
        StartDate = new DateOnly(2026, 6, 15),
        PlannedEndDate = new DateOnly(2026, 6, 20),
        ParticipantCount = 12,
        SeasonId = 5,
        SeasonName = "Summer 2026"
    };

    private static CampaignCloseoutReadinessDto CreateReadiness(
        CampaignPlacementSummaryDto? summary = null,
        bool isReady = true)
        => new(
            CampaignId: 10,
            Status: CampaignStatus.Active,
            IsReady: isReady,
            Summary: summary ?? CreateSummary(),
            Blockers: []);

    private static CampaignPlacementSummaryDto CreateSummary(
        int assigned = 1,
        int notSelected = 1,
        int withdrawn = 1,
        int undecided = 0)
        => new(
            AssignedCount: assigned,
            NotSelectedCount: notSelected,
            WithdrawnCount: withdrawn,
            UndecidedCount: undecided,
            TotalCount: assigned + notSelected + withdrawn + undecided);

    private static CampaignActivityResult CreateActivity() => new(
    [
        CreateActivityItem(1, CampaignLifecycleEventType.Closed, "Coach Rivera",
            new DateTimeOffset(2026, 6, 10, 9, 0, 0, TimeSpan.Zero))
    ]);

    private static CampaignActivityItemDto CreateActivityItem(
        long eventId,
        CampaignLifecycleEventType eventType,
        string actorDisplayName,
        DateTimeOffset createdAt)
        => new(
            CampaignLifecycleEventId: eventId,
            EventType: eventType,
            CreatedAt: createdAt,
            ActorUserId: 101,
            ActorDisplayName: actorDisplayName);

    /// <summary>
    /// A test-only <see cref="CampaignOverviewPanel"/> subclass that seeds persisted prerender state.
    /// </summary>
    /// <param name="closeoutQueryService">The closeout query service.</param>
    private sealed class PersistedStateCampaignOverviewPanel(
        ICampaignCloseoutQueryService closeoutQueryService)
        : CampaignOverviewPanel(closeoutQueryService)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public CampaignCloseoutReadinessDto? PersistedCampaignReadiness { get; set; }

        [Parameter]
        public CampaignActivityResult? PersistedCampaignActivity { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedReadiness = PersistedCampaignReadiness;
                PersistedActivity = PersistedCampaignActivity;
            }

            return base.OnInitializedAsync();
        }
    }
}
