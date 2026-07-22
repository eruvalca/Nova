using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nova.Features.Players;
using Nova.Shared.Players;
using Nova.Shared.Security;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Verifies player lifecycle endpoint metadata enforces the intended authorization boundary.
/// </summary>
public sealed class PlayerLifecycleEndpointTests
{
    /// <summary>
    /// Verifies archive and restore endpoints require the club-administrator policy.
    /// </summary>
    [Fact]
    public async Task PlayerLifecycleEndpoints_RequireClubAdminPolicy()
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();

        app.MapPlayerLifecycleEndpoints();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is PlayerEndpoints.ArchiveTemplate or PlayerEndpoints.RestoreTemplate)
            .ToList();

        routeEndpoints.Count.ShouldBe(2);
        foreach (var endpoint in routeEndpoints)
        {
            endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .ShouldContain(metadata => metadata.Policy == Policies.RequireClubAdmin);
        }
    }
}
