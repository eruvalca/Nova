using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Evaluation lookup, independent evidence regions, lifecycle and receipt recovery transitions.</summary>
public sealed partial class CampaignEvaluationPanelTests : BunitContext
{
    private readonly ICampaignParticipantQueryService _participants = Substitute.For<ICampaignParticipantQueryService>();
    private readonly IEffectivePlacementQueryService _placements = Substitute.For<IEffectivePlacementQueryService>();
    private readonly ICampaignEvaluationQueryService _evidence = Substitute.For<ICampaignEvaluationQueryService>();
    private readonly ICampaignEvaluationNoteService _notes = Substitute.For<ICampaignEvaluationNoteService>();
    private readonly ICampaignTagApplicationService _tags = Substitute.For<ICampaignTagApplicationService>();
    private readonly TabStorage _storage = new();

    public CampaignEvaluationPanelTests()
    {
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(call.Arg<GetCampaignParticipantDetailInput>().PlayerCampaignAssignmentId))));
        _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results(call.Arg<GetCampaignEffectivePlacementsInput>().Page ?? 1))));
        _placements.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                new(10, "Tryouts", CampaignStatus.Closed, new(5, "Autumn")), new([], 1, 20, 0)))));
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note()], null))));
        _evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>(
                [new(1, 11, "Strong", "#65743A", false, "Coach Rivera", DateTimeOffset.UtcNow, false)], null))));
        _evidence.GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<EvaluationTagChoice>>(new EvaluationTagChoice[] { new(11, "Strong", "#65743A", 1) })));
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(Success(call.Arg<AddEvaluationNoteInput>()))));
        Services.AddSingleton(_participants);
        Services.AddSingleton(_placements);
        Services.AddSingleton(_evidence);
        Services.AddSingleton(_notes);
        Services.AddSingleton(_tags);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IJSRuntime>(new EvaluationJsRuntime(JSInterop.JSRuntime, _storage));
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        await _storage.DisposeAsync();
    }

    [Fact]
    public void NativeFinderFormPreservesCloseCorrectionContext()
    {
        var close = new CampaignWorkspaceCloseState { Search = "A & B", Blocker = "outcomes", Page = 3 };
        var cut = Panel(new());
        cut.Render(parameters => parameters.Add(component => component.PreserveCloseContext, url => close.Apply(url, returnToClose: true)));
        cut.Find("input[name=closeSearch]").GetAttribute("value").ShouldBe("A & B");
        cut.Find("input[name=closeBlocker]").GetAttribute("value").ShouldBe("outcomes");
        cut.Find("input[name=closePage]").GetAttribute("value").ShouldBe("3");
        cut.Find("input[name=returnToClose]").GetAttribute("value").ShouldBe("true");
    }

    [Fact]
    public void BlankLookupDoesNotDiscoverOrSelectPlayer()
    {
        var cut = Panel(new());
        cut.Find("#evaluation-search").GetAttribute("value").ShouldBeEmpty();
        cut.Markup.ShouldContain("Start with a name or tryout number");
        cut.FindAll(".evaluation-sheet").ShouldBeEmpty();
        _ = _placements.DidNotReceive().GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
        _ = _participants.DidNotReceive().GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ExactNumberLookupRequiresDeliberateSelectionAndRequestsRelevanceBeforePaging()
    {
        var cut = Panel(new() { Search = "42" });
        cut.WaitForAssertion(() => cut.FindAll("a[data-eval-result]").Count.ShouldBe(1));
        cut.Markup.ShouldContain("1 player matches");
        cut.FindAll(".evaluation-sheet").ShouldBeEmpty();
        cut.Find("a[data-eval-result]").GetAttribute("href")!.ShouldContain("evalParticipant=301");
        _ = _placements.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(i => i.Search == "42" && i.SortBy == "searchRelevance" && i.Page == 1 && i.PageSize == 20), Arg.Any<CancellationToken>());
        _ = _participants.DidNotReceive().GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatedCurrentQuerySubmissionsPreserveInFlightFinderAndChangedQueryNavigatesAsync()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignEffectivePlacementsResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestToken = CancellationToken.None;
        _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(call => { requestToken = call.Arg<CancellationToken>(); return pending.Task; });
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var state = new CampaignWorkspaceEvaluationState { Search = "42" };
        navigation.NavigateTo(CampaignWorkspaceUrlState.BuildEvaluationLookupUrl(10, state, new() { Search = "Roster query", Page = 4 }, 999));
        var historyCount = navigation.History.Count;
        var cut = Panel(state);
        try
        {
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Finding players"));
            var debounce = cut.Find("#evaluation-search").InputAsync(new ChangeEventArgs { Value = "42" });
            // Find and Enter share the form submit boundary; neither may restart its current URL.
            await cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
            await cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
            await debounce;
            navigation.History.Count.ShouldBe(historyCount);
            _ = _placements.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
            requestToken.CanBeCanceled.ShouldBeTrue();
            requestToken.IsCancellationRequested.ShouldBeFalse();
            pending.Task.IsCompleted.ShouldBeFalse();
            cut.Markup.ShouldContain("Finding players");
            var changedDebounce = cut.Find("#evaluation-search").InputAsync(new ChangeEventArgs { Value = "Jordan" });
            await cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
            await changedDebounce;
            navigation.History.Count.ShouldBe(historyCount + 1);
            navigation.Uri.ShouldContain("evalSearch=Jordan");
            navigation.Uri.ShouldContain("search=Roster%20query");
            navigation.Uri.ShouldContain("page=4");
        }
        finally
        {
            pending.TrySetResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results()));
        }
    }

    [Fact]
    public void ExplicitFinderRetryReloadsFailedCurrentQueryWithoutNavigating()
    {
        _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("Finder unavailable."))),
                Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results())));
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var state = new CampaignWorkspaceEvaluationState { Search = "42" };
        navigation.NavigateTo(CampaignWorkspaceUrlState.BuildEvaluationLookupUrl(10, state, new() { Search = "Roster query", Page = 4 }, 999));
        var historyCount = navigation.History.Count;
        var cut = Panel(state);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Finder unavailable."));
        cut.Find("form").Submit();
        _ = _placements.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
        Button(cut, "Retry search").Click();
        cut.WaitForAssertion(() => cut.FindAll("a[data-eval-result]").Count.ShouldBe(1));
        cut.Markup.ShouldNotContain("Finder unavailable.");
        _ = _placements.Received(2).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Search == "42"), Arg.Any<CancellationToken>());
        navigation.History.Count.ShouldBe(historyCount);
    }

    [Fact]
    public void LookupPagingPreservesSeparateRosterFiltersAndClearsSelection()
    {
        _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results(2, 45))));
        var cut = Panel(new() { Search = "Jordan", Page = 2 });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Page 2 of 3"));
        var next = cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Next page", StringComparison.Ordinal)).GetAttribute("href")!;
        next.ShouldContain("evalPage=3");
        next.ShouldContain("evalSearch=Jordan");
        next.ShouldContain("search=Roster%20query");
        next.ShouldContain("page=4");
        next.ShouldContain("participant=999");
        next.ShouldNotContain("evalParticipant=");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void FinderDisambiguatesMatchingNamesWithCampaignLocalPlacementContext(bool closed)
    {
        var decision = new CampaignSavedPlacementDecision(301, 7, 10, 5, 1, PlacementOutcome.Assigned, 21,
            DateTimeOffset.UtcNow, 101, "Coach Rivera", Guid.NewGuid());
        var assigned = Results().Participants.Items[0] with
        {
            LocalDecision = decision,
            LocalTeam = new(21, "Falcons"),
            EffectiveTeam = new(22, "Inherited team"),
            EffectiveDecision = new(decision with { CampaignId = 9, TeamId = 22 }, "Previous campaign", new(22, "Inherited team")),
        };
        var notSelected = assigned with { PlayerCampaignAssignmentId = 302, PlayerId = 8, LocalDecision = decision with { PlayerCampaignAssignmentId = 302, PlayerId = 8, Outcome = PlacementOutcome.NotSelected, TeamId = null }, LocalTeam = null };
        _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results() with { Participants = new([assigned, notSelected], 1, 20, 2) })));
        _placements.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                new(10, "Tryouts", CampaignStatus.Closed, new(5, "Autumn")), new([
                    new(301, 7, "Jordan", "Lee", 2030, 42, new(decision, "Tryouts", new(21, "Owls"))),
                    new(302, 8, "Jordan", "Lee", 2030, 42, new(decision with { PlayerCampaignAssignmentId = 302, PlayerId = 8, Outcome = PlacementOutcome.NotSelected, TeamId = null }, "Tryouts", null)),
                ], 1, 20, 2)))));
        var cut = Panel(new() { Search = "Jordan Lee" }, closed ? CampaignStatus.Closed : CampaignStatus.Active);
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-result-placement").Select(element => element.TextContent.Trim())
            .ShouldBe([closed ? "Assigned · Owls" : "Assigned · Falcons", "Not selected"]));
        cut.Markup.ShouldNotContain("Inherited team");
        cut.FindAll("a[data-eval-result]").Count.ShouldBe(2);
        cut.FindAll(".evaluation-sheet").ShouldBeEmpty();
        _ = _participants.DidNotReceive().GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AuthorizedDeepLinkLoadsIdentityWithoutRequiringCurrentSearchResults()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find("#evaluation-player-heading").TextContent.ShouldContain("#42 Jordan Lee"));
        cut.FindAll("a[data-eval-result]").ShouldBeEmpty();
        cut.FindAll(".evaluation-sequence a").ShouldBeEmpty();
        _ = _participants.Received(1).GetParticipantDetailAsync(Arg.Is<GetCampaignParticipantDetailInput>(i => i.PlayerCampaignAssignmentId == 301), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IdentityFocusRetriesAfterHeadingMountsAndStopsAfterSuccess()
    {
        _storage.FocusAvailable = false;
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => _storage.FocusOwners.ShouldNotBeEmpty());
        cut.Find("#evaluation-player-heading").TextContent.ShouldContain("#42 Jordan Lee");
        var attemptsBeforeMount = _storage.FocusOwners.Count;
        _storage.FocusAvailable = true;
        cut.Render();
        cut.WaitForAssertion(() => _storage.FocusOwners.Count.ShouldBe(attemptsBeforeMount + 1));
        _storage.FocusOwners.ShouldAllBe(owner => string.Equals(owner, "coach:club-1:False:10:301", StringComparison.Ordinal));
        cut.Render();
        _storage.FocusOwners.Count.ShouldBe(attemptsBeforeMount + 1);
        cut.Find("#evaluation-note").Input("Capture remains interactive after focus");
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
    }

    [Fact]
    public void PlayerHandoffsRetainLookupAndRosterReturnContext()
    {
        SetEvaluationCapabilities(true, true, true);
        var cut = Panel(new() { Search = "Jordan", Page = 2, ParticipantId = 301, RosterLanding = true });
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-place").Count.ShouldBe(1));
        var place = cut.Find(".evaluation-place").GetAttribute("href")!;
        place.ShouldContain("tab=place");
        place.ShouldContain("placementParticipant=301");
        place.ShouldContain("evalSearch=Jordan");
        place.ShouldContain("evalPage=2");
        place.ShouldContain("evalParticipant=301");
        place.ShouldContain("participant=999");
        place.ShouldContain("returnToEvaluation=true");
        var back = cut.Find(".evaluation-back").GetAttribute("href")!;
        back.ShouldContain("evalSearch=Jordan");
        back.ShouldNotContain("evalParticipant=");
        var another = cut.Find(".evaluation-another").GetAttribute("href")!;
        another.ShouldNotContain("evalSearch=");
        another.ShouldNotContain("evalPage=");
        another.ShouldContain("search=Roster%20query");
    }

    [Fact]
    public void SaveNoteRequiresExplicitActionAndKeepsSelectedIdentityOpen()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("Tracks runners well.");
        _ = _notes.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Jordan Lee");
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBeEmpty();
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(i => i.Content == "Tracks runners well." && i.PlayerCampaignAssignmentId == 301 && i.OperationId != Guid.Empty), Arg.Any<CancellationToken>());
        _storage.Writes.Any(s => s.Contains("Tracks runners well.", StringComparison.Ordinal) && s.Contains("OperationId", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageWriteFailurePreventsDispatchAndKeepsDraft(bool throwException)
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("Must remain copyable.");
        _storage.FailWrites = !throwException;
        _storage.ThrowWrites = throwException;
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain(throwException ? "Nothing new was submitted" : "nothing new was submitted"));
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Must remain copyable.");
        _ = _notes.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UnknownSubmissionRetriesOriginalIdentityAndExactPayload()
    {
        var inputs = new List<AddEvaluationNoteInput>();
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<AddEvaluationNoteInput>();
            inputs.Add(input);
            return inputs.Count == 1 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost response"))
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(Success(input)));
        });
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("Original retained evidence");
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("outcome is not yet known"));
        cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeTrue();
        Button(cut, "Retry original operation").HasAttribute("disabled").ShouldBeFalse();
        Button(cut, "Retry original operation").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
    }

    [Fact]
    public void CorrectedNoteCanRetryAfterServerValidationAndUnchangedParentRender()
    {
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult(string.Equals(call.Arg<AddEvaluationNoteInput>().Content, "First draft", StringComparison.Ordinal)
                ? new ServiceResult<EvaluationNoteMutationSuccess>(EvaluationMutationRejection.NotCommitted(ServiceProblem.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["Content"] = ["Review this observation."] }), call.Arg<AddEvaluationNoteInput>().OperationId))
                : new ServiceResult<EvaluationNoteMutationSuccess>(Success(call.Arg<AddEvaluationNoteInput>()))));
        var state = new CampaignWorkspaceEvaluationState { ParticipantId = 301 };
        var cut = Panel(state);
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("First draft");
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.FindAll(".evaluation-capture-error").Count.ShouldBe(1);
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(i => i.Content == "First draft"), Arg.Any<CancellationToken>());
        cut.Find("#evaluation-note").Input("Corrected observation");
        cut.Render(p => p.Add(c => c.State, state));
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(i => i.Content == "Corrected observation"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NotesFailurePreservesIdentityTraitsAndDraftAndRetriesOnlyNotes()
    {
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(ServiceProblem.ServerError("Notes unavailable."))),
            Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note()], null))));
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Notes unavailable."));
        cut.Find("#evaluation-note").Input("Independent draft");
        Button(cut, "Retry notes").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("First observation."));
        cut.Markup.ShouldContain("Strong");
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Independent draft");
        _ = _participants.Received(1).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.Received(1).GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.Received(1).GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClosedTransitionRemovesCaptureControlsAndKeepsUnsubmittedTextCopyable()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.FindAll("#evaluation-note").Count.ShouldBe(1));
        cut.Find("#evaluation-note").Input("Keep after closing");
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(301) with { CampaignStatus = CampaignStatus.Closed })));
        cut.Render(p => p.Add(c => c.Status, CampaignStatus.Closed));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This campaign is read-only"));
        cut.FindAll(".evaluation-save").ShouldBeEmpty();
        cut.FindAll(".evaluation-place").ShouldBeEmpty();
        cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeTrue();
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Keep after closing");
        cut.Markup.ShouldContain("First observation.");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviousParticipantNoteCompletionCannotReplaceNewOwnerEvidenceAsync(bool failure)
    {
        var pending = new TaskCompletionSource<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<GetEvaluationHistoryInput>().PlayerCampaignAssignmentId == 301 ? pending.Task
                : Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note() with { Content = "New player evidence" }], null))));
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.InvokeAsync(() => cut.Render(p => p.Add(c => c.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("New player evidence"));
        pending.SetResult(failure ? new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(ServiceProblem.ServerError("Old player error"))
            : new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note() with { Content = "Old player evidence" }], null)));
        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.Markup.ShouldNotContain("Old player evidence");
        cut.Markup.ShouldNotContain("Old player error");
        cut.Markup.ShouldContain("New player evidence");
    }

    [Fact]
    public void UnsavedDraftBlocksDepartureUntilUserExplicitlyDiscards()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.FindAll("#evaluation-note").Count.ShouldBe(1));
        cut.Find("#evaluation-note").Input("Unsaved work");
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10?tab=roster");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Your text has not been saved as shared evidence"));
        Button(cut, "Keep working").Click();
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Unsaved work");
        navigation.NavigateTo("/campaigns/10?tab=roster");
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-protection").Count.ShouldBe(1));
        Button(cut, "Discard and leave").Click();
        cut.WaitForAssertion(() => navigation.Uri.ShouldEndWith("/campaigns/10?tab=roster"));
        _ = _notes.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReloadAfterAmbiguousSubmissionReplaysOriginalStoredPayload()
    {
        var submitted = new List<AddEvaluationNoteInput>();
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            submitted.Add(call.Arg<AddEvaluationNoteInput>());
            return submitted.Count == 1 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost response"))
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(Success(submitted[^1])));
        });
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("Recover this exact note");
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("outcome is not yet known"));
        _storage.ReadSnapshot = _storage.Writes[^1];
        cut.Dispose();
        var restored = Panel(new() { ParticipantId = 301 });
        restored.WaitForAssertion(() => restored.Markup.ShouldContain("previous submission needs its receipt"));
        restored.Find("#evaluation-note").GetAttribute("value").ShouldBe("Recover this exact note");
        Button(restored, "Retry original operation").Click();
        restored.WaitForAssertion(() => restored.Markup.ShouldContain("Note saved."));
        submitted.Count.ShouldBe(2);
        submitted[1].ShouldBe(submitted[0]);
    }

    [Fact]
    public void DelayedStorageReadKeepsComposerReadOnlyUntilExactDraftRestores()
    {
        var pendingRead = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage.PendingRead = pendingRead.Task;
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note() with { CanEdit = true }], null))));
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => _storage.ReadStarted.ShouldBeTrue());
        cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeTrue();
        cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Edit").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#evaluation-note").Input("Premature input must not displace recovery");
        _storage.Writes.ShouldBeEmpty();
        pendingRead.SetResult(JsonSerializer.Serialize(new
        {
            Revision = 4,
            TraitSearch = string.Empty,
            Draft = "  Exact recovered observation.\nSecond line.",
            EditingNoteId = (long?)null,
            EditContent = string.Empty,
            EditOriginal = string.Empty,
            EditVersion = Guid.Empty,
            Pending = (object?)null,
        }));
        cut.WaitForAssertion(() => cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeFalse());
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("  Exact recovered observation.\nSecond line.");
        Button(cut, "Edit").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#evaluation-note").Input("Edited after recovery");
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input.Content == "Edited after recovery"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ReopeningWaitsForAuthoritativeIdentityBeforeRestoringCaptureControls()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-save").Count.ShouldBe(1));
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(301) with { CampaignStatus = CampaignStatus.Closed })));
        cut.Render(p => p.Add(c => c.Status, CampaignStatus.Closed));
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-save").ShouldBeEmpty());
        var pending = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        cut.Render(p => p.Add(c => c.Status, CampaignStatus.Active));
        cut.FindAll(".evaluation-save").ShouldBeEmpty();
        pending.SetResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(301)));
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
    }

    [Fact]
    public void OlderNotesUseExplicitCursorContinuationWithoutReplacingRecentObservations()
    {
        var now = DateTimeOffset.UtcNow;
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(call.Arg<GetEvaluationHistoryInput>().BeforeId is null
                ? new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note() with { NoteId = 3, Content = "Newest", CreatedAt = now }, Note() with { NoteId = 2, Content = "Second", CreatedAt = now.AddMinutes(-1) }], new(now.AddMinutes(-1), 2))
                : new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note() with { NoteId = 1, Content = "Older observation", CreatedAt = now.AddMinutes(-2) }], null))));
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Newest"));
        cut.Markup.ShouldNotContain("Older observation");
        Button(cut, "Show older notes").Click();
        Button(cut, "Load older notes").HasAttribute("disabled").ShouldBeFalse();
        Button(cut, "Load older notes").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Older observation"));
        cut.Find(".evaluation-observations").TextContent.ShouldContain("Newest");
        cut.Find(".evaluation-observations").TextContent.ShouldContain("Second");
        _ = _evidence.Received(1).GetNotesAsync(Arg.Is<GetEvaluationHistoryInput>(i => i.BeforeId == 2 && i.BeforeCreatedAt == now.AddMinutes(-1)), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EvidenceFailuresNeverConcealIdentityOrSuccessfulNeighborRegions(int failures)
    {
        if ((failures & 1) != 0)
        {
            _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(ServiceProblem.ServerError("Notes failed"))));
        }
        if ((failures & 2) != 0)
        {
            _evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(ServiceProblem.ServerError("Traits failed"))));
        }
        if ((failures & 4) != 0)
        {
            _evidence.GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<EvaluationTagChoice>>(ServiceProblem.ServerError("Choices failed"))));
        }
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Jordan Lee");
        cut.Find(".evaluation-observations").TextContent.ShouldContain((failures & 1) != 0 ? "Notes failed" : "First observation.");
        cut.Find(".evaluation-traits").TextContent.ShouldContain((failures & 2) != 0 ? "Traits failed" : "Coach Rivera");
        Button(cut, "Add a trait").Click();
        cut.Find(".evaluation-trait-picker").TextContent.ShouldContain((failures & 4) != 0 ? "Choices failed" : "Strong");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PriorClubMutationCompletionCannotPublishFeedbackOrClearNewDraftAsync(bool failure)
    {
        var pending = new TaskCompletionSource<ServiceResult<EvaluationNoteMutationSuccess>>(TaskCreationOptions.RunContinuationsAsynchronously);
        AddEvaluationNoteInput? original = null;
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call => { original = call.Arg<AddEvaluationNoteInput>(); return pending.Task; });
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Old club note" });
        var submitting = cut.Find(".evaluation-save").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Waiting for receipt"));
        cut.Render(p => p.Add(c => c.AuthorityScope, "coach:club-2:False").Add(c => c.CaptureScope, "coach:club-2"));
        await cut.WaitForAssertionAsync(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "New club draft" });
        pending.SetResult(failure ? new ServiceResult<EvaluationNoteMutationSuccess>(ServiceProblem.Forbidden("Old scope denied"))
            : new ServiceResult<EvaluationNoteMutationSuccess>(Success(original!)));
        await submitting;
        cut.Markup.ShouldNotContain("Note saved.");
        cut.Markup.ShouldNotContain("Old scope denied");
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New club draft");
        cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void RetryStorageFailureCannotDispatchPendingOriginalOperation()
    {
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost response")));
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("Retain while storage fails");
        cut.Find(".evaluation-save").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("outcome is not yet known"));
        _storage.ThrowWrites = true;
        Button(cut, "Retry original operation").HasAttribute("disabled").ShouldBeFalse();
        Button(cut, "Retry original operation").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Nothing new was submitted"));
        _ = _notes.Received(1).AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Retain while storage fails");
        cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeTrue();
    }

    [Fact]
    public void EditConflictRetainsBothDraftsUntilExplicitlyReviewingNewVersion()
    {
        var originalVersion = Guid.NewGuid();
        var currentVersion = Guid.NewGuid();
        var reads = 0;
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>(
                [Note() with { CanEdit = true, CanDelete = true, Version = ++reads == 1 ? originalVersion : currentVersion, Content = reads == 1 ? "Original shared note" : "Concurrent shared revision" }], null))));
        var edits = new List<EditEvaluationNoteInput>();
        _notes.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<EditEvaluationNoteInput>();
            edits.Add(input);
            return Task.FromResult(edits.Count == 1
                ? new ServiceResult<EvaluationNoteMutationSuccess>(EvaluationMutationRejection.NotCommitted(ServiceProblem.Conflict("Another session updated the note."), input.OperationId))
                : new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(1, Guid.NewGuid(), new(input.OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))));
        });
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-note-item").Count.ShouldBe(1));
        cut.Find("#evaluation-note").Input("Unrelated new note draft");
        Button(cut, "Edit").Click();
        cut.Find("#edit-note-1").Input("My edit draft");
        Button(cut, "Save changes").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This note has changed"));
        cut.Find("#edit-note-1").GetAttribute("value").ShouldBe("My edit draft");
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Unrelated new note draft");
        cut.Find(".evaluation-note-current").TextContent.ShouldBe("Concurrent shared revision");
        edits[0].ExpectedVersion.ShouldBe(originalVersion);
        Button(cut, "Use current version for this edit").Click();
        Button(cut, "Save changes").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note updated."));
        edits.Count.ShouldBe(2);
        edits[1].ExpectedVersion.ShouldBe(currentVersion);
        edits[1].Content.ShouldBe("My edit draft");
        edits[1].OperationId.ShouldNotBe(edits[0].OperationId);
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Unrelated new note draft");
    }

    [Fact]
    public void PersistedAttachRestoresAllRegionsWithoutRepeatingStartupQueries()
    {
        var cut = Render<RestoredEvaluationPanel>(p => p.Add(c => c.CampaignId, 10).Add(c => c.Status, CampaignStatus.Active)
            .Add(c => c.AuthorityScope, "coach:club-1:False").Add(c => c.CaptureScope, "coach:club-1")
            .Add(c => c.State, new CampaignWorkspaceEvaluationState { Search = "42", ParticipantId = 301 })
            .Add(c => c.RosterState, new CampaignWorkspaceRosterState()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Persisted observation"));
        cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Jordan Lee");
        cut.FindAll("a[data-eval-result]").Count.ShouldBe(1);
        cut.Find(".evaluation-result-count").TextContent.ShouldContain("1 player matches");
        _ = _participants.DidNotReceive().GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.DidNotReceive().GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.DidNotReceive().GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.DidNotReceive().GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
        _ = _placements.DidNotReceive().GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(i => i.Search == "42"), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public void NoteLengthBoundariesValidateBeforeDispatch(int length, bool valid)
    {
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        var content = new string('x', length);
        cut.Find("#evaluation-note").Input(content);
        cut.Find(".evaluation-save").Click();
        if (valid)
        {
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
            _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(i => i.Content == content), Arg.Any<CancellationToken>());
        }
        else
        {
            cut.FindAll(".evaluation-capture-error").Count.ShouldBe(1);
            cut.Find("#evaluation-note").GetAttribute("value").ShouldBe(content);
            _ = _notes.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public void AtomicTraitCreationKeepsUnrelatedNoteDraftAndUsesRetainedOperation()
    {
        _tags.CreateAndApplyAsync(Arg.Any<CreateAndApplyCampaignTagInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreateAndApplyCampaignTagInput>();
            return Task.FromResult(new ServiceResult<CampaignTagApplicationMutationSuccess>(new CampaignTagApplicationMutationSuccess(2, 12, false,
                new(input.OperationId, input.PlayerCampaignAssignmentId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))));
        });
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-note").Input("Unrelated observation draft");
        Button(cut, "Add a trait").Click();
        cut.Find(".evaluation-trait-choices button").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#evaluation-trait-search").Input("Quick feet");
        cut.FindAll("button").Single(b => b.TextContent.StartsWith("Create and apply", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Trait applied."));
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Unrelated observation draft");
        _ = _tags.Received(1).CreateAndApplyAsync(Arg.Is<CreateAndApplyCampaignTagInput>(i => i.Label == "Quick feet" && i.PlayerCampaignAssignmentId == 301 && i.OperationId != Guid.Empty), Arg.Any<CancellationToken>());
        _ = _notes.DidNotReceive().AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MissingNoteAfterConflictKeepsEditDraftCopyableOutsideLoadedHistory()
    {
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note() with { CanEdit = true }], null))),
            Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([], null))));
        _notes.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(EvaluationMutationRejection.NotCommitted(ServiceProblem.Conflict("The original note has been deleted."), call.Arg<EditEvaluationNoteInput>().OperationId))));
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-note-item").Count.ShouldBe(1));
        Button(cut, "Edit").Click();
        cut.Find("#edit-note-1").Input("Retain this attempted edit");
        Button(cut, "Save changes").Click();
        cut.WaitForAssertion(() => cut.FindAll(".evaluation-note-item").ShouldBeEmpty());
        cut.Find("#evaluation-retained-edit").GetAttribute("value").ShouldBe("Retain this attempted edit");
        cut.Find("#evaluation-retained-edit").HasAttribute("readonly").ShouldBeTrue();
        Button(cut, "Cancel retained edit").Click();
        cut.FindAll("#evaluation-retained-edit").ShouldBeEmpty();
        _ = _notes.Received(1).EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<CampaignEvaluationPanel> Panel(CampaignWorkspaceEvaluationState state, CampaignStatus status = CampaignStatus.Active) => Render<CampaignEvaluationPanel>(p =>
        p.Add(c => c.CampaignId, 10).Add(c => c.Status, status).Add(c => c.AuthorityScope, "coach:club-1:False").Add(c => c.CaptureScope, "coach:club-1")
            .Add(c => c.State, state).Add(c => c.RosterState, new CampaignWorkspaceRosterState { Search = "Roster query", Page = 4 }).Add(c => c.RosterParticipantId, 999));

    private static IElement Button(IRenderedComponent<CampaignEvaluationPanel> cut, string text) => cut.FindAll("button").Single(b => string.Equals(b.TextContent.Trim(), text, StringComparison.Ordinal));
    private static CampaignParticipantNoteDto Note() => new(1, "First observation.", "Coach Rivera", DateTimeOffset.UtcNow, null, false, false, Guid.NewGuid());
    private static CampaignParticipantDetailDto Identity(long id) => new(id, 7, id == 301 ? "Jordan Lee" : "Alex Morgan", 2030, 42, PlacementOutcome.Undecided,
        null, DateTimeOffset.UtcNow, null, CampaignStatus.Active, Guid.NewGuid(), new(false, true, true, false));
    private static CampaignEffectivePlacementsResult Results(int page = 1, int total = 1) => new(
        new(10, "Tryouts", CampaignStatus.Active, new(5, "Autumn")), new(total, 0, 0, 0),
        new([new(301, 7, "Jordan", "Lee", 2030, 42, LifecycleStatus.Active, Guid.NewGuid(), null, null, null, EffectivePlacementEligibility.NeedsPlacement, PlacementCorrectionReason.None)], page, 20, total));
    private static EvaluationNoteMutationSuccess Success(AddEvaluationNoteInput input) => new(2, Guid.NewGuid(),
        new(input.OperationId, input.PlayerCampaignAssignmentId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));

