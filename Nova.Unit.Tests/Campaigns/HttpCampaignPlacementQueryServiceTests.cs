using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies route and response handling for the campaign placement query HTTP client.
/// </summary>
public sealed class HttpCampaignPlacementQueryServiceTests
{
    /// <summary>
    /// Verifies roster filters reach the placement route and a valid payload is accepted.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_SendsFiltersToPlacementRoute_AndReadsRows()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            var payload = new PagedResult<CampaignPlacementRosterItem>(
                [new CampaignPlacementRosterItem(
                    101,
                    202,
                    "Zoe Adams",
                    "Zoe",
                    "Adams",
                    2028,
                    PlacementOutcome.Undecided,
                    null,
                    Guid.NewGuid())],
                1,
                50,
                1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = 42,
                GraduationYear = 2028,
                UnresolvedOnly = true,
                Page = 1,
                PageSize = 50
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(1);
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri!.PathAndQuery.ShouldBe("/api/campaigns/42/placements?graduationYear=2028&unresolvedOnly=true&page=1&pageSize=50");
    }

    /// <summary>
    /// Verifies omitted optional filters still carry the default paging values in the URL.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_OmitsOptionalFiltersFromUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            var payload = new PagedResult<CampaignPlacementRosterItem>([], 1, 50, 0);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/campaigns/42/placements?page=1&pageSize=50");
    }

    /// <summary>
    /// Verifies invalid caller input is rejected before any HTTP request is made.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsValidation_ForInvalidInput()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>
    /// Verifies paging combinations that would overflow are rejected before sending an HTTP request.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsValidation_WithoutRequestForOverflowingPageOffset()
    {
        var requestSent = false;
        var handler = new RecordingHandler(_ =>
        {
            requestSent = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = 42,
                Page = int.MaxValue,
                PageSize = 2
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors!.ShouldContainKey(nameof(GetCampaignPlacementRosterInput.Page));
        requestSent.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies non-success status codes are converted to service problems.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsNotFound_ForProblemResponse()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new ProblemPayload(404, "Not Found", "A problem occurred."))
            }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies invalid successful roster bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies roster rows violating portable invariants are rejected.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenRowViolatesInvariants()
    {
        var handler = new RecordingHandler(_ =>
        {
            var payload = new PagedResult<CampaignPlacementRosterItem>(
                [new CampaignPlacementRosterItem(
                    101,
                    202,
                    "Zoe Adams",
                    "Zoe",
                    "Adams",
                    2028,
                    PlacementOutcome.Assigned,
                    null,
                    Guid.NewGuid())],
                1,
                50,
                1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies an unresolved-only row violating the requested filter is rejected.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenRowViolatesUnresolvedFilter()
    {
        var handler = new RecordingHandler(_ =>
        {
            var payload = new PagedResult<CampaignPlacementRosterItem>(
                [new CampaignPlacementRosterItem(
                    101,
                    202,
                    "Zoe Adams",
                    "Zoe",
                    "Adams",
                    2028,
                    PlacementOutcome.Assigned,
                    new CampaignParticipantTeamSummaryDto(301, "Alpha"),
                    Guid.NewGuid())],
                1,
                50,
                1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42, UnresolvedOnly = true },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the summary client uses the summary route and accepts a consistent payload.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummaryAsync_SendsSummaryRoute_AndReadsCounts()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            var payload = new CampaignPlacementSummaryDto(65, 11, 2, 6, 84);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AssignedCount.ShouldBe(65);
        result.Value.TotalCount.ShouldBe(84);
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/campaigns/42/placements/summary");
    }

    /// <summary>
    /// Verifies a summary whose total does not equal the sum of its counts is rejected.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummaryAsync_ReturnsServerError_WhenCountsAreInconsistent()
    {
        var handler = new RecordingHandler(_ =>
        {
            var payload = new CampaignPlacementSummaryDto(65, 11, 2, 6, 83);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies invalid caller input is rejected before any summary request is made.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummaryAsync_ReturnsValidation_ForInvalidInput()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>
    /// Verifies a summary success payload with no counts is rejected as a protocol failure instead of
    /// silently defaulting every count to zero.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummaryAsync_ReturnsServerError_WhenSuccessBodyIsEmptyObject()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a summary success payload missing one required count is rejected as a protocol failure.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummaryAsync_ReturnsServerError_WhenSummaryMissingACount()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"assignedCount\":65,\"notSelectedCount\":11,\"withdrawnCount\":2,\"undecidedCount\":6}",
                Encoding.UTF8,
                "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a roster row missing its required placement outcome is rejected as a protocol failure.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenRosterRowMissingOutcome()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"items\":[{\"playerCampaignAssignmentId\":101,\"playerId\":202,\"displayName\":\"Zoe Adams\",\"firstName\":\"Zoe\",\"lastName\":\"Adams\",\"graduationYear\":2028,\"team\":null,\"concurrencyToken\":\"6f8b3e1a-0000-0000-0000-000000000001\"}],\"page\":1,\"pageSize\":50,\"totalCount\":1}",
                Encoding.UTF8,
                "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies an out-of-order roster page is rejected against the server's deterministic ordering contract.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenRosterPageIsOutOfOrder()
    {
        var handler = new RecordingHandler(_ =>
        {
            var payload = new PagedResult<CampaignPlacementRosterItem>(
                [
                    new CampaignPlacementRosterItem(
                        101,
                        202,
                        "Zoe Smith",
                        "Zoe",
                        "Smith",
                        2028,
                        PlacementOutcome.Undecided,
                        null,
                        Guid.NewGuid()),
                    new CampaignPlacementRosterItem(
                        102,
                        203,
                        "Aaron Adams",
                        "Aaron",
                        "Adams",
                        2028,
                        PlacementOutcome.Undecided,
                        null,
                        Guid.NewGuid())
                ],
                1,
                50,
                2);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a correctly-ordered multi-row roster page is accepted.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_AcceptsCorrectlyOrderedRosterPage()
    {
        var handler = new RecordingHandler(_ =>
        {
            var payload = new PagedResult<CampaignPlacementRosterItem>(
                [
                    new CampaignPlacementRosterItem(
                        102,
                        203,
                        "Aaron Adams",
                        "Aaron",
                        "Adams",
                        2028,
                        PlacementOutcome.Undecided,
                        null,
                        Guid.NewGuid()),
                    new CampaignPlacementRosterItem(
                        101,
                        202,
                        "Zoe Smith",
                        "Zoe",
                        "Smith",
                        2028,
                        PlacementOutcome.Undecided,
                        null,
                        Guid.NewGuid())
                ],
                1,
                50,
                2);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
    }

    /// <summary>
    /// Verifies the ordering contract is ordinal, not locale-aware: the server orders name columns
    /// with PostgreSQL's "C" collation, so accent-differing names sort by code point and the client's
    /// <see cref="StringComparison.Ordinal"/> comparison must not reject a correct page. "Peterson"
    /// precedes "Pérez" ordinally (code point 'e' &lt; 'é'), whereas a locale-aware collation would
    /// order "Pérez" first — the divergence the reviewer flagged.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_AcceptsOrdinalOrdering_ForAccentDifferingNames()
    {
        var handler = new RecordingHandler(_ =>
        {
            var payload = new PagedResult<CampaignPlacementRosterItem>(
                [
                    new CampaignPlacementRosterItem(
                        102,
                        203,
                        "Avery Peterson",
                        "Avery",
                        "Peterson",
                        2028,
                        PlacementOutcome.Undecided,
                        null,
                        Guid.NewGuid()),
                    new CampaignPlacementRosterItem(
                        101,
                        202,
                        "Zoe Pérez",
                        "Zoe",
                        "Pérez",
                        2028,
                        PlacementOutcome.Undecided,
                        null,
                        Guid.NewGuid())
                ],
                1,
                50,
                2);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
    }

    /// <summary>
    /// Minimal problem-details payload shape for problem-response tests.
    /// </summary>
    private sealed record ProblemPayload(int Status, string Title, string Detail);

    /// <summary>
    /// Records and serves a canned response for the next request.
    /// </summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request);
    }
}
