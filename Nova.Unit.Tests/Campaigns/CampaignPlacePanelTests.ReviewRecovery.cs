using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData("owner")]
    [InlineData("closure")]
    [InlineData("storage")]
    public async Task ExpiryEvidenceCannotDiscardADifferentAttachedOperationAsync(string transition)
    {
        RegisterServices();
        var expired = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        var replacement = expired with { OperationId = Guid.CreateVersion7() };
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(expired, null));
        _mutations.UpdatePlacementAsync(expired, Arg.Any<CancellationToken>()).Returns(new ServiceResult<PlacementMutationSuccess>(
            ServiceProblem.Conflict("expired") with
            {
                Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                { [PlacementMutationRejection.ExpiredExtension] = expired.OperationId.ToString("D") }
            }));
        var cut = RenderPanel(selectedParticipantId: 301, owner: "actor:club");
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Recover save"));
        await RecoveryButton(cut, "Recover save").ClickAsync(new MouseEventArgs());
        RecoveryButton(cut, "Review current placement").HasAttribute("disabled").ShouldBeFalse();

        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(replacement, null));
        if (string.Equals(transition, "owner", StringComparison.Ordinal))
        {
            cut.Render(parameters => parameters.Add(component => component.Owner, "other:club"));
        }
        else if (string.Equals(transition, "closure", StringComparison.Ordinal))
        {
            cut.Render(parameters => parameters.Add(component => component.CampaignStatus, CampaignStatus.Closed)
                .Add(component => component.CanEditPlacements, false));
        }
        else
        {
            // A failed retry exposes storage attachment again; another context replaced its bytes meanwhile.
            _placementStorage.SetupVoid("writePending", _ => true).SetException(new JSException("unavailable"));
            await RecoveryButton(cut, "Recover save").ClickAsync(new MouseEventArgs());
            await RecoveryButton(cut, "Retry storage").ClickAsync(new MouseEventArgs());
            _placementStorage.SetupVoid("writePending", _ => true).SetVoidResult();
        }

        await cut.WaitForAssertionAsync(() => RecoveryButton(cut, "Recover save").HasAttribute("disabled").ShouldBeFalse());
        cut.FindAll("button").ShouldNotContain(button => string.Equals(Collapse(button), "Review current placement", StringComparison.Ordinal));
        _placementStorage.Invocations.ShouldNotContain(call => string.Equals(call.Identifier, "clearPending", StringComparison.Ordinal));
        SaveControlsBlocked(cut);
        await RecoveryButton(cut, "Recover save").ClickAsync(new MouseEventArgs());
        _ = _mutations.Received(1).UpdatePlacementAsync(replacement, Arg.Any<CancellationToken>());
        _placementStorage.Invocations.Where(call => string.Equals(call.Identifier, "clearPending", StringComparison.Ordinal))
            .ShouldAllBe(call => Equals(call.Arguments[1], replacement.OperationId));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DurableRefusalSurvivesClosureButCannotLeakIntoAnotherOwnerAsync(bool changesOwner)
    {
        var queries = RegisterServices();
        const string Refusal = "The chosen team is no longer eligible.";
        _mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(call => new ServiceResult<PlacementMutationSuccess>(PlacementMutationRejection.NotCommitted(
                ServiceProblem.Validation("TeamId", Refusal), call.Arg<UpdateCampaignPlacementInput>().OperationId)));
        var cut = RenderPanel(selectedParticipantId: 301, owner: "actor:club");
        await cut.WaitForAssertionAsync(() => cut.FindAll("#place-outcome").Count.ShouldBe(1));
        var (entered, release) = HoldNextQueueRead(queries);
        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = nameof(PlacementOutcome.NotSelected) });
        var save = SaveButton(cut).ClickAsync(new MouseEventArgs());
        try
        {
            await cut.WaitForAssertionAsync(() => entered.IsCompleted.ShouldBeTrue());
            cut.Find(".place-status[role='alert']").TextContent.ShouldContain(Refusal);
            cut.Render(parameters => parameters.Add(component => component.CampaignStatus, CampaignStatus.Closed)
                .Add(component => component.CanEditPlacements, false)
                .Add(component => component.Owner, changesOwner ? "other:club" : "actor:club"));
        }
        finally
        {
            await cut.InvokeAsync(release);
            await save;
        }
        await cut.WaitForAssertionAsync(() => cut.FindAll(".place-readonly").Count.ShouldBeGreaterThan(0));
        if (changesOwner)
        {
            cut.Markup.ShouldNotContain(Refusal);
            cut.Render(parameters => parameters.Add(component => component.Owner, "actor:club"));
            await cut.WaitForAssertionAsync(() => cut.FindAll(".place-readonly").Count.ShouldBeGreaterThan(0));
            cut.Markup.ShouldNotContain(Refusal);
        }
        else { cut.Find(".place-status[role='alert']").TextContent.ShouldContain(Refusal); }
        cut.FindAll("#place-outcome").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Recover save");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedInvalidStorageCleanupRequiresFreshEvidenceAndNeverSavesAsync(bool readFails)
    {
        var queries = RegisterServices();
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, "{bad"));
        var cut = RenderPanel(status: CampaignStatus.Closed, selectedParticipantId: 301, canEdit: false);
        await cut.WaitForAssertionAsync(() => RecoveryButton(cut, "Discard invalid data and refresh").HasAttribute("disabled").ShouldBeFalse());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, null));
        _placementStorage.Setup<bool>("discardInvalidPending", _ => true).SetResult(true);
        if (readFails)
        {
            queries.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.Forbidden("membership changed")));
        }
        await RecoveryButton(cut, "Discard invalid data and refresh").ClickAsync(new MouseEventArgs());
        var deletes = _placementStorage.Invocations.Where(call => string.Equals(call.Identifier, "discardInvalidPending", StringComparison.Ordinal)).ToList();
        deletes.Count.ShouldBe(readFails ? 0 : 1);
        if (!readFails) { deletes[0].Arguments[1].ShouldBe("{bad"); }
        cut.Markup.ShouldContain(readFails ? "Invalid recovery data is retained" : "earlier save result remains unknown");
        cut.FindAll("#place-outcome, #place-team").ShouldBeEmpty();
        _ = _mutations.DidNotReceive().UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClosedUnavailableStorageCanRetryAndRecoverTheExactCommandAsync()
    {
        RegisterServices();
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetException(new JSException("unavailable"));
        var cut = RenderPanel(status: CampaignStatus.Closed, selectedParticipantId: 301, canEdit: false);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Retry storage"));
        var pending = new UpdateCampaignPlacementInput(301, PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7());
        _placementStorage.Setup<PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(pending, null));
        await RecoveryButton(cut, "Retry storage").ClickAsync(new MouseEventArgs());
        await RecoveryButton(cut, "Recover save").ClickAsync(new MouseEventArgs());
        _ = _mutations.Received(1).UpdatePlacementAsync(pending, Arg.Any<CancellationToken>());
        cut.Markup.ShouldContain("Placement saved.");
        cut.FindAll("#place-outcome, #place-team").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingParticipantHidesThePreviousHistoryCursorThroughoutReloadAsync(bool readFails)
    {
        RegisterServices(rows: [CreateRow(301), Row(302, "Second", "Player")]);
        var pending = new TaskCompletionSource<ServiceResult<PlacementContextResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var input = call.Arg<GetPlacementContextInput>();
                if (input.PlayerCampaignAssignmentId == 302) { return pending.Task; }
                return Task.FromResult(new ServiceResult<PlacementContextResult>(new PlacementContextResult(301, null, [],
                    input.BeforeEventId is null ? 81 : null, false)));
            });
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Earlier changes"));
        await RecoveryButton(cut, "Earlier changes").ClickAsync(new MouseEventArgs());
        cut.Find(".place-history").TextContent.ShouldContain("Latest changes");
        cut.Render(parameters => parameters.Add(component => component.SelectedParticipantId, 302));
        await cut.WaitForAssertionAsync(() => SheetName(cut).ShouldBe("Second Player"));
        cut.Find(".place-history").TextContent.ShouldNotContain("Latest changes");
        await cut.InvokeAsync(() => pending.SetResult(readFails ? ServiceProblem.ServerError("offline")
            : new ServiceResult<PlacementContextResult>(new PlacementContextResult(302, null, [], null, false))));
        await cut.WaitForAssertionAsync(() => cut.Find(".place-history").TextContent.ShouldContain(readFails ? "Retry history" : "No recorded placement changes"));
        cut.Find(".place-history").TextContent.ShouldNotContain("Latest changes");
    }

    private static AngleSharp.Dom.IElement RecoveryButton(IRenderedComponent<CampaignPlacePanel> cut, string text)
        => cut.FindAll("button").Single(button => string.Equals(Collapse(button), text, StringComparison.Ordinal));
}
