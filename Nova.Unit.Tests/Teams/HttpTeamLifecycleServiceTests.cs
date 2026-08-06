using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Results;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Tests the WebAssembly HTTP client implementation for team lifecycle mutations.
/// </summary>
public sealed class HttpTeamLifecycleServiceTests
{
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

    [Fact]
    public async Task ArchiveAsync_PostsToSharedRoute()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpTeamLifecycleService(httpClient);

        var result = await service.ArchiveAsync(42, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(TeamEndpoints.ArchiveUrl(42));
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsStructuredConflict()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                detail = "Resolve active placements first.",
                archiveBlockers = new[]
                {
                    new
                    {
                        campaignId = 700L,
                        campaignName = "Active Campaign",
                        placementIds = new[] { 801L }
                    }
                }
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpTeamLifecycleService(httpClient);

        var result = await service.ArchiveAsync(42, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.TryGetArchiveBlockers(out var blockers).ShouldBeTrue();
        blockers.Count.ShouldBe(1);
        blockers[0].CampaignId.ShouldBe(700);
        blockers[0].CampaignName.ShouldBe("Active Campaign");
        blockers[0].PlacementIds.ShouldBe([801]);
    }

    [Fact]
    public async Task RestoreAsync_PostsToSharedRoute()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpTeamLifecycleService(httpClient);

        var result = await service.RestoreAsync(77, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(TeamEndpoints.RestoreUrl(77));
    }
}
