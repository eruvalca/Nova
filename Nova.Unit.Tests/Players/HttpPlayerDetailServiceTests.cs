using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="HttpPlayerDetailService"/> and shared player endpoint URL builders.
/// </summary>
public sealed class HttpPlayerDetailServiceTests
{
    /// <summary>
    /// Captures outgoing requests and returns a preconfigured response.
    /// </summary>
    /// <param name="response">The response returned to the caller.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the last request sent through the handler.
        /// </summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Verifies the client calls the shared detail URL and deserializes a successful payload.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsPlayerDetail_OnSuccess()
    {
        var payload = new PlayerDetailDto(
            PlayerId: 42,
            FirstName: "Alex",
            LastName: "Athlete",
            DateOfBirth: new DateOnly(2010, 2, 3),
            Gender: Gender.Male,
            GraduationYear: 2028,
            JerseyNumber: 11,
            LifecycleStatus: LifecycleStatus.Active,
            CurrentTraits: [new PlayerCurrentTraitDto(1, "Leadership", "#001122")],
            CampaignHistory: []);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerDetailService(httpClient);

        var result = await service.GetPlayerDetailAsync(42, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerId.ShouldBe(42);
        result.Value.FirstName.ShouldBe("Alex");
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe("/api/players/42");
    }

    /// <summary>
    /// Verifies the client maps unsuccessful responses into <see cref="ServiceProblem"/>.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServiceProblem_OnNotFound()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { detail = "Not found." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerDetailService(httpClient);

        var result = await service.GetPlayerDetailAsync(404, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies the shared URL builder creates the canonical detail route.
    /// </summary>
    [Fact]
    public void GetDetailUrl_BuildsCanonicalPlayerDetailRoute()
    {
        PlayerEndpoints.GetDetailUrl(123).ShouldBe("/api/players/123");
    }
}
