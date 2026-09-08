using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Players;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>Verifies player-import endpoint response metadata matches reachable outcomes.</summary>
public sealed class PlayerImportEndpointTests
{
    /// <summary>Verifies both administrator-only routes advertise authentication failures.</summary>
    [Fact]
    public async Task PlayerImportEndpointsAdvertiseUnauthorizedAsync()
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
    public async Task PreviewEndpointAdvertisesUnsupportedMediaTypeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IPlayerImportService, FakePlayerImportService>();
        await using var app = builder.Build();

        app.MapPlayerImportEndpoints();

        var previewEndpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(endpoint.RoutePattern.RawText, PlayerEndpoints.ImportPreview, StringComparison.Ordinal));
        previewEndpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode)
            .ShouldContain(StatusCodes.Status415UnsupportedMediaType);
    }

#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class FakePlayerImportService : IPlayerImportService
#pragma warning restore CA1812
    {
        /// <inheritdoc />
        public Task<ServiceResult<PlayerImportCompletion>> CommitAsync(
            PlayerImportCommitInput input,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("This test double does not support this operation.");

        /// <inheritdoc />
        public Task<ServiceResult<PlayerImportTemplate>> GetTemplateAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException("This test double does not support this operation.");

        /// <inheritdoc />
        public Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
            PlayerImportUploadInput upload,
            CancellationToken cancellationToken = default) => throw new NotSupportedException("This test double does not support this operation.");
    }
}
