using Bunit;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;
using CampaignWorkspacePlacementState = Nova.UI.Features.Campaigns.Services.CampaignWorkspacePlacementState;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Callback tests for the Place destination: every discovery control must reach the URL owner, because the
/// panel never navigates itself and a silently dropped callback would leave the surface inert.
/// </summary>
public sealed partial class CampaignPlacePanelTests
{
    [Fact]
    public void PagerNextRaisesTheNextPageForTheUrlOwner()
    {
        RegisterServices(rows: [.. Enumerable.Range(1, 50).Select(index => CreateRow(index))], totalCount: 60);

        CampaignWorkspacePlacementState? raised = null;
        var cut = RenderPanel(onStateChanged: state => raised = state);
        cut.WaitForAssertion(() => cut.FindAll("a.place-row").Count.ShouldBe(50));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Next", StringComparison.Ordinal)).Click();

        raised.ShouldNotBeNull();
        raised.Page.ShouldBe(2);
    }

    [Fact]
    public void SelectingASectionRaisesThatSection()
    {
        RegisterServices();

        CampaignWorkspacePlacementState? raised = null;
        var cut = RenderPanel(onStateChanged: state => raised = state);
        cut.WaitForAssertion(() => cut.FindAll("button.place-section").Count.ShouldBe(4));

        cut.FindAll("button.place-section")[1].Click();

        raised.ShouldNotBeNull();
        raised.Eligibility.ShouldBe("OptionalReassignment");
        raised.Page.ShouldBe(1);
    }

    [Fact]
    public void ClearingFiltersRaisesTheDefaultState()
    {
        // Page 3 is a real page for this fixture, so clearing is what the assertion observes rather than the
        // out-of-range clamp.
        RegisterServices(rows: [CreateRow(301)], totalCount: 150);

        CampaignWorkspacePlacementState? raised = null;
        var cut = RenderPanel(
            state: new CampaignWorkspacePlacementState { Search = "Chen", Eligibility = "Resolved", Page = 3 },
            onStateChanged: state => raised = state);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Chen"));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Clear filters", StringComparison.Ordinal)).Click();

        raised.ShouldNotBeNull();
        raised.ShouldBe(new CampaignWorkspacePlacementState());
    }

    [Fact]
    public async Task AReadPastTheLastPageCorrectsThePageRatherThanClaimingAnEmptyCampaignAsync()
    {
        // One row on a 50-row page: page 9 does not exist, and publishing that snapshot would render an
        // empty queue with no pager and claim the campaign has no participants.
        RegisterServices(rows: [CreateRow(301)], totalCount: 1);

        CampaignWorkspacePlacementState? raised = null;
        var cut = RenderPanel(state: new CampaignWorkspacePlacementState { Page = 9 }, onStateChanged: state => raised = state);

        await cut.WaitForAssertionAsync(() => raised.ShouldNotBeNull());
        raised.ShouldNotBeNull();
        raised.Page.ShouldBe(1);
        cut.Markup.ShouldNotContain("No participants in this campaign yet");
    }

    [Fact]
    public void QueueRowsLinkToTheCanonicalSelectionUrl()
    {
        // Selection is URL-backed, so a row is a real local link rather than only an event handler: a direct
        // link, a refresh, and scripting-disabled navigation all reach the same sheet.
        RegisterServices(rows: [CreateRow(301), CreateRow(302)]);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.FindAll("a.place-row").Count.ShouldBe(2));

        var second = cut.Find("#placement-row-302");
        second.LocalName.ShouldBe("a");
        second.GetAttribute("href").ShouldBe("/campaigns/10?placementParticipant=302&tab=place");
        cut.Find("#placement-row-301").GetAttribute("aria-current").ShouldBeNull();
    }

    [Fact]
    public void AReadOnlySheetStillOffersAWayBackToTheQueue()
    {
        // The queue is hidden on narrow viewports while a sheet is open, so this affordance must exist for
        // every selected participant, not only when the decision controls render.
        RegisterServices(rows: [CreateRow(301)]);

        long? raised = -1;
        var cut = RenderPanel(selectedParticipantId: 301, canEdit: false, onSelectionChanged: id => raised = id);
        cut.WaitForAssertion(() => cut.FindAll(".place-name").Count.ShouldBe(1));
        cut.FindAll("#place-outcome").ShouldBeEmpty();

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Back to placements", StringComparison.Ordinal)).Click();

        raised.ShouldBeNull();
    }
}
