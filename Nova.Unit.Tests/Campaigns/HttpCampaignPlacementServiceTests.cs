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
    private const long AssignmentId = 42;
    private const long TeamId = 7;

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
            Content = JsonContent.Create(new PlacementMutationSuccess(newToken))
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
            Content = JsonContent.Create(new PlacementMutationSuccess(Guid.Empty))
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
            Content = JsonContent.Create(new PlacementMutationSuccess(expectedToken))
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
        => new(AssignmentId, PlacementOutcome.Assigned, TeamId, expectedConcurrencyToken);
}
