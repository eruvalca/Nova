using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class CampaignClosedRecordTests : BunitContext
{
    private readonly IEffectivePlacementQueryService _roster = Substitute.For<IEffectivePlacementQueryService>();
    private readonly IPlacementContextQueryService _history = Substitute.For<IPlacementContextQueryService>();
    private readonly ICampaignCloseoutQueryService _activity = Substitute.For<ICampaignCloseoutQueryService>();

    public CampaignClosedRecordTests()
    {
        Services.AddSingleton(_roster);
        Services.AddSingleton(_history);
        Services.AddSingleton(_activity);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _roster.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<ClosedCampaignRosterResult>(Record()));
        _history.GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementContextResult>(new PlacementContextResult(101, null,
                [new(90, 10, "Summer", PlacementOutcome.NotSelected, null, PlacementOutcome.Assigned, "North", "Original member", DateTimeOffset.UnixEpoch)], 80, false)));
        _activity.GetActivityAsync(Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignActivityResult>(new CampaignActivityResult([Record().ClosingEvent])));
    }

    [Fact]
    public void FinalRecordShowsOriginalEvidenceArchiveContextAndIndependentTotals()
    {
        var cut = RenderRecord(new() { Search = "Alex", Outcome = "assigned" });
        cut.Find(".record-summary").TextContent.ShouldContain("3 participants");
        cut.Find(".record-summary").TextContent.ShouldContain("Assigned 1 · Not selected 1 · Withdrawn 1");
        cut.Find(".record-summary").TextContent.ShouldContain("Departed closer");
        var row = cut.Find("tbody tr:not(.group-row)");
        row.TextContent.ShouldContain("Archived player");
        row.TextContent.ShouldContain("Archived team");
        row.TextContent.ShouldContain("Original decision maker");
        row.TextContent.ShouldContain("17");
        row.TextContent.ShouldContain("2030");
        cut.Markup.ShouldContain("1 of 1 matching participants");
        cut.FindAll(".lifecycle-history li").Count.ShouldBe(1);
        _ = _roster.Received(1).GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(x =>
            x.Search == "Alex" && x.LocalOutcome == "assigned" && x.PageSize == 50 && x.SortBy == "closeout"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NativeAndInteractiveDiscoveryPreserveSiblingsAndResetSelection()
    {
        CampaignWorkspaceCloseState? applied = null;
        var cut = RenderRecord(new() { Search = "A & B", Outcome = "assigned", Page = 3, ParticipantId = 101, BeforeEventId = 91 });
        cut.Render(p => p.Add(x => x.OnStateChanged, state => applied = state));
        cut.Find("form").GetAttribute("method").ShouldBe("get");
        cut.FindAll("input[type=hidden]").Select(x => (x.GetAttribute("name"), x.GetAttribute("value")))
            .ShouldBe(new (string?, string?)[] { ("tab", "close"), ("search", "Roster & context"), ("evalSearch", "Evaluate"), ("tag", "1"), ("tag", "2") });
        cut.Find("#closed-outcome option[selected]").GetAttribute("value").ShouldBe("assigned");
        cut.Find("#closed-search").Change(" new search ");
        cut.Find("#closed-outcome").Change("withdrawn");
        cut.Find("form").Submit();
        applied.ShouldBe(new CampaignWorkspaceCloseState { Search = "new search", Outcome = "withdrawn" });
        var previous = cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Previous page", StringComparison.Ordinal)).GetAttribute("href")!;
        previous.ShouldContain("closePage=2");
        previous.ShouldContain("closeOutcome=assigned");
        previous.ShouldNotContain("closeParticipant");
        previous.ShouldNotContain("closeBeforeEventId");
    }

    [Fact]
    public void SelectedHistoryUsesExactParticipantAndClosedScopeWithNativeCursorAndEvaluation()
    {
        var cut = RenderRecord(new() { ParticipantId = 101, BeforeEventId = 100, Page = 4 });
        cut.Find(".participant-history").TextContent.ShouldContain("Not selected → Assigned · North");
        cut.Find(".participant-history").TextContent.ShouldContain("Original member");
        _ = _roster.Received(1).GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(x =>
            x.ParticipantId == 101 && x.Search == null && x.Page == 1 && x.PageSize == 1), Arg.Any<CancellationToken>());
        _ = _history.Received(1).GetContextAsync(Arg.Is<GetPlacementContextInput>(x =>
            x.RequireClosed == true && x.PlayerCampaignAssignmentId == 101 && x.BeforeEventId == 100), Arg.Any<CancellationToken>());
        cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Earlier changes", StringComparison.Ordinal)).GetAttribute("href")!.ShouldContain("closeBeforeEventId=80");
        cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Latest changes", StringComparison.Ordinal)).GetAttribute("href")!.ShouldNotContain("closeBeforeEventId");
        cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Read evaluation", StringComparison.Ordinal)).GetAttribute("href")!.ShouldBe("/campaigns/10?tab=evaluate&evalParticipant=101&returnToClose=true");
    }

    [Theory]
    [InlineData(0, 0, "has no participants")]
    [InlineData(3, 0, "No participants match")]
    [InlineData(60, 60, "beyond the current results")]
    public void EmptyResultsDistinguishCampaignFilterAndStalePage(int all, int matching, string message)
    {
        _roster.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<ClosedCampaignRosterResult>(Record() with { ParticipantCount = all, Summary = new(all, 0, 0, 0, all), Participants = new([], 3, 50, matching) }));
        var cut = RenderRecord(new() { Page = 3, Search = "missing" });
        cut.Markup.ShouldContain(message);
        if (matching > 0)
        {
            cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Go to last available page", StringComparison.Ordinal)).GetAttribute("href")!.ShouldContain("closePage=2");
        }
    }

    [Fact]
    public void ActivityAndHistoryFailuresRetryIndependentlyOfVerifiedRecord()
    {
        _history.GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementContextResult>(ServiceProblem.ServerError("Unavailable")), new ServiceResult<PlacementContextResult>(new PlacementContextResult(101, null, [], null, false)));
        _activity.GetActivityAsync(Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignActivityResult>(ServiceProblem.ServerError("Unavailable")), new ServiceResult<CampaignActivityResult>(new CampaignActivityResult([Record().ClosingEvent])));
        var cut = RenderRecord(new() { ParticipantId = 101 });
        cut.FindAll("tbody tr:not(.group-row)").Count.ShouldBe(1);
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Retry activity", StringComparison.Ordinal)).Click();
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Retry history", StringComparison.Ordinal)).Click();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        _ = _roster.Received(1).GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(x => x.ParticipantId == null), Arg.Any<CancellationToken>());
        _ = _history.Received(2).GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>());
        _ = _activity.Received(2).GetActivityAsync(Arg.Any<GetCampaignActivityInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IntegrityFailureHidesFinalEvidenceAndDoesNotCreateRefreshLoop()
    {
        _roster.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.Conflict("Incomplete", new Dictionary<string, string[]>(StringComparer.Ordinal)
            { [ClosedCampaignRecordErrors.Integrity] = ["Incomplete"] })));
        var reloads = 0;
        var cut = Render<CampaignClosedRecord>(p => p.Add(x => x.CampaignId, 10).Add(x => x.Owner, "owner")
            .Add(x => x.BuildCloseUrl, Url).Add(x => x.OnLifecycleChanged, () => reloads++));
        cut.FindAll("table,.record-summary").ShouldBeEmpty();
        cut.Markup.ShouldContain("could not be verified");
        reloads.ShouldBe(0);
        _roster.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.Conflict("Reopened")));
        cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Retry record", StringComparison.Ordinal)).Click();
        reloads.ShouldBe(1);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshFailureDefersHistoryFocusUntilRecordRetryRendersHeadingAsync(bool delayed)
    {
        var cut = RenderRecord(new() { ParticipantId = 101 });
        cut.Find("#closed-history-heading").TextContent.ShouldBe("Participant decision history");
        var pending = new TaskCompletionSource<ServiceResult<ClosedCampaignRosterResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.ServerError("Unavailable"));
        _roster.GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(x => x.ParticipantId == null), Arg.Any<CancellationToken>())
            .Returns(delayed ? pending.Task : Task.FromResult(failure));
        try
        {
            cut.Render(p => p.Add(x => x.RefreshGeneration, 1));
            if (delayed)
            {
                cut.Markup.ShouldContain("Loading final campaign record");
                cut.FindAll("#closed-history-heading").ShouldBeEmpty();
                JSInterop.Invocations.ShouldNotContain(call => string.Equals(call.Identifier, "Blazor._internal.domWrapper.focus", StringComparison.Ordinal));
                pending.SetResult(failure);
            }
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Retry record"));
            cut.FindAll("#closed-history-heading").ShouldBeEmpty();
            JSInterop.Invocations.ShouldNotContain(call => string.Equals(call.Identifier, "Blazor._internal.domWrapper.focus", StringComparison.Ordinal));

            _roster.GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(x => x.ParticipantId == null), Arg.Any<CancellationToken>())
                .Returns(new ServiceResult<ClosedCampaignRosterResult>(Record()));
            await cut.FindAll("button").Single(b => string.Equals(b.TextContent, "Retry record", StringComparison.Ordinal)).ClickAsync(new());
            cut.Find(".participant-history").TextContent.ShouldContain("Original member");
            await cut.WaitForAssertionAsync(() => JSInterop.Invocations.Count(call => string.Equals(call.Identifier, "Blazor._internal.domWrapper.focus", StringComparison.Ordinal)).ShouldBe(1));
            _ = _history.Received(2).GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>());
        }
        finally { pending.TrySetResult(failure); }
    }

    [Fact]
    public async Task RemovingSelectionDuringRefreshDiscardsPendingHistoryFocusAsync()
    {
        var cut = RenderRecord(new() { ParticipantId = 101 });
        cut.Find("#closed-history-heading").TextContent.ShouldBe("Participant decision history");
        var pending = new TaskCompletionSource<ServiceResult<ClosedCampaignRosterResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _roster.GetClosedCampaignRosterAsync(Arg.Is<GetClosedCampaignRosterInput>(x => x.ParticipantId == null), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        try
        {
            cut.Render(p => p.Add(x => x.RefreshGeneration, 1));
            cut.Render(p => p.Add(x => x.State, new CampaignWorkspaceCloseState()));
            pending.SetResult(new ServiceResult<ClosedCampaignRosterResult>(Record()));
            await cut.WaitForAssertionAsync(() => cut.Find(".record-summary").TextContent.ShouldContain("Departed closer"));
            cut.FindAll("#closed-history-heading").ShouldBeEmpty();
            JSInterop.Invocations.ShouldNotContain(call => string.Equals(call.Identifier, "Blazor._internal.domWrapper.focus", StringComparison.Ordinal));
            cut.Render(p => p.Add(x => x.State, new CampaignWorkspaceCloseState { ParticipantId = 101 }));
            cut.Find(".participant-history").TextContent.ShouldContain("Original member");
            await cut.WaitForAssertionAsync(() => JSInterop.Invocations.Count(call => string.Equals(call.Identifier, "Blazor._internal.domWrapper.focus", StringComparison.Ordinal)).ShouldBe(1));
        }
        finally { pending.TrySetResult(new ServiceResult<ClosedCampaignRosterResult>(Record())); }
    }

    [Fact]
    public async Task LateRecordFromPreviousOwnerCannotReplaceNewOwnerAsync()
    {
        var pending = new TaskCompletionSource<ServiceResult<ClosedCampaignRosterResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _roster.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task, Task.FromResult(new ServiceResult<ClosedCampaignRosterResult>(Record() with { Campaign = new(10, "New owner", CampaignStatus.Closed, new(20, "Season")) })));
        var cut = RenderRecord();
        cut.Render(p => p.Add(x => x.Owner, "new-owner"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("New owner"));
        pending.SetResult(new ServiceResult<ClosedCampaignRosterResult>(Record()));
        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.Markup.ShouldContain("New owner");
        cut.Markup.ShouldNotContain("Summer · Season");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingPersistedSuccessOrFailureSkipsDuplicateStartupReads(bool failure)
    {
        var cut = Render<RestoredRecord>(p => p.Add(x => x.CampaignId, 10).Add(x => x.Owner, "owner")
            .Add(x => x.RestoreFailure, failure).Add(x => x.BuildCloseUrl, Url));
        cut.Markup.ShouldContain(failure ? "Restored failure" : "Departed closer");
        _roster.ReceivedCalls().ShouldBeEmpty();
        _history.ReceivedCalls().ShouldBeEmpty();
        _activity.ReceivedCalls().ShouldBeEmpty();
        cut.Render(p => p.Add(x => x.RefreshGeneration, 1));
        _ = _roster.Received(1).GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<CampaignClosedRecord> RenderRecord(CampaignWorkspaceCloseState? state = null)
        => Render<CampaignClosedRecord>(p => p.Add(x => x.CampaignId, 10).Add(x => x.Owner, "owner")
            .Add(x => x.State, state ?? new()).Add(x => x.BuildCloseUrl, Url)
            .Add(x => x.BuildEvaluationUrl, id => $"/campaigns/10?tab=evaluate&evalParticipant={id}&returnToClose=true"));

    private static string Url(CampaignWorkspaceCloseState state)
        => state.Apply("/campaigns/10?tab=close&search=Roster%20%26%20context&evalSearch=Evaluate&tag=1&tag=2");

    private static ClosedCampaignRosterResult Record() => new(new(10, "Summer", CampaignStatus.Closed, new(20, "Season")),
        new([new(101, 30, "Alex", "Player", 2030, 17,
            new(new(101, 30, 10, 20, 1, PlacementOutcome.Assigned, 40, DateTimeOffset.UnixEpoch, 1, "Original decision maker", Guid.NewGuid()), "Summer", new(40, "North")))
        { PlayerLifecycleStatus = LifecycleStatus.Archived, TeamLifecycleStatus = LifecycleStatus.Archived }], 1, 50, 1))
    { ParticipantCount = 3, Summary = new(1, 1, 1, 0, 3), ClosingEvent = new(100, CampaignLifecycleEventType.Closed, DateTimeOffset.UnixEpoch, 1, "Departed closer") };

#pragma warning disable CA1812 // bUnit constructs the prerender-state fixture through reflection.
    internal sealed class RestoredRecord(IEffectivePlacementQueryService roster, IPlacementContextQueryService history, ICampaignCloseoutQueryService activity)
        : CampaignClosedRecord(roster, history, activity)
#pragma warning restore CA1812
    {
        [Parameter] public bool RestoreFailure { get; set; }
        protected override void OnInitialized()
        {
            var scope = $"{Owner}:{CampaignId}:{RefreshGeneration}";
            RecordKey = scope + ":::1";
            HistoryKey = scope + "::";
            ActivityKey = scope;
            RecordError = RestoreFailure ? "Restored failure" : null;
            Record = RestoreFailure ? null : CampaignClosedRecordTests.Record();
            Activity = new([]);
        }
    }
}
