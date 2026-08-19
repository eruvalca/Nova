using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Tags;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Verifies route and response handling for the typed tag-definition management HTTP client.
/// </summary>
public sealed class HttpTagDefinitionServiceTests
{
    private static TagDefinitionDto ValidDto(
        long playerTagId = 7,
        string name = "Forward",
        string color = "#FF0000",
        LifecycleStatus lifecycleStatus = LifecycleStatus.Active)
        => new()
        {
            PlayerTagId = playerTagId,
            Name = name,
            Color = color,
            LifecycleStatus = lifecycleStatus
        };

    [Fact]
    public async Task CreateAsync_SendsPostToCreateRoute_AndReadsDto()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(ValidDto())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.Create);
    }

    [Fact]
    public async Task UpdateAsync_SendsPutToUpdateRoute_AndReadsDto()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidDto())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).UpdateAsync(
            new UpdateTagDefinitionInput { TagId = 7, Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.UpdateUrl(7));
    }

    /// <summary>
    /// Verifies update accepts an archived snapshot: a concurrent archive can commit between the
    /// update and its ambiguous-commit verification, which re-reads the current row.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsSuccess_WhenResponseLifecycleIsArchived()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidDto(lifecycleStatus: LifecycleStatus.Archived))
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).UpdateAsync(
            new UpdateTagDefinitionInput { TagId = 7, Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
    }

    /// <summary>
    /// Verifies create accepts an archived snapshot: a concurrent archive can commit between the
    /// create and its ambiguous-commit verification, which re-reads the current row.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ReturnsSuccess_WhenResponseLifecycleIsArchived()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(ValidDto(lifecycleStatus: LifecycleStatus.Archived))
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsServerError_WhenResponseTagIdDoesNotMatch()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidDto(playerTagId: 8))
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).UpdateAsync(
            new UpdateTagDefinitionInput { TagId = 7, Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies invalid successful create-response bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
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
            new CreateTagDefinitionInput { Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task CreateAsync_ReturnsServerError_WhenTagDefinitionInvariantIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(ValidDto(playerTagId: 0))
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies defined lifecycle states, bounded names, and normalized uppercase colors are required
    /// in tag-definition responses.
    /// </summary>
    /// <param name="name">The name returned by the server.</param>
    /// <param name="color">The color returned by the server.</param>
    /// <param name="lifecycleStatus">The lifecycle status returned by the server.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("Forward", "#ff0000", LifecycleStatus.Active)]
    [InlineData("Forward", "#FF0000", (LifecycleStatus)99)]
    public async Task CreateAsync_ReturnsServerError_WhenTagDefinitionStateIsInvalid(
        string name,
        string color,
        LifecycleStatus lifecycleStatus)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(ValidDto(name: name, color: color, lifecycleStatus: lifecycleStatus))
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Forward", Color = "#ff0000" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies update still rejects an undefined lifecycle status, even though it accepts archived.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsServerError_WhenLifecycleStatusIsUndefined()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidDto(lifecycleStatus: (LifecycleStatus)99))
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).UpdateAsync(
            new UpdateTagDefinitionInput { TagId = 7, Name = "Forward", Color = "#ff0000" },
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
