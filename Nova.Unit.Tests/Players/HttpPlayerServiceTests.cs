using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="HttpPlayerService"/> roster query behavior.
/// </summary>
public sealed class HttpPlayerServiceTests
{
    /// <summary>
    /// A test HTTP handler that captures the outgoing request.
    /// </summary>
    /// <param name="response">The response to return.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task GetPlayerRosterAsync_SendsGetToRosterEndpoint_AndReturnsPagedResult()
    {
        var payload = new PagedResult<PlayerListItem>(
            [new PlayerListItem(10, "Alex Archer", new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero))],
            Page: 1,
            PageSize: 20,
            TotalCount: 1);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerService(httpClient);

        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput
            {
                ClubId = 42,
                Search = "Alex",
                SortBy = "joinedAt",
                SortDirection = "desc",
                Page = 1,
                PageSize = 20
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().DisplayName.ShouldBe("Alex Archer");
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/clubs/42/players/roster?search=Alex&sortBy=joinedAt&sortDirection=desc&page=1&pageSize=20");
    }

    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServiceProblem_OnNonSuccessStatusCode()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new { detail = "Forbidden." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerService(httpClient);

        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_OnNullSuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerService(httpClient);

        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
