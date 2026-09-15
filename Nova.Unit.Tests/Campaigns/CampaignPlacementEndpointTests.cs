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
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Security;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies campaign placement endpoint route metadata and the placement result HTTP conversion.
/// </summary>
public sealed class CampaignPlacementEndpointTests
{
    /// <summary>
    /// Verifies the placement update route is registered with club-member authorization,
    /// disabled antiforgery, the PUT verb, and the shared route name.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementEndpointRequiresClubMemberAndDisablesAntiforgeryAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ICampaignPlacementService>(_ => new CampaignPlacementService(
            Substitute.For<IDbContextFactory<NovaDbContext>>(),
            Substitute.For<ICurrentUserProvider>(),
            NullLogger<CampaignPlacementService>.Instance));
        builder.Services.AddSingleton<ICampaignPlacementQueryService>(_ => Substitute.For<ICampaignPlacementQueryService>());
        builder.Services.AddSingleton<IPlacementContextQueryService>(_ => Substitute.For<IPlacementContextQueryService>());
        await using var app = builder.Build();

        app.MapCampaignPlacementEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SingleOrDefault(candidate => string.Equals(candidate.RoutePattern.RawText, CampaignEndpoints.UpdateCampaignPlacement, StringComparison.Ordinal));

        endpoint.ShouldNotBeNull(
            $"The placement update endpoint must be registered at '{CampaignEndpoints.UpdateCampaignPlacement}'.");
        endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(metadata => metadata.Policy == Policies.RequireClubMember);
        endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>()!.RequiresValidation.ShouldBeFalse();
        endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe(CampaignEndpoints.UpdateCampaignPlacementRouteName);
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ShouldContain(HttpMethods.Put, StringComparer.Ordinal);
    }

    /// <summary>
    /// Verifies a successful placement result converts to a 200 response containing the new token.
    /// </summary>
    [Fact]
    public async Task ToHttpResultReturnsOkWithConcurrencyTokenForSuccessAsync()
    {
        var token = Guid.NewGuid();
        PlacementUpdateResult result = PlacementTestReceipts.Success(new UpdateCampaignPlacementInput(42, Nova.SharedKernel.Enums.PlacementOutcome.NotSelected, null, Guid.NewGuid(), Guid.CreateVersion7()), token);

        var httpContext = await ExecuteAsync(result);

        httpContext.StatusCode.ShouldBe(StatusCodes.Status200OK);
        using var document = JsonDocument.Parse(httpContext.Body);
        document.RootElement.GetProperty("concurrencyToken").GetGuid().ShouldBe(token);
    }

    /// <summary>
    /// Verifies validation errors convert to a validation problem naming the offending fields.
    /// </summary>
    [Fact]
    public async Task ToHttpResultReturnsValidationProblemWithErrorsForValidationFailureAsync()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [nameof(UpdateCampaignPlacementInput.TeamId)] = ["A team is required for an assigned outcome."]
        };
        PlacementUpdateResult result = new Error<IReadOnlyDictionary<string, string[]>>(errors);

        var httpContext = await ExecuteAsync(result);

        httpContext.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        using var document = JsonDocument.Parse(httpContext.Body);
        document.RootElement.GetProperty("errors")
            .GetProperty(nameof(UpdateCampaignPlacementInput.TeamId))
            .GetArrayLength().ShouldBe(1);
    }

    /// <summary>
    /// Verifies unavailable participation converts to a non-disclosing 404 response.
    /// </summary>
    [Fact]
    public async Task ToHttpResultReturnsNotFoundWithoutDisclosureForUnavailableParticipationAsync()
    {
        PlacementUpdateResult result = new NotFound();

        var httpContext = await ExecuteAsync(result);

        httpContext.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        using var document = JsonDocument.Parse(httpContext.Body);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies forbidden placement results convert to a 403 response with the service detail.
    /// </summary>
    [Fact]
    public async Task ToHttpResultReturnsForbiddenWithServiceDetailForNonMemberCallerAsync()
    {
        const string Detail = "You must be an approved club member to update campaign placements.";
        PlacementUpdateResult result = new PlacementForbidden(Detail);

        var httpContext = await ExecuteAsync(result);

        httpContext.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        using var document = JsonDocument.Parse(httpContext.Body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
    }

    /// <summary>
    /// Verifies conflict placement results convert to a 409 response with the service detail.
    /// </summary>
    [Fact]
    public async Task ToHttpResultReturnsConflictWithServiceDetailForConflictAsync()
    {
        const string Detail = "The placement was changed by another user. Reload it and try again.";
        PlacementUpdateResult result = new PlacementConflict(Detail);

        var httpContext = await ExecuteAsync(result);

        httpContext.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        using var document = JsonDocument.Parse(httpContext.Body);
        document.RootElement.GetProperty("detail").GetString().ShouldBe(Detail);
    }

    /// <summary>
    /// Converts a placement update result to HTTP and executes it against an isolated HTTP context.
    /// </summary>
    /// <param name="result">The placement update result to convert and execute.</param>
    /// <returns>The captured response status code and body text.</returns>
    private static async Task<(int StatusCode, string Body)> ExecuteAsync(PlacementUpdateResult result)
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

        await result.ToHttpResult().ExecuteAsync(httpContext);

        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        return (httpContext.Response.StatusCode, body);
    }
}
