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
/// Verifies route and response handling for the campaign closeout-readiness HTTP client.
/// </summary>
public sealed class HttpCampaignCloseoutQueryServiceTests
{
    /// <summary>Verifies readiness requests use the shared route and accept a populated ready payload.</summary>
    [Fact]
    public async Task GetCloseoutReadinessAsync_RequestsSharedRoute_AndReadsReadyPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new CampaignCloseoutReadinessDto(
            42,
            CampaignStatus.Active,
            IsReady: true,
            new CampaignPlacementSummaryDto(1, 1, 1, 0, 3),
            []);
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReady.ShouldBeTrue();
        result.Value.Blockers.ShouldBeEmpty();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/campaigns/42/closeout-readiness");
    }

    /// <summary>Verifies a populated blocked payload is accepted with its condition and ids intact.</summary>
    [Fact]
    public async Task GetCloseoutReadinessAsync_AcceptsPopulatedBlockedPayload()
    {
        var payload = new CampaignCloseoutReadinessDto(
            42,
            CampaignStatus.Active,
            IsReady: false,
            new CampaignPlacementSummaryDto(0, 0, 0, 2, 2),
            [
                new CampaignCloseoutBlockerDto(
                    CloseoutBlockerConditions.Outcomes,
                    2,
                    [1, 2],
                    "Every participant must have a final outcome before closing. Found 2 undecided participation record(s).")
            ]);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReady.ShouldBeFalse();
        result.Value.Blockers.ShouldHaveSingleItem();
        result.Value.Blockers[0].AssignmentIds.ShouldBe([1, 2]);
    }

    /// <summary>
    /// Verifies a not-ready payload whose outcomes blocker count differs from the summary undecided
    /// count — a momentary cross-read disagreement the server does not guarantee atomically — is
    /// accepted rather than surfaced as a server error.
    /// </summary>
    [Fact]
    public async Task GetCloseoutReadinessAsync_AcceptsMismatchedOutcomesCount()
    {
        var payload = new CampaignCloseoutReadinessDto(
            42,
            CampaignStatus.Active,
            IsReady: false,
            new CampaignPlacementSummaryDto(0, 0, 0, 2, 2),
            [
                new CampaignCloseoutBlockerDto(
                    CloseoutBlockerConditions.Outcomes,
                    1,
                    [1],
                    "Every participant must have a final outcome before closing. Found 1 undecided participation record(s).")
            ]);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Blockers.ShouldHaveSingleItem();
        result.Value.Blockers[0].Count.ShouldBe(1);
    }

    /// <summary>Verifies invalid caller input is rejected before any HTTP request is made.</summary>
    [Fact]
    public async Task GetCloseoutReadinessAsync_ReturnsValidation_ForInvalidInput()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies non-success ProblemDetails responses retain their problem kind.</summary>
    [Fact]
    public async Task GetCloseoutReadinessAsync_ReturnsNotFound_FromProblemDetails()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new ProblemPayload(404, "Not Found", "A problem occurred."))
            }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies invalid successful readiness bodies are surfaced as protocol failures.</summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":true,"summary":null,"blockers":[]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":null}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":[{"condition":"outcomes","count":-1,"assignmentIds":[1,2],"message":"msg"}]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":[{"condition":"outcomes","count":2,"assignmentIds":[1,-2],"message":"msg"}]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":[{"condition":"outcomes","count":2,"assignmentIds":[1,1],"message":"msg"}]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":[{"condition":"unknown","count":2,"assignmentIds":[1,2],"message":"msg"}]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":true,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":[{"condition":"outcomes","count":2,"assignmentIds":[1,2],"message":"msg"}]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":2},"blockers":[]}""")]
    [InlineData("""{"campaignId":2,"status":0,"isReady":false,"summary":{"assignedCount":0,"notSelectedCount":0,"withdrawnCount":0,"undecidedCount":2,"totalCount":3},"blockers":[{"condition":"outcomes","count":2,"assignmentIds":[1,2],"message":"msg"}]}""")]
    public async Task GetCloseoutReadinessAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignCloseoutQueryService(http);

        var result = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 2 },
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
