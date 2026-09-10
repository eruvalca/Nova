using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using NSubstitute;
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
    private readonly Dictionary<long, (IReadOnlyList<CampaignParticipantNoteDto> Notes, IReadOnlyList<CampaignParticipantTagApplicationDto> Applications)> _evidence = [];

    private const string DrawerModulePath = "./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js";

    // ── Loading state ─────────────────────────────────────────────────────────

    [Fact]
    public void DrawerShowsLoadingStateWhileDetailRequestIsPending()
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
    public void DrawerRendersLoadedDetailWithPlayerCampaignNotesAndFooter()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                notes:
                [
                    new CampaignParticipantNoteDto(
                        NoteId: 1,
                        Version: Guid.NewGuid(),
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
        successBadges.ShouldContain("Assigned", StringComparer.Ordinal);
        successBadges.ShouldContain("Active", StringComparer.Ordinal);
        cut.Markup.ShouldContain("Blue");
        cut.Markup.ShouldContain("Active");
        cut.Markup.ShouldContain("Strong defensive player.");
        cut.Markup.ShouldContain("Coach Rivera");
        cut.Markup.ShouldContain("· edited");
        cut.Markup.ShouldContain("Created");
        cut.Markup.ShouldContain("· modified");
    }

    [Fact]
    public void DrawerRendersTagMetadataAndArchivedTagIndicators()
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
    public void DrawerRendersFallbacksWhenOptionalFieldsAreMissing()
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

        cut.FindAll("dd").Count(dd => string.Equals(dd.TextContent.Trim(), "—", StringComparison.Ordinal)).ShouldBe(2);
        cut.Markup.ShouldContain("No notes yet.");
        cut.Markup.ShouldContain("No tags applied.");
    }

    // ── Failure and retry ─────────────────────────────────────────────────────

    [Fact]
    public void DrawerShowsErrorAndRetryWhenDetailLoadFails()
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
        _ = queryService.Received(2).GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerUsesRosterItemNameForHeadingWhenDetailLoadFails()
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
    public void DrawerReloadsDetailWhenParticipantParameterChanges()
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

        _ = queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input =>
                input.CampaignId == 10 && input.PlayerCampaignAssignmentId == 301),
            Arg.Any<CancellationToken>());
        _ = queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input =>
                input.CampaignId == 10 && input.PlayerCampaignAssignmentId == 302),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerDiscardsStaleResponseWhenParticipantChangesBeforeFirstLoadCompletes()
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
        _ = queryService.Received(2).GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    // ── Close callbacks ───────────────────────────────────────────────────────

    [Fact]
    public async Task DrawerInvokesOnCloseWhenCloseButtonClickedAsync()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        await cut.Find("#participant-drawer-close").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        closed.ShouldBeTrue();
        close.Invocations.ShouldBeEmpty();
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
    }

    [Fact]
    public async Task DrawerInvokesOnCloseWhenEscapePressedAsync()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        await cut.Find("aside.participant-drawer").TriggerEventAsync("onkeydown",
            new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        closed.ShouldBeTrue();
        close.Invocations.ShouldBeEmpty();
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
    }

    [Fact]
    public async Task DrawerInvokesOnCloseWhenBackdropClickedAsync()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        await cut.Find(".participant-drawer-backdrop").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        closed.ShouldBeTrue();
        close.Invocations.ShouldBeEmpty();
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
    }

    // ── Focus trap management ───────────────────────────────────────────────────

    [Fact]
    public void DrawerInstallsFocusTrapOnFirstRender()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var module = JSInterop.SetupModule(DrawerModulePath);
        module.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        var read = module.Setup<string?>("readOperation", _ => true);
        var open = module.SetupVoid("open", _ => true);
        open.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        var dialogRefId = cut.Find("aside.participant-drawer").GetAttribute("blazor:elementreference");
        var closeRefId = cut.Find("#participant-drawer-close").GetAttribute("blazor:elementreference");

        var invocation = open.Invocations.Single();
        invocation.Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(dialogRefId);
        invocation.Arguments[1].ShouldBeOfType<ElementReference>().Id.ShouldBe(closeRefId);
        read.SetResult(null);
        open.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public void DrawerDoesNotReinstallFocusTrapWhenParticipantParameterChanges()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
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
    public void DrawerRestoresFocusIntoDialogWhenParticipantParameterChanges()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var restoreFocus = drawerModule.SetupVoid("restoreFocus", _ => true);
        restoreFocus.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        var dialogRefId = open.Invocations.Single().Arguments[0].ShouldBeOfType<ElementReference>().Id;
        var closeRefId = open.Invocations.Single().Arguments[1].ShouldBeOfType<ElementReference>().Id;

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        var invocation = restoreFocus.Invocations.Single();
        invocation.Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(dialogRefId);
        invocation.Arguments[1].ShouldBeOfType<ElementReference>().Id.ShouldBe(closeRefId);
    }

    [Fact]
    public void DrawerDoesNotRestoreFocusWhenParticipantParameterUnchanged()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
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
    public async Task DrawerRestoresFocusToRosterRowWhenDisposedWhileOpenForBrowserBackAsync()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
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
    public async Task DrawerDoesNotRestoreFocusAgainWhenDisposedAfterCloseAsync()
    {
        RegisterServices();
        JSInterop.Mode = JSRuntimeMode.Strict;
        var drawerModule = JSInterop.SetupModule(DrawerModulePath);
        drawerModule.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        drawerModule.Setup<string?>("readOperation", _ => true).SetResult(null);
        var open = drawerModule.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var close = drawerModule.SetupVoid("close", _ => true);
        close.SetVoidResult();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("#participant-drawer-close").Click();
#pragma warning restore CA1849, S6966
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();

        close.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DrawerDisposeCompletesWhenPendingModuleImportIsCanceledAsync()
    {
        RegisterServices();
        var runtime = new PendingModuleRuntime(JSInterop.JSRuntime, DrawerModulePath);
        Services.AddSingleton<IJSRuntime>(runtime);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        runtime.ImportedPaths.ShouldBe([DrawerModulePath]);

        var disposal = cut.Instance.DisposeAsync().AsTask();
        disposal.IsCompleted.ShouldBeFalse();
        runtime.CancelImport(Xunit.TestContext.Current.CancellationToken);
        await disposal;
        await cut.Instance.DisposeAsync();

        runtime.ImportedPaths.ShouldBe([DrawerModulePath]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawerDisposesFocusOwnershipOnceWhenCleanupCompletesOrIsCanceledAsync(bool canceled)
    {
        RegisterServices();
        var module = JSInterop.SetupModule(DrawerModulePath);
        module.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        module.Setup<string?>("readOperation", _ => true).SetResult(null);
        module.Mode = JSRuntimeMode.Loose;
        var open = module.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        await cut.WaitForAssertionAsync(() => open.Invocations.Count.ShouldBe(1));
        var dialog = open.Invocations.Single().Arguments[0];
        var close = module.SetupVoid("close", _ => true);
        if (canceled)
        {
            close.SetCanceled();
        }
        else
        {
            close.SetVoidResult();
        }

        await cut.Instance.DisposeAsync();
        await cut.Instance.DisposeAsync();

        close.Invocations.Count.ShouldBe(1);
        close.Invocations.Single().Arguments[0].ShouldBe("roster-row-301");
        close.Invocations.Single().Arguments[1].ShouldBe(dialog);
    }

    [Fact]
    public async Task DrawerDisposalDoesNotSwallowUnrelatedJavascriptFailureAsync()
    {
        RegisterServices();
        var module = JSInterop.SetupModule(DrawerModulePath);
        module.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        module.Setup<string?>("readOperation", _ => true).SetResult(null);
        module.Mode = JSRuntimeMode.Loose;
        var open = module.SetupVoid("open", _ => true);
        open.SetVoidResult();
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        await cut.WaitForAssertionAsync(() => open.Invocations.Count.ShouldBe(1));
        var close = module.SetupVoid("close", _ => true);
        close.SetException(new JSException("Unexpected focus cleanup failure"));

        var error = await Should.ThrowAsync<JSException>(() => cut.Instance.DisposeAsync().AsTask());

        error.Message.ShouldBe("Unexpected focus cleanup failure");
        close.Invocations.Count.ShouldBe(1);
    }

    // ── Sequence navigation ─────────────────────────────────────────────────────

    [Fact]
    public void DrawerRendersPositionTextWhenSequenceParametersProvided()
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
    public void DrawerInvokesNavigationCallbacksWhenNavigationButtonsClicked()
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
    public void DrawerDisablesNavigationButtonsAtSequenceEnds()
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
    public void DrawerHidesPositionAndDisablesNavigationWhenParticipantIsOffPage()
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
    public void DrawerRestoresPersistedDetailWithoutRefetching()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(queryService);

        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedDetail, CreateDetail()));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Graduation year"));

        _ = queryService.DidNotReceive().GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerRestoresPersistedErrorWithoutRefetching()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        RegisterServices(queryService);

        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedError, "Participant not found."));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Participant not found"));

        cut.Find("#participant-drawer-retry").ShouldNotBeNull();
        _ = queryService.DidNotReceive().GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    // ── Evaluation mutations: read-only mode and infrastructure ─────────────

    [Fact]
    public void DrawerRendersNoReadOnlyIndicatorForActiveCampaign()
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
    public void DrawerRendersReadOnlyIndicatorAndHidesMutationControlsForClosedCampaign()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                campaignStatus: CampaignStatus.Closed,
                notes: [CreateNote(canEdit: true, canDelete: true)],
                capabilities: MutationCapabilities(canAddNote: true, canApplyTag: true)))));

        RegisterServices(queryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only — campaign is closed."));
        cut.Markup.ShouldNotContain("Add note");
        cut.Markup.ShouldNotContain("Apply");
        cut.Markup.ShouldNotContain("Select a tag…");
        // Read-only hides per-note Edit/Delete commands even when a stale server flag says they are allowed.
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Trim() == "Edit");
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Trim() == "Delete");
        // Read content still renders in full.
        cut.Markup.ShouldContain("Strong defensive player.");
    }

    [Fact]
    public void DrawerLoadsTagChoicesWhenDetailCanApplyTags()
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

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Lefty", StringComparer.Ordinal));

        _ = tagDefinitionQueryService.Received(1).GetChoicesAsync(Arg.Any<CancellationToken>());
        cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Captain", StringComparer.Ordinal);
    }

    [Fact]
    public void DrawerShowsTagChoicesErrorWithRetryWhenChoiceLoadFails()
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

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Retry", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Lefty", StringComparer.Ordinal));
        cut.Markup.ShouldNotContain("Tag choices unavailable.");
        _ = tagDefinitionQueryService.Received(2).GetChoicesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerRestoresPersistedTagChoicesWithoutRefetching()
    {
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();

        RegisterServices(tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedDetail, CreateDetail(
                capabilities: MutationCapabilities(canApplyTag: true)))
            .Add(component => component.SeedTagChoices, CreateTagChoices().ToList()));

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Lefty", StringComparer.Ordinal));

        _ = tagDefinitionQueryService.DidNotReceive().GetChoicesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerMovesFocusToMutationErrorSummaryWhenMutationFails()
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
    public void DrawerDisablesMutationControlsWhileMutationIsPending()
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

        pending.SetResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(99)));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note added."));
    }

    [Fact]
    public void DrawerAddNoteSuccessRefreshesDetailAndClearsStatusOnNextUserAction()
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
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(5))));

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
        _ = noteService.Received(1).AddAsync(
            Arg.Is<AddEvaluationNoteInput>(input =>
                input.PlayerCampaignAssignmentId == 301 && input.Content == "New note"),
            Arg.Any<CancellationToken>());

        // Opening the add form again is an intentional user-action boundary that clears the status.
        FindButtonByText(cut, "Add note").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Note added."));
    }

    // ── Note mutation controls ───────────────────────────────────────────────

    [Fact]
    public void DrawerDoesNotRenderAddNoteButtonWhenReadOnlyOrNotAllowed()
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
    public void DrawerAddNoteValidationFailureRendersInlineErrorAndDoesNotCallService()
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
        _ = noteService.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerEditNoteSwapsContentForTextareaSavesAndRefreshes()
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
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(1))));

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
        _ = noteService.Received(1).EditAsync(
            Arg.Is<EditEvaluationNoteInput>(input => input.NoteId == 1 && input.Content == "Updated."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerEditNoteCancelRestoresRenderedTextWithoutServiceCall()
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
        _ = noteService.DidNotReceive().EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerDeleteNoteConfirmsAndDeletes()
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
        noteService.DeleteAsync(Arg.Is<DeleteEvaluationNoteInput>(input => input.NoteId == 1 && input.ExpectedVersion != Guid.Empty && input.OperationId != Guid.Empty), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(1))));

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
        _ = noteService.Received(1).DeleteAsync(Arg.Is<DeleteEvaluationNoteInput>(input => input.NoteId == 1 && input.ExpectedVersion != Guid.Empty && input.OperationId != Guid.Empty), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerHidesNoteCommandsWhenCannotEditOrDelete()
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
    public void DrawerServerValidationProblemRendersDetailInErrorSummary()
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
        _ = queryService.Received(1).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerResetsMutationFormsWhenParticipantChanges()
    {
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<GetCampaignParticipantDetailInput>();
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    CreateDetail(assignmentId: input.PlayerCampaignAssignmentId,
                        displayName: $"Player {input.PlayerCampaignAssignmentId}",
                        capabilities: MutationCapabilities(canAddNote: true, canApplyTag: true))));
            });
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));

        RegisterServices(queryService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Draft for participant A");
        cut.Find("select").Change("11");
        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeFalse());

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        // The add form closes with the draft discarded, and the tag selection resets so Apply is
        // disabled again instead of being retargeted to participant B.
        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Player 302"));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        cut.FindAll("textarea").ShouldBeEmpty();
        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeTrue());
    }

    [Fact]
    public void DrawerDoesNotSurfaceMutationFeedbackWhenParticipantChangesMidMutation()
    {
        var pending = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>();
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<GetCampaignParticipantDetailInput>();
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    CreateDetail(assignmentId: input.PlayerCampaignAssignmentId,
                        displayName: $"Player {input.PlayerCampaignAssignmentId}",
                        capabilities: MutationCapabilities(canAddNote: true))));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Draft for participant A");
        FindButtonByText(cut, "Save note").Click();

        // Navigate to participant B while the mutation is still in flight.
        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Player 302"));

        pending.SetResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(99)));
        cut.WaitForAssertion(() =>
        {
            _ = queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input => input.PlayerCampaignAssignmentId == 302),
            Arg.Any<CancellationToken>());
        });

        // The success message is not surfaced on the participant the mutation did not target.
        cut.Markup.ShouldNotContain("Note added.");
    }

    [Fact]
    public void DrawerDoesNotSurfaceProblemFeedbackWhenParticipantChangesMidMutation()
    {
        var pending = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>();
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<GetCampaignParticipantDetailInput>();
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    CreateDetail(assignmentId: input.PlayerCampaignAssignmentId,
                        displayName: $"Player {input.PlayerCampaignAssignmentId}",
                        capabilities: MutationCapabilities(canAddNote: true))));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Draft for participant A");
        FindButtonByText(cut, "Save note").Click();

        // Navigate to participant B while the mutation is still in flight.
        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Player 302"));

        pending.SetResult(new ServiceResult<EvaluationNoteMutationSuccess>(
            ServiceProblem.Conflict("Closed campaigns are read-only and cannot accept new notes.")));
        cut.WaitForAssertion(() =>
        {
            _ = queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input => input.PlayerCampaignAssignmentId == 302),
            Arg.Any<CancellationToken>());
        });

        // The conflict message is not surfaced on the participant the mutation did not target,
        // and the drawer does not enter read-only mode for it.
        cut.Markup.ShouldNotContain("Closed campaigns are read-only");
        cut.Markup.ShouldNotContain("Read-only — campaign is closed.");
    }

    [Fact]
    public void DrawerDoesNotSurfaceTransportFailureFeedbackWhenParticipantChangesMidMutation()
    {
        var pending = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>();
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<GetCampaignParticipantDetailInput>();
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    CreateDetail(assignmentId: input.PlayerCampaignAssignmentId,
                        displayName: $"Player {input.PlayerCampaignAssignmentId}",
                        capabilities: MutationCapabilities(canAddNote: true))));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Draft for participant A");
        FindButtonByText(cut, "Save note").Click();

        // Navigate to participant B while the mutation is still in flight.
        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        cut.WaitForAssertion(() => cut.Find(".participant-drawer-header h2").TextContent.Trim().ShouldBe("Player 302"));

        pending.SetException(new HttpRequestException("Connection refused."));
        cut.WaitForAssertion(() =>
        {
            _ = queryService.Received(1).GetParticipantDetailAsync(
            Arg.Is<GetCampaignParticipantDetailInput>(input => input.PlayerCampaignAssignmentId == 302),
            Arg.Any<CancellationToken>());
        });

        // The transport-failure banner is not surfaced on the participant the mutation did not target.
        cut.Markup.ShouldNotContain("Could not reach the server.");
    }

    // ── Tag apply/remove controls ────────────────────────────────────────────

    [Fact]
    public void DrawerApplyControlExcludesAppliedAndArchivedDefinitions()
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

        cut.WaitForAssertion(() => cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldContain("Strong arm", StringComparer.Ordinal));
        cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldNotContain("Lefty", StringComparer.Ordinal);
        cut.FindAll("option").Select(option => option.TextContent.Trim()).ShouldNotContain("Captain", StringComparer.Ordinal);
    }

    [Fact]
    public void DrawerApplyDisabledUntilTagSelected()
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
    public void DrawerApplySuccessCallsServiceAndRefreshesDetail()
    {
        var callCount = 0;
        var rosterRefreshes = 0;
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
                TagSuccess(1))));
        var tagDefinitionQueryService = Substitute.For<ITagDefinitionQueryService>();
        tagDefinitionQueryService.GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));

        RegisterServices(queryService, tagApplicationService: tagApplicationService, tagDefinitionQueryService: tagDefinitionQueryService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnDataChanged, EventCallback.Factory.Create(this, () => rosterRefreshes++)));

        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").ShouldNotBeNull());
        cut.Find("select").Change("11");
        cut.WaitForAssertion(() => FindButtonByText(cut, "Apply").HasAttribute("disabled").ShouldBeFalse());
        FindButtonByText(cut, "Apply").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Tag applied."));
        rosterRefreshes.ShouldBe(1);
        cut.Markup.ShouldContain("Lefty");
        _ = tagApplicationService.Received(1).ApplyAsync(
            Arg.Is<ApplyCampaignTagApplicationInput>(input =>
                input.PlayerCampaignAssignmentId == 301 && input.PlayerTagId == 11),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerRemoveTagVisibleOnlyWhenCanRemoveAndNotArchived()
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
        cut.FindAll("button").Count(button => string.Equals(button.TextContent.Trim(), "Remove", StringComparison.Ordinal)).ShouldBe(1);
        // Archived application keeps the archived indicator and no Remove command is rendered for it.
        cut.Find(".participant-drawer-tag-archived").TextContent.Trim().ShouldBe("Veteran");
    }

    [Fact]
    public void DrawerRemoveTagConfirmsAndRemoves()
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
            .Returns(Task.FromResult(new ServiceResult<CampaignTagApplicationMutationSuccess>(TagSuccess(1))));

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
        _ = tagApplicationService.Received(1).RemoveAsync(
            Arg.Is<RemoveCampaignTagApplicationInput>(input => input.CampaignTagApplicationId == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerArchivedApplicationNeverRendersRemoveEvenWithStaleCanRemove()
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
    public void DrawerConflictRefreshEntersReadOnlyWhenReloadIsClosed()
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
        _ = queryService.Received(2).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("participant")]
    [InlineData("authority")]
    public void DrawerRetainsConflictAcrossStatusOnlyRefreshAndClearsItWhenContextChanges(string nextContext)
    {
        var detailReads = 0;
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                assignmentId: call.Arg<GetCampaignParticipantDetailInput>().PlayerCampaignAssignmentId,
                campaignStatus: ++detailReads == 1 ? CampaignStatus.Active : CampaignStatus.Closed,
                capabilities: MutationCapabilities(canAddNote: true)))));
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(
                ServiceProblem.Conflict("Campaign closed before this note could be saved."))));
        RegisterServices(query, notes);
        var lifecycleRefreshes = 0;
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorityScope, "101:42:True").Add(component => component.AuthorizedStatus, CampaignStatus.Active)
            .Add(component => component.OnLifecycleChanged, EventCallback.Factory.Create(this, () => lifecycleRefreshes++)));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("An observation from before closing");
        FindButtonByText(cut, "Save note").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign closed before this note could be saved."));
        lifecycleRefreshes.ShouldBe(1);

        cut.Render(parameters => parameters.Add(component => component.AuthorizedStatus, CampaignStatus.Closed));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only"));
        cut.Markup.ShouldContain("Campaign closed before this note could be saved.");
        cut.Find("textarea").GetAttribute("value").ShouldBe("An observation from before closing");
        cut.Find("textarea").HasAttribute("readonly").ShouldBeTrue();
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Add note", StringComparison.Ordinal));

        if (string.Equals(nextContext, "participant", StringComparison.Ordinal))
        {
            cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        }
        else
        {
            cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "101:43:True"));
        }
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Campaign closed before this note could be saved."));
        cut.Markup.ShouldContain("Read-only");
        _ = notes.Received(1).AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerConflictReadOnlyTransitionKeepsEditCopyableAndRemovesMutationControls()
    {
        var callCount = 0;
        var queryService = Substitute.For<ICampaignParticipantQueryService>();
        queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(
                    callCount == 1
                        ? CreateDetail(notes: [CreateNote(canEdit: true, canDelete: false)], capabilities: MutationCapabilities())
                        : CreateDetail(campaignStatus: CampaignStatus.Closed,
                            notes: [CreateNote(canEdit: true, canDelete: false)],
                            capabilities: MutationCapabilities())));
            });
        var noteService = Substitute.For<ICampaignEvaluationNoteService>();
        noteService.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(
                ServiceProblem.Conflict("Closed campaigns are read-only and cannot accept note edits."))));

        RegisterServices(queryService, noteService);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        // Open the inline editor on the Active detail.
        cut.WaitForAssertion(() => FindButtonByText(cut, "Edit").ShouldNotBeNull());
        FindButtonByText(cut, "Edit").Click();
        cut.WaitForAssertion(() => cut.FindAll("textarea").ShouldNotBeEmpty());

        // Save returns a Closed conflict; the drawer enters read-only mode.
        cut.Find("textarea").Input("Attempted edit on a closed campaign");
        FindButtonByText(cut, "Save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Closed campaigns are read-only and cannot accept note edits."));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only — campaign is closed."));

        // The unsaved edit remains copyable while mutation actions disappear.
        cut.Find("textarea").GetAttribute("value").ShouldBe("Attempted edit on a closed campaign");
        cut.Find("textarea").HasAttribute("readonly").ShouldBeTrue();
        cut.FindAll("button").ShouldAllBe(button => !string.Equals(button.TextContent.Trim(), "Save", StringComparison.Ordinal)
            && !string.Equals(button.TextContent.Trim(), "Cancel", StringComparison.Ordinal));
        // Read content still renders in full.
        cut.Markup.ShouldContain("Strong defensive player.");
    }

    [Fact]
    public void DrawerConflictRefreshStaysEditableWhenReloadIsActive()
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
        _ = queryService.Received(2).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerConflictRefreshFailureKeepsMessageAndDoesNotCrash()
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

    [Fact]
    public void DrawerUsesNonmodalRegionBeforeResponsiveJavascriptEnhancement()
    {
        RegisterServices();
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        var panel = cut.Find("aside.participant-drawer");
        panel.GetAttribute("role").ShouldBe("region");
        panel.HasAttribute("aria-modal").ShouldBeFalse();
        panel.GetAttribute("aria-labelledby").ShouldBe("participant-drawer-heading");
    }

    [Fact]
    public void PlacementContextRetriesBoundedExactParticipantReadWithoutReloadingNotes()
    {
        RegisterServices();
        var placement = Services.GetRequiredService<IEffectivePlacementQueryService>();
        var row = new CampaignEffectivePlacementItem(301, 7, "Avery", "Johnson", 2032, 14,
            LifecycleStatus.Active, Guid.NewGuid(), null, null, null, EffectivePlacementEligibility.NeedsPlacement, PlacementCorrectionReason.None);
        placement.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("Unavailable"))),
                Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(new CampaignEffectivePlacementsResult(
                    new(10, "Summer Tryouts", CampaignStatus.Active, new(5, "Summer 2026")), new(143, 0, 0, 0), new([row], 1, 1, 1)))));
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorizedStatus, CampaignStatus.Active));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement context unavailable"));
        cut.Markup.ShouldContain("Avery Johnson");
        FindButtonByText(cut, "Retry placement context").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Needs placement"));
        cut.Markup.ShouldNotContain("Placement context unavailable");
        _ = placement.Received(2).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input =>
            input.CampaignId == 10 && input.ParticipantId == 301 && input.PageSize == 1 && input.Search == null
            && input.LocalOutcome == null && input.LocalTeamId == null && input.TagDefinitionIds == null && input.GraduationYears == null), Arg.Any<CancellationToken>());
        _ = Services.GetRequiredService<ICampaignParticipantQueryService>().Received(1).GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PlacementContextReusesLoadedActiveRowWithoutAnotherPlacementRead()
    {
        RegisterServices();
        var saved = new CampaignSavedPlacementDecision(201, 7, 9, 5, 1, PlacementOutcome.Assigned, 21,
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), 101, "Coach Rivera", Guid.NewGuid());
        var row = new CampaignEffectivePlacementItem(301, 7, "Avery", "Johnson", 2032, 14,
            LifecycleStatus.Active, Guid.NewGuid(), null, new PlacementDecisionSource(saved, "Spring campaign", new(21, "Archived Blue")), null,
            EffectivePlacementEligibility.NeedsPlacement, PlacementCorrectionReason.TeamArchived);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorizedStatus, CampaignStatus.Active).Add(component => component.WorkingRow, row));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Needs placement"));
        cut.Markup.ShouldContain("Saved team is archived; correction required.");
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().DidNotReceive().GetCampaignEffectivePlacementsAsync(
            Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(CampaignStatus.Active, false)]
    [InlineData(CampaignStatus.Active, true)]
    [InlineData(CampaignStatus.Closed, false)]
    [InlineData(CampaignStatus.Closed, true)]
    public void PlacementContextRestoresMatchingSnapshotWithoutDuplicateRead(CampaignStatus status, bool failed)
    {
        RegisterServices();
        var cut = Render<RestoredPlacementContext>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301).Add(component => component.Status, status)
            .Add(component => component.Owner, "101:42:False").Add(component => component.SeedError, failed));
        var expected = status == CampaignStatus.Active ? "Needs placement" : "Immutable campaign";
        cut.Markup.ShouldContain(failed ? "Placement context unavailable" : expected);
        var queries = Services.GetRequiredService<IEffectivePlacementQueryService>();
        _ = queries.DidNotReceive().GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
        _ = queries.DidNotReceive().GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("102:42:False:10:301:Active")]
    [InlineData("101:43:False:10:301:Active")]
    [InlineData("101:42:True:10:301:Active")]
    [InlineData("101:42:False:11:301:Active")]
    [InlineData("101:42:False:10:302:Active")]
    [InlineData("101:42:False:10:301:Closed")]
    public void PlacementContextRejectsSnapshotFromAnotherAuthorityCampaignParticipantOrLifecycle(string owner)
    {
        RegisterServices();
        var cut = Render<RestoredPlacementContext>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301).Add(component => component.Status, CampaignStatus.Active)
            .Add(component => component.Owner, "101:42:False").Add(component => component.SeedOwner, owner));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement context unavailable"));
        cut.Markup.ShouldNotContain("Needs placement");
        cut.Instance.PersistedOwner.ShouldBe("101:42:False:10:301:Active");
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.CampaignId == 10 && input.ParticipantId == 301), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("other-user:42:False:10:301:Active")]
    [InlineData("101:other-club:False:10:301:Active")]
    [InlineData("101:42:True:10:301:Active")]
    [InlineData("101:42:False:10:302:Active")]
    [InlineData("101:42:False:10:301:Closed")]
    public void DrawerRejectsPersistedDetailAndTagChoicesFromAnotherOwner(string owner)
    {
        RegisterServices();
        var cut = Render<RestoredDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorityScope, "101:42:False").Add(component => component.AuthorizedStatus, CampaignStatus.Active)
            .Add(component => component.SeedOwner, owner).Add(component => component.SeedDetail, CreateDetail(displayName: "Obsolete participant"))
            .Add(component => component.SeedTagChoices, new List<TagDefinitionDto> { new() { PlayerTagId = 88, Name = "Other club tag", Color = "#0D6EFD", LifecycleStatus = LifecycleStatus.Active } }));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Markup.ShouldNotContain("Obsolete participant");
        cut.Markup.ShouldNotContain("Other club tag");
        _ = Services.GetRequiredService<ICampaignParticipantQueryService>().Received(1).GetParticipantDetailAsync(
            Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true)]
    [InlineData(false)]
    public void DrawerIgnoresOldAuthorityMutationCompletionWithoutClearingNewMutation(bool obsoleteSuccess)
    {
        var oldMutation = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>();
        var newMutation = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>();
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canAddNote: true)))));
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(oldMutation.Task, newMutation.Task);
        RegisterServices(query, notes);
        var refreshes = 0;
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorityScope, "101:42:True")
            .Add(component => component.OnDataChanged, EventCallback.Factory.Create(this, () => refreshes++)));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Old club note");
        FindButtonByText(cut, "Save note").Click();

        cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "101:43:True"));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("New club note");
        FindButtonByText(cut, "Save note").Click();
        oldMutation.SetResult(obsoleteSuccess
            ? new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(1))
            : new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.Forbidden("Old authority denied")));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Cancel").HasAttribute("disabled").ShouldBeTrue());
        cut.Markup.ShouldNotContain("Old authority denied");
        cut.Markup.ShouldNotContain("Note added.");
        refreshes.ShouldBe(0);
        newMutation.SetResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(2)));
        cut.WaitForAssertion(() => refreshes.ShouldBe(1));
        cut.Markup.ShouldContain("Note added.");
    }

    [Fact]
    public void DrawerDiscardsOldMutationRefreshAfterParticipantChanges()
    {
        var oldRefresh = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>();
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canAddNote: true)))),
                oldRefresh.Task,
                Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(assignmentId: 302, displayName: "New participant"))));
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(NoteSuccess(1))));
        RegisterServices(query, notes);
        var refreshes = 0;
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.OnDataChanged, EventCallback.Factory.Create(this, () => refreshes++)));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Saved on previous participant");
        FindButtonByText(cut, "Save note").Click();
        cut.WaitForAssertion(() => { _ = query.Received(2).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()); });
        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("New participant"));
        oldRefresh.SetResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(displayName: "Obsolete refreshed participant")));
        cut.WaitForAssertion(() => cut.Find("h2").TextContent.ShouldBe("New participant"));
        cut.Markup.ShouldNotContain("Obsolete refreshed participant");
        cut.Markup.ShouldNotContain("Note added.");
        refreshes.ShouldBe(0);
    }

    [Fact]
    public void DrawerKeepsCloseRejectionAndCopyableDraftVisibleWhileIdentityRefreshIsPending()
    {
        const string Draft = "Keep this observation after the campaign closes.";
        const string Rejection = "Closed campaigns are read-only and cannot accept evaluation notes.";
        var pending = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canAddNote: true)))), pending.Task);
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.Conflict(Rejection))));
        RegisterServices(query, notes);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorizedStatus, CampaignStatus.Active));
        try
        {
            cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").HasAttribute("disabled").ShouldBeFalse());
            FindButtonByText(cut, "Add note").Click();
            cut.Find("#participant-drawer-note-content").Input(Draft);
            FindButtonByText(cut, "Save note").Click();
            cut.WaitForAssertion(() => cut.Find(".participant-drawer-mutation-error").TextContent.ShouldBe(Rejection));
            cut.Markup.ShouldContain("Loading participant details");
            cut.Render(parameters => parameters.Add(component => component.AuthorizedStatus, CampaignStatus.Closed));
            cut.WaitForAssertion(() => cut.Find(".participant-drawer-readonly-note").TextContent.ShouldContain("Read-only — campaign is closed."));
            cut.Find(".participant-drawer-mutation-error").TextContent.ShouldBe(Rejection);
            cut.Find("#drawer-retained-draft").GetAttribute("value").ShouldBe(Draft);
            cut.Find("#drawer-retained-draft").HasAttribute("readonly").ShouldBeTrue();
            cut.Find("#drawer-retained-draft").HasAttribute("disabled").ShouldBeFalse();
            cut.Markup.ShouldContain("Loading participant details");
            pending.Task.IsCompleted.ShouldBeFalse();
            _ = notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input.Content == Draft && input.PlayerCampaignAssignmentId == 301), Arg.Any<CancellationToken>());
        }
        finally
        {
            pending.TrySetResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(campaignStatus: CampaignStatus.Closed)));
        }
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Loading participant details"));
        cut.Find(".participant-drawer-mutation-error").TextContent.ShouldBe(Rejection);
        cut.Find("#drawer-retained-draft").GetAttribute("value").ShouldBe(Draft);
    }

    [Fact]
    public void DrawerDisablesCaptureImmediatelyWhenAuthorizedLifecycleClosesBeforeDetailReturns()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>();
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(true, true)))), pending.Task);
        RegisterServices(query);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.AuthorizedStatus, CampaignStatus.Active));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        cut.Render(parameters => parameters.Add(component => component.AuthorizedStatus, CampaignStatus.Closed));
        cut.FindAll("textarea").ShouldBeEmpty();
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Add note", StringComparison.Ordinal) || string.Equals(button.TextContent.Trim(), "Apply", StringComparison.Ordinal));
        pending.SetResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(campaignStatus: CampaignStatus.Closed, capabilities: MutationCapabilities(true, true))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only"));
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Add note", StringComparison.Ordinal) || string.Equals(button.TextContent.Trim(), "Apply", StringComparison.Ordinal));
    }

    [Fact]
    public void DrawerGuardsDraftMovementUntilDiscardIsExplicit()
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canAddNote: true)))));
        RegisterServices(query);
        var closed = false;
        var cut = Render<CampaignParticipantDrawerComponent>(p => p.Add(c => c.CampaignId, 10).Add(c => c.ParticipantId, 301)
            .Add(c => c.OnClose, () => closed = true));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Unsubmitted observation");
        cut.Find("button[aria-label='Close participant details']").Click();
        closed.ShouldBeFalse();
        cut.WaitForAssertion(() => FindButtonByText(cut, "Keep working").ShouldNotBeNull());
        FindButtonByText(cut, "Keep working").Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Unsubmitted observation");
        cut.Find("button[aria-label='Close participant details']").Click();
        FindButtonByText(cut, "Discard and leave").Click();
        closed.ShouldBeTrue();
    }

    [Fact]
    public void DrawerUnknownSubmissionRetainsOriginalOperationAndPayloadForReplay()
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canAddNote: true)))));
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var inputs = new List<AddEvaluationNoteInput>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<AddEvaluationNoteInput>());
            return inputs.Count == 1 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost response"))
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(1, Guid.NewGuid(),
                    new(inputs[^1].OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))));
        });
        RegisterServices(query, notes);
        var cut = Render<CampaignParticipantDrawerComponent>(p => p.Add(c => c.CampaignId, 10).Add(c => c.ParticipantId, 301));
        cut.WaitForAssertion(() => FindButtonByText(cut, "Add note").ShouldNotBeNull());
        FindButtonByText(cut, "Add note").Click();
        cut.Find("textarea").Input("Keep original payload");
        FindButtonByText(cut, "Save note").Click();
        cut.WaitForAssertion(() => cut.FindAll("button").Any(button => button.TextContent.Contains("Recover original operation", StringComparison.Ordinal)).ShouldBeTrue());
        FindButtonByText(cut, "Recover original operation").HasAttribute("disabled").ShouldBeFalse();
        FindButtonByText(cut, "Recover original operation").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawerReloadRestoresCopyableOriginalNoteAndPreservesItAfterExpiryRejection(bool editing)
    {
        const string RetainedText = "  Keep this original observation.\nSecond line.";
        var original = CreateNote(canEdit: true);
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(notes: [original], capabilities: MutationCapabilities(canAddNote: true)))));
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var rejection = new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.Conflict("The 24-hour recovery window has expired."));
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(rejection));
        notes.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(rejection));
        RegisterServices(query, notes);
        var module = JSInterop.SetupModule(DrawerModulePath);
        module.SetupVoid("protectNavigation", _ => true).SetVoidResult();
        var read = module.Setup<string?>("readOperation", _ => true);
        var operationId = Guid.CreateVersion7();
        EvaluationOperationInput input = editing
            ? new EditEvaluationNoteInput { NoteId = original.NoteId, Content = RetainedText, ExpectedVersion = original.Version, OperationId = operationId }
            : new AddEvaluationNoteInput { PlayerCampaignAssignmentId = 301, Content = RetainedText, OperationId = operationId };
        var stored = JsonSerializer.Serialize(new { Kind = input.GetType().Name, Payload = JsonSerializer.Serialize(input, input.GetType()) });
        var cut = Render<CampaignParticipantDrawerComponent>(p => p.Add(c => c.CampaignId, 10).Add(c => c.ParticipantId, 301));
        cut.WaitForAssertion(() => read.Invocations.Count.ShouldBe(1));
        FindButtonByText(cut, "Add note").HasAttribute("disabled").ShouldBeTrue();
        read.SetResult(stored);
        var selector = editing ? "#participant-drawer-note-edit-1" : "#participant-drawer-note-content";
        cut.WaitForAssertion(() => cut.Find(selector).GetAttribute("value").ShouldBe(RetainedText));
        cut.Find(selector).HasAttribute("readonly").ShouldBeTrue();
        cut.Find(selector).HasAttribute("disabled").ShouldBeFalse();
        FindButtonByText(cut, "Recover original operation").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("24-hour recovery window has expired"));
        cut.Find(selector).GetAttribute("value").ShouldBe(RetainedText);
        cut.FindAll("button").Any(button => button.TextContent.Contains("Recover original operation", StringComparison.Ordinal)).ShouldBeFalse();
        if (editing)
        {
            _ = notes.Received(1).EditAsync(Arg.Is<EditEvaluationNoteInput>(value => value.OperationId == operationId && value.Content == RetainedText && value.ExpectedVersion == original.Version), Arg.Any<CancellationToken>());
            _ = notes.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        }
        else
        {
            _ = notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(value => value.OperationId == operationId && value.Content == RetainedText && value.PlayerCampaignAssignmentId == 301), Arg.Any<CancellationToken>());
            _ = notes.DidNotReceive().EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawerNeverShowsPreviousPlayersEvidenceWhileNewEvidenceIsDelayedOrFailsAsync(bool failure)
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.Arg<GetCampaignParticipantDetailInput>().PlayerCampaignAssignmentId;
            return Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(assignmentId: id,
                displayName: id == 301 ? "Previous player" : "Replacement player",
                notes: id == 301 ? [CreateNote(content: "Previous shared observation")] : [],
                tags: id == 301 ? [CreateTag(tagName: "Previous trait")] : [])));
        });
        RegisterServices(query);
        var evidence = Services.GetRequiredService<ICampaignEvaluationQueryService>();
        var notes = new TaskCompletionSource<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applications = new TaskCompletionSource<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        evidence.GetNotesAsync(Arg.Is<GetEvaluationHistoryInput>(i => i.PlayerCampaignAssignmentId == 302), Arg.Any<CancellationToken>()).Returns(notes.Task);
        evidence.GetApplicationsAsync(Arg.Is<GetEvaluationHistoryInput>(i => i.PlayerCampaignAssignmentId == 302), Arg.Any<CancellationToken>()).Returns(applications.Task);
        var cut = Render<CampaignParticipantDrawerComponent>(p => p.Add(c => c.CampaignId, 10).Add(c => c.ParticipantId, 301));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Previous shared observation"));
        cut.Render(p => p.Add(c => c.ParticipantId, 302));
        await cut.WaitForAssertionAsync(() => cut.Find("#participant-drawer-heading").TextContent.ShouldBe("Replacement player"));
        cut.Markup.ShouldNotContain("Previous shared observation");
        cut.Markup.ShouldNotContain("Previous trait");
        notes.SetResult(failure ? new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(ServiceProblem.ServerError("Replacement notes failed"))
            : new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([CreateNote(content: "Replacement evidence")], null)));
        applications.SetResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>([], null)));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(failure ? "Replacement notes failed" : "Replacement evidence"));
        cut.Markup.ShouldNotContain("Previous shared observation");
        cut.Markup.ShouldNotContain("Previous trait");
    }

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

        var evidence = Substitute.For<ICampaignEvaluationQueryService>();
        evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(
                new EvaluationHistoryPage<CampaignParticipantNoteDto>(_evidence.GetValueOrDefault(call.Arg<GetEvaluationHistoryInput>().PlayerCampaignAssignmentId).Notes ?? [], null))));
        evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(
                new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>(_evidence.GetValueOrDefault(call.Arg<GetEvaluationHistoryInput>().PlayerCampaignAssignmentId).Applications ?? [], null))));
        Services.AddSingleton(evidence);
        Services.AddSingleton(queryService);
        Services.AddSingleton(noteService);
        Services.AddSingleton(tagApplicationService);
        Services.AddSingleton(tagDefinitionQueryService);
        var placementContext = Substitute.For<IEffectivePlacementQueryService>();
        placementContext.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.NotFound())));
        placementContext.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.NotFound())));
        Services.AddSingleton(placementContext);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private CampaignParticipantDetailDto CreateDetail(
        long assignmentId = 301,
        string displayName = "Avery Johnson",
        int? tryoutNumber = 14,
        CampaignParticipantTeamSummaryDto? team = null,
        bool includeTeam = true,
        IReadOnlyList<CampaignParticipantNoteDto>? notes = null,
        IReadOnlyList<CampaignParticipantTagApplicationDto>? tags = null,
        CampaignParticipantCapabilitiesDto? capabilities = null,
        CampaignStatus campaignStatus = CampaignStatus.Active)
    {
        _evidence[assignmentId] = (notes ?? [], tags ?? []);
        return new(
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
        Capabilities: capabilities ?? new CampaignParticipantCapabilitiesDto(
            CanEditPlacement: false,
            CanAddNote: false,
            CanApplyTag: false,
            CanArchiveTagDefinitions: false));
    }

    private static EvaluationMutationReceipt Receipt() => new(Guid.CreateVersion7(), 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24));

    private static EvaluationNoteMutationSuccess NoteSuccess(long id) => new(id, Guid.NewGuid(), Receipt());

    private static CampaignTagApplicationMutationSuccess TagSuccess(long id) => new(id, 11, false, Receipt());

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
            Version: Guid.NewGuid(),
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
        => cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), text, StringComparison.Ordinal));

    private static CampaignParticipantRosterItem CreateRosterItem() => new(
        PlayerCampaignAssignmentId: 301,
        PlayerId: 7,
        DisplayName: "Avery Johnson",
        GraduationYear: 2032,
        TryoutNumber: 14,
        PlacementOutcome: PlacementOutcome.Undecided,
        Team: null,
        AppliedTags: []);

