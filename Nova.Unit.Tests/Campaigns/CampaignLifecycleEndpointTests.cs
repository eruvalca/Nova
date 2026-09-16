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
using Nova.Features.Common;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Security;
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
    /// Verifies every lifecycle route is registered with club-administrator authorization,
    /// disabled antiforgery, the intended verb, and the shared route name.
    /// </summary>
    [Fact]
    public async Task CampaignLifecycleEndpointsRequireClubAdminAndDisableAntiforgeryAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_ => new CampaignLifecycleService(
            Substitute.For<IDbContextFactory<NovaDbContext>>(),
            Substitute.For<ICurrentUserProvider>(),
            NullLogger<CampaignLifecycleService>.Instance));
        await using var app = builder.Build();

        app.MapCampaignLifecycleEndpoints();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var close = routeEndpoints.SingleOrDefault(
            candidate => string.Equals(candidate.RoutePattern.RawText, CampaignEndpoints.Close, StringComparison.Ordinal));
        var reopen = routeEndpoints.SingleOrDefault(
            candidate => string.Equals(candidate.RoutePattern.RawText, CampaignEndpoints.Reopen, StringComparison.Ordinal));
        var open = routeEndpoints.SingleOrDefault(
            candidate => string.Equals(candidate.RoutePattern.RawText, CampaignEndpoints.Open, StringComparison.Ordinal));
        var delete = routeEndpoints.SingleOrDefault(
            candidate => string.Equals(candidate.RoutePattern.RawText, CampaignEndpoints.DeleteDraft, StringComparison.Ordinal));

        close.ShouldNotBeNull(
            $"The close endpoint must be registered at '{CampaignEndpoints.Close}'.");
        reopen.ShouldNotBeNull(
            $"The reopen endpoint must be registered at '{CampaignEndpoints.Reopen}'.");
        open.ShouldNotBeNull(
            $"The open endpoint must be registered at '{CampaignEndpoints.Open}'.");
        delete.ShouldNotBeNull(
            $"The delete endpoint must be registered at '{CampaignEndpoints.DeleteDraft}'.");

        AssertLifecycleEndpoint(close, CampaignEndpoints.CloseRouteName);
        AssertLifecycleEndpoint(reopen, CampaignEndpoints.ReopenRouteName);
        AssertLifecycleEndpoint(open, CampaignEndpoints.OpenRouteName);
        AssertLifecycleEndpoint(delete, CampaignEndpoints.DeleteDraftRouteName, HttpMethods.Delete);
    }

    /// <summary>
    /// Verifies a successful close result converts to a 204 no-content response with an empty body.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResultReturnsNoContentForSuccessAsync()
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
    public async Task CloseToHttpResultReturnsNotFoundWithoutDisclosureAsync()
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
    public async Task CloseToHttpResultReturnsForbiddenWithServiceDetailAsync()
    {
        const string Detail = "You must be a club administrator to close a campaign.";
        CampaignCloseResult result = new LifecycleForbidden(Detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status403Forbidden);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
    }

    /// <summary>
    /// Verifies close blockers convert to a 409 response whose errors extension carries the
    /// condition-keyed blocker groups with their policy messages.
    /// </summary>
    [Fact]
    public async Task CloseToHttpResultReturnsConflictWithConditionKeyedBlockerErrorsAsync()
    {
        const string Detail = "Resolve all campaign close blockers before closing this campaign.";
        CampaignCloseResult result = new CampaignCloseBlocked(
            Detail,
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["outcomes"] = ["Every participant must have a final outcome before closing. Found 1 undecided participation record(s)."],
                ["eligibility"] = ["Every assigned participant must remain eligible for their team. Ineligible assignment ids: 903."],
                ["archivedTeams"] = ["Assigned participants cannot reference archived teams. Blocked assignment ids: 904."]
            });

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
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
    public async Task CloseToHttpResultReturnsConflictWithServiceDetailAsync()
    {
        const string Detail = "The campaign is already closed.";
        CampaignCloseResult result = new LifecycleConflict(Detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
    }

    /// <summary>
    /// Verifies a successful reopen result converts to a 204 no-content response with an empty body.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResultReturnsNoContentForSuccessAsync()
    {
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown> result = new Success();

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status204NoContent);
        body.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies an unavailable reopen target converts to a non-disclosing 404 response.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResultReturnsNotFoundWithoutDisclosureAsync()
    {
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown> result = new NotFound();

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status404NotFound);
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies a forbidden reopen result converts to a 403 response with the service detail.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResultReturnsForbiddenWithServiceDetailAsync()
    {
        const string Detail = "You must be a club administrator to reopen a campaign.";
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown> result =
            new LifecycleForbidden(Detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status403Forbidden);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
    }

    /// <summary>
    /// Verifies a reopen lifecycle conflict converts to a 409 response with the conflict detail.
    /// </summary>
    [Fact]
    public async Task ReopenToHttpResultReturnsConflictWithServiceDetailAsync()
    {
        const string Detail = "The campaign is already active.";
        OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict, LifecycleOutcomeUnknown> result =
            new LifecycleConflict(Detail);

        var (statusCode, body) = await ExecuteAsync(result.ToHttpResult());

        statusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
    }

    /// <summary>
    /// Asserts the shared registration contract for one lifecycle endpoint.
    /// </summary>
    /// <param name="endpoint">The registered route endpoint to inspect.</param>
    /// <param name="routeName">The expected shared route name.</param>
    /// <param name="method">The expected HTTP method.</param>
    private static void AssertLifecycleEndpoint(
        RouteEndpoint endpoint,
        string routeName,
        string? method = null)
    {
        endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(metadata => metadata.Policy == Policies.RequireClubAdmin);
        endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation.ShouldBeFalse();
        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.ShouldBe(routeName);
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
            .ShouldContain(method ?? HttpMethods.Post, StringComparer.Ordinal);
    }

    /// <summary>
    /// Executes an HTTP result against an isolated HTTP context and captures the response status and body.
    /// </summary>
    /// <param name="result">The HTTP result to execute.</param>
    /// <returns>The captured response status code and body text.</returns>
    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using var services = new ServiceCollection()
#pragma warning restore MA0004
            .AddLogging()
            .BuildServiceProvider();
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using var responseBody = new MemoryStream();
#pragma warning restore MA0004
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
