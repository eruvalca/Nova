using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using CampaignCloseoutPanel = Nova.UI.Features.Campaigns.Components.CampaignCloseoutPanel;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign closeout panel: the readiness checklist, close gating and
/// flow, closed-state metadata/summary/banner, the reopen confirm flow, and the unresolved-review
/// drill-down callbacks.
/// </summary>
public sealed class CampaignCloseoutPanelTests : BunitContext
{
    // ── Checklist rendering ────────────────────────────────────────────────────

    [Fact]
    public void Panel_RendersChecklistRows_WithExactCountsAndMessages_WhenBlocked()
    {
        var summary = CreateSummary(assigned: 5, notSelected: 2, withdrawn: 2, undecided: 3);
        var readiness = CreateReadiness(
            summary,
            isReady: false,
            [
                CreateBlocker(CloseoutBlockerConditions.Outcomes, 3, "Every participant must have a final outcome before closing. Found 3 undecided participation record(s)."),
                CreateBlocker(CloseoutBlockerConditions.Eligibility, 2, "Every assigned participant must remain eligible for their team. Ineligible assignment ids: 301, 302."),
                CreateBlocker(CloseoutBlockerConditions.ArchivedTeams, 1, "Assigned participants cannot reference archived teams. Blocked assignment ids: 303.")
            ]);

        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(readiness));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Enrolled"));

