using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Features.Account;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests required success-response handling in <see cref="HttpClubMemberService"/>.
/// </summary>
public class HttpClubMemberServiceTests
{
    /// <summary>
    /// A test message handler that returns a configured response.
    /// </summary>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    /// <summary>
    /// GetClubMembersAsync accepts a populated member list when each row satisfies the contract.
    /// </summary>
    [Fact]
    public async Task GetClubMembersAsync_ReturnsMembers_WhenSuccessBodyIsValid()
    {
        var members = new[] { new ClubMemberDto(7, "Test User") };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(members)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubMemberService(httpClient)
            .GetClubMembersAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([members[0]]);
    }

    /// <summary>
    /// GetClubMembersAsync accepts a literal empty JSON array as an empty member list.
    /// </summary>
    [Fact]
    public async Task GetClubMembersAsync_ReturnsEmptyList_WhenSuccessBodyIsEmptyArray()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubMemberService(httpClient);

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.Count.ShouldBe(0);
    }

    /// <summary>
    /// GetClubMembersAsync returns a server error when a successful response has an invalid body.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetClubMembersAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubMemberService(httpClient);

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// GetClubMembersAsync returns a server error when one member violates an invariant.
    /// </summary>
    [Fact]
    public async Task GetClubMembersAsync_ReturnsServerError_WhenMemberElementIsInvalid()
    {
        // Arrange
        var members = new[] { new ClubMemberDto(0, "Test User") };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(members)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubMemberService(httpClient);

        // Act
        var result = await service.GetClubMembersAsync(TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// AssignClubAdminAsync rejects JSON false because success requires an affirmative acknowledgement.
    /// </summary>
    [Fact]
    public async Task AssignClubAdminAsync_ReturnsServerError_WhenSuccessBodyIsFalse()
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("false", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubMemberService(httpClient);
        var input = new AssignAdminInput { TargetUserId = 99 };

        // Act
        var result = await service.AssignClubAdminAsync(input, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// AssignClubAdminAsync accepts an affirmative successful acknowledgement.
    /// </summary>
    [Fact]
    public async Task AssignClubAdminAsync_ReturnsTrue_WhenSuccessBodyIsTrue()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("true", Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var input = new AssignAdminInput { TargetUserId = 99 };

        var result = await new HttpClubMemberService(httpClient)
            .AssignClubAdminAsync(input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
    }

    /// <summary>
    /// AssignClubAdminAsync returns a server error when a successful response has an invalid body.
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task AssignClubAdminAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        // Arrange
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubMemberService(httpClient);
        var input = new AssignAdminInput { TargetUserId = 99 };

        // Act
        var result = await service.AssignClubAdminAsync(input, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
