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
        cut.WaitForAssertion(() => cut.FindAll("button.place-row").Count.ShouldBe(50));

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
    public void SelectingARowRaisesTheParticipant()
    {
        RegisterServices(rows: [CreateRow(301), CreateRow(302)]);

        long? raised = null;
        var cut = RenderPanel(onSelectionChanged: id => raised = id);
        cut.WaitForAssertion(() => cut.FindAll("button.place-row").Count.ShouldBe(2));

        cut.FindAll("button.place-row")[1].Click();

        raised.ShouldBe(302);
    }
}
