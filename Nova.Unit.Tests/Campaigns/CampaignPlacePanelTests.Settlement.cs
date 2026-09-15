using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using CampaignPlacePanel = Nova.UI.Features.Campaigns.Components.CampaignPlacePanel;
using CampaignWorkspacePlacementState = Nova.UI.Features.Campaigns.Services.CampaignWorkspacePlacementState;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeepAdvancesOnlyAfterTheCorrectedQueueIsFreshAsync(bool fail)
    {
        var queries = RegisterServices(totalCount: 51);
        var history = Services.GetRequiredService<IPlacementContextQueryService>();
        history.GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementContextResult>(new PlacementContextResult(301,
                new PreviousSeasonPlacement(new(4, "Prior season"), new(CreateDecision(205, PlacementOutcome.Assigned, 21),
                    "Prior campaign", new(21, "Elite Silver")), true), [], null, false)));
        CampaignWorkspacePlacementState? corrected = null;
        var cut = RenderPanel(selectedParticipantId: 301, state: new() { Page = 2 }, onStateChanged: state => corrected = state);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Keep on Elite Silver"));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;
        queries.GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([], 50)));
        await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Keep on Elite Silver", StringComparison.Ordinal))
            .TriggerEventAsync("onclick", new MouseEventArgs());
        corrected.ShouldNotBeNull();
        navigation.Uri.ShouldBe(originalUrl);
        AssertSettlementBlocked(cut);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await gate.Task;
                return fail ? new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline"))
                    : new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([CreateRow(302)], 50));
            });
        ReRender(cut, corrected, selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => entered.Task.IsCompleted.ShouldBeTrue());
        navigation.Uri.ShouldBe(originalUrl);
        await cut.InvokeAsync(() => gate.SetResult());
        if (fail)
        {
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Review latest placement"));
            navigation.Uri.ShouldBe(originalUrl);
        }
        else { await cut.WaitForAssertionAsync(() => navigation.Uri.ShouldContain("placementParticipant=302")); }
        _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Is<UpdateCampaignPlacementInput>(input =>
            input.Outcome == PlacementOutcome.Assigned && input.TeamId == 21 && input.PlayerCampaignAssignmentId == 301), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BackForwardDuringCorrectedSelectionReadCanCorrectToTheAlreadyAppliedPageAsync()
    {
        var queries = RegisterServices(totalCount: 51);
        var corrections = new List<CampaignWorkspacePlacementState>();
        var cut = RenderPanel(selectedParticipantId: 301, state: new() { Page = 2 }, onStateChanged: corrections.Add);
        await cut.WaitForAssertionAsync(() => cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse());
        var selectedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectedRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.Arg<GetCampaignEffectivePlacementsInput>();
                if (input.ParticipantId is not null)
                {
                    selectedEntered.TrySetResult();
                    await selectedRelease.Task;
                    var saved = CreateRow(301) with
                    {
                        LocalDecision = CreateDecision(301, PlacementOutcome.NotSelected, null) with { ConcurrencyToken = _replacementToken },
                        Eligibility = EffectivePlacementEligibility.Resolved
                    };
                    return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([saved], 1));
                }
                return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult(
                    input.Page == 2 ? [] : [CreateRow(302)], 50) with
                { Counts = new(50, 0, 1, 0) });
            });
        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        await SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        corrections.Count.ShouldBe(1);
        ReRender(cut, corrections[0], selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => selectedEntered.Task.IsCompleted.ShouldBeTrue());
        ReRender(cut, new() { Page = 2 }, selectedParticipantId: 301);
        await cut.InvokeAsync(() => selectedRelease.SetResult());
        await cut.WaitForAssertionAsync(() => corrections.Count.ShouldBe(2));
        AssertSettlementBlocked(cut);
        ReRender(cut, corrections[1], selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Placement saved. 50 need placement."));
        cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
        cut.Markup.ShouldNotContain("A later decision");
        _ = queries.Received(2).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null && input.Page == 1), Arg.Any<CancellationToken>());
        _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task LastPageSettlementWaitsForCorrectedParametersAndFreshEvidenceAsync(bool immediateParameters, bool readFails)
    {
        var queries = RegisterServices(totalCount: 51);
        CampaignWorkspacePlacementState? corrected = null;
        IRenderedComponent<CampaignPlacePanel>? panel = null;
        var cut = RenderPanel(selectedParticipantId: 301, state: new() { Page = 2 }, onStateChanged: state =>
        {
            corrected = state;
            if (immediateParameters) { ReRender(panel!, state, selectedParticipantId: 301); }
        });
        panel = cut;
        await cut.WaitForAssertionAsync(() => cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = CreateRow(301) with
        {
            LocalDecision = CreateDecision(301, PlacementOutcome.NotSelected, null) with { ConcurrencyToken = _replacementToken },
            Eligibility = EffectivePlacementEligibility.Resolved
        };
        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.Arg<GetCampaignEffectivePlacementsInput>();
                if (input.ParticipantId is not null) { return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([saved], 1)); }
                if (input.Page == 2) { return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([], 50)); }
                entered.TrySetResult();
                await release.Task;
                return readFails
                    ? new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline"))
                    : new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([CreateRow(302)], 50) with { Counts = new(50, 0, 1, 0) });
            });
        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => corrected.ShouldNotBeNull());
        corrected.ShouldNotBeNull().Page.ShouldBe(1);
        if (!immediateParameters)
        {
            await save;
            AssertSettlementBlocked(cut);
            ReRender(cut, corrected, selectedParticipantId: 301);
        }
        await cut.WaitForAssertionAsync(() => entered.Task.IsCompleted.ShouldBeTrue());
        AssertSettlementBlocked(cut);
        release.SetResult();
        await save;
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(readFails
            ? "Current placement evidence could not be refreshed."
            : "Placement saved. 50 need placement."));
        cut.Markup.ShouldNotContain("A later decision");
        AssertCorrectedSettlement(cut, readFails);
        _ = queries.Received(1).GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == 301), Arg.Any<CancellationToken>());
        _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    private static void AssertCorrectedSettlement(IRenderedComponent<CampaignPlacePanel> cut, bool readFails)
    {
        if (readFails)
        {
            cut.FindAll("#place-outcome").ShouldBeEmpty();
            cut.Markup.ShouldContain("Review latest placement");
            cut.Markup.ShouldNotContain("50 need placement.");
        }
        else
        {
            cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
            cut.FindAll("a.place-row").Single().GetAttribute("href").ShouldNotBeNull().ShouldContain("placementParticipant=302");
        }
    }

    [Fact]
    public async Task ClosureDuringCorrectedPageReadDiscardsTheOldSettlementAsync()
    {
        var queries = RegisterServices(totalCount: 51);
        CampaignWorkspacePlacementState? corrected = null;
        var cut = RenderPanel(selectedParticipantId: 301, state: new() { Page = 2 }, onStateChanged: state => corrected = state);
        await cut.WaitForAssertionAsync(() => cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse());
        queries.GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([], 50)));
        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        await SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());
        corrected.ShouldNotBeNull();
        var (entered, release) = HoldNextQueueRead(queries);
        ReRender(cut, corrected, selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());
        ReRender(cut, corrected, selectedParticipantId: 301, status: CampaignStatus.Closed);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Read-only — campaign is closed."));
        await cut.InvokeAsync(release);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").ShouldBeEmpty());
        cut.Markup.ShouldNotContain("need placement.");
        cut.Markup.ShouldNotContain("A later decision");
        cut.Markup.ShouldContain("The original operation is confirmed.");
    }

    private static void AssertSettlementBlocked(IRenderedComponent<CampaignPlacePanel> cut)
    {
        cut.Markup.ShouldContain("The original operation is confirmed.");
        cut.Markup.ShouldNotContain("need placement.");
        cut.Markup.ShouldNotContain("A later decision");
        cut.FindAll("#place-outcome").ShouldAllBe(element => element.HasAttribute("disabled"));
        cut.FindAll("button").Where(button => button.TextContent.Contains("Reassign player", StringComparison.Ordinal))
            .ShouldAllBe(element => element.HasAttribute("disabled"));
    }
}