        var rows = cut.FindAll("ul.list-group > li").Select(element => Collapse(element.TextContent)).ToList();
        rows[0].ShouldBe("Enrolled 12");
        rows[1].ShouldBe("Final outcomes 9");
        rows[2].ShouldContain("Undecided 3");
        rows[2].ShouldContain("Every participant must have a final outcome before closing. Found 3 undecided participation record(s).");
        rows[3].ShouldContain("Eligibility 2");
        rows[3].ShouldContain("Every assigned participant must remain eligible for their team. Ineligible assignment ids: 301, 302.");
        rows[4].ShouldContain("Archived teams 1");
        rows[4].ShouldContain("Assigned participants cannot reference archived teams. Blocked assignment ids: 303.");
    }

    [Fact]
    public void Panel_RendersSatisfiedRows_WhenAllClear()
    {
        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(
            CreateReadiness(CreateSummary(assigned: 6, notSelected: 3, withdrawn: 3, undecided: 0))));

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Enrolled"));

        var rows = cut.FindAll("ul.list-group > li").Select(element => Collapse(element.TextContent)).ToList();
        rows[0].ShouldBe("Enrolled 12");
        rows[1].ShouldBe("Final outcomes 12");
        rows[2].ShouldBe("Undecided Satisfied");
        rows[3].ShouldBe("Eligibility Satisfied");
        rows[4].ShouldBe("Archived teams Satisfied");

        cut.FindAll("button").Any(button => button.TextContent.Trim() == "Review unresolved").ShouldBeFalse();
    }

    // ── Close gating ───────────────────────────────────────────────────────────

    [Fact]
    public void Panel_DisablesClose_WhenReadinessIsNotReady()
    {
        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(
            CreateReadiness(CreateSummary(undecided: 3), isReady: false)));

        var cut = RenderPanel(isClubAdmin: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close campaign"));

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Panel_DisablesClose_ForNonAdmin()
    {
        RegisterServices();

        var cut = RenderPanel(isClubAdmin: false);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close campaign"));

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue();
    }

    // ── Close flow ─────────────────────────────────────────────────────────────

    [Fact]
    public void Panel_CloseSuccess_ShowsMessage_AndFiresReload()
    {
        var lifecycleService = Substitute.For<ICampaignLifecycleService>();
        lifecycleService.CloseAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        var reloaded = false;
        var onReloadRequested = EventCallback.Factory.Create(this, () => reloaded = true);

        RegisterServices(lifecycleService: lifecycleService);

        var cut = RenderPanel(isClubAdmin: true, onReloadRequested: onReloadRequested);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close campaign"));

        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign closed."));
        reloaded.ShouldBeTrue();
    }

    [Fact]
    public void Panel_CloseConflict_ShowsWarning_AndRefetchesReadiness()
    {
        var ready = CreateReadiness(CreateSummary(undecided: 0));
        var blocked = CreateReadiness(
            CreateSummary(undecided: 3),
            isReady: false,
            [CreateBlocker(CloseoutBlockerConditions.Outcomes, 3, "Every participant must have a final outcome before closing. Found 3 undecided participation record(s).")]);

        var queryService = Substitute.For<ICampaignCloseoutQueryService>();
        queryService.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(ready)),
                Task.FromResult(new ServiceResult<CampaignCloseoutReadinessDto>(blocked)));

        var lifecycleService = Substitute.For<ICampaignLifecycleService>();
        lifecycleService.CloseAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict("Resolve all campaign close blockers before closing this campaign."))));

        RegisterServices(closeoutQueryService: queryService, lifecycleService: lifecycleService);

        var cut = RenderPanel(isClubAdmin: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close campaign"));

        cut.Find("button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Resolve all campaign close blockers before closing this campaign."));
        cut.Markup.ShouldContain("alert-warning");
        queryService.Received(2).GetCloseoutReadinessAsync(
            Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
        cut.Markup.ShouldContain("Found 3 undecided participation record(s).");
    }

    [Fact]
    public void Panel_PendingClose_DisablesButtons_WhileInFlight()
    {
        var pending = new TaskCompletionSource<ServiceResult<Success>>();
        var lifecycleService = Substitute.For<ICampaignLifecycleService>();
        lifecycleService.CloseAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(lifecycleService: lifecycleService);

        var cut = RenderPanel(isClubAdmin: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close campaign"));

        cut.Find("button.btn-primary").Click();

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue();

        pending.SetResult(new ServiceResult<Success>(new Success()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign closed."));
    }

    // ── Closed view ────────────────────────────────────────────────────────────

    [Fact]
    public void Panel_ClosedView_ShowsClosureMetadata_Summary_AndReadOnlyBanner()
    {
        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(
            CreateReadiness(CreateSummary(assigned: 6, notSelected: 3, withdrawn: 3, undecided: 0))));

        var cut = RenderPanel(
            isClubAdmin: true,
            status: CampaignStatus.Closed,
            closedAt: new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero),
            closedByDisplayName: "Coach Rivera");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Reopen campaign"));

        cut.Markup.ShouldContain("This campaign is closed and read-only.");
        cut.Markup.ShouldContain($"Closed {new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero):MMM d, yyyy} by Coach Rivera");
        cut.Markup.ShouldContain("Final outcome summary");
        cut.FindAll("div[aria-label=\"Final outcome summary\"] .fs-4.fw-semibold")
            .Select(element => element.TextContent.Trim())
            .ShouldBe(["6", "3", "3", "0"]);
    }

    [Fact]
    public void Panel_ReopenButton_HiddenForNonAdmin()
    {
        RegisterServices();

        var cut = RenderPanel(isClubAdmin: false, status: CampaignStatus.Closed);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This campaign is closed and read-only."));

        cut.FindAll("button").Any(button => button.TextContent.Trim() == "Reopen campaign").ShouldBeFalse();
    }

    // ── Reopen confirm flow ────────────────────────────────────────────────────

    [Fact]
    public void Panel_ReopenConfirm_CancelIsNoOp()
    {
        var lifecycleService = Substitute.For<ICampaignLifecycleService>();
        RegisterServices(lifecycleService: lifecycleService);

        var cut = RenderPanel(isClubAdmin: true, status: CampaignStatus.Closed);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Reopen campaign"));

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Reopen campaign").Click();
        cut.Markup.ShouldContain("Reopening restores editing without discarding outcomes and is recorded for audit.");

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Cancel").Click();
        cut.Markup.ShouldNotContain("Reopening restores editing without discarding outcomes and is recorded for audit.");
        lifecycleService.DidNotReceive().ReopenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Panel_ReopenSuccess_FiresReload()
    {
        var lifecycleService = Substitute.For<ICampaignLifecycleService>();
        lifecycleService.ReopenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        var reloaded = false;
        var onReloadRequested = EventCallback.Factory.Create(this, () => reloaded = true);

        RegisterServices(lifecycleService: lifecycleService);

        var cut = RenderPanel(isClubAdmin: true, status: CampaignStatus.Closed, onReloadRequested: onReloadRequested);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Reopen campaign"));

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Reopen campaign").Click();
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Confirm reopen").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign reopened."));
        reloaded.ShouldBeTrue();
    }

    // ── Unresolved-review drill-down ───────────────────────────────────────────

    [Fact]
    public void Panel_ReviewUnresolved_ReceivesTrueForOutcomes_AndFalseOtherwise()
    {
        var readiness = CreateReadiness(
            CreateSummary(undecided: 3),
            isReady: false,
            [
                CreateBlocker(CloseoutBlockerConditions.Outcomes, 3, "Every participant must have a final outcome before closing. Found 3 undecided participation record(s)."),
                CreateBlocker(CloseoutBlockerConditions.Eligibility, 2, "Every assigned participant must remain eligible for their team. Ineligible assignment ids: 301, 302."),
                CreateBlocker(CloseoutBlockerConditions.ArchivedTeams, 1, "Assigned participants cannot reference archived teams. Blocked assignment ids: 303.")
            ]);
        var received = new List<bool>();
        var onReviewUnresolved = EventCallback.Factory.Create<bool>(this, value => received.Add(value));

        RegisterServices(readinessResult: new ServiceResult<CampaignCloseoutReadinessDto>(readiness));

        var cut = RenderPanel(onReviewUnresolved: onReviewUnresolved);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Review unresolved"));

        var buttons = cut.FindAll("button")
            .Where(button => button.TextContent.Trim() == "Review unresolved")
            .ToList();
        buttons.Count.ShouldBe(3);
        foreach (var button in buttons)
        {
            button.Click();
        }

        received.ShouldBe([true, false, false]);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void RegisterServices(
        ICampaignCloseoutQueryService? closeoutQueryService = null,
        ICampaignLifecycleService? lifecycleService = null,
        ServiceResult<CampaignCloseoutReadinessDto>? readinessResult = null)
    {
        if (closeoutQueryService is null)
        {
            closeoutQueryService = Substitute.For<ICampaignCloseoutQueryService>();
            closeoutQueryService.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(readinessResult ?? new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness())));
        }

        lifecycleService ??= Substitute.For<ICampaignLifecycleService>();

        Services.AddSingleton(closeoutQueryService);
        Services.AddSingleton(lifecycleService);
    }

    private IRenderedComponent<CampaignCloseoutPanel> RenderPanel(
        bool isClubAdmin = true,
        CampaignStatus status = CampaignStatus.Active,
        DateTimeOffset? closedAt = null,
        string? closedByDisplayName = null,
        EventCallback? onReloadRequested = null,
        EventCallback<bool>? onReviewUnresolved = null)
        => Render<CampaignCloseoutPanel>(parameters =>
        {
            parameters.Add(component => component.CampaignId, 10);
            parameters.Add(component => component.Detail, CreateDetail(status, closedAt, closedByDisplayName));
            parameters.Add(component => component.IsClubAdmin, isClubAdmin);
            if (onReloadRequested is not null)
            {
                parameters.Add(component => component.OnReloadRequested, onReloadRequested.Value);
            }

            if (onReviewUnresolved is not null)
            {
                parameters.Add(component => component.OnReviewUnresolved, onReviewUnresolved.Value);
            }
        });

    private static CampaignDetailResult CreateDetail(
        CampaignStatus status = CampaignStatus.Active,
        DateTimeOffset? closedAt = null,
        string? closedByDisplayName = null) => new()
        {
            CampaignId = 10,
            Name = "Summer Tryouts",
            Status = status,
            StartDate = new DateOnly(2026, 6, 15),
            PlannedEndDate = new DateOnly(2026, 6, 20),
            ParticipantCount = 12,
            SeasonId = 5,
            SeasonName = "Summer 2026",
            ClosedAt = closedAt,
            ClosedByUserId = closedAt is null ? null : 101,
            ClosedByDisplayName = closedByDisplayName
        };

    private static CampaignCloseoutReadinessDto CreateReadiness(
        CampaignPlacementSummaryDto? summary = null,
        bool isReady = true,
        IReadOnlyList<CampaignCloseoutBlockerDto>? blockers = null)
        => new(
            CampaignId: 10,
            Status: CampaignStatus.Active,
            IsReady: isReady,
            Summary: summary ?? CreateSummary(),
            Blockers: blockers ?? []);

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

    private static CampaignCloseoutBlockerDto CreateBlocker(string condition, int count, string message)
        => new(condition, count, [301, 302, 303], message);

    private static string Collapse(string text)
        => Regex.Replace(text, @"\s+", " ").Trim();
}