#pragma warning disable CA1812 // bUnit constructs the test-only restored component through reflection.
    private sealed class RestoredPlacementContext(IEffectivePlacementQueryService queries)
        : Nova.UI.Features.Campaigns.Components.CampaignParticipantPlacementContext(queries)
#pragma warning restore CA1812
    {
        [Parameter] public string? SeedOwner { get; set; }
        [Parameter] public bool SeedError { get; set; }

        protected override void OnInitialized()
        {
            Initialized = true;
            PersistedOwner = SeedOwner ?? $"{Owner}:{CampaignId}:{ParticipantId}:{Status}";
            PersistedError = SeedError;
            if (!SeedError && Status == CampaignStatus.Active)
            {
                PersistedWorking = new(301, 7, "Avery", "Johnson", 2032, 14, LifecycleStatus.Active, Guid.NewGuid(),
                    null, null, null, EffectivePlacementEligibility.NeedsPlacement, PlacementCorrectionReason.None);
            }
            if (!SeedError && Status == CampaignStatus.Closed)
            {
                var decision = new CampaignSavedPlacementDecision(301, 7, 10, 5, 1, PlacementOutcome.NotSelected, null,
                    new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), 101, "Coach Rivera", Guid.NewGuid());
                PersistedClosed = new(301, 7, "Avery", "Johnson", 2032, 14, new PlacementDecisionSource(decision, "Immutable campaign", null));
            }
            base.OnInitialized();
        }
    }

    /// <summary>
    /// A test-only <see cref="CampaignParticipantDrawerComponent"/> subclass that seeds persisted
    /// prerender state before startup initialization runs.
    /// </summary>
#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class RestoredDrawer(
#pragma warning restore CA1812
        ICampaignParticipantQueryService participantQueryService,
        ICampaignEvaluationQueryService evaluationQueryService,
        ICampaignEvaluationNoteService noteService,
        ICampaignTagApplicationService tagApplicationService,
        ITagDefinitionQueryService tagDefinitionQueryService,
        IJSRuntime jsRuntime)
        : CampaignParticipantDrawerComponent(participantQueryService, evaluationQueryService, noteService, tagApplicationService, tagDefinitionQueryService, jsRuntime)
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

        [Parameter]
        public string? SeedOwner { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            Initialized = true;
            PersistedDetail = SeedDetail;
            PersistedDetailError = SeedError;
            PersistedTagChoices = SeedTagChoices;
            PersistedOwner = SeedOwner ?? $"{AuthorityScope}:{CampaignId}:{ParticipantId}:{AuthorizedStatus}";

            return base.OnInitializedAsync();
        }
    }
}
