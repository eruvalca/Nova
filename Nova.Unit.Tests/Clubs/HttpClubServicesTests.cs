using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="HttpClubService"/> and <see cref="HttpClubJoinRequestService"/>,
/// the WebAssembly client implementations of club and join request services.
///
/// These tests verify HTTP behavior: correct HTTP methods, URLs with parameters,
/// deserialization of response bodies, and error handling for various HTTP status codes.
/// URL builder constants are also tested.
/// </summary>
public class HttpClubServicesTests
{
    /// <summary>
    /// A test double for <see cref="HttpMessageHandler"/> that captures the request
    /// and returns a pre-configured response.
    /// </summary>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    #region HttpClubService.CreateClubAsync Tests

    /// <summary>
    /// CreateClubAsync sends a POST to /api/clubs and deserializes the ClubDto response.
    /// </summary>
    [Fact]
    public async Task CreateClubAsync_ReturnsClubDto_OnSuccess()
    {
        // Arrange
        var clubDto = new ClubDto(ClubId: 42, Name: "Manchester United", City: "Manchester", State: "England");
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(clubDto)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);
        var input = new CreateClubInput { Name = "Manchester United", City = "Manchester", State = "England" };

        // Act
        var result = await service.CreateClubAsync(input, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(42);
        result.Value.Name.ShouldBe("Manchester United");
        result.Value.City.ShouldBe("Manchester");
        result.Value.State.ShouldBe("England");
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs");
    }

    /// <summary>
    /// CreateClubAsync returns a ServiceProblem when the server returns a non-success status (e.g., 400).
    /// </summary>
    [Fact]
    public async Task CreateClubAsync_ReturnsServiceProblem_OnBadRequest()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { detail = "The club name must be unique." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);
        var input = new CreateClubInput { Name = "Manchester United", City = "Manchester", State = "England" };

