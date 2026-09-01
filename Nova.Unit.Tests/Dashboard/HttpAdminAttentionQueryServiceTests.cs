using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Dashboard;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>Verifies strict client validation for independent administrator attention projections.</summary>
public sealed class HttpAdminAttentionQueryServiceTests
{
    /// <summary>Proves a multi-campaign placement total may omit a single campaign link.</summary>
    [Fact]
    public async Task GetAsync_AcceptsMultiCampaignPlacementTotal()
    {
        var payload = new AdminAttentionResult
        {
            PendingJoinRequests = new PendingJoinRequestAttentionDto
            {
                State = AttentionProjectionState.Available,
                Count = 2,
                OldestSubmittedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            NeedsPlacement = new NeedsPlacementAttentionDto
            {
                State = AttentionProjectionState.Available,
                Count = 7
            }
        };
        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        var service = new HttpAdminAttentionQueryService(http);

        var result = await service.GetAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NeedsPlacement.Count.ShouldBe(7);
        result.Value.NeedsPlacement.CampaignId.ShouldBeNull();
    }

    /// <summary>Proves unavailable and malformed projection shapes cannot masquerade as valid success.</summary>
    /// <param name="body">The malformed response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"pendingJoinRequests":{"state":1,"count":0,"oldestSubmittedAt":null},"needsPlacement":{"state":0,"count":0,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"state":0,"count":1,"oldestSubmittedAt":null},"needsPlacement":{"state":0,"count":0,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"state":0,"count":0,"oldestSubmittedAt":null},"needsPlacement":{"state":0,"count":1,"campaignId":42,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"state":0,"count":0,"oldestSubmittedAt":null},"needsPlacement":{"state":0,"count":-1,"campaignId":null,"campaignName":null}}""")]
    public async Task GetAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        var service = new HttpAdminAttentionQueryService(http);

        var result = await service.GetAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Proves non-success responses preserve the server problem kind.</summary>
    [Fact]
    public async Task GetAsync_ReturnsProblem_FromNonSuccess()
    {
        using var http = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new ProblemPayload(403, "Forbidden", "Not allowed."))
        });
        var service = new HttpAdminAttentionQueryService(http);

        var result = await service.GetAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> callback)
        => new(new RecordingHandler(callback)) { BaseAddress = new Uri("https://example.com") };

    private sealed record ProblemPayload(int Status, string Title, string Detail);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
