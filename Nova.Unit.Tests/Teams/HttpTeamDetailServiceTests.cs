using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies response handling for the team-detail HTTP client.
/// </summary>
public sealed class HttpTeamDetailServiceTests
{
    /// <summary>
    /// Verifies successful detail payloads are requested through the shared route.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsDetail_OnSuccess()
    {
        var payload = new TeamDetailDto(7, 8, "U16", 2028, LifecycleStatus.Active, [], []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(7, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TeamId.ShouldBe(7);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/teams/7");
    }

    /// <summary>
    /// Verifies malformed successful responses are surfaced as protocol failures.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenPayloadIsEmpty()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create<object?>(null)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(7, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(Nova.Shared.Results.ServiceProblemKind.ServerError);
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
