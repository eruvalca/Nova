using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
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

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        cut.Find("#participant-drawer-close").Click();

        closed.ShouldBeTrue();
        JSInterop.VerifyInvoke("novaCampaignParticipantDrawerClose")
            .Arguments.ShouldBe(new object?[] { "roster-row-301" });
    }

    [Fact]
    public void Drawer_InvokesOnClose_WhenEscapePressed()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        cut.Find("aside.participant-drawer").TriggerEvent("onkeydown",
            new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        closed.ShouldBeTrue();
        JSInterop.VerifyInvoke("novaCampaignParticipantDrawerClose")
            .Arguments.ShouldBe(new object?[] { "roster-row-301" });
    }

    [Fact]
    public void Drawer_InvokesOnClose_WhenBackdropClicked()
    {
        var closed = false;
        var onClose = EventCallback.Factory.Create(this, () => closed = true);
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301)
            .Add(component => component.OnClose, onClose));

        cut.Find(".participant-drawer-backdrop").Click();

        closed.ShouldBeTrue();
        JSInterop.VerifyInvoke("novaCampaignParticipantDrawerClose")
            .Arguments.ShouldBe(new object?[] { "roster-row-301" });
    }

    // ── Focus trap management ───────────────────────────────────────────────────

    [Fact]
    public void Drawer_InstallsFocusTrap_OnFirstRender()
    {
        RegisterServices();

        Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        JSInterop.VerifyInvoke("novaCampaignParticipantDrawerOpen")
            .Arguments.ShouldBe(new object?[] { ".participant-drawer", "participant-drawer-close" });
    }

    [Fact]
    public void Drawer_DoesNotReinstallFocusTrap_WhenParticipantParameterChanges()
    {
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));

        JSInterop.Invocations.Count(invocation => invocation.Identifier == "novaCampaignParticipantDrawerOpen")
            .ShouldBe(1);
    }

    [Fact]
    public async Task Drawer_RemovesFocusTrapWithoutRestoringFocus_WhenDisposed()
    {
        RegisterServices();

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10)
            .Add(component => component.ParticipantId, 301));

        await ((IAsyncDisposable)cut.Instance).DisposeAsync();

        JSInterop.VerifyInvoke("novaCampaignParticipantDrawerDispose");
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "novaCampaignParticipantDrawerClose");
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RegisterServices(ICampaignParticipantQueryService? queryService = null)
    {
        if (queryService is null)
        {
            queryService = Substitute.For<ICampaignParticipantQueryService>();
            queryService.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail())));
        }

        Services.AddSingleton(queryService);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CampaignParticipantDetailDto CreateDetail(
        long assignmentId = 301,
        string displayName = "Avery Johnson",
        int? tryoutNumber = 14,
        CampaignParticipantTeamSummaryDto? team = null,
        bool includeTeam = true,
        IReadOnlyList<CampaignParticipantNoteDto>? notes = null,
        IReadOnlyList<CampaignParticipantTagApplicationDto>? tags = null) => new(
        PlayerCampaignAssignmentId: assignmentId,
        PlayerId: 7,
        DisplayName: displayName,
        GraduationYear: 2032,
        TryoutNumber: tryoutNumber,
        PlacementOutcome: PlacementOutcome.Assigned,
        Team: includeTeam ? team ?? new CampaignParticipantTeamSummaryDto(21, "Blue") : null,
        CreatedAt: new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero),
        ModifiedAt: new DateTimeOffset(2026, 5, 3, 14, 30, 0, TimeSpan.Zero),
        CampaignStatus: CampaignStatus.Active,
        ConcurrencyToken: Guid.NewGuid(),
        Notes: notes ?? [],
        AppliedTags: tags ?? [],
        Capabilities: new CampaignParticipantCapabilitiesDto(
            CanEditPlacement: false,
            CanAddNote: false,
            CanApplyTag: false,
            CanArchiveTagDefinitions: false));

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
        IJSRuntime jsRuntime)
        : CampaignParticipantDrawerComponent(participantQueryService, jsRuntime)
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

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            Initialized = true;
            PersistedDetail = SeedDetail;
            PersistedDetailError = SeedError;

            return base.OnInitializedAsync();
        }
    }
}
