using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies team-management endpoint route metadata, including the
/// <c>CreatedAtRoute</c> target that backs the <c>Location</c> header on team creation.
/// </summary>
public sealed class TeamManagementEndpointTests
{
    private sealed class FakeTeamManagementService : ITeamManagementService
    {
        /// <inheritdoc />
        public Task<ServiceResult<TeamDto>> CreateAsync(
            CreateTeamInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ServiceResult<TeamDto>> UpdateAsync(
            UpdateTeamInput input,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeTeamDetailService : ITeamDetailService
    {
        /// <inheritdoc />
        public Task<ServiceResult<TeamDetailDto>> GetTeamDetailAsync(
            long teamId,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Verifies that the team-detail GET endpoint is registered with the route name
    /// <c>GetTeamDetail</c> (stored in <see cref="TeamEndpoints.GetDetailRouteName"/>),
    /// which is the name referenced by <c>TypedResults.CreatedAtRoute</c> in the create
    /// handler. Without this wiring the <c>Location</c> header would throw at runtime.
    /// </summary>
    [Fact]
    public async Task TeamDetailEndpoint_IsRegistered_WithGetTeamDetailRouteName()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ITeamManagementService, FakeTeamManagementService>();
        builder.Services.AddSingleton<ITeamDetailService, FakeTeamDetailService>();
        await using var app = builder.Build();

        app.MapTeamManagementEndpoints();
        app.MapTeamDetailEndpoints();

        // The GET endpoint named "GetTeamDetail" must be discoverable so that the
        // LinkGenerator used by CreatedAtRoute can resolve /api/teams/{teamId} at runtime.
        var detailEndpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(ep => ep.RoutePattern.RawText == TeamEndpoints.GetDetailTemplate)
            .SingleOrDefault(ep =>
                ep.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == TeamEndpoints.GetDetailRouteName);

        detailEndpoint.ShouldNotBeNull(
            $"The GET team-detail endpoint must carry the route name '{TeamEndpoints.GetDetailRouteName}' " +
            "so that TypedResults.CreatedAtRoute can resolve the Location header after team creation.");
    }
}
