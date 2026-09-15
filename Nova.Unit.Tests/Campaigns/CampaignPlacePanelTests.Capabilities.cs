using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PriorWithdrawalCapabilityRemainsUnknownThroughFailureUntilRetrySucceedsAsync(bool canSupersede)
    {
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                Eligibility = EffectivePlacementEligibility.Unavailable,
                EffectiveDecision = new(CreateDecision(205, PlacementOutcome.Withdrawn, null) with { CampaignId = 9 },
                    "Earlier campaign", null)
            }
        ]);
        var first = new TaskCompletionSource<ServiceResult<PlacementContextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = new TaskCompletionSource<ServiceResult<PlacementContextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = Services.GetRequiredService<IPlacementContextQueryService>();
        queries.GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(first.Task);
        var cut = RenderPanel(selectedParticipantId: 301);
        const string Unavailable = "Administrator recovery of a prior-campaign withdrawal is not available here.";
        await cut.WaitForAssertionAsync(() => cut.Find(".place-history").TextContent.ShouldContain("Loading placement history"));
        cut.Markup.ShouldNotContain(Unavailable);
        cut.FindAll("#place-outcome").ShouldBeEmpty();
        await cut.InvokeAsync(() => first.SetResult(ServiceProblem.ServerError("offline")));
        await cut.WaitForAssertionAsync(() => cut.Find(".place-history").TextContent.ShouldContain("Retry history"));
        cut.Markup.ShouldNotContain(Unavailable);
        cut.FindAll("button").ShouldNotContain(button => string.Equals(Collapse(button), "Supersede prior withdrawal", StringComparison.Ordinal));
        queries.GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(retry.Task);
        var retryClick = cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Retry history", StringComparison.Ordinal))
            .TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Find(".place-history").TextContent.ShouldContain("Loading placement history"));
        cut.Markup.ShouldNotContain(Unavailable);
        await cut.InvokeAsync(() => retry.SetResult(new PlacementContextResult(301, null, [], null, canSupersede)));
        await retryClick;
        if (canSupersede)
        {
            cut.Markup.ShouldNotContain(Unavailable);
            await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Supersede prior withdrawal", StringComparison.Ordinal))
                .TriggerEventAsync("onclick", new MouseEventArgs());
            cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
        }
        else
        {
            cut.Markup.ShouldContain(Unavailable);
            cut.FindAll("#place-outcome").ShouldBeEmpty();
        }
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalPlacementReasonsDoNotDependOnOptionalContextAsync(bool archived)
    {
        var row = archived ? CreateRow(301) with { PlayerLifecycleStatus = LifecycleStatus.Archived }
            : CreateRow(301) with { LocalDecision = CreateDecision(301, PlacementOutcome.Withdrawn, null) };
        RegisterServices(rows: [row]);
        var pending = new TaskCompletionSource<ServiceResult<PlacementContextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = RenderPanel(selectedParticipantId: 301);
        var reason = archived ? "This player is archived" : "Only a superseding decision in a later active campaign can change it.";
        await cut.WaitForAssertionAsync(() => cut.Find(".place-readonly").TextContent.ShouldContain(reason));
        cut.FindAll("#place-outcome").ShouldBeEmpty();
        await cut.InvokeAsync(() => pending.SetResult(ServiceProblem.ServerError("offline")));
        await cut.WaitForAssertionAsync(() => cut.Find(".place-history").TextContent.ShouldContain("Retry history"));
        cut.Find(".place-readonly").TextContent.ShouldContain(reason);
        cut.FindAll("#place-outcome").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("initial")]
    [InlineData("inherited")]
    [InlineData("local")]
    public async Task KeepPreviousTeamOnlyOffersAnInitialDecisionAsync(string placement)
    {
        var row = CreateRow(301);
        var decision = CreateDecision(205, PlacementOutcome.Assigned, 21) with { CampaignId = 9 };
        if (!string.Equals(placement, "initial", StringComparison.Ordinal))
        {
            row = row with
            {
                Eligibility = EffectivePlacementEligibility.OptionalReassignment,
                EffectiveDecision = new(decision, "Earlier campaign", new(21, "Elite Silver")),
                LocalDecision = string.Equals(placement, "local", StringComparison.Ordinal)
                    ? CreateDecision(301, PlacementOutcome.Assigned, 21) : null
            };
        }
        RegisterServices(rows: [row]);
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementContextResult>(new PlacementContextResult(301,
                new(new(4, "Prior season"), new(decision, "Prior campaign", new(21, "Elite Silver")), true), [], null, false)));
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Find(".place-sheet").TextContent.ShouldContain("Previous placement"));
        if (string.Equals(placement, "initial", StringComparison.Ordinal))
        {
            var keep = cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Keep on Elite Silver", StringComparison.Ordinal));
            keep.HasAttribute("disabled").ShouldBeFalse();
            await keep.TriggerEventAsync("onclick", new MouseEventArgs());
            _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Is<UpdateCampaignPlacementInput>(input =>
                input.PlayerCampaignAssignmentId == 301 && input.Outcome == PlacementOutcome.Assigned && input.TeamId == 21),
                Arg.Any<CancellationToken>());
            cut.FindAll(".place-confirmation").ShouldBeEmpty();
        }
        else
        {
            cut.FindAll("button").ShouldNotContain(button => string.Equals(Collapse(button), "Keep on Elite Silver", StringComparison.Ordinal));
            await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Reassign player", StringComparison.Ordinal))
                .TriggerEventAsync("onclick", new MouseEventArgs());
            cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
            cut.FindAll("button").ShouldNotContain(button => string.Equals(Collapse(button), "Keep on Elite Silver", StringComparison.Ordinal));
            _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
        }
    }
}
