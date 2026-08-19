using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies route and response handling for the campaign recent-activity HTTP client.
/// </summary>
public sealed class HttpCampaignActivityQueryServiceTests
{
    /// <summary>Verifies activity requests use the shared route and read a populated payload.</summary>
    [Fact]
    public async Task GetActivityAsync_RequestsSharedRoute_AndReadsEvents()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new CampaignActivityResult(
        [
            new CampaignActivityItemDto(
                2,
                CampaignLifecycleEventType.Reopened,
                new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero),
                300,
                "Admin A"),
            new CampaignActivityItemDto(
                1,
                CampaignLifecycleEventType.Closed,
                new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
                300,
                "Admin A")
        ]);
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(2);
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/campaigns/42/activity");
    }

    /// <summary>Verifies an explicit limit is emitted in the query string.</summary>
    [Fact]
    public async Task GetActivityAsync_EmitsLimitQuery_WhenLimitProvided()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new CampaignActivityResult([]);
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 42, Limit = 5 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/campaigns/42/activity?limit=5");
    }

    /// <summary>Verifies invalid caller input is rejected before any HTTP request is made.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, null)]
    [InlineData(42, 0)]
    [InlineData(42, 51)]
    public async Task GetActivityAsync_ReturnsValidation_ForInvalidInput(long campaignId, int? limit)
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = campaignId, Limit = limit },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies non-success ProblemDetails responses retain their problem kind.</summary>
    [Fact]
    public async Task GetActivityAsync_ReturnsNotFound_FromProblemDetails()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new ProblemPayload(404, "Not Found", "A problem occurred."))
            }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies invalid successful activity bodies are surfaced as protocol failures.</summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("""{"events":null}""")]
    [InlineData("""{"events":[null]}""")]
    [InlineData("""{"events":[{"campaignLifecycleEventId":0,"eventType":0,"createdAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A"}]}""")]
    [InlineData("""{"events":[{"campaignLifecycleEventId":-1,"eventType":0,"createdAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A"}]}""")]
    [InlineData("""{"events":[{"campaignLifecycleEventId":1,"eventType":99,"createdAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A"}]}""")]
    [InlineData("""{"events":[{"campaignLifecycleEventId":1,"eventType":0,"createdAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":" "}]}""")]
    [InlineData("""{"events":[{"campaignLifecycleEventId":1,"eventType":0,"createdAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A"},{"campaignLifecycleEventId":2,"eventType":0,"createdAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A"}]}""")]
    public async Task GetActivityAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an over-bound success payload (more events than the requested limit) is rejected.</summary>
    [Fact]
    public async Task GetActivityAsync_ReturnsServerError_ForOverBoundPayload()
    {
        var payload = new CampaignActivityResult(
        [
            new CampaignActivityItemDto(1, CampaignLifecycleEventType.Closed, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), 300, "Admin A"),
            new CampaignActivityItemDto(2, CampaignLifecycleEventType.Closed, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), 300, "Admin A")
        ]);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 42, Limit = 1 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
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
