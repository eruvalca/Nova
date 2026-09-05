using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Players;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>Verifies player-import endpoint response metadata matches reachable outcomes.</summary>
public sealed class PlayerImportEndpointTests
{
    /// <summary>Verifies both administrator-only routes advertise authentication failures.</summary>
    [Fact]
    public async Task PlayerImportEndpoints_AdvertiseUnauthorized()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPlayerImportService, FakePlayerImportService>();
        await using var app = builder.Build();

        app.MapPlayerImportEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        endpoints.Count.ShouldBe(3);
        endpoints.ShouldAllBe(endpoint => endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .Contains(StatusCodes.Status401Unauthorized));
    }

    /// <summary>Verifies the multipart preview route advertises framework media-type rejection.</summary>
    [Fact]
    public async Task PreviewEndpoint_AdvertisesUnsupportedMediaType()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPlayerImportService, FakePlayerImportService>();
        await using var app = builder.Build();

        app.MapPlayerImportEndpoints();

        var previewEndpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == PlayerEndpoints.ImportPreview);
        previewEndpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ShouldContain(StatusCodes.Status415UnsupportedMediaType);
    }

    private sealed class FakePlayerImportService : IPlayerImportService
    {
        /// <inheritdoc />
        public Task<ServiceResult<PlayerImportCompletion>> CommitAsync(
            PlayerImportCommitInput input,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ServiceResult<PlayerImportTemplate>> GetTemplateAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
            PlayerImportUploadInput upload,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
