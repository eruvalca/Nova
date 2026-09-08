using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Teams;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies team lifecycle endpoint routes and authorization metadata.
/// </summary>
public sealed class TeamLifecycleEndpointTests
{
#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class FakeTeamLifecycleService : ITeamLifecycleService
#pragma warning restore CA1812
    {
        public Task<ServiceResult<Success>> ArchiveAsync(
            long teamId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ServiceResult<Success>>(new Success());

        public Task<ServiceResult<Success>> RestoreAsync(
            long teamId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<ServiceResult<Success>>(new Success());
    }

    [Fact]
    public async Task TeamLifecycleEndpointsRequireClubAdminPolicyAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ITeamLifecycleService, FakeTeamLifecycleService>();
        await using var app = builder.Build();

        app.MapTeamLifecycleEndpoints();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(
                endpoint => endpoint.RoutePattern.RawText is
                    TeamEndpoints.ArchiveTemplate or
                    TeamEndpoints.RestoreTemplate)
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
