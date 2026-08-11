using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the WebAssembly evaluation note client route and response contract.
/// </summary>
public sealed class HttpCampaignEvaluationNoteServiceTests
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

        /// <summary>
        /// Gets the serialized body of the request sent by the client.
        /// </summary>
        public string? LastRequestBody { get; private set; }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    /// <summary>
    /// Verifies a successful add posts to the shared route and deserializes the created note identifier.
    /// </summary>
    [Fact]
    public async Task AddAsync_PostsToSharedRoute_AndReturnsCreatedIdentifier()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new EvaluationNoteMutationSuccess(7))
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NoteId.ShouldBe(7);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.AddEvaluationNote);
    }

    /// <summary>
    /// Verifies a validation problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task AddAsync_ReturnsValidation_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                title = "One or more validation errors occurred.",
                status = 400,
                detail = "Note content is required.",
                errors = new Dictionary<string, string[]>
                {
                    ["Content"] = ["The Content field is required."]
                }
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors["Content"].ShouldBe(["The Content field is required."]);
    }

    /// <summary>
    /// Verifies a forbidden problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task AddAsync_ReturnsForbidden_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new
            {
                title = "Forbidden",
                status = 403,
                detail = "Only club members can add evaluation notes."
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        result.Problem.Detail.ShouldBe("Only club members can add evaluation notes.");
    }

    /// <summary>
    /// Verifies a not-found problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task AddAsync_ReturnsNotFound_FromProblemDetails()
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
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a conflict problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task AddAsync_ReturnsConflict_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "Evaluation notes can only be added to an Active campaign."
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("Evaluation notes can only be added to an Active campaign.");
    }

    /// <summary>
    /// Verifies a successful response without the required payload becomes an explicit server error.
    /// </summary>
    [Fact]
    public async Task AddAsync_ReturnsServerError_ForEmptySuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(string.Empty)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        result.Problem.Detail.ShouldBe("The server returned an invalid evaluation note response.");
    }

    /// <summary>
    /// Verifies a successful response with a non-positive identifier is rejected as an invalid payload.
    /// </summary>
    [Fact]
    public async Task AddAsync_ReturnsServerError_ForInvalidCreatedIdentifier()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new EvaluationNoteMutationSuccess(0))
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(
            ValidAddInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a successful edit puts to the shared route and returns success for a no-content response.
    /// </summary>
    [Fact]
    public async Task EditAsync_PutsToSharedRoute_AndReturnsSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).EditAsync(
            ValidEditInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.EditEvaluationNoteUrl(7));
        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody.ShouldContain("\"content\":\"Updated evaluation note content.\"");
        handler.LastRequestBody.ShouldNotContain("noteId");
    }

    /// <summary>
    /// Verifies a conflict problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task EditAsync_ReturnsConflict_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "Evaluation notes can only be edited in an Active campaign."
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).EditAsync(
            ValidEditInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("Evaluation notes can only be edited in an Active campaign.");
    }

    /// <summary>
    /// Verifies a successful delete deletes the shared route and returns success for a no-content response.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_DeletesToSharedRoute_AndReturnsSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).DeleteAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.DeleteEvaluationNoteUrl(7));
    }

    /// <summary>
    /// Verifies a not-found problem response maps to the matching kind.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ReturnsNotFound_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new
            {
                title = "Not Found",
                status = 404,
                detail = "The evaluation note was not found."
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignEvaluationNoteService(http).DeleteAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        result.Problem.Detail.ShouldBe("The evaluation note was not found.");
    }

    /// <summary>
    /// Creates a valid add request.
    /// </summary>
    /// <returns>A valid request for client serialization.</returns>
    private static AddEvaluationNoteInput ValidAddInput() => new()
    {
        PlayerCampaignAssignmentId = 100,
        Content = "Showing solid tactical awareness in transition."
    };

    /// <summary>
    /// Creates a valid edit request.
    /// </summary>
    /// <returns>A valid request for client serialization.</returns>
    private static EditEvaluationNoteInput ValidEditInput() => new()
    {
        NoteId = 7,
        Content = "Updated evaluation note content."
    };
}
