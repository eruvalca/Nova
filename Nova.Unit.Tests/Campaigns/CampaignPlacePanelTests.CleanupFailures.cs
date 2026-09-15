using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SettledSaveCleanupFailureRetainsTheExactCommandAndTruthfulResultAsync(bool unavailable, bool rejected)
    {
        RegisterServices();
        if (rejected)
        {
            _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
                .Returns(call => new ServiceResult<PlacementMutationSuccess>(PlacementMutationRejection.NotCommitted(
                    ServiceProblem.Conflict("Latest decision changed."), call.Arg<UpdateCampaignPlacementInput>().OperationId)));
        }
        var cleanup = _placementStorage.SetupVoid("clearPending", _ => true);
        cleanup.SetException<Exception>(unavailable ? new InvalidOperationException("circuit unavailable") : new JSException("storage unavailable"));
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        await SaveButton(cut).TriggerEventAsync("onclick", new MouseEventArgs());

        cut.Markup.ShouldContain(rejected ? "The save was refused. Recover to clear its stored result." : "Placement saved. Recovery storage could not be cleared; recover to finish.");
        cut.Markup.ShouldNotContain("The pending placement could not be stored.");
        SaveControlsBlocked(cut);
        var original = _placementStorage.Invocations.Single(call => string.Equals(call.Identifier, "writePending", StringComparison.Ordinal)).Arguments[1]
            .ShouldBeOfType<UpdateCampaignPlacementInput>();
        var clears = _placementStorage.Invocations.Where(call => string.Equals(call.Identifier, "clearPending", StringComparison.Ordinal)).ToList();
        clears.Count.ShouldBe(1);
        clears[0].Arguments[1].ShouldBe(original.OperationId);

        cleanup.SetVoidResult();
        await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Recover save", StringComparison.Ordinal))
            .TriggerEventAsync("onclick", new MouseEventArgs());
        _ = _mutations.Received(2).UpdatePlacementAsync(Arg.Is<UpdateCampaignPlacementInput>(input => input == original), Arg.Any<CancellationToken>());
        _placementStorage.Invocations.Where(call => string.Equals(call.Identifier, "writePending", StringComparison.Ordinal))
            .ShouldAllBe(call => Equals(call.Arguments[1], original));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredRecoveryCleanupFailureKeepsTheOperationBlockedAndRetryableAsync(bool unavailable)
    {
        RegisterServices();
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(pending, null));
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementMutationSuccess>(ServiceProblem.Conflict("expired") with
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { [PlacementMutationRejection.ExpiredExtension] = pending.OperationId.ToString("D") }
            }));
        var cleanup = _placementStorage.SetupVoid("clearPending", _ => true);
        cleanup.SetException<Exception>(unavailable ? new InvalidOperationException("circuit unavailable") : new JSException("storage unavailable"));
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Recover save"));
        await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Recover save", StringComparison.Ordinal)).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Review current placement", StringComparison.Ordinal)).TriggerEventAsync("onclick", new MouseEventArgs());

        cut.Markup.ShouldContain("Recovery storage could not be cleared. Retry reviewing current placement.");
        SaveControlsBlocked(cut);
        _ = _mutations.Received(1).UpdatePlacementAsync(pending, Arg.Any<CancellationToken>());
        cleanup.SetVoidResult();
        await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Review current placement", StringComparison.Ordinal)).TriggerEventAsync("onclick", new MouseEventArgs());
        cut.Markup.ShouldContain("The earlier save's result remains unknown");
        cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
        _ = _mutations.Received(1).UpdatePlacementAsync(pending, Arg.Any<CancellationToken>());
        _placementStorage.Invocations.Where(call => string.Equals(call.Identifier, "clearPending", StringComparison.Ordinal))
            .ShouldAllBe(call => Equals(call.Arguments[1], pending.OperationId));
    }
}
