using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies route and response handling for the typed team HTTP client.
/// </summary>
public sealed class HttpTeamManagementServiceTests
{
    [Fact]
    public async Task Create_SendsPostToTeamRoute_AndReadsDto()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 7,
                ClubId = 42,
                Name = "U16",
                GraduationYear = 2028,
                LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/teams");
    }

    [Fact]
    public async Task Update_SendsPutToTeamRoute_AndReadsDto()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 7,
                ClubId = 42,
                Name = "U16",
                GraduationYear = 2028,
                LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).UpdateAsync(
            new UpdateTeamInput { TeamId = 7, Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/teams/7");
    }

    /// <summary>
    /// Verifies update rejects a success payload for a different team.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsServerError_WhenResponseTeamIdDoesNotMatch()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 8,
                ClubId = 42,
                Name = "U16",
                GraduationYear = 2028,
                LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).UpdateAsync(
            new UpdateTeamInput { TeamId = 7, Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies invalid successful create-response bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task CreateAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies create responses that violate portable team invariants are rejected.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ReturnsServerError_WhenTeamInvariantIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 0,
                ClubId = 42,
                Name = "U16",
                GraduationYear = 2028,
                LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies bounded years and defined lifecycle states are required in team responses.
    /// </summary>
    /// <param name="graduationYear">The graduation year returned by the server.</param>
    /// <param name="lifecycleStatus">The lifecycle status returned by the server.</param>
    [Theory]
    [InlineData(1999, LifecycleStatus.Active)]
    [InlineData(2028, (LifecycleStatus)99)]
    public async Task CreateAsync_ReturnsServerError_WhenTeamStateIsInvalid(
        int graduationYear,
        LifecycleStatus lifecycleStatus)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 7,
                ClubId = 42,
                Name = "U16",
                GraduationYear = graduationYear,
                LifecycleStatus = lifecycleStatus
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
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
}
