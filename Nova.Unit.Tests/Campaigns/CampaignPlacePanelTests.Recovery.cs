using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    [Fact]
    public void HistoryPagesStayBoundedAndCanReturnToLatestChanges()
    {
        RegisterServices();
        var latest = Enumerable.Range(1, 20).Select(index => new PlacementHistoryItem(101 - index, 10, "Latest page",
            null, null, PlacementOutcome.NotSelected, null, "Member", DateTimeOffset.UtcNow)).ToArray();
        var earlier = latest[0] with { EventId = 80, CampaignName = "Earlier page" };
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(call =>
                new ServiceResult<PlacementContextResult>(call.Arg<GetPlacementContextInput>().BeforeEventId is null
                    ? new PlacementContextResult(301, null, latest, 81, false) : new PlacementContextResult(301, null, [earlier], null, false)));
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll(".place-history-list li").Count.ShouldBe(20));
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Earlier changes", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.FindAll(".place-history-list li").Single().TextContent.ShouldContain("Earlier page"));
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Latest changes", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.FindAll(".place-history-list li").Count.ShouldBe(20));
        cut.Find(".place-history").TextContent.ShouldNotContain("Earlier page");
        cut.Find(".place-history").TextContent.ShouldNotContain("Latest changes");
    }

    [Fact]
    public void StalledOptionalHistoryDoesNotBlockCompatibleTeamsOrInitialSave()
    {
        RegisterServices();
        var pending = new TaskCompletionSource<ServiceResult<PlacementContextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

        var cut = RenderPanel(selectedParticipantId: 301);

        cut.WaitForAssertion(() => cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Assigned));
        cut.WaitForAssertion(() => cut.FindAll("#place-team option").Count.ShouldBeGreaterThan(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => _ = _mutations.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input => input.Outcome == PlacementOutcome.NotSelected), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void ConfirmedReceiptFeedbackSurvivesSameScopeCampaignClosure()
    {
        RegisterServices();
        var cut = RenderPanel(selectedParticipantId: 301, owner: "actor:club:active");
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));

        cut.Render(parameters => parameters.Add(component => component.CampaignStatus, CampaignStatus.Closed));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));
        cut.FindAll("#place-outcome").ShouldBeEmpty();
    }

    [Fact]
    public void ExpiredRecoveryReviewNavigatesOriginalParticipantBeforeClearingPendingCommand()
    {
        RegisterServices();
        var pending = new UpdateCampaignPlacementInput(302, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<Nova.UI.Features.Campaigns.Components.PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(pending, null));
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementMutationSuccess>(ServiceProblem.Conflict("expired") with
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [PlacementMutationRejection.ExpiredExtension] = pending.OperationId.ToString("D")
                }
            }));
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recover save"));
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Recover save", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Review current placement"));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Review current placement", StringComparison.Ordinal)).Click();

        Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("placementParticipant=302");
        _placementStorage.Invocations.ShouldNotContain(invocation => string.Equals(invocation.Identifier, "clearPending", StringComparison.Ordinal));
        cut.FindAll("#place-outcome").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageWriteFailureAlsoBlocksConfirmationAndRecovery(bool recovering)
    {
        RegisterServices();
        if (recovering)
        {
            _placementStorage.Setup<Nova.UI.Features.Campaigns.Components.PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(
                new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7()), null));
        }
        _placementStorage.SetupVoid("writePending", _ => true).SetException(new JSException("quota"));
        var cut = RenderPanel(selectedParticipantId: 301);
        if (recovering)
        {
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recover save"));
            cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Recover save", StringComparison.Ordinal)).Click();
        }
        else
        {
            cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
            cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Withdrawn));
            SaveButton(cut).Click();
            cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Confirm change", StringComparison.Ordinal)).Click();
        }

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("pending placement could not be stored"));
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AssignedPlayerRequiresDeliberateReassignmentAndConfirmation()
    {
        var team = new CampaignParticipantTeamSummaryDto(21, "Elite Silver");
        RegisterServices(rows: [CreateRow(301) with
        {
            EffectiveTeam = team, Eligibility = EffectivePlacementEligibility.OptionalReassignment,
            EffectiveDecision = new PlacementDecisionSource(CreateDecision(205, PlacementOutcome.Assigned, 21), "Earlier campaign", team)
        }]);
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Reassign player"));
        cut.FindAll("#place-outcome").ShouldBeEmpty();

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Reassign player", StringComparison.Ordinal)).Click();
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();

        cut.Markup.ShouldContain("Confirm change");
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Confirm change", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => _ = _mutations.Received(1).UpdatePlacementAsync(
            Arg.Is<UpdateCampaignPlacementInput>(input => input.Outcome == PlacementOutcome.NotSelected && input.TeamId == null), Arg.Any<CancellationToken>()));
    }
    [Fact]
    public void FailedConflictReloadKeepsEditingBlockedUntilFreshEvidenceArrives()
    {
        var queries = RegisterServices();
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(call => new ServiceResult<PlacementMutationSuccess>(PlacementMutationRejection.NotCommitted(
                ServiceProblem.Conflict("Another member changed this placement."), call.Arg<UpdateCampaignPlacementInput>().OperationId)));
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Review latest placement"));
        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline")));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Review latest placement", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Review latest placement"));
        cut.FindAll("#place-outcome").ShouldBeEmpty();
        _ = _mutations.Received(1).UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public void StorageWriteFailurePreventsPlacementDispatch()
    {
        RegisterServices();
        _placementStorage.SetupVoid("writePending", _ => true).SetException(new JSException("storage full"));
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));

        SaveButton(cut).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("pending placement could not be stored"));
        cut.Markup.ShouldContain("Retry storage");
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UnknownOutcomeRecoveryReplaysTheExactOriginalCommand()
    {
        RegisterServices();
        var sent = new List<UpdateCampaignPlacementInput>();
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<UpdateCampaignPlacementInput>();
                sent.Add(input);
                return sent.Count == 1 ? new ServiceResult<PlacementMutationSuccess>(ServiceProblem.ServerError("lost"))
                    : PlacementTestReceipts.Success(input, _replacementToken);
            });
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.NotSelected));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recover this save"));
        cut.FindAll("#place-outcome").ShouldBeEmpty();

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Recover save", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Placement saved."));
        sent.Count.ShouldBe(2);
        sent[1].ShouldBe(sent[0]);
        sent[0].OperationId.Version.ShouldBe(7);
        _placementStorage.Invocations.Where(invocation => string.Equals(invocation.Identifier, "writePending", StringComparison.Ordinal))
            .Select(invocation => invocation.Arguments[1]).ShouldAllBe(value => Equals(value, sent[0]));
    }

    [Fact]
    public void RestoredPendingOperationBlocksAnotherSaveUntilItsOriginalReceiptIsRecovered()
    {
        RegisterServices();
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<Nova.UI.Features.Campaigns.Components.PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(pending, null));

        var cut = RenderPanel(selectedParticipantId: 301);

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("original save"));
        cut.FindAll("#place-outcome").ShouldBeEmpty();
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Recover save", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => _ = _mutations.Received(1).UpdatePlacementAsync(pending, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void CancellingWithdrawalConfirmationPreservesCurrentDecision()
    {
        RegisterServices();
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        cut.Find("#place-outcome").Change(nameof(PlacementOutcome.Withdrawn));
        SaveButton(cut).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Withdraw Avery Chen for this season?"));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Keep current placement", StringComparison.Ordinal)).Click();

        cut.Markup.ShouldNotContain("Confirm change");
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }
}
