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
    [Theory(IncludeTestCaseIndex = true)]
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

    [Theory]
    [InlineData("promote", "POST", "/api/clubs/members/99/promote")]
    [InlineData("demote", "POST", "/api/clubs/members/99/demote")]
    [InlineData("remove", "DELETE", "/api/clubs/members/99")]
    [InlineData("leave", "DELETE", "/api/clubs/membership")]
    public async Task MembershipMutation_UsesExpectedMethodAndRoute(string operation, string method, string path)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpClubMemberService(httpClient);

        var result = operation switch
        {
            "promote" => await service.PromoteMemberAsync(99, TestContext.Current.CancellationToken),
            "demote" => await service.DemoteMemberAsync(99, TestContext.Current.CancellationToken),
            "remove" => await service.RemoveMemberAsync(99, TestContext.Current.CancellationToken),
            _ => await service.LeaveClubAsync(TestContext.Current.CancellationToken),
        };

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.Method.ShouldBe(method);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(path);
    }
}
