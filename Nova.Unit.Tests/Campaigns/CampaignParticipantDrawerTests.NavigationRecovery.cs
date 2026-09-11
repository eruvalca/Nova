using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using NSubstitute;
using Shouldly;
using CampaignParticipantDrawerComponent = Nova.UI.Features.Campaigns.Components.CampaignParticipantDrawer;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignParticipantDrawerTests
{
    [Fact]
    public async Task DrawerUnreadableRecoveryCanKeepWorkingThenLeaveWithoutChangingStoredDataAsync()
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var (storage, module) = PrepareDrawerUnreadableNavigation(notes);
        module.ResumeResults.Enqueue(Task.FromResult(false));
        var cut = StorageRecoveryDrawer();
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/retained", "retained-history"));
        cut.FindAll("p").ShouldContain(paragraph => paragraph.TextContent.Contains("a previous submission's outcome may be unknown", StringComparison.Ordinal));

        await FindButtonByText(cut, "Keep working").ClickAsync(new MouseEventArgs());

        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Contains("Leave and keep", StringComparison.Ordinal));
        module.ReleasedOwners.ShouldBeEmpty();
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/retained", "retained-history"));
        await FindButtonByText(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());
        module.CanceledOwners.ShouldBe([module.Owner]);
        cut.Markup.ShouldContain("Navigation was interrupted");
        await FindButtonByText(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());

        module.RetainUnreadable.ShouldBe([true, true]);
        module.ResumedKeys.ShouldBe(["retained-history", "retained-history"]);
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Contains("Leave and keep", StringComparison.Ordinal));
        storage.ReadJson.ShouldBe("{}");
        storage.ClearCalls.ShouldBe(0);
        storage.SuccessfulCalls.ShouldNotContain("writeOperation", StringComparer.Ordinal);
        notes.ReceivedCalls().ShouldBeEmpty();
        Services.GetRequiredService<ICampaignTagApplicationService>().ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawerUnreadableDepartureCannotOutliveRestorationOrOwnerChangeAsync(bool changeOwner)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var (storage, module) = PrepareDrawerUnreadableNavigation(notes);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => release.TrySetResult());
        module.ReleaseResults.Enqueue(release.Task);
        var cut = StorageRecoveryDrawer();
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        var owner = module.Owner;
        var originalUrl = Services.GetRequiredService<NavigationManager>().Uri;
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(owner, module.Lease, "/obsolete", "obsolete-history"));
        var leave = FindButtonByText(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ReleasedOwners.ShouldBe([owner]));
        if (changeOwner)
        {
            await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "new-owner:club-2:False")));
            await cut.WaitForAssertionAsync(() => module.Owner.ShouldNotBe(owner, StringComparer.Ordinal));
        }
        else
        {
            storage.ReadJson = StoredDrawerAdd(new() { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Original pending evidence" });
            await FindButtonByText(cut, "Retry recovery storage").ClickAsync(new MouseEventArgs());
            cut.Find("textarea").GetAttribute("value").ShouldBe("Original pending evidence");
        }
        if (changeOwner) { await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "newest-history")); }

        release.SetResult();
        await leave;

        if (!changeOwner) { await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "newest-history")); }
        module.ResumedKeys.ShouldBeEmpty();
        module.RetainUnreadable.ShouldBe([true]);
        module.ReleasedOwners.ShouldBe([owner]);
        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(originalUrl);
        if (changeOwner) { module.CanceledOwners.ShouldNotContain(module.Owner, StringComparer.Ordinal); FindButtonByText(cut, "Leave and keep recovery data").ShouldNotBeNull(); }
        else { FindButtonByText(cut, "Recover original operation").ShouldNotBeNull(); cut.FindAll("button").ShouldNotContain(button => button.TextContent.Contains("Leave and keep", StringComparison.Ordinal)); }
        storage.ClearCalls.ShouldBe(0);
        storage.SuccessfulCalls.ShouldNotContain("writeOperation", StringComparer.Ordinal);
        notes.ReceivedCalls().ShouldBeEmpty();
        Services.GetRequiredService<ICampaignTagApplicationService>().ReceivedCalls().ShouldBeEmpty();
    }

    private (StorageRecoveryModule Storage, NavigationRecoveryModule Navigation) PrepareDrawerUnreadableNavigation(ICampaignEvaluationNoteService notes)
    {
        RegisterMutableDrawer(notes);
        Services.AddSingleton(_ => new StorageRecoveryModule(Substitute.For<IJSObjectReference>()) { ReadJson = "{}" });
        Services.AddSingleton(provider => new NavigationRecoveryModule(provider.GetRequiredService<StorageRecoveryModule>()));
        Services.AddSingleton<IJSRuntime>(provider => new NavigationRecoveryRuntime(JSInterop.JSRuntime, provider.GetRequiredService<NavigationRecoveryModule>()));
        return (Services.GetRequiredService<StorageRecoveryModule>(), Services.GetRequiredService<NavigationRecoveryModule>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DrawerInterruptedHistoryDepartureAcceptsNewestPromptAndRetryAsync(bool throws, bool newerBeforeCompletion)
    {
        var module = PrepareDrawerNavigation();
        var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => resume.TrySetResult(false));
        module.ResumeResults.Enqueue(resume.Task);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/original", "old-history"));
        var discard = FindButtonByText(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ResumedKeys.ShouldBe(["old-history"]));
        if (newerBeforeCompletion)
        {
            await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "New draft during departure" });
            await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "new-history"));
            FindButtonByText(cut, "Discard and leave").ShouldNotBeNull();
        }
        if (throws) { resume.SetException(new JSException("Original history replay failed")); }
        else { resume.SetResult(false); }
        await discard;
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Discard and leave").ShouldNotBeNull());
        if (!newerBeforeCompletion)
        {
            cut.Markup.ShouldContain(throws ? "Navigation could not continue" : "Navigation was interrupted");
            await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "new-history"));
        }
        else
        {
            cut.Find("textarea").GetAttribute("value").ShouldBe("New draft during departure");
            cut.Markup.ShouldNotContain("Navigation could not continue");
            cut.Markup.ShouldNotContain("Navigation was interrupted");
        }

        await FindButtonByText(cut, "Discard and leave").ClickAsync(new MouseEventArgs());

        module.ResumedKeys.ShouldBe(["old-history", "new-history"]);
        module.RetainUnreadable.ShouldBe([false, false]);
        module.ReleasedOwners.Count.ShouldBe(2);
        module.ReleasedOwners.ShouldAllBe(owner => string.Equals(owner, module.Owner, StringComparison.Ordinal));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldNotContain("/original");
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Trim() == "Discard and leave");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawerOldReleaseCompletionCannotNavigateOrOverwriteNewOwnerAsync(bool throws)
    {
        var module = PrepareDrawerNavigation();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => release.TrySetResult());
        module.ReleaseResults.Enqueue(release.Task);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        var originalOwner = module.Owner;
        var originalUrl = Services.GetRequiredService<NavigationManager>().Uri;
        await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/old-owner-destination", null));
        var discard = FindButtonByText(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ReleasedOwners.ShouldBe([originalOwner]));
        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "new-owner:club-2:False")));
        await cut.WaitForAssertionAsync(() => module.Owner.ShouldNotBe(originalOwner, StringComparer.Ordinal));
        await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "New owner draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/new-owner-destination", "new-owner-history"));

        if (throws) { release.SetException(new JSException("Old owner release failed")); }
        else { release.SetResult(); }
        await discard;

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(originalUrl);
        module.ReleasedOwners.ShouldBe([originalOwner]);
        module.CanceledOwners.ShouldNotContain(module.Owner, StringComparer.Ordinal);
        module.ResumedKeys.ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Navigation could not continue");
        cut.Markup.ShouldNotContain("Navigation was interrupted");
        cut.Find("textarea").GetAttribute("value").ShouldBe("New owner draft");
        await FindButtonByText(cut, "Keep working").ClickAsync(new MouseEventArgs());
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Trim() == "Discard and leave");
        cut.Find("textarea").GetAttribute("value").ShouldBe("New owner draft");
    }

    [Fact]
    public async Task DrawerNewDraftDuringReleaseRevokesDepartureWithoutLosingDraftAsync()
    {
        var module = PrepareDrawerNavigation();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => release.TrySetResult());
        module.ReleaseResults.Enqueue(release.Task);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        var owner = module.Owner;
        var originalUrl = Services.GetRequiredService<NavigationManager>().Uri;
        await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(owner, module.Lease, "/history-target", "history-key"));
        var departure = FindButtonByText(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ReleasedOwners.ShouldBe([owner]));

        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "New draft while release waits" });
        release.SetResult();
        await departure;

        module.CanceledOwners.ShouldBe([owner]);
        module.ResumedKeys.ShouldBeEmpty();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(originalUrl);
        cut.Find("textarea").GetAttribute("value").ShouldBe("New draft while release waits");
        await FindButtonByText(cut, "Keep working").ClickAsync(new MouseEventArgs());
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Trim() == "Discard and leave");
        cut.Find("textarea").GetAttribute("value").ShouldBe("New draft while release waits");
    }

    private NavigationRecoveryModule PrepareDrawerNavigation()
    {
        RegisterMutableDrawer(Substitute.For<ICampaignEvaluationNoteService>());
        var module = new NavigationRecoveryModule(Substitute.For<IJSObjectReference>());
        Services.AddSingleton<IJSRuntime>(new NavigationRecoveryRuntime(JSInterop.JSRuntime, module));
        return module;
    }
}
