using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignWorkspaceTests
{
    [Fact]
    public async Task PersistentClosedHistoryConflictRetainsDrawerAndBoundsLifecycleReconciliationAsync()
    {
        var participants = Substitute.For<ICampaignParticipantQueryService>();
        participants.GetParticipantRosterAsync(Arg.Any<GetCampaignParticipantRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<PagedResult<CampaignParticipantRosterItem>>(CreateRoster()));
        participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignParticipantDetailDto>(CreateParticipantDetail() with { CampaignStatus = CampaignStatus.Closed }));
        var reads = CreateEffectiveFixture(participants);
        var detailReads = 0;
        var campaigns = Substitute.For<ICampaignQueryService>();
        campaigns.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await Task.Yield();
            // Fail closed after a runaway refresh so a regressed renderer cannot keep this test alive.
            return ++detailReads <= 4 ? new ServiceResult<CampaignDetailResult>(CreateDetail(status: CampaignStatus.Closed))
                : new ServiceResult<CampaignDetailResult>(ServiceProblem.ServerError("Unbounded lifecycle reconciliation"));
        });
        RegisterServices(campaignQueryService: campaigns, participantQueryService: participants, effectivePlacementQueryService: reads);
        Services.GetRequiredService<ICampaignCloseoutQueryService>().GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness() with { Status = CampaignStatus.Closed }));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster?participant=301");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var drawer = cut.FindComponent<CampaignParticipantDrawer>().Instance;
        var healthyRoster = await reads.GetClosedCampaignRosterAsync(new() { CampaignId = 10 }, Xunit.TestContext.Current.CancellationToken);
        reads.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(async _ => { await Task.Yield(); return new ServiceResult<ClosedCampaignRosterResult>(ServiceProblem.Conflict("The Closed campaign contains an incomplete decision record.")); });

        await cut.InvokeAsync(() => drawer.OnLifecycleChanged.InvokeAsync());

        await cut.WaitForAssertionAsync(() => cut.Find(".workspace-board .alert-danger").TextContent.ShouldContain("incomplete decision record"));
        cut.FindComponent<CampaignParticipantDrawer>().Instance.ShouldBeSameAs(drawer);
        detailReads.ShouldBe(2);
        await cut.InvokeAsync(() => cut.Render());
        detailReads.ShouldBe(2);
        cut.Markup.ShouldNotContain("Loading campaign...");
        cut.Markup.ShouldNotContain("Unbounded lifecycle reconciliation");
        reads.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>()).Returns(healthyRoster);
        await cut.Find(".workspace-board .alert-danger button").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.FindAll(".workspace-board .alert-danger").ShouldBeEmpty());
        cut.Find("#roster-row-301").TextContent.ShouldContain("Avery Johnson");
        cut.FindComponent<CampaignParticipantDrawer>().Instance.ShouldBeSameAs(drawer);
        detailReads.ShouldBe(2);
    }
}
