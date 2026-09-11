using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Fact]
    public async Task TraitOnlyDraftProtectsDepartureUntilExplicitDiscardAsync()
    {
        var module = PrepareEvaluationNavigation();
        var cut = Panel(new() { ParticipantId = 301 });
        await EnterTraitDraftAsync(cut);
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/next", "next-history"));

        await Button(cut, "Keep working").ClickAsync(new MouseEventArgs());
        cut.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe("Quick feet");
        module.ResumedKeys.ShouldBeEmpty();
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/next", "next-history"));
        await Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());

        module.ResumedKeys.ShouldBe(["next-history"]);
        using var stored = JsonDocument.Parse(_storage.Writes[^1]);
        stored.RootElement.GetProperty("TraitSearch").GetString().ShouldBeEmpty();
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TraitOnlyDraftReloadRestoresPickerAndClosedTextRemainsCopyableAsync(bool closed)
    {
        var cut = Panel(new() { ParticipantId = 301 });
        await EnterTraitDraftAsync(cut);
        _storage.ReadSnapshot = _storage.Writes[^1];
        cut.Dispose();

        var restored = Panel(new() { ParticipantId = 301 }, closed ? CampaignStatus.Closed : CampaignStatus.Active);

        await restored.WaitForAssertionAsync(() => restored.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe("Quick feet"));
        restored.Find("#evaluation-trait-search").HasAttribute("readonly").ShouldBe(closed);
        restored.Find(".evaluation-workspace").GetAttribute("data-evidence-protected").ShouldBe("true");
        if (closed) { restored.Markup.ShouldContain("Your unsubmitted trait text remains available to copy."); }
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TraitDraftStorageFailureAndRetryPreserveLabelWithoutDispatchAsync()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        _storage.ThrowWrites = true;
        await EnterTraitDraftAsync(cut);
        await cut.FindAll("button").Single(button => button.TextContent.StartsWith("Create and apply", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());
        _tags.ReceivedCalls().ShouldBeEmpty();
        cut.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe("Quick feet");

        _storage.ThrowWrites = false;
        await Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());

        cut.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe("Quick feet");
        using var stored = JsonDocument.Parse(_storage.Writes[^1]);
        stored.RootElement.GetProperty("TraitSearch").GetString().ShouldBe("Quick feet");
        stored.RootElement.GetProperty("Pending").ValueKind.ShouldBe(JsonValueKind.Null);
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SavingNeighboringNotePreservesUnsubmittedTraitLabelAsync()
    {
        var cut = Panel(new() { ParticipantId = 301 });
        await EnterTraitDraftAsync(cut);
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Shared observation" });

        await Button(cut, "Save note").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note saved."));
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBeEmpty();
        cut.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe("Quick feet");
        cut.Find(".evaluation-workspace").GetAttribute("data-evidence-protected").ShouldBe("true");
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyingExistingTraitClearsCompletedLookupAndPreservesNoteDraftAsync()
    {
        _evidence.GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<EvaluationTagChoice>>(new EvaluationTagChoice[] { new(12, "Quick feet", "#65743A", null) })));
        _tags.ApplyAsync(Arg.Any<ApplyCampaignTagApplicationInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignTagApplicationMutationSuccess>(new CampaignTagApplicationMutationSuccess(2, 12, false,
                new(call.Arg<ApplyCampaignTagApplicationInput>().OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))))));
        var cut = Panel(new() { ParticipantId = 301 });
        await EnterTraitDraftAsync(cut);
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Unsubmitted neighboring note" });

        await Button(cut, "Quick feet").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Trait applied."));
        cut.FindAll("#evaluation-trait-search").ShouldBeEmpty();
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Unsubmitted neighboring note");
        using var stored = JsonDocument.Parse(_storage.Writes[^1]);
        stored.RootElement.GetProperty("TraitSearch").GetString().ShouldBeEmpty();
        _ = _tags.Received(1).ApplyAsync(Arg.Is<ApplyCampaignTagApplicationInput>(input => input.PlayerTagId == 12), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbiguousTraitCreationReloadRetainsExactLabelAndReceiptEvenWhenClosedAsync(bool closed)
    {
        var inputs = new List<CreateAndApplyCampaignTagInput>();
        _tags.CreateAndApplyAsync(Arg.Any<CreateAndApplyCampaignTagInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreateAndApplyCampaignTagInput>();
            inputs.Add(input);
            return inputs.Count == 1
                ? Task.FromException<ServiceResult<CampaignTagApplicationMutationSuccess>>(new HttpRequestException("Lost committed receipt"))
                : Task.FromResult(TraitDraftReceipt(input));
        });
        var cut = Panel(new() { ParticipantId = 301 });
        await EnterTraitDraftAsync(cut);
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Neighboring unsubmitted note" });
        await cut.FindAll("button").Single(button => button.TextContent.StartsWith("Create and apply", StringComparison.Ordinal)).ClickAsync(new MouseEventArgs());
        _storage.ReadSnapshot = _storage.Writes[^1];
        cut.Dispose();
        var restored = Panel(new() { ParticipantId = 301 }, closed ? CampaignStatus.Closed : CampaignStatus.Active);

        await restored.WaitForAssertionAsync(() => restored.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe("Quick feet"));
        restored.Find("#evaluation-trait-search").HasAttribute("readonly").ShouldBeTrue();
        await Button(restored, "Retry original operation").ClickAsync(new MouseEventArgs());

        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("Trait applied."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
        inputs[0].Label.ShouldBe("Quick feet");
        restored.Find("#evaluation-note").GetAttribute("value").ShouldBe("Neighboring unsubmitted note");
        using var stored = JsonDocument.Parse(_storage.Writes[^1]);
        stored.RootElement.GetProperty("TraitSearch").GetString().ShouldBeEmpty();
        stored.RootElement.GetProperty("Pending").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task NewParticipantDoesNotDisplayPreviousIdentityErrorWhileLoadingAsync()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignParticipantDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => pending.TrySetResult(new(Identity(302))));
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GetCampaignParticipantDetailInput>().PlayerCampaignAssignmentId == 301
                ? Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(ServiceProblem.ServerError("Previous participant identity failure"))) : pending.Task);
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Previous participant identity failure"));

        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));

        cut.Markup.ShouldContain("Loading player identity");
        cut.Markup.ShouldNotContain("Previous participant identity failure");
        cut.FindAll(".evaluation-place").ShouldBeEmpty();
        pending.SetResult(new(Identity(302)));
        await cut.WaitForAssertionAsync(() => cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Alex Morgan"));
        cut.Markup.ShouldNotContain("Previous participant identity failure");
    }

    [Fact]
    public async Task OwnerSwitchClearsTraitPickerDraftAndRemovalConfirmationAsync()
    {
        _evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>(
                [new(1, 11, "Strong", "#65743A", false, "Coach Rivera", DateTimeOffset.UtcNow, true)], null))));
        var cut = Panel(new() { ParticipantId = 301 });
        await EnterTraitDraftAsync(cut);
        await Button(cut, "Remove").ClickAsync(new MouseEventArgs());

        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));

        await cut.WaitForAssertionAsync(() => cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Alex Morgan"));
        cut.FindAll("#evaluation-trait-search").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Confirm removal");
        await Button(cut, "Add a trait").ClickAsync(new MouseEventArgs());
        cut.Find("#evaluation-trait-search").GetAttribute("value").ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    private static async Task EnterTraitDraftAsync(IRenderedComponent<Nova.UI.Features.Campaigns.Components.CampaignEvaluationPanel> cut)
    {
        await cut.WaitForAssertionAsync(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        await Button(cut, "Add a trait").ClickAsync(new MouseEventArgs());
        await cut.Find("#evaluation-trait-search").InputAsync(new ChangeEventArgs { Value = "Quick feet" });
    }

    private static ServiceResult<CampaignTagApplicationMutationSuccess> TraitDraftReceipt(CreateAndApplyCampaignTagInput input) =>
        new(new CampaignTagApplicationMutationSuccess(2, 12, false,
            new(input.OperationId, input.PlayerCampaignAssignmentId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))));
}
