using Bunit;
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
    public async Task RecoveryCleanupAppliesDeferredNavigationOrKeepsAConflictGateAsync(bool expired, bool readFails)
    {
        var queries = RegisterServices(rows: [CreateRow(301), Row(302, "New", "Selection")]);
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(expired ? new(pending, null) : new(null, "{old"));
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PlacementMutationSuccess>(ServiceProblem.Conflict("expired") with
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal) { [PlacementMutationRejection.ExpiredExtension] = pending.OperationId.ToString("D") }
            }));
        using var cleanup = await DelayPlacementCleanupAsync(expired ? "clearPending" : "discardInvalidPending");
        _placementStorage.SetupVoid("clearPending", _ => true).SetVoidResult();
        _placementStorage.Setup<bool>("discardInvalidPending", _ => true).SetResult(true);
        var cut = RenderPanel(selectedParticipantId: 301);
        if (expired)
        {
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Recover save"));
            await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Recover save", StringComparison.Ordinal)).TriggerEventAsync("onclick", new MouseEventArgs());
        }
        var title = expired ? "Review current placement" : "Discard invalid data and refresh";
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(title));
        var action = cut.FindAll("button").Single(button => string.Equals(Collapse(button), title, StringComparison.Ordinal)).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cleanup.Entered.Task.IsCompleted.ShouldBeTrue());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, null));
        if (readFails)
        {
            queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
                .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline")));
        }
        ReRender(cut, new() { Search = "New" }, selectedParticipantId: 302);
        cleanup.Release.SetResult();
        await action;
        if (readFails)
        {
            cut.Markup.ShouldContain("Review latest placement");
            SaveControlsBlocked(cut);
        }
        else
        {
            SheetName(cut).ShouldBe("New Selection");
            cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
        }
        cut.Markup.ShouldContain("result remains unknown");
        _ = queries.Received().GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Search == "New"), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("discarded")]
    [InlineData("changed")]
    [InlineData("read-failed")]
    [InlineData("delete-failed")]
    public async Task InvalidStorageRequiresExplicitDiscardAndFreshEvidenceAsync(string outcome)
    {
        var queries = RegisterServices();
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, "{bad"));
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Discard invalid data and refresh"));
        cut.Markup.ShouldContain("earlier save result remains unknown");
        SaveControlsBlocked(cut);
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(
            string.Equals(outcome, "changed", StringComparison.Ordinal) ? pending : null, null));
        var delete = _placementStorage.Setup<bool>("discardInvalidPending", _ => true);
        if (string.Equals(outcome, "delete-failed", StringComparison.Ordinal)) { delete.SetException(new JSException("quota")); }
        else { delete.SetResult(!string.Equals(outcome, "changed", StringComparison.Ordinal)); }
        if (string.Equals(outcome, "read-failed", StringComparison.Ordinal))
        {
            queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
                .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("offline")));
        }
        await cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Discard invalid data and refresh", StringComparison.Ordinal))
            .TriggerEventAsync("onclick", new MouseEventArgs());
        var deletes = _placementStorage.Invocations.Where(call => string.Equals(call.Identifier, "discardInvalidPending", StringComparison.Ordinal)).ToList();
        deletes.Count.ShouldBe(string.Equals(outcome, "read-failed", StringComparison.Ordinal) ? 0 : 1);
        if (deletes.Count > 0) { deletes[0].Arguments[1].ShouldBe("{bad"); }
        if (string.Equals(outcome, "discarded", StringComparison.Ordinal))
        {
            cut.Markup.ShouldContain("Invalid recovery data discarded.");
            cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse();
        }
        else
        {
            SaveControlsBlocked(cut);
            cut.Markup.ShouldContain(string.Equals(outcome, "changed", StringComparison.Ordinal) ? "Recover save" : "recovery data");
        }
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidDataDiscardCannotUnlockANewStorageOwnerAsync()
    {
        RegisterServices();
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, "{old"));
        using var cleanup = await DelayPlacementCleanupAsync("discardInvalidPending");
        _placementStorage.Setup<bool>("discardInvalidPending", _ => true).SetResult(true);
        var cut = RenderPanel(selectedParticipantId: 301, owner: "club:old");
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Discard invalid data and refresh"));
        var action = cut.FindAll("button").Single(button => string.Equals(Collapse(button), "Discard invalid data and refresh", StringComparison.Ordinal))
            .TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cleanup.Entered.Task.IsCompleted.ShouldBeTrue());
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(pending, null));
        cut.Render(parameters => parameters.Add(component => component.Owner, "club:new"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Recover save"));
        cleanup.Release.SetResult();
        await action;
        SaveControlsBlocked(cut);
        cut.Markup.ShouldContain("Recover save");
        cut.Markup.ShouldNotContain("Invalid recovery data discarded.");
    }

    [Fact]
    public void UnavailableStorageDoesNotOfferInvalidDataDiscard()
    {
        RegisterServices();
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetException(new JSException("unavailable"));
        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Retry storage"));
        cut.Markup.ShouldNotContain("Discard invalid data and refresh");
        SaveButton(cut).HasAttribute("disabled").ShouldBeTrue();
    }

    private static void SaveControlsBlocked(IRenderedComponent<CampaignPlacePanel> cut)
    {
        cut.FindAll("#place-outcome").ShouldAllBe(control => control.HasAttribute("disabled"));
        cut.FindAll("button").Where(button => button.TextContent.Contains("Save placement", StringComparison.Ordinal))
            .ShouldAllBe(button => button.HasAttribute("disabled"));
    }
}
