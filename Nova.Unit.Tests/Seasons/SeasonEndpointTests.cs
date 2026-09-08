using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Seasons;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies season endpoint response metadata matches reachable outcomes.</summary>
public sealed class SeasonEndpointTests
{
    /// <summary>Verifies list and detail bind their annotated request DTOs at the endpoint boundary.</summary>
    [Fact]
    public void SeasonQueryHandlersBindAnnotatedInputsAsParameters()
    {
        var handlerType = typeof(SeasonEndpointRouteBuilderExtensions);
        var listHandler = handlerType.GetMethod(
            "ListHandlerAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var detailHandler = handlerType.GetMethod(
            "GetHandlerAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        listHandler.ShouldNotBeNull();
        detailHandler.ShouldNotBeNull();
        listHandler.GetParameters()[0]
            .GetCustomAttributes(typeof(AsParametersAttribute), inherit: false)
            .ShouldHaveSingleItem();
        detailHandler.GetParameters()[0]
            .GetCustomAttributes(typeof(AsParametersAttribute), inherit: false)
            .ShouldHaveSingleItem();
    }

    /// <summary>Verifies every authorized season route advertises 401 and advancement omits 404.</summary>
    [Fact]
    public async Task SeasonEndpointsAdvertiseUnauthorizedAndOnlyReachableProblemsAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ISeasonCommandService, FakeSeasonCommandService>();
        builder.Services.AddSingleton<ISeasonQueryService, FakeSeasonQueryService>();
        await using var app = builder.Build();

        app.MapSeasonEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        endpoints.Count.ShouldBe(5);
        endpoints.ShouldAllBe(endpoint => ResponseStatuses(endpoint)
            .Contains(StatusCodes.Status401Unauthorized));

        var startNext = endpoints.Single(endpoint => string.Equals(endpoint.RoutePattern.RawText, SeasonEndpoints.StartNext, StringComparison.Ordinal));
        ResponseStatuses(startNext).ShouldNotContain(StatusCodes.Status404NotFound);
        ResponseStatuses(startNext).ShouldContain(StatusCodes.Status409Conflict);
    }

    private static int[] ResponseStatuses(RouteEndpoint endpoint)
        => endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();

#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class FakeSeasonCommandService : ISeasonCommandService
#pragma warning restore CA1812
    {
        public Task<ServiceResult<SeasonSummary>> CreateAsync(
            CreateSeasonInput input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test double does not support this operation.");

        public Task<ServiceResult<SeasonSummary>> UpdateAsync(
            long seasonId,
            UpdateSeasonInput input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test double does not support this operation.");

        public Task<ServiceResult<StartNextSeasonResult>> StartNextAsync(
            StartNextSeasonInput input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test double does not support this operation.");
    }

#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class FakeSeasonQueryService : ISeasonQueryService
#pragma warning restore CA1812
    {
        public Task<ServiceResult<SeasonPageResult>> ListAsync(
            GetSeasonListInput input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test double does not support this operation.");

        public Task<ServiceResult<SeasonDetailResult>> GetAsync(
            GetSeasonDetailInput input,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test double does not support this operation.");
    }
}
