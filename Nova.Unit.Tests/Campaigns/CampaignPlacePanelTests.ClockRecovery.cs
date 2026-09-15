using Bunit;
using Microsoft.AspNetCore.Components.Web;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task FutureClockDiagnosticRetainsTheExactCommandUntilStorageIsRecheckedAsync(bool matchingOperation)
    {
        RegisterServices();
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(pending, null));
        _mutations.UpdatePlacementAsync(pending, Arg.Any<CancellationToken>()).Returns(new ServiceResult<PlacementMutationSuccess>(
            ServiceProblem.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["OperationId"] = ["Check clock"] }) with
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                { [PlacementMutationRejection.FutureOperationIdExtension] = (matchingOperation ? pending.OperationId : Guid.CreateVersion7()).ToString("D") }
            }));
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Recover save"));
        await RecoveryButton(cut, "Recover save").ClickAsync(new MouseEventArgs());

        SaveControlsBlocked(cut);
        _ = _mutations.Received(1).UpdatePlacementAsync(pending, Arg.Any<CancellationToken>());
        _placementStorage.Invocations.ShouldNotContain(call => string.Equals(call.Identifier, "clearPending", StringComparison.Ordinal));
        RecoveryButton(cut, "Recover save").HasAttribute("disabled").ShouldBe(matchingOperation);
        if (!matchingOperation)
        {
            cut.Markup.ShouldNotContain("Check your device clock");
            cut.FindAll("button").ShouldNotContain(button => string.Equals(Collapse(button), "Retry storage", StringComparison.Ordinal));
            return;
        }
        cut.Markup.ShouldContain("Check your device clock");
        cut.Markup.ShouldContain("earlier result remains unknown");

        // Correcting a fast device clock makes its retained future ID invalid to the browser's read.
        const string OriginalBytes = "original future-dated command bytes";
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, OriginalBytes));
        await RecoveryButton(cut, "Retry storage").ClickAsync(new MouseEventArgs());
        cut.Markup.ShouldContain("Discarding invalid recovery data does not undo a save or prove it failed");
        _placementStorage.Setup<bool>("discardInvalidPending", _ => true).SetResult(true);
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, null));
        await RecoveryButton(cut, "Discard invalid data and refresh").ClickAsync(new MouseEventArgs());
        cut.Markup.ShouldContain("earlier save result remains unknown");
        _placementStorage.Invocations.Single(call => string.Equals(call.Identifier, "discardInvalidPending", StringComparison.Ordinal))
            .Arguments[1].ShouldBe(OriginalBytes);
        _ = _mutations.Received(1).UpdatePlacementAsync(pending, Arg.Any<CancellationToken>());
        cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
    }
}
