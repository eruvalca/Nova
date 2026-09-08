using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services.Tags;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Tests the WebAssembly HTTP client implementation for tag-definition lifecycle mutations.
/// </summary>
public sealed class HttpTagDefinitionLifecycleServiceTests
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
    public async Task ArchiveAsyncPostsToSharedRouteAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        using var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpTagDefinitionLifecycleService(httpClient);

        var result = await service.ArchiveAsync(42, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.ArchiveUrl(42));
    }

    [Fact]
    public async Task RestoreAsyncPostsToSharedRouteAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        using var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpTagDefinitionLifecycleService(httpClient);

        var result = await service.RestoreAsync(77, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.RestoreUrl(77));
    }

    [Fact]
    public async Task ArchiveAsyncReturnsStructuredConflictAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new { detail = "Resolve active player associations first." })
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpTagDefinitionLifecycleService(httpClient);

        var result = await service.ArchiveAsync(42, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }
}