        // Act
        var result = await service.CreateClubAsync(input, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.BadRequest);
        result.Problem.Detail.ShouldBe("The club name must be unique.");
    }

    #endregion

    #region HttpClubService.SearchClubsAsync Tests

    /// <summary>
    /// SearchClubsAsync sends a GET to the correct URL including the query parameter.
    /// </summary>
    [Fact]
    public async Task SearchClubsAsync_SendsGetToCorrectUrl_WithQueryParameter()
    {
        // Arrange
        var clubs = new[] { new ClubDto(1, "Manchester United", "Manchester", "England") };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(clubs)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);

        // Act
        var result = await service.SearchClubsAsync("Manchester", TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/search");
        handler.LastRequest!.RequestUri!.Query.ShouldContain("q=Manchester");
    }

    /// <summary>
    /// SearchClubsAsync returns a server error when a successful response has an invalid body.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task SearchClubsAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);

        // Act
        var result = await service.SearchClubsAsync(null, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// SearchClubsAsync returns a ServiceProblem on non-success HTTP status (e.g., 404).
    /// </summary>
    [Fact]
    public async Task SearchClubsAsync_ReturnsServiceProblem_OnNotFound()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { detail = "No clubs found." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);

        // Act
        var result = await service.SearchClubsAsync("NonExistent", TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    #endregion

    #region HttpClubService URL Builder Tests

    /// <summary>
    /// SearchUrl constructs the correct URL with null query.
    /// </summary>
    [Fact]
    public void HttpClubService_SearchUrl_ReturnsBaseSearchUrl_WhenQueryIsNull()
    {
        // Arrange
        const string expectedUrl = "/api/clubs/search";

        // Act
        var url = ClubEndpoints.SearchUrl(null);

        // Assert
        url.ShouldBe(expectedUrl);
    }

    /// <summary>
    /// SearchUrl constructs the correct URL with empty query.
    /// </summary>
    [Fact]
    public void HttpClubService_SearchUrl_ReturnsBaseSearchUrl_WhenQueryIsEmpty()
    {
        // Arrange
        const string expectedUrl = "/api/clubs/search";

        // Act
        var url = ClubEndpoints.SearchUrl(string.Empty);

        // Assert
        url.ShouldBe(expectedUrl);
    }

    /// <summary>
    /// SearchUrl constructs the correct URL with whitespace query.
    /// </summary>
    [Fact]
    public void HttpClubService_SearchUrl_ReturnsBaseSearchUrl_WhenQueryIsWhitespace()
    {
        // Arrange
        const string expectedUrl = "/api/clubs/search";

        // Act
        var url = ClubEndpoints.SearchUrl("   ");

        // Assert
        url.ShouldBe(expectedUrl);
    }

    /// <summary>
    /// SearchUrl constructs the correct URL with query parameter.
    /// </summary>
    [Fact]
    public void HttpClubService_SearchUrl_IncludesQuery_WhenProvided()
    {
        // Arrange
        const string query = "Manchester United";
        const string expectedUrl = "/api/clubs/search?q=Manchester%20United";

        // Act
        var url = ClubEndpoints.SearchUrl(query);

        // Assert
        url.ShouldBe(expectedUrl);
    }

    /// <summary>
    /// SearchUrl URL-encodes special characters in query.
    /// </summary>
    [Fact]
    public void HttpClubService_SearchUrl_UrlEncodesSpecialCharacters()
    {
        // Arrange
        const string query = "FC & Friends";

        // Act
        var url = ClubEndpoints.SearchUrl(query);

        // Assert
        url.ShouldContain("%26");  // Ampersand should be encoded
    }

    #endregion

    #region HttpClubJoinRequestService.GetCurrentUserPendingRequestAsync Tests

    /// <summary>
    /// GetCurrentUserPendingRequestAsync sends a GET to /api/clubs/join-requests/pending
    /// and deserializes the ClubJoinRequestDto from the response.
    /// </summary>
    [Fact]
    public async Task GetCurrentUserPendingRequestAsync_ReturnsClubJoinRequestDto_OnSuccess()
    {
        // Arrange
        var dto = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Liverpool",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: DateTimeOffset.UtcNow);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(dto)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubJoinRequestId.ShouldBe(10);
        result.Value.ClubId.ShouldBe(5);
        result.Value.ClubName.ShouldBe("Liverpool");
        result.Value.RequestingUserId.ShouldBe(99);
        result.Value.Status.ShouldBe(RequestStatus.Pending);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/join-requests/pending");
    }

    /// <summary>
    /// GetCurrentUserPendingRequestAsync returns ServiceProblem.NotFound on 404.
    /// </summary>
    [Fact]
    public async Task GetCurrentUserPendingRequestAsync_ReturnsNotFound_On404()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { detail = "No pending request found." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// GetCurrentUserPendingRequestAsync accepts the current user's existing non-pending request.
    /// </summary>
    [Fact]
    public async Task GetCurrentUserPendingRequestAsync_ReturnsRequest_ForNonPendingResponse()
    {
        var dto = new ClubJoinRequestDto(
            ClubJoinRequestId: 10,
            ClubId: 5,
            ClubName: "Liverpool",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Approved,
            CreatedAt: DateTimeOffset.UtcNow);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(dto)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubJoinRequestService(httpClient)
            .GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(RequestStatus.Approved);
    }

    #endregion

    #region HttpClubJoinRequestService.CreateJoinRequestAsync Tests

    /// <summary>
    /// CreateJoinRequestAsync sends a POST to /api/clubs/{clubId}/join-requests.
    /// </summary>
    [Fact]
    public async Task CreateJoinRequestAsync_SendsPostToCorrectUrl_WithClubId()
    {
        // Arrange
        var dto = new ClubJoinRequestDto(
            ClubJoinRequestId: 20,
            ClubId: 7,
            ClubName: "Arsenal",
            RequestingUserId: 100,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: DateTimeOffset.UtcNow);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(dto)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.CreateJoinRequestAsync(7, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ClubId.ShouldBe(7);
        result.Value.ClubName.ShouldBe("Arsenal");
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/7/join-requests");
    }

    #endregion

    #region HttpClubJoinRequestService.CancelJoinRequestAsync Tests

    /// <summary>
    /// CancelJoinRequestAsync sends a DELETE to /api/clubs/join-requests/{requestId}
    /// and returns a Success result.
    /// </summary>
    [Fact]
    public async Task CancelJoinRequestAsync_SendsDeleteToCorrectUrl_AndReturnsSuccess()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.CancelJoinRequestAsync(25, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<Success>();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/join-requests/25");
    }

    /// <summary>
    /// CancelJoinRequestAsync returns a ServiceProblem on 403 Forbidden.
    /// </summary>
    [Fact]
    public async Task CancelJoinRequestAsync_ReturnsForbidden_On403()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new { detail = "You do not have permission to cancel this request." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.CancelJoinRequestAsync(25, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        result.Problem.Detail.ShouldBe("You do not have permission to cancel this request.");
    }

    #endregion

    #region Endpoint Constants Verification Tests

    /// <summary>
    /// Create endpoint constant has the expected value.
    /// </summary>
    [Fact]
    public void ClubEndpoints_Create_HasCorrectValue() =>
        // Assert
        ClubEndpoints.Create.ShouldBe("/api/clubs");

    /// <summary>
    /// Search endpoint constant has the expected value.
    /// </summary>
    [Fact]
    public void ClubEndpoints_Search_HasCorrectValue() =>
        // Assert
        ClubEndpoints.Search.ShouldBe("/api/clubs/search");

    /// <summary>
    /// PendingRequest endpoint constant has the correct value.
    /// </summary>
    [Fact]
    public void ClubEndpoints_PendingRequest_HasCorrectValue() =>
        // Assert
        ClubEndpoints.PendingRequest.ShouldBe("/api/clubs/join-requests/pending");

    /// <summary>
    /// CreateJoinRequestTemplate has the expected value.
    /// </summary>
    [Fact]
    public void ClubEndpoints_CreateJoinRequestTemplate_HasCorrectValue() =>
        // Assert
        ClubEndpoints.CreateJoinRequestTemplate.ShouldBe("/api/clubs/{clubId:long}/join-requests");

    /// <summary>
    /// CancelJoinRequestTemplate has the expected value.
    /// </summary>
    [Fact]
    public void ClubEndpoints_CancelJoinRequestTemplate_HasCorrectValue() =>
        // Assert
        ClubEndpoints.CancelJoinRequestTemplate.ShouldBe("/api/clubs/join-requests/{requestId:long}");

    #endregion

    #region Endpoint URL Builder Tests

    /// <summary>
    /// CreateJoinRequestUrl builds the correct URL for a given club ID.
    /// </summary>
    [Theory]
    [InlineData(1, "/api/clubs/1/join-requests")]
    [InlineData(42, "/api/clubs/42/join-requests")]
    [InlineData(12345, "/api/clubs/12345/join-requests")]
    [InlineData(long.MaxValue, "/api/clubs/9223372036854775807/join-requests")]
    public void ClubEndpoints_CreateJoinRequestUrl_BuildsCorrectUrl(long clubId, string expectedUrl)
    {
        // Act
        var url = ClubEndpoints.CreateJoinRequestUrl(clubId);

        // Assert
        url.ShouldBe(expectedUrl);
    }

    /// <summary>
    /// CancelJoinRequestUrl builds the correct URL for a given request ID.
    /// </summary>
    [Theory]
    [InlineData(1, "/api/clubs/join-requests/1")]
    [InlineData(42, "/api/clubs/join-requests/42")]
    [InlineData(12345, "/api/clubs/join-requests/12345")]
    [InlineData(long.MaxValue, "/api/clubs/join-requests/9223372036854775807")]
    public void ClubEndpoints_CancelJoinRequestUrl_BuildsCorrectUrl(long requestId, string expectedUrl)
    {
        // Act
        var url = ClubEndpoints.CancelJoinRequestUrl(requestId);

        // Assert
        url.ShouldBe(expectedUrl);
    }

    #endregion

    #region Required Success Response Tests

    /// <summary>
    /// CreateClubAsync returns a server error when a successful response has an invalid body.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task CreateClubAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);
        var input = new CreateClubInput { Name = "Liverpool", City = "Liverpool", State = "England" };

        // Act
        var result = await service.CreateClubAsync(input, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// CreateClubAsync returns a server error when the response violates a club invariant.
    /// </summary>
    [Fact]
    public async Task CreateClubAsync_ReturnsServerError_WhenClubInvariantIsInvalid()
    {
        // Arrange
        var club = new ClubDto(0, "Liverpool", "Liverpool", "England");
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(club)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);
        var input = new CreateClubInput { Name = "Liverpool", City = "Liverpool", State = "England" };

        // Act
        var result = await service.CreateClubAsync(input, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// SearchClubsAsync accepts a literal empty JSON array as an empty club list.
    /// </summary>
    [Fact]
    public async Task SearchClubsAsync_ReturnsEmptyList_WhenSuccessBodyIsEmptyArray()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);

        // Act
        var result = await service.SearchClubsAsync(null, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Count.ShouldBe(0);
    }

    /// <summary>
    /// SearchClubsAsync returns a server error when one club violates a response invariant.
    /// </summary>
    [Fact]
    public async Task SearchClubsAsync_ReturnsServerError_WhenClubElementIsInvalid()
    {
        // Arrange
        var clubs = new[] { new ClubDto(0, "Liverpool", "Liverpool", "England") };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(clubs)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubService(httpClient);

        // Act
        var result = await service.SearchClubsAsync(null, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetCurrentUserPendingRequestAsync returns a server error for an invalid successful body.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetCurrentUserPendingRequestAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetCurrentUserPendingRequestAsync returns a server error for an invalid request identifier.
    /// </summary>
    [Fact]
    public async Task GetCurrentUserPendingRequestAsync_ReturnsServerError_WhenRequestInvariantIsInvalid()
    {
        // Arrange
        var request = CreateJoinRequest(0);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(request)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetCurrentUserPendingRequestAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetClubJoinRequestsAsync accepts a literal empty JSON array as an empty request list.
    /// </summary>
    [Fact]
    public async Task GetClubJoinRequestsAsync_ReturnsEmptyList_WhenSuccessBodyIsEmptyArray()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetClubJoinRequestsAsync(5, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Count.ShouldBe(0);
    }

    /// <summary>
    /// GetClubJoinRequestsAsync returns a server error when a successful response has an invalid body.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetClubJoinRequestsAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetClubJoinRequestsAsync(5, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetClubJoinRequestsAsync returns a server error when one request violates an invariant.
    /// </summary>
    [Fact]
    public async Task GetClubJoinRequestsAsync_ReturnsServerError_WhenRequestElementIsInvalid()
    {
        // Arrange
        var requests = new[] { CreateJoinRequest(0) };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(requests)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubJoinRequestService(httpClient);

        // Act
        var result = await service.GetClubJoinRequestsAsync(5, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetClubJoinRequestsAsync rejects entries belonging to a different club.
    /// </summary>
    [Fact]
    public async Task GetClubJoinRequestsAsync_ReturnsServerError_WhenResponseClubIdDoesNotMatch()
    {
        var requests = new[] { CreateJoinRequest(10) with { ClubId = 6 } };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(requests)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubJoinRequestService(httpClient).GetClubJoinRequestsAsync(
            5,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetClubJoinRequestsAsync rejects rows outside the contracted oldest-first identifier order.
    /// </summary>
    [Fact]
    public async Task GetClubJoinRequestsAsync_ReturnsServerError_WhenRequestsAreOutOfOrder()
    {
        var requests = new[] { CreateJoinRequest(11), CreateJoinRequest(10) };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(requests)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubJoinRequestService(httpClient).GetClubJoinRequestsAsync(
            5,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetClubJoinRequestsAsync rejects non-pending rows from the administrative pending queue.
    /// </summary>
    [Fact]
    public async Task GetClubJoinRequestsAsync_ReturnsServerError_WhenRequestIsNotPending()
    {
        var requests = new[] { CreateJoinRequest(10) with { Status = RequestStatus.Approved } };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(requests)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubJoinRequestService(httpClient).GetClubJoinRequestsAsync(
            5,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// CreateJoinRequestAsync rejects a success payload for a different club.
    /// </summary>
    [Fact]
    public async Task CreateJoinRequestAsync_ReturnsServerError_WhenResponseClubIdDoesNotMatch()
    {
        var request = CreateJoinRequest(10) with { ClubId = 6 };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(request)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubJoinRequestService(httpClient).CreateJoinRequestAsync(
            5,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Creates an otherwise valid club join request with the specified identifier.
    /// </summary>
    private static ClubJoinRequestDto CreateJoinRequest(long requestId) =>
        new(
            ClubJoinRequestId: requestId,
            ClubId: 5,
            ClubName: "Liverpool",
            RequestingUserId: 99,
            RequestingUserName: "Test User",
            Status: RequestStatus.Pending,
            CreatedAt: DateTimeOffset.Parse("2026-08-04T12:00:00+00:00"));

    #endregion
}