#pragma warning disable CA1812 // bUnit constructs the restore-path test component through reflection.
    private sealed class RestoredEvaluationPanel(ICampaignParticipantQueryService participants, IEffectivePlacementQueryService placements,
        ICampaignEvaluationQueryService evidence, ICampaignEvaluationNoteService notes, ICampaignTagApplicationService tags,
        NavigationManager navigation, IJSRuntime js) : CampaignEvaluationPanel(participants, placements, evidence, notes, tags, navigation, js)
#pragma warning restore CA1812
    {
        protected override void OnInitialized()
        {
            PersistedEvidenceScope = $"{AuthorityScope}:{CampaignId}:{State.ParticipantId}:{Status}";
            PersistedIdentity = Identity(301);
            PersistedNotes = new([Note() with { Content = "Persisted observation" }], null);
            PersistedApplications = new([], null);
            PersistedChoices = [];
            PersistedFinderScope = $"{AuthorityScope}:{CampaignId}:{State.Search}:{State.Page}:{Status}";
            PersistedFinderRows = [new(301, "Jordan Lee", 2030, 42, "Undecided")];
            PersistedFinderCount = 1;
            base.OnInitialized();
        }
    }

    private sealed class EvaluationJsRuntime(IJSRuntime fallback, TabStorage storage) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (string.Equals(identifier, "import", StringComparison.Ordinal)
                && args?[0]?.ToString()?.EndsWith("CampaignEvaluationPanel.razor.js", StringComparison.Ordinal) == true)
            {
                return ValueTask.FromResult((TValue)(object)storage);
            }
            return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
        }
    }

    private sealed class TabStorage : IJSObjectReference
    {
        public string? ReadSnapshot { get; set; }
        public Task<string?>? PendingRead { get; set; }
        public bool ReadStarted { get; private set; }
        public bool FailWrites { get; set; }
        public bool ThrowWrites { get; set; }
        public bool FocusAvailable { get; set; } = true;
        public List<string> FocusOwners { get; } = [];
        public List<string> Writes { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (string.Equals(identifier, "focusSheet", StringComparison.Ordinal))
            {
                FocusOwners.Add((string)args![1]!);
                return ValueTask.FromResult((TValue)(object)FocusAvailable);
            }
            if (string.Equals(identifier, "read", StringComparison.Ordinal))
            {
                ReadStarted = true;
                if (PendingRead is not null) { return ReadDelayedAsync<TValue>(PendingRead); }
                if (ReadSnapshot is not null) { return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(ReadSnapshot)!); }
            }
            if (string.Equals(identifier, "write", StringComparison.Ordinal))
            {
                if (ThrowWrites) { throw new JSException("Storage quota"); }
                Writes.Add(JsonSerializer.Serialize(args![3]));
                return ValueTask.FromResult((TValue)(object)!FailWrites);
            }
            return ValueTask.FromResult(default(TValue)!);
        }
        private static async ValueTask<TValue> ReadDelayedAsync<TValue>(Task<string?> pendingRead)
        {
            var snapshot = await pendingRead;
            return snapshot is null ? default! : JsonSerializer.Deserialize<TValue>(snapshot)!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
