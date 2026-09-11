using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationUnreadableRecoveryGuardsFormAndCodeNavigationUntilConfirmedAsync(bool submitSearch)
    {
        var (storage, module) = PrepareEvaluationUnreadableNavigation();
        var cut = Panel(new() { Search = "42", ParticipantId = 301 });
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        cut.Find("a[data-eval-result]").GetAttribute("aria-current").ShouldBe("page");
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();

        await AttemptEvaluationNavigationAsync(cut, navigation, submitSearch);

        await cut.WaitForAssertionAsync(() => Button(cut, "Leave and keep recovery data").ShouldNotBeNull());
        navigation.Uri.ShouldBe(originalUrl);
        await Button(cut, "Keep working").ClickAsync(new MouseEventArgs());
        module.ReleasedOwners.ShouldBeEmpty();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        await AttemptEvaluationNavigationAsync(cut, navigation, submitSearch);
        await Button(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => navigation.Uri.ShouldContain(submitSearch ? "evalSearch=42" : "tab=roster"));
        module.RetainUnreadable.ShouldBe([true]);
        storage.ReadJson.ShouldBe("{}");
        storage.ClearCalls.ShouldBe(0);
        module.WriteCalls.ShouldBe(0);
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task EvaluationUnreadableProtectionRemainsWhileRetryReadWaitsAsync()
    {
        var (storage, module) = PrepareEvaluationUnreadableNavigation();
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => read.TrySetResult(null));
        storage.ReadResults.Enqueue(read.Task);
        var retry = Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => storage.ReadOwners.Count.ShouldBe(2));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;

        await cut.InvokeAsync(() => navigation.NavigateTo("/campaigns/10?tab=roster"));

        await cut.WaitForAssertionAsync(() => cut.FindAll(".evaluation-protection").Count.ShouldBe(1));
        navigation.Uri.ShouldBe(originalUrl);
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        read.SetResult(EvaluationRecoverySnapshot(new() { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Pending original evidence" }));
        await retry;
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Pending original evidence");
        cut.FindAll(".evaluation-protection button").ShouldContain(button => string.Equals(button.TextContent.Trim(), "Retry original operation", StringComparison.Ordinal));
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Contains("Leave and keep", StringComparison.Ordinal));
        navigation.Uri.ShouldBe(originalUrl);
        module.ReleasedOwners.ShouldBeEmpty();
        _notes.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationFailedDiscardPreservesEveryDraftUntilStorageRetryAndConfirmedDepartureAsync(bool throws)
    {
        var module = PrepareEvaluationNavigation();
        var note = Note() with { CanEdit = true };
        SetCapabilityNote(note);
        var cut = Panel(new() { ParticipantId = 301 });
        await PopulateEvaluationDraftsAsync(cut, "Original");
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;
        await cut.InvokeAsync(() => navigation.NavigateTo("/campaigns/10?tab=roster"));
        await cut.WaitForAssertionAsync(() => Button(cut, "Discard and leave").ShouldNotBeNull());
        _storage.FailWrites = !throws;
        _storage.ThrowWrites = throws;

        await Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());

        AssertEvaluationDrafts(cut, "Original");
        navigation.Uri.ShouldBe(originalUrl);
        module.ReleasedOwners.ShouldBeEmpty();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        Button(cut, "Retry storage").ShouldNotBeNull();
        _storage.FailWrites = _storage.ThrowWrites = false;
        await Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());
        AssertEvaluationDrafts(cut, "Original");
        AssertStoredEvaluationDrafts(_storage.Writes[^1], note.Version);

        await Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => navigation.Uri.ShouldEndWith("/campaigns/10?tab=roster"));
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBeEmpty();
        cut.FindAll("#edit-note-1").ShouldBeEmpty();
        module.RetainUnreadable.ShouldBe([false]);
        AssertStoredEvaluationDiscard(_storage.Writes[^1]);
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationOldDiscardWriteCannotClearNewerDraftOrOwnerAsync(bool changeOwner)
    {
        var module = PrepareEvaluationNavigation();
        SetCapabilityNote(Note() with { CanEdit = true });
        var cut = Panel(new() { ParticipantId = 301 });
        await PopulateEvaluationDraftsAsync(cut, "Original");
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;
        await cut.InvokeAsync(() => navigation.NavigateTo("/campaigns/10?tab=roster"));
        await cut.WaitForAssertionAsync(() => Button(cut, "Discard and leave").ShouldNotBeNull());
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => persisted.TrySetResult());
        var writes = module.WriteCalls;
        module.WriteGates.Enqueue(persisted.Task);
        var discard = Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.WriteCalls.ShouldBe(writes + 1));
        if (changeOwner)
        {
            await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Alex Morgan"));
        }
        await PopulateEvaluationDraftsAsync(cut, "Newer");

        persisted.SetResult();
        await discard;

        AssertEvaluationDrafts(cut, "Newer");
        navigation.Uri.ShouldBe(originalUrl);
        module.ReleasedOwners.ShouldBeEmpty();
        module.ResumedKeys.ShouldBeEmpty();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    private static Task AttemptEvaluationNavigationAsync(IRenderedComponent<CampaignEvaluationPanel> cut, NavigationManager navigation, bool submitSearch)
        => submitSearch ? cut.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty)
            : cut.InvokeAsync(() => navigation.NavigateTo("/campaigns/10?tab=roster"));

    private static async Task PopulateEvaluationDraftsAsync(IRenderedComponent<CampaignEvaluationPanel> cut, string prefix)
    {
        await cut.WaitForAssertionAsync(() => cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeFalse());
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = $"{prefix} add draft" });
        if (cut.FindAll("#evaluation-trait-search").Count == 0) { await Button(cut, "Add a trait").ClickAsync(new MouseEventArgs()); }
        await cut.Find("#evaluation-trait-search").InputAsync(new ChangeEventArgs { Value = $"{prefix} trait draft" });
        if (cut.FindAll("#edit-note-1").Count == 0) { await Button(cut, "Edit").ClickAsync(new MouseEventArgs()); }
        await cut.Find("#edit-note-1").InputAsync(new ChangeEventArgs { Value = $"{prefix} edit draft" });
    }

    private static void AssertEvaluationDrafts(IRenderedComponent<CampaignEvaluationPanel> cut, string prefix)
    {
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe($"{prefix} add draft");
        cut.Find("#evaluation-trait-search").GetAttribute("value").ShouldBe($"{prefix} trait draft");
        cut.Find("#edit-note-1").GetAttribute("value").ShouldBe($"{prefix} edit draft");
    }

    private static void AssertStoredEvaluationDrafts(string snapshot, Guid version)
    {
        using var stored = JsonDocument.Parse(snapshot);
        stored.RootElement.GetProperty("Draft").GetString().ShouldBe("Original add draft");
        stored.RootElement.GetProperty("TraitSearch").GetString().ShouldBe("Original trait draft");
        stored.RootElement.GetProperty("EditContent").GetString().ShouldBe("Original edit draft");
        stored.RootElement.GetProperty("EditingNoteId").GetInt64().ShouldBe(1);
        stored.RootElement.GetProperty("EditVersion").GetGuid().ShouldBe(version);
        stored.RootElement.GetProperty("Pending").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static void AssertStoredEvaluationDiscard(string snapshot)
    {
        using var stored = JsonDocument.Parse(snapshot);
        stored.RootElement.GetProperty("Draft").GetString().ShouldBeEmpty();
        stored.RootElement.GetProperty("TraitSearch").GetString().ShouldBeEmpty();
        stored.RootElement.GetProperty("EditContent").GetString().ShouldBeEmpty();
        stored.RootElement.GetProperty("EditingNoteId").ValueKind.ShouldBe(JsonValueKind.Null);
        stored.RootElement.GetProperty("EditVersion").GetGuid().ShouldBe(Guid.Empty);
        stored.RootElement.GetProperty("Pending").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
