using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Campaigns;
using NSubstitute;
using Shouldly;
using CampaignParticipantDrawerComponent = Nova.UI.Features.Campaigns.Components.CampaignParticipantDrawer;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignParticipantDrawerTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("close")]
    [InlineData("previous")]
    [InlineData("next")]
    [InlineData("navigation")]
    public async Task DrawerUnreadableRecoveryGuardsCallbacksAndCodeNavigationUntilConfirmedAsync(string action)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var (storage, module) = PrepareDrawerUnreadableNavigation(notes);
        var moves = new List<string>();
        var cut = NavigationProtectionDrawer(moves);
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();

        await AttemptDrawerNavigationAsync(cut, navigation, action);

        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Leave and keep recovery data").ShouldNotBeNull());
        moves.ShouldBeEmpty();
        navigation.Uri.ShouldBe(originalUrl);
        await FindButtonByText(cut, "Keep working").ClickAsync(new MouseEventArgs());
        module.ReleasedOwners.ShouldBeEmpty();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        await AttemptDrawerNavigationAsync(cut, navigation, action);
        await FindButtonByText(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());

        if (string.Equals(action, "navigation", StringComparison.Ordinal))
        {
            await cut.WaitForAssertionAsync(() => navigation.Uri.ShouldEndWith("/campaigns/10?tab=evaluate"));
            moves.ShouldBeEmpty();
        }
        else { moves.ShouldBe([action]); navigation.Uri.ShouldBe(originalUrl); }
        module.RetainUnreadable.ShouldBe([true]);
        // These callback fakes deliberately complete without changing the participant owner.
        // A no-op parent move must rearm the native guard as well as NavigationLock.
        module.CanceledOwners.ShouldBe([module.Owner]);
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        storage.ReadJson.ShouldBe("{}");
        storage.ClearCalls.ShouldBe(0);
        storage.SuccessfulCalls.ShouldNotContain("writeOperation", StringComparer.Ordinal);
        notes.ReceivedCalls().ShouldBeEmpty();
        Services.GetRequiredService<ICampaignTagApplicationService>().ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("close")]
    [InlineData("navigation")]
    public async Task DrawerUnreadableProtectionRemainsWhileRetryReadWaitsAsync(string action)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var (storage, module) = PrepareDrawerUnreadableNavigation(notes);
        var moves = new List<string>();
        var cut = NavigationProtectionDrawer(moves);
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => read.TrySetResult(null));
        storage.ReadResults.Enqueue(read.Task);
        var retry = FindButtonByText(cut, "Retry recovery storage").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => storage.ReadOwners.Count.ShouldBe(2));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var originalUrl = navigation.Uri;

        await AttemptDrawerNavigationAsync(cut, navigation, action);

        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Keep working").ShouldNotBeNull());
        moves.ShouldBeEmpty();
        navigation.Uri.ShouldBe(originalUrl);
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        read.SetResult(StoredDrawerAdd(new() { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Pending original evidence" }));
        await retry;
        cut.Find("textarea").GetAttribute("value").ShouldBe("Pending original evidence");
        FindButtonByText(cut, "Recover original operation").ShouldNotBeNull();
        cut.FindAll("button").ShouldNotContain(button => button.TextContent.Contains("Leave and keep", StringComparison.Ordinal));
        moves.ShouldBeEmpty();
        navigation.Uri.ShouldBe(originalUrl);
        module.ReleasedOwners.ShouldBeEmpty();
        notes.ReceivedCalls().ShouldBeEmpty();
    }

    private IRenderedComponent<CampaignParticipantDrawerComponent> NavigationProtectionDrawer(List<string> moves)
        => Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.HasPrevious, true).Add(component => component.HasNext, true)
            .Add(component => component.OnClose, () => moves.Add("close"))
            .Add(component => component.OnPrevious, () => moves.Add("previous"))
            .Add(component => component.OnNext, () => moves.Add("next")));

    private static Task AttemptDrawerNavigationAsync(IRenderedComponent<CampaignParticipantDrawerComponent> cut, NavigationManager navigation, string action)
        => action switch
        {
            "close" => cut.Find("button[aria-label='Close participant details']").ClickAsync(new MouseEventArgs()),
            "previous" => cut.Find("button[aria-label='Previous participant']").ClickAsync(new MouseEventArgs()),
            "next" => cut.Find("button[aria-label='Next participant']").ClickAsync(new MouseEventArgs()),
            "navigation" => cut.InvokeAsync(() => navigation.NavigateTo("/campaigns/10?tab=evaluate")),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
}
