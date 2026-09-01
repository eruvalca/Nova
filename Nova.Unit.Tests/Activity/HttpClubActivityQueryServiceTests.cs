using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Activity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Activity;

/// <summary>
/// Verifies route building, cursor emission, and strict success-payload validation for the club
/// activity HTTP client.
/// </summary>
public sealed class HttpClubActivityQueryServiceTests
{
    /// <summary>Verifies the feed request uses the shared route and reads a populated payload.</summary>
    [Fact]
    public async Task GetClubActivityAsync_RequestsSharedRoute_AndReadsPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new ClubActivityResult(
        [
            NewItem(2, ActivityEventKind.CampaignClosed, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero)),
            NewItem(1, ActivityEventKind.CampaignOpened, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero))
        ],
        HasMore: false,
        NextCursor: null);
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(2);
        result.Value.Events[0].Kind.ShouldBe(ActivityEventKind.CampaignClosed);
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/activity");
    }

    /// <summary>Verifies a supplied cursor is emitted as separate query parameters.</summary>
    [Fact]
    public async Task GetClubActivityAsync_EmitsCursorQuery_WhenCursorProvided()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ClubActivityResult([], HasMore: false, NextCursor: null))
            });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput
            {
                BeforeActivityEventId = 15,
                BeforeOccurredAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe(
            "/api/activity?beforeActivityEventId=15&beforeOccurredAt=2026-09-30T12%3A00%3A00.0000000%2B00%3A00");
    }

    /// <summary>Verifies partial cursor input is rejected before any HTTP request is made.</summary>
    [Fact]
    public async Task GetClubActivityAsync_ReturnsValidation_ForPartialCursor()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput { BeforeActivityEventId = 15 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies a non-success ProblemDetails response retains its problem kind.</summary>
    [Fact]
    public async Task GetClubActivityAsync_ReturnsForbidden_FromProblemDetails()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = JsonContent.Create(new ProblemPayload(403, "Forbidden", "Not allowed."))
            }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies invalid successful activity bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("""{"events":null,"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[null],"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[{"kind":99,"activityEventId":1,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A","context":{"type":"campaignLifecycle"}}],"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[{"kind":3,"activityEventId":0,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A","context":{"type":"campaignLifecycle"}}],"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[{"kind":3,"activityEventId":1,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":0,"actorDisplayName":"Admin A","context":{"type":"campaignLifecycle"}}],"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[{"kind":3,"activityEventId":1,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":" ","context":{"type":"campaignLifecycle"}}],"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[{"kind":3,"activityEventId":1,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A","context":null}],"hasMore":false,"nextCursor":null}""")]
    [InlineData("""{"events":[{"kind":3,"activityEventId":1,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A","context":{"type":"campaignLifecycle"}}],"hasMore":false,"nextCursor":{"activityEventId":0,"occurredAt":"2026-10-01T09:00:00+00:00"}}""")]
    [InlineData("""{"events":[],"hasMore":true,"nextCursor":null}""")]
    public async Task GetClubActivityAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies a kind-family mismatch inside a successful payload is rejected.</summary>
    [Fact]
    public async Task GetClubActivityAsync_ReturnsServerError_ForKindFamilyMismatch()
    {
        var body = """
            {"events":[{"kind":3,"activityEventId":1,"occurredAt":"2026-10-01T09:00:00+00:00","actorUserId":300,"actorDisplayName":"Admin A","context":{"type":"placement","campaignId":1,"campaignName":"C","playerCampaignAssignmentId":1,"playerDisplayName":"P","outcome":0}}],"hasMore":false,"nextCursor":null}
            """;
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an out-of-order success payload (older-after-newer) is rejected.</summary>
    [Fact]
    public async Task GetClubActivityAsync_ReturnsServerError_ForOutOfOrderRows()
    {
        var payload = new ClubActivityResult(
        [
            NewItem(1, ActivityEventKind.CampaignOpened, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)),
            NewItem(2, ActivityEventKind.CampaignClosed, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero))
        ],
        HasMore: false,
        NextCursor: null);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an over-bound success payload (more than the fixed page size) is rejected.</summary>
    [Fact]
    public async Task GetClubActivityAsync_ReturnsServerError_ForOverBoundPayload()
    {
        var events = Enumerable.Range(1, GetClubActivityInput.PageSize + 1)
            .Select(index => NewItem(index, ActivityEventKind.CampaignOpened, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero).AddSeconds(index)))
            .ToList();
        var payload = new ClubActivityResult(events, HasMore: false, NextCursor: null);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies a valid populated payload with a cursor is accepted.</summary>
    [Fact]
    public async Task GetClubActivityAsync_AcceptsValidPopulatedPayload_WithCursor()
    {
        var payload = new ClubActivityResult(
        [
            NewItem(2, ActivityEventKind.CampaignClosed, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero))
        ],
        HasMore: true,
        NextCursor: new ClubActivityCursor(2, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero)));
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpClubActivityQueryService(http);

        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(1);
        result.Value.NextCursor.ShouldNotBeNull();
    }

    /// <summary>Builds a well-formed campaign-lifecycle activity row.</summary>
    private static ClubActivityItemDto NewItem(long id, ActivityEventKind kind, DateTimeOffset occurredAt)
        => new()
        {
            Kind = kind,
            ActivityEventId = id,
            OccurredAt = occurredAt,
            ActorUserId = 300,
            ActorDisplayName = "Admin A",
            Context = new CampaignLifecycleContext { CampaignId = 42, CampaignName = "Campaign A" }
        };

    /// <summary>Minimal problem-details payload shape for problem-response tests.</summary>
    private sealed record ProblemPayload(int Status, string Title, string Detail);

    /// <summary>Records and serves a canned response for the next request.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request);
    }
}
