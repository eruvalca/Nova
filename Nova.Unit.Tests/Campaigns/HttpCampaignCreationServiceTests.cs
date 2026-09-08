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
/// Verifies the WebAssembly campaign creation client route and response contract.
/// </summary>
public sealed class HttpCampaignCreationServiceTests
{
    /// <summary>
    /// Captures the request and returns one configured response.
    /// </summary>
    /// <param name="response">The response returned for the request.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the request sent by the client.
        /// </summary>
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
    /// Verifies successful campaign creation posts to the shared route and deserializes the result.
    /// </summary>
    [Fact]
    public async Task CreateAsyncPostsToSharedRouteAndReturnsResultAsync()
    {
        var input = ValidInput();
        var expected = CreatedResult(input.OperationId);
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(expected)
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.Create);
    }

    /// <summary>
    /// Verifies ProblemDetails responses retain their service problem kind and detail.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReturnsConflictFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "A campaign with that name already exists."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("A campaign with that name already exists.");
    }

    /// <summary>
    /// Verifies a successful response without the required payload becomes an explicit server error.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReturnsServerErrorForEmptySuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(string.Empty)
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a successful JSON null response is rejected as an invalid payload.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReturnsServerErrorForNullSuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldBe("The server returned an invalid campaign creation response.");
    }

    /// <summary>
    /// Verifies a syntactically valid but incomplete success object is rejected.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReturnsServerErrorForIncompleteSuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldBe("The server returned an invalid campaign creation response.");
    }

    /// <summary>Verifies stale Active creation responses are rejected.</summary>
    [Fact]
    public async Task CreateAsyncReturnsServerErrorForActiveSuccessPayloadAsync()
    {
        var input = ValidInput();
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(CreatedResult(input.OperationId) with { Status = CampaignStatus.Active })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies the removed enrollment-count response shape is rejected.</summary>
    [Fact]
    public async Task CreateAsyncReturnsServerErrorForRemovedEnrollmentFieldAsync()
    {
        var input = ValidInput();
        var payload = $$"""
            {"operationId":"{{input.OperationId}}","campaignId":100,"campaignName":"Summer Tryouts",
            "campaignStartDate":"2026-06-01","campaignPlannedEndDate":"2026-06-30","status":2,
            "seasonId":42,"seasonName":"2026","seasonStartDate":"2026-01-01","seasonEndDate":"2026-12-31",
            "seasonCreatedInline":false,"enrolledPlayerCount":12}
            """;
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies malformed success JSON becomes an explicit server error.
    /// </summary>
    [Fact]
    public async Task CreateAsyncReturnsServerErrorForMalformedSuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{not-json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Creates a valid existing-season request.
    /// </summary>
    /// <returns>A valid request for client serialization.</returns>
    private static CreateCampaignInput ValidInput() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = "Summer Tryouts",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30),
        ExistingSeasonId = 42
    };

    /// <summary>
    /// Creates a representative successful response.
    /// </summary>
    /// <returns>A campaign creation result for response deserialization.</returns>
    private static CreateCampaignResult CreatedResult(Guid operationId) => new(
        operationId,
        100,
        "Summer Tryouts",
        new DateOnly(2026, 6, 1),
        new DateOnly(2026, 6, 30),
        CampaignStatus.Draft,
        42,
        "2026",
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31),
        false);
}
