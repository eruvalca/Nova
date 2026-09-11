using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the WebAssembly campaign tag application client route and response contract.
/// </summary>
public sealed partial class HttpCampaignTagApplicationServiceTests
{
    private readonly Guid _operationId = Guid.CreateVersion7();

    private EvaluationMutationReceipt Receipt() => new(_operationId, 100, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(23));

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

        public string? LastRequestBody { get; private set; }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    /// <summary>
    /// Verifies successful application posts to the shared route and deserializes the created identifier.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncPostsToSharedRouteAndReturnsCreatedIdentifierAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new CampaignTagApplicationMutationSuccess(42, 200, false, Receipt()))
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignTagApplicationId.ShouldBe(42);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.ApplyCampaignTagApplication);
    }

    /// <summary>
    /// Verifies ProblemDetails responses retain their service problem kind and detail.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsForbiddenFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new
            {
                title = "Forbidden",
                status = 403,
                detail = "Only a club administrator can apply tags."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        result.Problem.Detail.ShouldBe("Only a club administrator can apply tags.");
    }

    /// <summary>
    /// Verifies a not-found problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsNotFoundFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new
            {
                title = "Not Found",
                status = 404,
                detail = "The campaign participation was not found."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a conflict problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsConflictFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "The selected tag has already been applied to this participation."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("The selected tag has already been applied to this participation.");
    }

    /// <summary>
    /// Verifies a successful response without the required payload becomes an explicit server error.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsServerErrorForEmptySuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(string.Empty)
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldBe("The server did not return a valid trait receipt. Retry the original operation to recover its result.");
    }

    /// <summary>
    /// Verifies malformed success JSON becomes an explicit server error.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsServerErrorForMalformedSuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{not-json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldBe("The server did not return a valid trait receipt. Retry the original operation to recover its result.");
    }

    /// <summary>
    /// Verifies a successful JSON null response is rejected as an invalid payload.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsServerErrorForNullSuccessPayloadAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a successful response with a non-positive identifier is rejected as an invalid payload.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsServerErrorForInvalidCreatedIdentifierAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new CampaignTagApplicationMutationSuccess(0, 200, false, Receipt()))
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(
            ValidApplyInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies successful removal deletes the shared route and returns the immutable mutation receipt.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncDeletesToSharedRouteAndReturnsSuccessAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CampaignTagApplicationMutationSuccess(42, 200, false, Receipt())) };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).RemoveAsync(
            ValidRemoveInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(
            CampaignEndpoints.RemoveCampaignTagApplicationUrl(42));
    }

    /// <summary>
    /// Verifies a not-found problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsNotFoundFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new
            {
                title = "Not Found",
                status = 404,
                detail = "The campaign tag application was not found."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).RemoveAsync(
            ValidRemoveInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        result.Problem.Detail.ShouldBe("The campaign tag application was not found.");
    }

    /// <summary>
    /// Verifies a forbidden problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsForbiddenFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new
            {
                title = "Forbidden",
                status = 403,
                detail = "Only the applying user or a club administrator can remove this tag application."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).RemoveAsync(
            ValidRemoveInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        result.Problem.Detail.ShouldBe("Only the applying user or a club administrator can remove this tag application.");
    }

    /// <summary>
    /// Verifies a conflict problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsConflictFromProblemDetailsAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "The tag application cannot be removed."
            })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignTagApplicationService(http).RemoveAsync(
            ValidRemoveInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Creates a valid apply request.
    /// </summary>
    /// <returns>A valid request for client serialization.</returns>
    private ApplyCampaignTagApplicationInput ValidApplyInput() => new()
    {
        OperationId = _operationId,
        PlayerCampaignAssignmentId = 100,
        PlayerTagId = 200
    };

    /// <summary>
    /// Creates a valid remove request.
    /// </summary>
    /// <returns>A valid request for client serialization.</returns>
    private RemoveCampaignTagApplicationInput ValidRemoveInput() => new()
    {
        OperationId = _operationId,
        CampaignTagApplicationId = 42
    };
}
