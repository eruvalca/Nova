using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using CampaignParticipantDrawerComponent = Nova.UI.Features.Campaigns.Components.CampaignParticipantDrawer;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign participant drawer covering detail-load states, retry,
/// stale-response discard, parameter-change reloads, close/Escape callbacks, focus-trap management,
/// and persisted-state restoration.
/// </summary>
public sealed class CampaignParticipantDrawerTests : BunitContext
{
    private const string DrawerModulePath = "./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js";

    // ── Loading state ─────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_ShowsLoadingState_WhileDetailRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>();
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.Markup.ShouldContain("Loading participant details");

        pending.SetResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));
    }

    // ── Loaded detail ─────────────────────────────────────────────────────────

    [Fact]
    public void Drawer_RendersLoadedDetail_WithPlayerCampaignNotesAndFooter()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                notes:
                [
                    new CampaignParticipantNoteDto(
                        NoteId: 1,
                        Content: "Strong defensive player.",
                        AuthorDisplayName: "Coach Rivera",
                        CreatedAt: new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero),
                        ModifiedAt: new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero),
                        CanEdit: false,
                        CanDelete: false)
                ]))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));

        cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Avery Johnson");
        cut.Markup.ShouldContain("Graduation year");
        cut.Markup.ShouldContain("Tryout number");
        cut.Markup.ShouldContain("14");
        var successBadges = cut.FindAll("span.badge.text-bg-success").Select(badge => badge.TextContent.Trim()).ToList();
        successBadges.ShouldContain("Assigned");
        successBadges.ShouldContain("Active");
        cut.Markup.ShouldContain("Blue");
        cut.Markup.ShouldContain("Active");
        cut.Markup.ShouldContain("Strong defensive player.");
        cut.Markup.ShouldContain("Coach Rivera");
        cut.Markup.ShouldContain("· edited");
        cut.Markup.ShouldContain("Created");
        cut.Markup.ShouldContain("· modified");
    }

    [Fact]
    public void Drawer_RendersTagMetadata_AndArchivedTagIndicators()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                tags:
                [
                    new CampaignParticipantTagApplicationDto(
                        CampaignTagApplicationId: 1,
                        PlayerTagId: 11,
                        TagName: "Lefty",
                        TagColor: "#0D6EFD",
                        IsArchived: false,
                        ActorDisplayName: "Coach Rivera",
                        AppliedAt: new DateTimeOffset(2026, 5, 2, 9, 30, 0, TimeSpan.Zero),
                        CanRemove: false),
                    new CampaignParticipantTagApplicationDto(
                        CampaignTagApplicationId: 2,
                        PlayerTagId: 12,
                        TagName: "Captain",
                        TagColor: "#FD7E14",
                        IsArchived: true,
                        ActorDisplayName: "Coach Rivera",
                        AppliedAt: new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero),
                        CanRemove: false)
                ]))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Lefty"));

        cut.Markup.ShouldContain("by Coach Rivera on");
        cut.Find(".participant-drawer-tag-archived").TextContent.Trim().ShouldBe("Captain");
        cut.Markup.ShouldContain("· archived");
    }

    [Fact]
    public void Drawer_RendersFallbacks_WhenOptionalFieldsAreMissing()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                tryoutNumber: null,
                includeTeam: false,
                notes: [],
                tags: []))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No notes yet."));

        cut.FindAll("dd").Count(dd => dd.TextContent.Trim() == "—").ShouldBe(2);
        cut.Markup.ShouldContain("No notes yet.");
        cut.Markup.ShouldContain("No tags applied.");
    }

    // ── Failure and retry ─────────────────────────────────────────────────────

    [Fact]
    public void Drawer_ShowsErrorAndRetry_WhenDetailLoadFails()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount == 1
                    ? Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                        ServiceProblem.NotFound("Participant not found.")))
                    : Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail()));
            });

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant not found"));

        cut.Find("#participant-drawer-retry").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));
        cut.Markup.ShouldNotContain("Participant not found");
        queryService.Received(2).GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_UsesRosterItemNameForHeading_WhenDetailLoadFails()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                ServiceProblem.NotFound("Participant not found."))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.RosterItem, CreateRosterItem()));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant not found"));

        cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Avery Johnson");
        cut.Markup.ShouldNotContain("Graduation year");
    }

    // ── Parameter changes and stale responses ─────────────────────────────────

    [Fact]
    public void Drawer_ReloadsDetail_WhenParticipantParameterChanges()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<GetCampaignParticipantDetailInput>();
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    CreateDetail(assignmentId: input.PlayerCampaignAssignmentId,
                        displayName: $"Player {input.PlayerCampaignAssignmentId}")));
            });

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Player 301"));

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Player 302"));

        queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input =>
                input.CampaignId == 10 && input.PlayerCampaignAssignmentId == 301),
            Arg.Any<CancellationToken>());
        queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input =>
                input.CampaignId == 10 && input.PlayerCampaignAssignmentId == 302),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_DiscardsStaleResponse_WhenParticipantChangesBeforeFirstLoadCompletes()
    {
        var firstLoad = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>();
        var secondLoad = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>();
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(firstLoad.Task, secondLoad.Task);

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.Markup.ShouldContain("Loading participant details");

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        secondLoad.SetResult(new ServiceResult<CampaignParticipantDetailDto>(
            CreateDetail(assignmentId: 302, displayName: "Jordan Lee")));
        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Jordan Lee"));

        firstLoad.SetResult(new ServiceResult<CampaignParticipantDetailDto>(
            CreateDetail(assignmentId: 301, displayName: "Avery Johnson")));

        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Jordan Lee"));
        cut.Markup.ShouldNotContain("Avery Johnson");
        queryService.Received(2).GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    // ── Close callbacks ───────────────────────────────────────────────────────

    [Fact]
    public void Drawer_InvokesOnClose_WhenCloseButtonClicked()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        cut.Find("#participant-drawer-close").Click();

        closed.ShouldBeTrue();
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
    }

    [Fact]
    public void Drawer_InvokesOnClose_WhenEscapePressed()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        cut.Find("aside.participant-drawer").TriggerEvent("onkeydown",
            new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        closed.ShouldBeTrue();
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
    }

    [Fact]
    public void Drawer_InvokesOnClose_WhenBackdropClicked()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        cut.Find(".participant-drawer-backdrop").Click();

        closed.ShouldBeTrue();
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
    }

    // ── Focus trap management ───────────────────────────────────────────────────

    [Fact]
    public void Drawer_InstallsFocusTrap_OnFirstRender()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var open = JSInterop.SetupModule(DrawerModulePath).SetupVoid("open", _ => true);
        open.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        var dialogRefId = cut.Find("aside.participant-drawer").GetAttribute("blazor:elementreference");
        var closeRefId = cut.Find("#participant-drawer-close").GetAttribute("blazor:elementreference");

        var invocation = open.Invocations.Single();
        invocation.Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(dialogRefId);
        invocation.Arguments[1].ShouldBeOfType<ElementReference>().Id.ShouldBe(closeRefId);
    }

    [Fact]
    public void Drawer_DoesNotReinstallFocusTrap_WhenParticipantParameterChanges()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var restoreFocus = drawerModule.SetupVoid("restoreFocus", _ => true);
        restoreFocus.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        open.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public void Drawer_RestoresFocusIntoDialog_WhenParticipantParameterChanges()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var restoreFocus = drawerModule.SetupVoid("restoreFocus", _ => true);
        restoreFocus.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        var dialogRefId = cut.Find("aside.participant-drawer").GetAttribute("blazor:elementreference");
        var closeRefId = cut.Find("#participant-drawer-close").GetAttribute("blazor:elementreference");

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        var invocation = restoreFocus.Invocations.Single();
        invocation.Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(dialogRefId);
        invocation.Arguments[1].ShouldBeOfType<ElementReference>().Id.ShouldBe(closeRefId);
    }

    [Fact]
    public void Drawer_DoesNotRestoreFocus_WhenParticipantParameterUnchanged()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var restoreFocus = drawerModule.SetupVoid("restoreFocus", _ => true);
        restoreFocus.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));
        cut.Render();

        restoreFocus.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Drawer_RestoresFocusToRosterRow_WhenDisposedWhileOpen_ForBrowserBack()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var detach = drawerModule.SetupVoid("detach", _ => true);
        detach.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        // Browser Back removes the participant query parameter, so the workspace stops rendering
        // the drawer without a close click; disposal must take the restoring close path.
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();

        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
        detach.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Drawer_DoesNotRestoreFocusAgain_WhenDisposedAfterClose()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.Find("#participant-drawer-close").Click();
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();

        close.Invocations.Count.ShouldBe(1);
    }

    // ── Sequence navigation ─────────────────────────────────────────────────────

    [Fact]
    public void Drawer_RendersPositionText_WhenSequenceParametersProvided()
    {
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 303)
            .Add(component => component.Position, 3)
            .Add(component => component.TotalCount, 142)
            .Add(component => component.HasPrevious, true)
            .Add(component => component.HasNext, true));

        cut.Find("#participant-drawer-position").TextContent.Trim().ShouldBe("3 of 142");
    }

    [Fact]
    public void Drawer_InvokesNavigationCallbacks_WhenNavigationButtonsClicked()
    {
        var previousCount = 0;
        var nextCount = 0;
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 302)
            .Add(component => component.Position, 2)
            .Add(component => component.TotalCount, 5)
            .Add(component => component.HasPrevious, true)
            .Add(component => component.HasNext, true)
            .Add(component => component.OnPrevious, EventCallback.Factory.Create(this, () => previousCount++))
            .Add(component => component.OnNext, EventCallback.Factory.Create(this, () => nextCount++)));

        cut.Find("#participant-drawer-previous").Click();
        cut.Find("#participant-drawer-next").Click();

        previousCount.ShouldBe(1);
        nextCount.ShouldBe(1);
    }

    [Fact]
    public void Drawer_DisablesNavigationButtons_AtSequenceEnds()
    {
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.Position, 1)
            .Add(component => component.TotalCount, 3)
            .Add(component => component.HasPrevious, false)
            .Add(component => component.HasNext, true));

        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeFalse();

        cut.Render(parameters => parameters
            .Add(component => component.Position, 3)
            .Add(component => component.HasPrevious, true)
            .Add(component => component.HasNext, false));

        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Drawer_HidesPositionAndDisablesNavigation_WhenParticipantIsOffPage()
    {
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 999));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));

        cut.FindAll("#participant-drawer-position").ShouldBeEmpty();
        cut.Find("#participant-drawer-previous").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#participant-drawer-next").HasAttribute("disabled").ShouldBeTrue();
    }

    // ── Persisted-state restoration ───────────────────────────────────────────

    [Fact]
    public void Drawer_RestoresPersistedDetail_WithoutRefetching()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(queryService);

        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedDetail, CreateDetail()));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));

        queryService.DidNotReceive().GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_RestoresPersistedError_WithoutRefetching()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(queryService);

        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedError, "Participant not found."));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant not found"));

        cut.Find("#participant-drawer-retry").ShouldNotBeNull();
        queryService.DidNotReceive().GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    // ── Evaluation mutations: read-only mode and infrastructure ─────────────

    [Fact]
    public void Drawer_RendersNoReadOnlyIndicator_ForActiveCampaign()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canAddNote: true, canApplyTag: true)))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add note"));
        cut.Markup.ShouldNotContain("Read-only — campaign is closed.");
        cut.Markup.ShouldContain("Apply");
    }

    [Fact]
    public void Drawer_RendersReadOnlyIndicator_AndHidesMutationControls_ForClosedCampaign()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                campaignStatus: CampaignStatus.Closed,
                capabilities: MutationCapabilities(canAddNote: true, canApplyTag: true)))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only — campaign is closed."));
        cut.Markup.ShouldNotContain("Add note");
        cut.Markup.ShouldNotContain("Apply");
        cut.Markup.ShouldNotContain("Select a tag…");
    }

    [Fact]
    public void Drawer_LoadsTagChoices_WhenDetailCanApplyTags()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canApplyTag: true)))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));

        RegisterServices(queryService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Lefty"));

        tagDefinitionQueryService.Received(1).GetChoicesAsync(Arg.Any<CancellationToken>());
        cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Captain");
    }

    [Fact]
    public void Drawer_ShowsTagChoicesError_WithRetry_WhenChoiceLoadFails()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canApplyTag: true)))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount == 1
                    ? Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(
                        ServiceProblem.ServerError("Tag choices unavailable.")))
                    : Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList()));
            });

        RegisterServices(queryService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        // Detail still renders with the inline choices failure note.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag choices unavailable."));
        cut.Markup.ShouldContain("Graduation year");

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry").Click();

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Lefty"));
        cut.Markup.ShouldNotContain("Tag choices unavailable.");
        tagDefinitionQueryService.Received(2).GetChoicesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_RestoresPersistedTagChoices_WithoutRefetching()
    {
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();

        RegisterServices(tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedDetail, CreateDetail(
                capabilities: MutationCapabilities(canApplyTag: true)))
            .Add(component => component.SeedTagChoices, CreateTagChoices().ToList()));

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Lefty"));

        tagDefinitionQueryService.DidNotReceive().GetChoicesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_MovesFocusToMutationErrorSummary_WhenMutationFails()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canAddNote: true)))));
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(
                ServiceProblem.Forbidden("You cannot add notes."))));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("A new note");
        FindButtonByText(cut, "Save note").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You cannot add notes."));
        cut.WaitForAssertion(() => JSInterop.Invocations.ShouldContain(invocation =>
            invocation.Identifier == "Blazor._internal.domWrapper.focus"));
    }

    [Fact]
    public void Drawer_DisablesMutationControls_WhileMutationIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>();
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                notes: [CreateNote(canEdit: true, canDelete: true)],
                capabilities: MutationCapabilities(canAddNote: true)))));
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("A note");
        FindButtonByText(cut, "Save note").Click();

        cut.WaitForAssertion(() =>
        {
            FindButtonByText(cut, "Cancel").HasAttribute("disabled").ShouldBeTrue();
            FindButtonByText(cut, "Edit").HasAttribute("disabled").ShouldBeTrue();
        });

        pending.SetResult(new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(99)));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note added."));
    }

    [Fact]
    public void Drawer_AddNoteSuccess_RefreshesDetail_AndClearsStatusOnNextUserAction()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(capabilities: MutationCapabilities(canAddNote: true))
                        : CreateDetail(
                            notes: [CreateNote(content: "New note", canEdit: false, canDelete: false)],
                            capabilities: MutationCapabilities(canAddNote: true))));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(5))));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("New note");
        FindButtonByText(cut, "Save note").Click();

        // The refreshed detail renders the new note and the status message survives the refresh.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note added."));
        cut.Markup.ShouldContain("New note");
        noteService.Received(1).AddAsync(
            Arg.Is<AddEvaluationNoteInput>(input =>
                input.PlayerCampaignAssignmentId == 301 && input.Content == "New note"),
            Arg.Any<CancellationToken>());

        // Opening the add form again is an intentional user-action boundary that clears the status.
        FindButtonByText(cut, "Add note").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Note added."));
    }

    // ── Note mutation controls ───────────────────────────────────────────────

    [Fact]
    public void Drawer_DoesNotRenderAddNoteButton_WhenReadOnlyOrNotAllowed()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail())));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));
        cut.Markup.ShouldNotContain("Add note");
    }

    [Fact]
    public void Drawer_AddNoteValidationFailure_RendersInlineError_AndDoesNotCallService()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canAddNote: true)))));
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("   ");
        FindButtonByText(cut, "Save note").Click();

        cut.WaitForAssertion(() => cut.Find(".invalid-feedback").TextContent.ShouldNotBeNullOrWhiteSpace());
        noteService.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_EditNote_SwapsContentForTextarea_SavesAndRefreshes()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(notes: [CreateNote(content: "Original.", canEdit: true, canDelete: false)])
                        : CreateDetail(notes: [CreateNote(content: "Updated.", canEdit: true, canDelete: false, modified: true)])));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Edit").ShouldNotBeNull());
        FindButtonByText(cut, "Edit").Click();

        // The note content is swapped for an inline textarea pre-filled with the note.
        cut.WaitForAssertion(() => cut.Find("textarea").GetAttribute("value").ShouldBe("Original."));
        cut.Find("textarea").Input("Updated.");
        FindButtonByText(cut, "Save").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note updated."));
        cut.Markup.ShouldContain("Updated.");
        cut.Markup.ShouldContain("· edited");
        noteService.Received(1).EditAsync(
            Arg.Is<EditEvaluationNoteInput>(input => input.NoteId == 1 && input.Content == "Updated."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_EditNoteCancel_RestoresRenderedText_WithoutServiceCall()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                notes: [CreateNote(content: "Original.", canEdit: true, canDelete: false)]))));
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Edit").ShouldNotBeNull());
        FindButtonByText(cut, "Edit").Click();
        cut.Find("textarea").Input("Changed but canceled.");
        FindButtonByText(cut, "Cancel").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Original."));
        cut.Markup.ShouldNotContain("textarea");
        noteService.DidNotReceive().EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_DeleteNote_ConfirmsAndDeletes()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(notes: [CreateNote(canEdit: false, canDelete: true)])
                        : CreateDetail()));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.DeleteAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Delete").ShouldNotBeNull());
        FindButtonByText(cut, "Delete").Click();

        // The confirm button is disabled until the checkbox is checked.
        cut.WaitForAssertion(() => FindButtonByText(cut, "Delete").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#participant-drawer-note-delete-confirm-1").Change(true);
        cut.WaitForAssertion(() => FindButtonByText(cut, "Delete").HasAttribute("disabled").ShouldBeFalse());
        FindButtonByText(cut, "Delete").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note deleted."));
        cut.Markup.ShouldContain("No notes yet.");
        noteService.Received(1).DeleteAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_HidesNoteCommands_WhenCannotEditOrDelete()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                notes: [CreateNote(canEdit: false, canDelete: false)]))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Strong defensive player."));
        cut.Markup.ShouldNotContain("Edit");
        cut.Markup.ShouldNotContain("Delete");
    }

    [Fact]
    public void Drawer_ServerValidationProblem_RendersDetailInErrorSummary()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canAddNote: true)))));
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(
                ServiceProblem.NotFound("The note is no longer available."))));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("A note");
        FindButtonByText(cut, "Save note").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The note is no longer available."));
        queryService.Received(1).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    // ── Tag apply/remove controls ────────────────────────────────────────────

    [Fact]
    public void Drawer_ApplyControl_ExcludesAppliedAndArchivedDefinitions()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                tags:
                [
                    CreateTag(campaignTagApplicationId: 1, playerTagId: 11, tagName: "Lefty", isArchived: false),
                    CreateTag(campaignTagApplicationId: 2, playerTagId: 12, tagName: "Captain", isArchived: true)
                ],
                capabilities: MutationCapabilities(canApplyTag: true)))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(
                new List<TagDefinitionDto>
                {
                    new() { PlayerTagId = 11, Name = "Lefty", Color = "#0D6EFD", LifecycleStatus = LifecycleStatus.Active },
                    new() { PlayerTagId = 12, Name = "Captain", Color = "#FD7E14", LifecycleStatus = LifecycleStatus.Active },
                    new() { PlayerTagId = 13, Name = "Strong arm", Color = "#198754", LifecycleStatus = LifecycleStatus.Active }
                })));

        RegisterServices(queryService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Strong arm"));
        cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldNotContain("Lefty");
        cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldNotContain("Captain");
    }

    [Fact]
    public void Drawer_ApplyDisabled_UntilTagSelected()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                capabilities: MutationCapabilities(canApplyTag: true)))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));

        RegisterServices(queryService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeTrue());

        cut.Find("select").Change("11");
        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeFalse());
    }

    [Fact]
    public void Drawer_ApplySuccess_CallsServiceAndRefreshesDetail()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(capabilities: MutationCapabilities(canApplyTag: true))
                        : CreateDetail(
                            tags: [CreateTag(campaignTagApplicationId: 1, playerTagId: 11, tagName: "Lefty", isArchived: false)],
                            capabilities: MutationCapabilities(canApplyTag: true))));
            });
        var tagApplicationService = Substitute.For<ICampaignTagApplicationService>();
        tagApplicationService.ApplyAsync(Arg.Any<ApplyCampaignTagApplicationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignTagApplicationMutationSuccess>(
                new CampaignTagApplicationMutationSuccess(1))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));

        RegisterServices(queryService, tagApplicationService: tagApplicationService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").ShouldNotBeNull());
        cut.Find("select").Change("11");
        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeFalse());
        FindButtonByText(cut, "Apply").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag applied."));
        cut.Markup.ShouldContain("Lefty");
        tagApplicationService.Received(1).ApplyAsync(
            Arg.Is<ApplyCampaignTagApplicationInput>(input =>
                input.PlayerCampaignAssignmentId == 301 && input.PlayerTagId == 11),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_RemoveTag_VisibleOnly_WhenCanRemove_AndNotArchived()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                tags:
                [
                    CreateTag(campaignTagApplicationId: 1, playerTagId: 11, tagName: "Lefty", isArchived: false, canRemove: true),
                    CreateTag(campaignTagApplicationId: 2, playerTagId: 12, tagName: "Captain", isArchived: false, canRemove: false),
                    CreateTag(campaignTagApplicationId: 3, playerTagId: 13, tagName: "Veteran", isArchived: true, canRemove: true)
                ]))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Lefty"));

        // Only the removable, non-archived application renders a Remove command.
        cut.FindAll("button").Count(button => button.TextContent.Trim() == "Remove").ShouldBe(1);
        // Archived application keeps the archived indicator and no Remove command is rendered for it.
        cut.Find(".participant-drawer-tag-archived").TextContent.Trim().ShouldBe("Veteran");
    }

    [Fact]
    public void Drawer_RemoveTag_ConfirmsAndRemoves()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(tags: [CreateTag(campaignTagApplicationId: 1, playerTagId: 11, tagName: "Lefty", isArchived: false, canRemove: true)])
                        : CreateDetail()));
            });
        var tagApplicationService = Substitute.For<ICampaignTagApplicationService>();
        tagApplicationService.RemoveAsync(Arg.Any<RemoveCampaignTagApplicationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(queryService, tagApplicationService: tagApplicationService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Remove").ShouldNotBeNull());
        FindButtonByText(cut, "Remove").Click();

        cut.WaitForAssertion(() => FindButtonByText(cut, "Remove").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#participant-drawer-tag-remove-confirm-1").Change(true);
        cut.WaitForAssertion(() => FindButtonByText(cut, "Remove").HasAttribute("disabled").ShouldBeFalse());
        FindButtonByText(cut, "Remove").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag removed."));
        cut.Markup.ShouldContain("No tags applied.");
        tagApplicationService.Received(1).RemoveAsync(
            Arg.Is<RemoveCampaignTagApplicationInput>(input => input.CampaignTagApplicationId == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_ArchivedApplication_NeverRendersRemove_EvenWithStaleCanRemove()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                tags:
                [
                    CreateTag(campaignTagApplicationId: 1, playerTagId: 11, tagName: "Veteran", isArchived: true, canRemove: true)
                ]))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Veteran"));
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Trim() == "Remove");
    }

    // ── Stale Active→Closed conflict recovery ───────────────────────────────

    [Fact]
    public void Drawer_ConflictRefresh_EntersReadOnly_WhenReloadIsClosed()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(capabilities: MutationCapabilities(canAddNote: true))
                        : CreateDetail(campaignStatus: CampaignStatus.Closed, capabilities: MutationCapabilities(canAddNote: true))));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(
                ServiceProblem.Conflict("Closed campaigns are read-only and cannot accept new notes."))));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("A note");
        FindButtonByText(cut, "Save note").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Closed campaigns are read-only and cannot accept new notes."));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only — campaign is closed."));
        cut.Markup.ShouldNotContain("Add note");
        cut.Markup.ShouldNotContain("Note added.");
        queryService.Received(2).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_ConflictRefresh_StaysEditable_WhenReloadIsActive()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                    capabilities: MutationCapabilities(canApplyTag: true))));
            });
        var tagApplicationService = Substitute.For<ICampaignTagApplicationService>();
        tagApplicationService.ApplyAsync(Arg.Any<ApplyCampaignTagApplicationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignTagApplicationMutationSuccess>(
                ServiceProblem.Conflict("The selected tag has already been applied to this participation."))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));

        RegisterServices(queryService, tagApplicationService: tagApplicationService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").ShouldNotBeNull());
        cut.Find("select").Change("11");
        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeFalse());
        FindButtonByText(cut, "Apply").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The selected tag has already been applied to this participation."));
        cut.Markup.ShouldNotContain("Read-only — campaign is closed.");
        cut.Markup.ShouldContain("Select a tag…");
        queryService.Received(2).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Drawer_ConflictRefreshFailure_KeepsMessage_AndDoesNotCrash()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount == 1
                    ? Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                        capabilities: MutationCapabilities(canAddNote: true))))
                    : Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                        ServiceProblem.NotFound("Participant not found.")));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(
                ServiceProblem.Conflict("Closed campaigns are read-only and cannot accept new notes."))));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("A note");
        FindButtonByText(cut, "Save note").Click();

        // The conflict message stays visible, the previous Active detail renders, and the drawer
        // stays editable instead of flipping to the load-failure state.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Closed campaigns are read-only and cannot accept new notes."));
        cut.Markup.ShouldNotContain("Participant not found.");
        cut.Markup.ShouldNotContain("Read-only — campaign is closed.");
        cut.Markup.ShouldNotContain("Loading participant details");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RegisterServices(
        ICampaignParticipantQueryService? queryService = null,
        ICampaignEvaluationNoteService? noteService = null,
        ICampaignTagApplicationService? tagApplicationService = null,
        ITagDefinitionQueryService? tagDefinitionQueryService = null)
    {
        if (queryService is null)
        {
            queryService = Substitute.For<ICampaignParticipantQueryService>();
            queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail())));
        }

        noteService ??= Substitute.For<ICampaignEvaluationNoteService>();
        tagApplicationService ??= Substitute.For<ICampaignTagApplicationService>();

        if (tagDefinitionQueryService is null)
        {
            tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
            tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(
                    CreateTagChoices().ToList())));
        }

        Services.AddSingleton(queryService);
        Services.AddSingleton(noteService);
        Services.AddSingleton(tagApplicationService);
        Services.AddSingleton(tagDefinitionQueryService);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CampaignParticipantDetailDto CreateDetail(
        long assignmentId = 301,
        string displayName = "Avery Johnson",
        int? tryoutNumber = 14,
        CampaignParticipantTeamSummaryDto? team = null,
        bool includeTeam = true,
        IReadOnlyList<CampaignParticipantNoteDto>? notes = null,
        IReadOnlyList<CampaignParticipantTagApplicationDto>? tags = null,
        CampaignParticipantCapabilitiesDto? capabilities = null,
        CampaignStatus campaignStatus = CampaignStatus.Active) => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: 7,
        DisplayName: displayName,
        GraduationYear: 2032,
        TryoutNumber: tryoutNumber,
        PlacementOutcome: PlacementOutcome.Assigned,
        Team: includeTeam ? team ?? new CampaignParticipantTeamSummaryDto(21, "Blue") : null,
        CreatedAt: new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
        ModifiedAt: new DateTimeOffset(2026, 5, 3, 14, 30, 0, TimeSpan.Zero),
        CampaignStatus: campaignStatus,
        ConcurrencyToken: Guid.NewGuid(),
        Notes: notes ?? [],
        AppliedTags: tags ?? [],
        Capabilities: capabilities ?? new CampaignParticipantCapabilitiesDto(
            CanEditPlacement: false,
            CanAddNote: false,
            CanApplyTag: false,
            CanArchiveTagDefinitions: false));

    private static IReadOnlyList<TagDefinitionDto> CreateTagChoices() =>
    [
        new() { PlayerTagId = 11, Name = "Lefty", Color = "#0D6EFD", LifecycleStatus = LifecycleStatus.Active },
        new() { PlayerTagId = 12, Name = "Captain", Color = "#FD7E14", LifecycleStatus = LifecycleStatus.Active }
    ];

    private static CampaignParticipantCapabilitiesDto MutationCapabilities(
        bool canAddNote = false,
        bool canApplyTag = false)
        => new(CanEditPlacement: false, CanAddNote: canAddNote, CanApplyTag: canApplyTag, CanArchiveTagDefinitions: false);

    private static CampaignParticipantNoteDto CreateNote(
        long noteId = 1,
        string content = "Strong defensive player.",
        bool canEdit = false,
        bool canDelete = false,
        bool modified = false)
        => new(
            NoteId: noteId,
            Content: content,
            AuthorDisplayName: "Coach Rivera",
            CreatedAt: new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero),
            ModifiedAt: modified ? new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero) : null,
            CanEdit: canEdit,
            CanDelete: canDelete);

    private static CampaignParticipantTagApplicationDto CreateTag(
        long campaignTagApplicationId = 1,
        long playerTagId = 11,
        string tagName = "Lefty",
        bool isArchived = false,
        bool canRemove = false)
        => new(
            CampaignTagApplicationId: campaignTagApplicationId,
            PlayerTagId: playerTagId,
            TagName: tagName,
            TagColor: "#0D6EFD",
            IsArchived: isArchived,
            ActorDisplayName: "Coach Rivera",
            AppliedAt: new DateTimeOffset(2026, 5, 2, 9, 30, 0, TimeSpan.Zero),
            CanRemove: canRemove);

    private static IElement FindButtonByText<T>(Bunit.IRenderedComponent<T> cut, string text)
        where T : class, IComponent
        => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

    private static CampaignParticipantRosterItem CreateRosterItem() => new(
        PlayerCampaignAssignmentId: 301,
        PlayerId: 7,
        DisplayName: "Avery Johnson",
        GraduationYear: 2032,
        TryoutNumber: 14,
        PlacementOutcome: PlacementOutcome.Undecided,
        Team: null,
        AppliedTags: []);

    /// <summary>
    /// A test-only <see cref="CampaignParticipantDrawerComponent"/> subclass that seeds persisted
    /// prerender state before startup initialization runs.
    /// </summary>
    private sealed class RestoredDrawer(
        ICampaignParticipantQueryService participantQueryService,
        ICampaignEvaluationNoteService noteService,
        ICampaignTagApplicationService tagApplicationService,
        ITagDefinitionQueryService tagDefinitionQueryService,
        IJSRuntime jsRuntime)
        : CampaignParticipantDrawerComponent(participantQueryService, noteService, tagApplicationService, tagDefinitionQueryService, jsRuntime)
    {
        /// <summary>
        /// Gets or sets the persisted detail payload seeded before initialization.
        /// </summary>
        [Parameter]
        public CampaignParticipantDetailDto? SeedDetail { get; set; }

        /// <summary>
        /// Gets or sets the persisted error message seeded before initialization.
        /// </summary>
        [Parameter]
        public string? SeedError { get; set; }

        /// <summary>
        /// Gets or sets the persisted tag choices seeded before initialization.
        /// </summary>
        [Parameter]
        public IReadOnlyList<TagDefinitionDto>? SeedTagChoices { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            Initialized = true;
            PersistedDetail = SeedDetail;
            PersistedDetailError = SeedError;
            PersistedTagChoices = SeedTagChoices;

            return base.OnInitializedAsync();
        }
    }
}
