using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Activity;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task PlacementHistorySkipsNonpositiveAssignmentSnapshotsAsync(long assignmentId)
    {
        ActAs(ClubAMemberId, ClubAId);
        (await SaveAsync(PlacementOutcome.Assigned, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();
        await using (var db = _harness.CreateAdminContext())
        {
            var row = await db.ActivityEvents.SingleAsync(TestContext.Current.CancellationToken);
            var context = JsonSerializer.Deserialize<ClubActivityContext>(row.PayloadJson, _caseInsensitiveJsonOptions).ShouldBeOfType<PlacementContext>();
            var malformed = JsonSerializer.Serialize<ClubActivityContext>(context with { PlayerCampaignAssignmentId = assignmentId });
            await db.ActivityEvents.Where(item => item.ActivityEventId == row.ActivityEventId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.PayloadJson, malformed), TestContext.Current.CancellationToken);
        }

        var result = await CreatePlacementContextService().GetContextAsync(
            new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task ActiveHistorySpansSavedParticipationsWhileClosedHistoryKeepsItsCampaignAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        (await SaveAsync(PlacementOutcome.Assigned, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();
        var laterToken = await SeedPriorDecisionAsync(PlacementOutcome.Undecided);
        await using (var db = _harness.CreateAdminContext())
        {
            var campaign = await db.Campaigns.SingleAsync(row => row.CampaignId == 610, TestContext.Current.CancellationToken);
            campaign.Status = CampaignStatus.Active;
            campaign.SeasonOpeningSequence = 20;
            campaign.ClosedAt = null;
            campaign.ClosedById = null;
            var old = await db.Campaigns.SingleAsync(row => row.CampaignId == 600, TestContext.Current.CancellationToken);
            old.Status = CampaignStatus.Closed;
            old.ClosedAt = DateTimeOffset.UtcNow;
            old.ClosedById = ClubAAdminId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var later = await CreateService().UpdatePlacementAsync(new UpdateCampaignPlacementInput(310, PlacementOutcome.NotSelected,
            null, laterToken, Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        later.Value.ShouldBeOfType<PlacementMutationSuccess>().Receipt.PlayerCampaignAssignmentId.ShouldBe(310);
        var activeInput = new GetPlacementContextInput { CampaignId = 610, PlayerCampaignAssignmentId = 310 };
        var active = (await CreatePlacementContextService().GetContextAsync(activeInput, TestContext.Current.CancellationToken)).Value;
        active.History.Select(item => item.CampaignId).ShouldBe([610L, 600L]);
        active.History.Select(item => item.Outcome).ShouldBe([PlacementOutcome.NotSelected, PlacementOutcome.Assigned]);
        await AssertHistoryAcceptedByHttpClientAsync(active, activeInput);
        var closedInput = new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId };
        var closed = (await CreatePlacementContextService().GetContextAsync(closedInput, TestContext.Current.CancellationToken)).Value;
        closed.History.ShouldHaveSingleItem().ShouldBe(active.History[1]);
        await AssertHistoryAcceptedByHttpClientAsync(closed, closedInput);
    }

    private static async Task AssertHistoryAcceptedByHttpClientAsync(PlacementContextResult payload, GetPlacementContextInput input)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        using var handler = new ProjectedHistoryHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var decoded = await new HttpPlacementContextQueryService(http).GetContextAsync(input, TestContext.Current.CancellationToken);
        decoded.IsSuccess.ShouldBeTrue();
        decoded.Value.History.ShouldBe(payload.History);
        decoded.Value.NextEventId.ShouldBe(payload.NextEventId);
    }

    private sealed class ProjectedHistoryHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
