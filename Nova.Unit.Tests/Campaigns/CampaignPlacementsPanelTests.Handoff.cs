using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementsPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SaveDefersLatestHandoffPairAndDoesNotReloadOrRefocusItTwiceAsync(bool changeFilters, bool returnToApplied)
    {
        var save = new TaskCompletionSource<ServiceResult<PlacementMutationSuccess>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reload = new TaskCompletionSource<ServiceResult<PagedResult<CampaignPlacementRosterItem>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = ConfigureHeldHandoff(save.Task, reload.Task);
        var initial = new CampaignWorkspacePlacementState();
        var requested = changeFilters ? new CampaignWorkspacePlacementState { GraduationYear = 2033, UnresolvedOnly = true, Page = 4 } : initial;
        var cut = RenderPanel(state: initial);
        await cut.Find("select[aria-label=\"Outcome for Avery Johnson\"]").ChangeAsync(new ChangeEventArgs { Value = "2" });
        var saving = cut.Find("button.btn-primary").ClickAsync(new MouseEventArgs());
        try
        {
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Saving"));
            await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(panel => panel.State, requested).Add(panel => panel.SelectedParticipantId, 302)));
            await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(panel => panel.SelectedParticipantId, returnToApplied ? null : 303L)));
            _ = query.Received(1).GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());
            FocusRequests().ShouldBe(0);
            save.SetResult(new(new PlacementMutationSuccess(Guid.NewGuid())));
            if (!returnToApplied)
            {
                await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Loading placements..."));
                _ = query.Received(1).GetPlacementRosterAsync(Arg.Is<GetCampaignPlacementRosterInput>(input => input.ParticipantId == 303
                    && input.Page == 1 && input.GraduationYear == null && input.UnresolvedOnly == null), Arg.Any<CancellationToken>());
                reload.SetResult(new(CreateRoster(CreateRosterItem(displayName: "Zoe Carter", firstName: "Zoe", lastName: "Carter", assignmentId: 303))));
            }
            await saving;
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(returnToApplied ? "Avery Johnson" : "Zoe Carter"));
            await cut.WaitForAssertionAsync(() => FocusRequests().ShouldBe(returnToApplied ? 0 : 1));
            await cut.InvokeAsync(() => cut.Render());
            _ = query.Received(returnToApplied ? 1 : 2).GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>());
            FocusRequests().ShouldBe(returnToApplied ? 0 : 1);
        }
        finally
        {
            save.TrySetResult(new(new PlacementMutationSuccess(Guid.NewGuid())));
            reload.TrySetResult(new(CreateRoster()));
        }
    }

    private int FocusRequests() => JSInterop.Invocations.Count(invocation => string.Equals(invocation.Identifier, "Blazor._internal.domWrapper.focus", StringComparison.Ordinal));

    private ICampaignPlacementQueryService ConfigureHeldHandoff(Task<ServiceResult<PlacementMutationSuccess>> save,
        Task<ServiceResult<PagedResult<CampaignPlacementRosterItem>>> reload)
    {
        var mutation = Substitute.For<ICampaignPlacementService>();
        mutation.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>()).Returns(save);
        var query = Substitute.For<ICampaignPlacementQueryService>();
        query.GetPlacementRosterAsync(Arg.Any<GetCampaignPlacementRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PagedResult<CampaignPlacementRosterItem>>(CreateRoster())), reload);
        query.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignPlacementSummaryDto>(CreateSummary())));
        RegisterServices(placementQueryService: query, placementService: mutation);
        return query;
    }
}
