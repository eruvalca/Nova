using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the WebAssembly campaign placement update client route and response contract.
/// </summary>
public sealed class HttpCampaignPlacementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("operation")]
    [InlineData("participant")]
    [InlineData("token")]
    [InlineData("outcome")]
    [InlineData("missing-team")]
    [InlineData("nonpositive-team")]
    [InlineData("unexpected-team")]
    public async Task InvalidPlacementInputReturnsLocalValidationWithoutHttpAsync(string invalid)
    {
        var input = ValidInput(Guid.NewGuid());
        input = invalid switch
        {
            "operation" => input with { OperationId = Guid.Empty },
            "participant" => input with { PlayerCampaignAssignmentId = 0 },
            "token" => input with { ExpectedConcurrencyToken = Guid.Empty },
            "outcome" => input with { Outcome = PlacementOutcome.Undecided, TeamId = null },
            "missing-team" => input with { TeamId = null },
            "nonpositive-team" => input with { TeamId = 0 },
            "unexpected-team" => input with { Outcome = PlacementOutcome.NotSelected },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(input, TestContext.Current.CancellationToken);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull().Keys.ShouldBe(Nova.SharedKernel.Validation.InputValidator.Validate(input).Keys, ignoreOrder: true);
        handler.LastRequest.ShouldBeNull();
        PlacementMutationRejection.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(86400, true)]
    [InlineData(86460, true)]
    [InlineData(86461, false)]
    [InlineData(172800, false)]
    public async Task ReceiptLifetimeHonorsTheBoundedRecoveryWindowAsync(int seconds, bool valid)
    {
        var input = ValidInput(Guid.NewGuid());
        var success = PlacementTestReceipts.Success(input, Guid.NewGuid());
        success = success with { Receipt = success.Receipt with { RecoveryExpiresAt = success.Receipt.CommittedAt.AddSeconds(seconds) } };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(success) };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
            PlacementMutationRejection.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
        }
    }

    private const long AssignmentId = 42;
    private const long TeamId = 7;
    private static readonly Guid _operationId = Guid.CreateVersion7();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("receipt")]
    [InlineData("operation")]
    [InlineData("participant")]
    [InlineData("decision")]
    [InlineData("decisionParticipant")]
    [InlineData("decisionToken")]
    [InlineData("outcome")]
    [InlineData("team")]
    [InlineData("actor")]
    [InlineData("deadline")]
    public async Task MalformedReceiptNeverConfirmsSaveAsync(string field)
    {
        var input = ValidInput(Guid.NewGuid());
        var node = System.Text.Json.JsonSerializer.SerializeToNode(PlacementTestReceipts.Success(input, Guid.NewGuid()))!;
        switch (field)
        {
            case "receipt": node.AsObject().Remove("Receipt"); break;
            case "operation": node["Receipt"]!["OperationId"] = Guid.CreateVersion7().ToString(); break;
            case "participant": node["Receipt"]!["PlayerCampaignAssignmentId"] = 99; break;
            case "decision": node["Receipt"]!["Decision"] = null; break;
            case "decisionParticipant": node["Receipt"]!["Decision"]!["PlayerCampaignAssignmentId"] = 99; break;
            case "decisionToken": node["Receipt"]!["Decision"]!["ConcurrencyToken"] = Guid.NewGuid().ToString(); break;
            case "outcome": node["Receipt"]!["Decision"]!["Outcome"] = (int)PlacementOutcome.NotSelected; break;
            case "team": node["Receipt"]!["Decision"]!["TeamId"] = 99; break;
            case "actor": node["Receipt"]!["Decision"]!["ActorDisplayName"] = " "; break;
            case "deadline": node["Receipt"]!["RecoveryExpiresAt"] = DateTimeOffset.MinValue; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(input, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        PlacementMutationRejection.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
    }


    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>Gets the last request sent by the client.</summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Verifies a successful update PUTs to the shared placement URL and returns the validated token.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncPutsToSharedPlacementUrlAndReturnsValidatedTokenAsync()
    {
        var expectedToken = Guid.NewGuid();
        var newToken = Guid.NewGuid();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(PlacementTestReceipts.Success(ValidInput(expectedToken), newToken))
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(expectedToken),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ConcurrencyToken.ShouldBe(newToken);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.AbsolutePath
            .ShouldBe(CampaignEndpoints.UpdateCampaignPlacementUrl(AssignmentId));
    }

    /// <summary>
    /// Verifies an empty concurrency token in a success payload is treated as a contract defect.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsServerErrorWhenSuccessTokenIsEmptyAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(PlacementTestReceipts.Success(ValidInput(Guid.NewGuid()), Guid.Empty))
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies an identical save succeeds when the server preserves the submitted token.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsSuccessWhenNoOpPreservesSubmittedTokenAsync()
    {
        var expectedToken = Guid.NewGuid();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(PlacementTestReceipts.Success(ValidInput(expectedToken), expectedToken))
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(expectedToken),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ConcurrencyToken.ShouldBe(expectedToken);
    }

    /// <summary>
    /// Verifies a null success response body is surfaced as a server error.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsServerErrorForNullBodyAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a malformed success response body is surfaced as a server error.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsServerErrorForMalformedBodyAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json")
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a validation ProblemDetails response is propagated with its structured errors.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsValidationFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    [nameof(UpdateCampaignPlacementInput.TeamId)] = ["A team is required for an assigned outcome."]
                }
            })
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies a not-found ProblemDetails response is propagated correctly.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsNotFoundFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not Found", status = 404 })
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a forbidden ProblemDetails response is propagated correctly.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsForbiddenFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new
            {
                title = "Forbidden",
                status = 403,
                detail = "You must be an approved club member to update campaign placements."
            })
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies a conflict ProblemDetails response is propagated correctly.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsyncReturnsConflictFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "The placement was changed by another user. Reload it and try again."
            })
        };
        using var httpHandler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignPlacementService(http).UpdatePlacementAsync(
            ValidInput(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Builds a structurally valid placement update input.
    /// </summary>
    /// <param name="expectedConcurrencyToken">The token observed when the placement was loaded.</param>
    /// <returns>The valid input.</returns>
    private static UpdateCampaignPlacementInput ValidInput(Guid expectedConcurrencyToken)
        => new(AssignmentId, PlacementOutcome.Assigned, TeamId, expectedConcurrencyToken, _operationId);
}
