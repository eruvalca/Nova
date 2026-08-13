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
/// Verifies route and response handling for the typed tag-definition query HTTP client.
/// </summary>
public sealed class HttpTagDefinitionQueryServiceTests
{
    private static TagDefinitionDto ValidDto(
        long playerTagId = 7,
        string name = "Forward",
        LifecycleStatus lifecycleStatus = LifecycleStatus.Active)
        => new()
        {
            PlayerTagId = playerTagId,
            Name = name,
            Color = "#FF0000",
            LifecycleStatus = lifecycleStatus
        };

    [Fact]
    public async Task GetManagementListAsync_SendsGetToManagementRoute_AndReadsList()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionListResult { Items = [ValidDto()], HasMore = false })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(1);
        result.Value.HasMore.ShouldBeFalse();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.GetListUrl());
    }

    [Fact]
    public async Task GetManagementListAsync_SendsGetWithFilters()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionListResult { Items = [ValidDto()], HasMore = false })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput { Search = "forward", LifecycleStatus = "active" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.GetListUrl("forward", "active"));
    }

    [Fact]
    public async Task GetChoicesAsync_SendsGetToChoicesRoute_AndReadsList()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<TagDefinitionDto> { ValidDto() })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetChoicesAsync(
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe(TagEndpoints.GetChoicesUrl());
    }

    /// <summary>
    /// Verifies invalid input is rejected client-side before any HTTP request is sent.
    /// </summary>
    [Fact]
    public async Task GetManagementListAsync_ReturnsValidation_WhenInputInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<TagDefinitionDto>())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput { Search = new string('a', TagDefinitionLimits.MaxSearchLength + 1) },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    /// <summary>
    /// Verifies invalid successful list-response bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetManagementListAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsServerError_WhenListExceedsBound()
    {
        var rows = Enumerable.Range(1, TagDefinitionLimits.MaxTagDefinitions + 1)
            .Select(i => ValidDto(playerTagId: i, name: $"Tag{i}"))
            .ToList();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionListResult { Items = rows, HasMore = true })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsServerError_WhenRowInvariantIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionListResult { Items = [ValidDto(playerTagId: 0)], HasMore = false })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a management-list row whose lifecycle mismatches the requested view is rejected.
    /// </summary>
    [Fact]
    public async Task GetManagementListAsync_ReturnsServerError_WhenRowLifecycleMismatchesView()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionListResult { Items = [ValidDto(lifecycleStatus: LifecycleStatus.Active)], HasMore = false })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput { LifecycleStatus = "archived" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetManagementListAsync_ReturnsServerError_WhenHasMoreButPageNotFull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TagDefinitionListResult { Items = [ValidDto()], HasMore = true })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetManagementListAsync(
            new GetTagDefinitionsInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the choices read path rejects an archived row, since choices only ever returns active.
    /// </summary>
    [Fact]
    public async Task GetChoicesAsync_ReturnsServerError_WhenChoiceIsArchived()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<TagDefinitionDto> { ValidDto(lifecycleStatus: LifecycleStatus.Archived) })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionQueryService(http).GetChoicesAsync(
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
