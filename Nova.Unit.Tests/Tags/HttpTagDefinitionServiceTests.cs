using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Tags;

/// <summary>
/// Verifies route and payload handling for the tag-definition HTTP client.
/// </summary>
public sealed class HttpTagDefinitionServiceTests
{
    [Fact]
    public async Task CreateAsync_SendsPostToCreateRoute_AndReadsMutationSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new TagDefinitionMutationSuccess { TagDefinitionId = 7 })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Skills", Color = "#123456" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/tags");
    }

    [Fact]
    public async Task UpdateAsync_SendsPutToUpdateRoute_AndReadsMutationSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionMutationSuccess { TagDefinitionId = 7 })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).UpdateAsync(
            new UpdateTagDefinitionInput { TagDefinitionId = 7, Name = "Skills", Color = "#123456" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/tags/7");
    }

    [Fact]
    public async Task GetActiveAsync_RejectsInvalidPayloads()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TagDefinitionSummary
                {
                    TagDefinitionId = 0,
                    Name = "",
                    Color = "#123456",
                    LifecycleStatus = LifecycleStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ArchivedAt = null
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetActiveAsync(
            new GetTagDefinitionsInput { Limit = 10 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetArchivedAsync_RejectsActiveListPayloads()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TagDefinitionSummary
                {
                    TagDefinitionId = 11,
                    Name = "Skills",
                    Color = "#123456",
                    LifecycleStatus = LifecycleStatus.Active,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ArchivedAt = null
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetArchivedAsync(
            new GetTagDefinitionsInput { Limit = 10 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetActiveAsync_ClampsLimit_WhenRequestExceedsBounds()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<TagDefinitionSummary>())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetActiveAsync(
            new GetTagDefinitionsInput { Limit = 999 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.Query.ShouldContain("limit=100");
    }

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

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Skills", Color = "#123456" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task ArchiveAsync_SendsPostToArchiveRoute_AndReadsMutationSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionMutationSuccess { TagDefinitionId = 7 })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).ArchiveAsync(7, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/tags/7/archive");
    }

    [Fact]
    public async Task RestoreAsync_SendsPostToRestoreRoute_AndReadsMutationSuccess()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionMutationSuccess { TagDefinitionId = 7 })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).RestoreAsync(7, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/tags/7/restore");
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
