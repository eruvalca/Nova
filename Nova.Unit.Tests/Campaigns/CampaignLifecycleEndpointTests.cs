using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Campaigns;
using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;
using NSubstitute;
using OneOf;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies campaign lifecycle endpoint route metadata and the close/reopen result HTTP conversion.
/// </summary>
public sealed class CampaignLifecycleEndpointTests
{
    /// <summary>
    /// Verifies the close and reopen routes are registered with club-administrator authorization,
    /// disabled antiforgery, the POST verb, and the shared route names.
    /// </summary>
    [Fact]
    public async Task CampaignLifecycleEndpoints_RequireClubAdmin_AndDisableAntiforgery()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_ => new CampaignLifecycleService(
            Substitute.For<IDbContextFactory<NovaDbContext>>(),
            Substitute.For<ICurrentUserProvider>(),
            NullLogger<CampaignLifecycleService>.Instance,
            new Nova.Features.ClubActivity.ClubActivityEventWriter()));
        await using var app = builder.Build();

        app.MapCampaignLifecycleEndpoints();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var close = routeEndpoints.SingleOrDefault(
            candidate => candidate.RoutePattern.RawText == CampaignEndpoints.Close);
        var reopen = routeEndpoints.SingleOrDefault(
            candidate => candidate.RoutePattern.RawText == CampaignEndpoints.Reopen);

        close.ShouldNotBeNull(
            $"The close endpoint must be registered at '{CampaignEndpoints.Close}'.");
        reopen.ShouldNotBeNull(
            $"The reopen endpoint must be registered at '{CampaignEndpoints.Reopen}'.");

        AssertLifecycleEndpoint(close!, CampaignEndpoints.CloseRouteName);
        AssertLifecycleEndpoint(reopen!, CampaignEndpoints.ReopenRouteName);
    }

    /// <summary>
    /// Verifies a successful close result converts to a 204 no-content response with an empty body.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResult_ReturnsNoContent_ForSuccess()
    {
        CampaignCloseResult result = new Success();

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status204NoContent);
        body.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies an unavailable campaign converts to a non-disclosing 404 response.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResult_ReturnsNotFound_WithoutDisclosure()
    {
        CampaignCloseResult result = new NotFound();

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status404NotFound);
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies a forbidden close result converts to a 403 response with the service detail.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResult_ReturnsForbidden_WithServiceDetail()
    {
        const string detail = "You must be a club administrator to close a campaign.";
        CampaignCloseResult result = new LifecycleForbidden(detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status403Forbidden);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
    }

    /// <summary>
    /// Verifies close blockers convert to a 409 response whose errors extension carries the
    /// condition-keyed blocker groups with their policy messages.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResult_ReturnsConflict_WithConditionKeyedBlockerErrors()
    {
        const string detail = "Resolve all campaign close blockers before closing this campaign.";
        CampaignCloseResult result = new CampaignCloseBlocked(
            detail,
            new Dictionary<string, string[]>
            {
                ["outcomes"] = ["Every participant must have a final outcome before closing. Found 1 undecided participation record(s)."],
                ["eligibility"] = ["Every assigned participant must remain eligible for their team. Ineligible assignment ids: 903."],
                ["archivedTeams"] = ["Assigned participants cannot reference archived teams. Blocked assignment ids: 904."]
            });

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
        var errors = document.RootElement.GetProperty("errors");
        errors.GetProperty("outcomes").GetArrayLength().ShouldBe(1);
        errors.GetProperty("eligibility").GetArrayLength().ShouldBe(1);
        errors.GetProperty("archivedTeams").GetArrayLength().ShouldBe(1);
        errors.GetProperty("outcomes")[0].GetString()!
            .ShouldContain("undecided participation record");
        errors.GetProperty("eligibility")[0].GetString()!
            .ShouldContain("Ineligible assignment ids: 903");
        errors.GetProperty("archivedTeams")[0].GetString()!
            .ShouldContain("Blocked assignment ids: 904");
    }

    /// <summary>
    /// Verifies a close lifecycle conflict converts to a 409 response with the conflict detail.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResult_ReturnsConflict_WithServiceDetail()
    {
        const string detail = "The campaign is already closed.";
        CampaignCloseResult result = new LifecycleConflict(detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
    }

    /// <summary>
    /// Verifies a successful reopen result converts to a 204 no-content response with an empty body.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResult_ReturnsNoContent_ForSuccess()
    {
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict> result = new Success();

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status204NoContent);
        body.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies an unavailable reopen target converts to a non-disclosing 404 response.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResult_ReturnsNotFound_WithoutDisclosure()
    {
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict> result = new NotFound();

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status404NotFound);
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies a forbidden reopen result converts to a 403 response with the service detail.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResult_ReturnsForbidden_WithServiceDetail()
    {
        const string detail = "You must be a club administrator to reopen a campaign.";
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict> result =
            new LifecycleForbidden(detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status403Forbidden);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
    }

    /// <summary>
    /// Verifies a reopen lifecycle conflict converts to a 409 response with the conflict detail.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResult_ReturnsConflict_WithServiceDetail()
    {
        const string detail = "The campaign is already active.";
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict> result =
            new LifecycleConflict(detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(detail);
    }

    /// <summary>
    /// Asserts the shared registration contract for one lifecycle endpoint.
    /// </summary>
    /// <param name="endpoint">The registered route endpoint to inspect.</param>
    /// <param name="routeName">The expected shared route name.</param>
    private static void AssertLifecycleEndpoint(RouteEndpoint endpoint, string routeName)
    {
        endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(metadata => metadata.Policy == Policies.RequireClubAdmin);
        endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation.ShouldBeFalse();
        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.ShouldBe(routeName);
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ShouldContain(HttpMethods.Post);
    }

    /// <summary>
    /// Executes an HTTP result against an isolated HTTP context and captures the response status and body.
    /// </summary>
    /// <param name="result">The HTTP result to execute.</param>
    /// <returns>The captured response status code and body text.</returns>
    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var responseBody = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = responseBody }
        };

        await result.ExecuteAsync(httpContext);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        return (httpContext.Response.StatusCode, body);
    }
}
