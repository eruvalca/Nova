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

    /// <summary>Checks a saved decision survives the HTTP contract with its source identity and attribution.</summary>
    [Fact]
    public async Task GetPlacementRosterAsync_RoundTripsSavedDecisionSnapshot()
    {
        var token = Guid.NewGuid();
        var decision = new CampaignSavedPlacementDecision(101, 202, 42, 50, 3,
            PlacementOutcome.NotSelected, null, DateTimeOffset.UnixEpoch, 70, "Casey Member", token);
        var row = new CampaignPlacementRosterItem(101, 202, "Zoe Adams", "Zoe", "Adams", 2028,
            PlacementOutcome.NotSelected, null, token)
        { SavedDecision = decision };
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResult<CampaignPlacementRosterItem>([row], 1, 50, 1))
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var result = await new HttpCampaignPlacementQueryService(http).GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Single().SavedDecision.ShouldBe(decision);
    }
    /// <summary>Checks each saved-decision invariant independently rejects corrupt successful responses.</summary>
    /// <param name="invalidField">The single malformed field or relationship.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("missing")]
    [InlineData("participation")]
    [InlineData("player")]
    [InlineData("campaign")]
    [InlineData("season")]
    [InlineData("sequence")]
    [InlineData("outcome")]
    [InlineData("team")]
    [InlineData("token")]
    [InlineData("technical")]
    public async Task GetPlacementRosterAsync_RejectsMalformedSavedDecision(string invalidField)
    {
        var token = Guid.NewGuid();
        CampaignSavedPlacementDecision? decision = new(101, 202, 42, 50, 3,
            PlacementOutcome.NotSelected, null, DateTimeOffset.UnixEpoch, 70, "Casey Member", token);
        decision = invalidField switch
        {
            "missing" => null,
            "participation" => decision with { PlayerCampaignAssignmentId = 102 },
            "player" => decision with { PlayerId = 203 },
            "campaign" => decision with { CampaignId = 43 },
            "season" => decision with { SeasonId = 0 },
            "sequence" => decision with { SeasonOpeningSequence = 0 },
            "outcome" => decision with { Outcome = PlacementOutcome.Withdrawn },
            "team" => decision with { TeamId = 301 },
            "token" => decision with { ConcurrencyToken = Guid.NewGuid() },
            _ => decision
        };
        var row = new CampaignPlacementRosterItem(101, 202, "Zoe Adams", "Zoe", "Adams", 2028,
            invalidField == "technical" ? PlacementOutcome.Undecided : PlacementOutcome.NotSelected, null, token)
        { SavedDecision = decision };
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PagedResult<CampaignPlacementRosterItem>([row], 1, 50, 1))
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var result = await new HttpCampaignPlacementQueryService(http).GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
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
    [Theory(IncludeTestCaseIndex = true)]
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
            var token = Guid.NewGuid();
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
                    token) { SavedDecision = new CampaignSavedPlacementDecision(101, 202, 42, 50, 1,
                        PlacementOutcome.Assigned, null, null, null, null, token) }],
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
            var token = Guid.NewGuid();
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
                    token) { SavedDecision = new CampaignSavedPlacementDecision(101, 202, 42, 50, 1,
                        PlacementOutcome.Assigned, 301, null, null, null, token) }],
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
    /// Verifies an empty-object success summary is rejected by strict required-member enforcement.
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
    /// Verifies a summary missing one required count is rejected by strict required-member enforcement.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummaryAsync_ReturnsServerError_WhenSummaryMissesACount()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"assignedCount":65,"notSelectedCount":11,"withdrawnCount":2,"undecidedCount":6}""",
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
    /// Verifies a roster row missing the placement outcome is rejected by strict required-member
    /// enforcement before the row invariant validator runs.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenRowMissesOutcome()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "playerCampaignAssignmentId": 101,
                      "playerId": 202,
                      "displayName": "Zoe Adams",
                      "firstName": "Zoe",
                      "lastName": "Adams",
                      "graduationYear": 2028,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abcd"
                    }
                  ],
                  "page": 1,
                  "pageSize": 50,
                  "totalCount": 1
                }
                """,
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
    /// Verifies a roster page whose equal-name rows violate the server ordering tie-breaker
    /// (assignment id must be non-decreasing within identical names) is rejected.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_ReturnsServerError_WhenRowsAreOutOfOrder()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "playerCampaignAssignmentId": 102,
                      "playerId": 203,
                      "displayName": "Avery Johnson",
                      "firstName": "Avery",
                      "lastName": "Johnson",
                      "graduationYear": 2028,
                      "placementOutcome": 0,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abcd"
                    },
                    {
                      "playerCampaignAssignmentId": 101,
                      "playerId": 202,
                      "displayName": "Avery Johnson",
                      "firstName": "Avery",
                      "lastName": "Johnson",
                      "graduationYear": 2028,
                      "placementOutcome": 0,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abce"
                    }
                  ],
                  "page": 1,
                  "pageSize": 50,
                  "totalCount": 2
                }
                """,
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
    /// Verifies a roster page whose name order follows the database collation rather than ordinal
    /// comparison is accepted: the server orders different names by the database collation (for
    /// example, accented names can precede otherwise-ordinally-earlier names), so only the
    /// equal-name assignment-id tie-breaker is portable to the client.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_AcceptsRows_WhenDatabaseCollationOrdersNamesDifferentlyFromOrdinal()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "playerCampaignAssignmentId": 101,
                      "playerId": 202,
                      "displayName": "Ana Álvarez",
                      "firstName": "Ana",
                      "lastName": "Álvarez",
                      "graduationYear": 2028,
                      "placementOutcome": 0,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abcd"
                    },
                    {
                      "playerCampaignAssignmentId": 102,
                      "playerId": 203,
                      "displayName": "James Bond",
                      "firstName": "James",
                      "lastName": "Bond",
                      "graduationYear": 2028,
                      "placementOutcome": 0,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abce"
                    }
                  ],
                  "page": 1,
                  "pageSize": 50,
                  "totalCount": 2
                }
                """,
                Encoding.UTF8,
                "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
    }

    /// <summary>
    /// Verifies an in-order multi-row roster page is accepted.
    /// </summary>
    [Fact]
    public async Task GetPlacementRosterAsync_AcceptsRowsInServerOrderingContract()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "items": [
                    {
                      "playerCampaignAssignmentId": 101,
                      "playerId": 202,
                      "displayName": "Zoe Adams",
                      "firstName": "Zoe",
                      "lastName": "Adams",
                      "graduationYear": 2028,
                      "placementOutcome": 0,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abcd"
                    },
                    {
                      "playerCampaignAssignmentId": 102,
                      "playerId": 203,
                      "displayName": "Avery Johnson",
                      "firstName": "Avery",
                      "lastName": "Johnson",
                      "graduationYear": 2028,
                      "placementOutcome": 0,
                      "team": null,
                      "concurrencyToken": "34d6a1d0-4f2e-4f2e-9f2e-34d6a1d0abce"
                    }
                  ],
                  "page": 1,
                  "pageSize": 50,
                  "totalCount": 2
                }
                """,
                Encoding.UTF8,
                "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpCampaignPlacementQueryService(http);

        var result = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
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
