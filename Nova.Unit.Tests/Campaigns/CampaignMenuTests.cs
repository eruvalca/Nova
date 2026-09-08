using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.SharedKernel.Features.Campaigns;
using Nova.UI.Features.Campaigns.Components;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign menu disclosure: open/close toggling, role gating, item
/// visibility by campaign status, Escape handling, and item callbacks.
/// </summary>
public sealed class CampaignMenuTests : BunitContext
{
    [Fact]
    public void MenuToggleFlipsAriaExpanded()
    {
        var cut = RenderMenu(isClubAdmin: true);

        var button = cut.Find("button[aria-haspopup='menu']");
        button.GetAttribute("aria-expanded").ShouldBe("false");

        button.Click();
        button.GetAttribute("aria-expanded").ShouldBe("true");

        button.Click();
        button.GetAttribute("aria-expanded").ShouldBe("false");
    }

    [Fact]
    public void MenuRendersNothingForNonAdmin()
    {
        var cut = RenderMenu(isClubAdmin: false);

        cut.Markup.ShouldNotContain("Campaign menu");
        cut.FindAll("[role='menu']").ShouldBeEmpty();
    }

    [Fact]
    public void MenuShowsEditAndCloseAndHidesReopenWhenActive()
    {
        var cut = RenderMenu(isClubAdmin: true, isClosed: false);
        cut.Find("button[aria-haspopup='menu']").Click();

        cut.Markup.ShouldContain("Edit metadata");
        cut.Markup.ShouldContain("Close campaign");
        cut.Markup.ShouldNotContain("Reopen");
    }

    [Fact]
    public void MenuShowsReopenAndHidesEditAndCloseWhenClosed()
    {
        var cut = RenderMenu(isClubAdmin: true, isClosed: true);
        cut.Find("button[aria-haspopup='menu']").Click();

        cut.Markup.ShouldContain("Reopen");
        cut.Markup.ShouldNotContain("Edit metadata");
        cut.Markup.ShouldNotContain("Close campaign");
    }

    [Fact]
    public void MenuEscapeClosesDisclosure()
    {
        var cut = RenderMenu(isClubAdmin: true);
        var button = cut.Find("button[aria-haspopup='menu']");
        button.Click();
        cut.FindAll("[role='menu']").Count.ShouldBe(1);

        button.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll("[role='menu']").ShouldBeEmpty();
    }

    [Fact]
    public void MenuItemClickClosesDisclosureAndInvokesCallback()
    {
        var editCount = 0;
        var closeCount = 0;

        var cut = RenderMenu(
            isClubAdmin: true,
            isClosed: false,
            onEditMetadata: EventCallback.Factory.Create(this, () => editCount++),
            onCloseCampaign: EventCallback.Factory.Create(this, () => closeCount++));
        cut.Find("button[aria-haspopup='menu']").Click();

        cut.FindAll("button[role='menuitem']")
            .Single(button => string.Equals(button.TextContent.Trim(), "Edit metadata", StringComparison.Ordinal))
            .Click();
        editCount.ShouldBe(1);
        closeCount.ShouldBe(0);
        cut.FindAll("[role='menu']").ShouldBeEmpty();

        cut.Find("button[aria-haspopup='menu']").Click();
        cut.FindAll("button[role='menuitem']")
            .Single(button => string.Equals(button.TextContent.Trim(), "Close campaign", StringComparison.Ordinal))
            .Click();
        closeCount.ShouldBe(1);
        editCount.ShouldBe(1);
        cut.FindAll("[role='menu']").ShouldBeEmpty();
    }

    [Fact]
    public void MenuReopenItemInvokesReopenCallback()
    {
        var reopenCount = 0;

        var cut = RenderMenu(
            isClubAdmin: true,
            isClosed: true,
            onReopen: EventCallback.Factory.Create(this, () => reopenCount++));
        cut.Find("button[aria-haspopup='menu']").Click();

        cut.FindAll("button[role='menuitem']")
            .Single(button => string.Equals(button.TextContent.Trim(), "Reopen", StringComparison.Ordinal))
            .Click();
        reopenCount.ShouldBe(1);
        cut.FindAll("[role='menu']").ShouldBeEmpty();
    }

    private IRenderedComponent<CampaignMenu> RenderMenu(
        bool isClubAdmin,
        bool isClosed = false,
        EventCallback? onEditMetadata = null,
        EventCallback? onCloseCampaign = null,
        EventCallback? onReopen = null)
        => Render<CampaignMenu>(parameters =>
        {
            parameters.Add(component => component.IsClubAdmin, isClubAdmin);
            parameters.Add(component => component.IsClosed, isClosed);
            if (onEditMetadata is not null)
            {
                parameters.Add(component => component.OnEditMetadata, onEditMetadata.Value);
            }

            if (onCloseCampaign is not null)
            {
                parameters.Add(component => component.OnCloseCampaign, onCloseCampaign.Value);
            }

            if (onReopen is not null)
            {
                parameters.Add(component => component.OnReopen, onReopen.Value);
            }
        });
}
