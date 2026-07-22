using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests the WebAssembly HTTP client implementation for player archive and restore lifecycle operations.
/// </summary>
public class HttpPlayerLifecycleServiceTests
{
    /// <summary>
    /// Captures the outbound request and returns a configured response.
    /// </summary>
    /// <param name="response">The response to return for every request.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the last request sent by the tested service.
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
    /// Verifies archive posts to the shared archive URL and returns success for 204 responses.
    /// </summary>
    [Fact]
    public async Task ArchiveAsync_ReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerLifecycleService(httpClient);

        var result = await service.ArchiveAsync(42, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(PlayerEndpoints.ArchiveUrl(42));
    }

    /// <summary>
    /// Verifies restore posts to the shared restore URL and returns success for 204 responses.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_ReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerLifecycleService(httpClient);

        var result = await service.RestoreAsync(77, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(PlayerEndpoints.RestoreUrl(77));
    }

    /// <summary>
    /// Verifies conflict responses preserve structured archive blockers on the reconstructed service problem.
    /// </summary>
    [Fact]
    public async Task ArchiveAsync_ReturnsConflict_WithStructuredBlockers()
    {
        var blocker = new PlayerArchiveBlocker
        {
            CampaignId = 700,
            CampaignName = "Active Campaign",
            ParticipationIds = [800]
        };

        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                detail = "Resolve blockers before archiving.",
                archiveBlockers = new[] { blocker }
            })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerLifecycleService(httpClient);

        var result = await service.ArchiveAsync(42, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.TryGetArchiveBlockers(out var blockers).ShouldBeTrue();
        blockers.Count.ShouldBe(1);
        blockers[0].CampaignId.ShouldBe(700);
        blockers[0].CampaignName.ShouldBe("Active Campaign");
        blockers[0].ParticipationIds.ShouldBe([800]);
    }
}
