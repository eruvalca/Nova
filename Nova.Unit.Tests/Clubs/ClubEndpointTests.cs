using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>Verifies Club endpoint response metadata matches reachable authorization outcomes.</summary>
public sealed class ClubEndpointTests
{
    /// <summary>Verifies the current-club identity endpoint advertises anonymous rejection.</summary>
    /// <returns>A task that completes after endpoint metadata is inspected.</returns>
    [Fact]
    public async Task CurrentClubIdentityEndpoint_AdvertisesUnauthorized()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterHandlerServices(builder.Services);
        await using var app = builder.Build();

        app.MapClubEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == ClubEndpoints.GetCurrent);
        var responseStatuses = endpoint.Metadata
            .GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Select(metadata => metadata.StatusCode);

        responseStatuses.ShouldContain(StatusCodes.Status401Unauthorized);
    }

    /// <summary>Registers handler parameter types so endpoint metadata can be materialized without resolving services.</summary>
    /// <param name="services">The service collection used by the metadata-only test host.</param>
    private static void RegisterHandlerServices(IServiceCollection services)
    {
        services.AddSingleton<IClubIdentityQueryService>(_ => throw MetadataOnly());
        services.AddSingleton<IClubService>(_ => throw MetadataOnly());
        services.AddSingleton<IDbContextFactory<NovaAdminDbContext>>(_ => throw MetadataOnly());
        services.AddSingleton<IDbContextFactory<NovaReadDbContext>>(_ => throw MetadataOnly());
        services.AddSingleton<ICurrentUserProvider>(_ => throw MetadataOnly());
        services.AddSingleton<ClubMembershipClaimRefresher>(_ => throw MetadataOnly());
        services.AddSingleton<ClubEndpointLogger>(_ => throw MetadataOnly());
        services.AddKeyedSingleton<BlobContainerClient>("club-crests", (_, _) => throw MetadataOnly());
        services.AddSingleton<UserManager<NovaUserEntity>>(_ => throw MetadataOnly());
        services.AddSingleton<SignInManager<NovaUserEntity>>(_ => throw MetadataOnly());
        services.AddSingleton<IClubCrestService>(_ => throw MetadataOnly());
        services.AddSingleton<IClubJoinRequestService>(_ => throw MetadataOnly());
        services.AddSingleton<IClubMemberService>(_ => throw MetadataOnly());
    }

    /// <summary>Creates the failure used if a metadata-only service is unexpectedly resolved.</summary>
    /// <returns>An exception identifying an invalid metadata-test resolution.</returns>
    private static InvalidOperationException MetadataOnly()
        => new("Endpoint metadata inspection must not resolve handler services.");
}
