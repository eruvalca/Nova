using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Seasons;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies season endpoint response metadata matches reachable outcomes.</summary>
public sealed class SeasonEndpointTests
{
    /// <summary>Verifies list and detail bind their annotated request DTOs at the endpoint boundary.</summary>
    [Fact]
    public void SeasonQueryHandlers_BindAnnotatedInputs_AsParameters()
    {
        var handlerType = typeof(SeasonEndpointRouteBuilderExtensions);
        var listHandler = handlerType.GetMethod(
            "ListHandler",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var detailHandler = handlerType.GetMethod(
            "GetHandler",
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
    public async Task SeasonEndpoints_AdvertiseUnauthorized_AndOnlyReachableProblems()
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

        var startNext = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText == $"{SeasonEndpoints.GroupPrefix}/{SeasonEndpoints.StartNextRelative}");
        ResponseStatuses(startNext).ShouldNotContain(StatusCodes.Status404NotFound);
        ResponseStatuses(startNext).ShouldContain(StatusCodes.Status409Conflict);
    }

    private static IReadOnlyCollection<int> ResponseStatuses(RouteEndpoint endpoint)
        => endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ToArray();

    private sealed class FakeSeasonCommandService : ISeasonCommandService
    {
        public Task<ServiceResult<SeasonSummary>> CreateAsync(
            CreateSeasonInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ServiceResult<SeasonSummary>> UpdateAsync(
            long seasonId,
            UpdateSeasonInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ServiceResult<StartNextSeasonResult>> StartNextAsync(
            StartNextSeasonInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeSeasonQueryService : ISeasonQueryService
    {
        public Task<ServiceResult<SeasonPageResult>> ListAsync(
            GetSeasonListInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ServiceResult<SeasonDetailResult>> GetAsync(
            GetSeasonDetailInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
