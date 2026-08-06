using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Results;
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
    public async Task CreateAsync_PostsToSharedRoute_AndReturnsResult()
    {
        var input = ValidInput();
        var expected = CreatedResult(input.OperationId);
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(expected)
        };
        var handler = new FakeHttpMessageHandler(response);
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
    public async Task CreateAsync_ReturnsConflict_FromProblemDetails()
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
        var handler = new FakeHttpMessageHandler(response);
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
    public async Task CreateAsync_ReturnsServerError_ForEmptySuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(string.Empty)
        };
        var handler = new FakeHttpMessageHandler(response);
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
    public async Task CreateAsync_ReturnsServerError_ForNullSuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
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
    public async Task CreateAsync_ReturnsServerError_ForIncompleteSuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignCreationService(http).CreateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldBe("The server returned an invalid campaign creation response.");
    }

    /// <summary>
    /// Verifies malformed success JSON becomes an explicit server error.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ReturnsServerError_ForMalformedSuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{not-json")
        };
        var handler = new FakeHttpMessageHandler(response);
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
        CampaignStatus.Active,
        42,
        "2026",
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31),
        false,
        12);
}
