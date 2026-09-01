using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Attention;
using Nova.Shared.Enums;
using Nova.Shared.Features.Attention;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Attention;

/// <summary>
/// Verifies route handling and strict success-payload validation for the club attention HTTP client.
/// </summary>
public sealed class HttpClubAttentionQueryServiceTests
{
    /// <summary>Verifies the attention request uses the shared route and reads a populated payload.</summary>
    [Fact]
    public async Task GetClubAttentionAsync_RequestsSharedRoute_AndReadsPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new ClubAttentionResult
        {
            PendingJoinRequests = new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 2,
                OldestRequestAt = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)
            },
            NeedsPlacement = new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 1,
                CampaignId = 42,
                CampaignName = "Campaign A"
            }
        };
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubAttentionQueryService(http);

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PendingJoinRequests.Count.ShouldBe(2);
        result.Value.NeedsPlacement.CampaignId.ShouldBe(42);
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/attention");
    }

    /// <summary>Verifies a non-success ProblemDetails response retains its problem kind.</summary>
    [Fact]
    public async Task GetClubAttentionAsync_ReturnsForbidden_FromProblemDetails()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = JsonContent.Create(new ProblemPayload(403, "Forbidden", "Not allowed."))
            }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubAttentionQueryService(http);

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies invalid successful attention bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("""{"pendingJoinRequests":null,"needsPlacement":{"status":0,"count":0,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":0,"oldestRequestAt":null},"needsPlacement":null}""")]
    [InlineData("""{"pendingJoinRequests":{"status":3,"count":0,"oldestRequestAt":null},"needsPlacement":{"status":0,"count":0,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":-1,"oldestRequestAt":null},"needsPlacement":{"status":0,"count":0,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":0,"oldestRequestAt":null},"needsPlacement":{"status":0,"count":1,"campaignId":42,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":0,"oldestRequestAt":"2026-10-01T09:00:00+00:00"},"needsPlacement":{"status":0,"count":0,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":0,"oldestRequestAt":null},"needsPlacement":{"status":0,"count":-1,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":0,"oldestRequestAt":null},"needsPlacement":{"status":0,"count":1,"campaignId":null,"campaignName":null}}""")]
    [InlineData("""{"pendingJoinRequests":{"status":0,"count":0,"oldestRequestAt":null},"needsPlacement":{"status":0,"count":1,"campaignId":42,"campaignName":" "}}""")]
    public async Task GetClubAttentionAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubAttentionQueryService(http);

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an Unavailable region paired with a loaded region is accepted.</summary>
    [Fact]
    public async Task GetClubAttentionAsync_AcceptsUnavailableRegionAlongsideLoaded()
    {
        var payload = new ClubAttentionResult
        {
            PendingJoinRequests = new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Unavailable,
                Count = 0,
                OldestRequestAt = null
            },
            NeedsPlacement = new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = 0,
                CampaignId = null,
                CampaignName = null
            }
        };
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubAttentionQueryService(http);

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Unavailable);
        result.Value.NeedsPlacement.Count.ShouldBe(0);
    }

    /// <summary>Minimal problem-details payload shape for problem-response tests.</summary>
    private sealed record ProblemPayload(int Status, string Title, string Detail);

    /// <summary>Records and serves a canned response for the next request.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request);
    }
}
