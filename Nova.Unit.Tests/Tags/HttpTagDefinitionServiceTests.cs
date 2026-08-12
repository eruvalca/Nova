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

    [Fact]
    public async Task CreateAsync_ReturnsStructuredProblem_FromConflictResponse()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.8",
                title = "Conflict",
                status = 409,
                detail = "A tag definition named 'Skills' already exists."
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Skills", Color = "#123456" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("A tag definition named 'Skills' already exists.");
    }

    [Fact]
    public async Task CreateAsync_ReturnsValidationProblem_FromValidationProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                title = "One or more validation errors occurred.",
                status = 400,
                errors = new Dictionary<string, string[]>
                {
                    ["Color"] = ["Color must be a hex value in the format #RRGGBB."]
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).CreateAsync(
            new CreateTagDefinitionInput { Name = "Skills", Color = "red" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey("Color");
        result.Problem.Errors!["Color"].ShouldBe(["Color must be a hex value in the format #RRGGBB."]);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNotFound_FromNotFoundResponse()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                title = "Not Found",
                status = 404,
                detail = "The requested tag definition was not found."
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).UpdateAsync(
            new UpdateTagDefinitionInput { TagDefinitionId = 99, Name = "Skills", Color = "#123456" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        result.Problem.Detail.ShouldBe("The requested tag definition was not found.");
    }

    [Fact]
    public async Task ArchiveAsync_ReturnsForbidden_FromForbiddenResponse()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                title = "Forbidden",
                status = 403,
                detail = "You must be a club administrator to archive tag definitions."
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).ArchiveAsync(7, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        result.Problem.Detail.ShouldBe("You must be a club administrator to archive tag definitions.");
    }

    [Fact]
    public async Task GetActiveAsync_AcceptsValidList_PreservingServerOrder()
    {
        var first = new TagDefinitionSummary
        {
            TagDefinitionId = 1,
            Name = "Alpha",
            Color = "#111111",
            LifecycleStatus = LifecycleStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            ArchivedAt = null
        };
        var second = new TagDefinitionSummary
        {
            TagDefinitionId = 2,
            Name = "Beta",
            Color = "#222222",
            LifecycleStatus = LifecycleStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            ArchivedAt = null
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { second, first })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetActiveAsync(
            new GetTagDefinitionsInput { Limit = 10 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var list = result.Value;
        list.Count.ShouldBe(2);
        list[0].Name.ShouldBe("Beta");
        list[1].Name.ShouldBe("Alpha");
    }

    [Fact]
    public async Task GetActiveAsync_AcceptsEmptyList()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(Array.Empty<TagDefinitionSummary>())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetActiveAsync(
            new GetTagDefinitionsInput { Limit = 10 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetArchivedAsync_AcceptsValidArchivedList()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TagDefinitionSummary
                {
                    TagDefinitionId = 11,
                    Name = "Former",
                    Color = "#AABBCC",
                    LifecycleStatus = LifecycleStatus.Archived,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1)
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetArchivedAsync(
            new GetTagDefinitionsInput { Limit = 10 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value[0].TagDefinitionId.ShouldBe(11);
        result.Value[0].LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
    }

    [Fact]
    public async Task GetActiveAsync_RejectsList_WithInvalidColor()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TagDefinitionSummary
                {
                    TagDefinitionId = 1,
                    Name = "Skills",
                    Color = "not-a-color",
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

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    public async Task GetActiveAsync_ReturnsServerError_WhenListBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTagDefinitionService(http).GetActiveAsync(
            new GetTagDefinitionsInput { Limit = 10 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
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
