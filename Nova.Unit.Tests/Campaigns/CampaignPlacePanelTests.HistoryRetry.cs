using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData(81L)]
    [InlineData(61L)]
    public async Task HistoryRetryRepeatsTheFailedPageCursorAsync(long? failedCursor)
    {
        RegisterServices();
        var requests = new List<long?>();
        var failing = true;
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var cursor = call.Arg<GetPlacementContextInput>().BeforeEventId;
                requests.Add(cursor);
                return cursor == failedCursor && failing ? ServiceProblem.ServerError("offline")
                    : new ServiceResult<PlacementContextResult>(new PlacementContextResult(301, null, [], cursor switch { null => 81, 81 => 61, _ => null }, false));
            });
        var cut = RenderPanel(selectedParticipantId: 301);
        if (failedCursor is not null)
        {
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Earlier changes"));
            await RecoveryButton(cut, "Earlier changes").ClickAsync(new MouseEventArgs());
            if (failedCursor == 61) { await RecoveryButton(cut, "Earlier changes").ClickAsync(new MouseEventArgs()); }
        }
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Retry history"));
        failing = false;
        await RecoveryButton(cut, "Retry history").ClickAsync(new MouseEventArgs());
        requests.TakeLast(2).ShouldBe([failedCursor, failedCursor]);
        cut.Find(".place-history").TextContent.ShouldNotContain("Retry history");
        if (failedCursor is not null)
        {
            cut.Find(".place-history").TextContent.ShouldContain("Latest changes");
            await RecoveryButton(cut, "Latest changes").ClickAsync(new MouseEventArgs());
            requests[^1].ShouldBeNull();
        }
        cut.Find(".place-history").TextContent.ShouldNotContain("Latest changes");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryRetryClearsFailedCursorWhenSelectionOrOwnerChangesAsync(bool ownerChanges)
    {
        RegisterServices(rows: [CreateRow(301), Row(302, "Second", "Player")]);
        var requests = new List<GetPlacementContextInput>();
        var failLatest = false;
        Services.GetRequiredService<IPlacementContextQueryService>()
            .GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var input = call.Arg<GetPlacementContextInput>();
                requests.Add(input);
                return failLatest || input.BeforeEventId is not null ? ServiceProblem.ServerError("offline")
                    : new ServiceResult<PlacementContextResult>(new PlacementContextResult(input.PlayerCampaignAssignmentId, null, [], 81, false));
            });
        var cut = RenderPanel(selectedParticipantId: 301, owner: "actor:club");
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Earlier changes"));
        await RecoveryButton(cut, "Earlier changes").ClickAsync(new MouseEventArgs());
        failLatest = true;
        if (ownerChanges) { cut.Render(parameters => parameters.Add(component => component.Owner, "other:club")); }
        else { cut.Render(parameters => parameters.Add(component => component.SelectedParticipantId, 302)); }
        await cut.WaitForAssertionAsync(() => requests[^1].BeforeEventId.ShouldBeNull());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Retry history"));
        failLatest = false;
        await RecoveryButton(cut, "Retry history").ClickAsync(new MouseEventArgs());
        requests[^1].BeforeEventId.ShouldBeNull();
        requests[^1].PlayerCampaignAssignmentId.ShouldBe(ownerChanges ? 301 : 302);
        cut.Find(".place-history").TextContent.ShouldNotContain("Latest changes");
    }
}
