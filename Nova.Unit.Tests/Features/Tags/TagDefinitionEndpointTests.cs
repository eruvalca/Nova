using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Tags;
using Nova.Shared.Features.Tags;
using Nova.Shared.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Verifies the tag-definition endpoint route metadata, especially the member/admin authorization split.
/// </summary>
public sealed class TagDefinitionEndpointTests
{
    /// <summary>
    /// Verifies create and choices carry <see cref="Policies.RequireClubMember"/> while the
    /// management list, update, archive, and restore retain <see cref="Policies.RequireClubAdmin"/>,
    /// so the endpoint-level policy split cannot silently regress to bare authentication.
    /// </summary>
    [Fact]
    public async Task TagDefinitionEndpoints_SplitMemberAndAdminAuthorization()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ITagDefinitionService>(_ => Substitute.For<ITagDefinitionService>());
        builder.Services.AddSingleton<ITagDefinitionLifecycleService>(_ => Substitute.For<ITagDefinitionLifecycleService>());
        builder.Services.AddSingleton<ITagDefinitionQueryService>(_ => Substitute.For<ITagDefinitionQueryService>());
        await using var app = builder.Build();

        app.MapTagDefinitionEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        AssertPolicy(endpoints, "CreateTagDefinition", Policies.RequireClubMember);
        AssertPolicy(endpoints, "UpdateTagDefinition", Policies.RequireClubAdmin);
        AssertPolicy(endpoints, "ArchiveTagDefinition", Policies.RequireClubAdmin);
        AssertPolicy(endpoints, "RestoreTagDefinition", Policies.RequireClubAdmin);
        AssertPolicy(endpoints, "GetTagDefinitions", Policies.RequireClubAdmin);
        AssertPolicy(endpoints, "GetTagDefinitionChoices", Policies.RequireClubMember);
    }

    /// <summary>
    /// Asserts the endpoint registered under the given route name carries the expected policy.
    /// </summary>
    private static void AssertPolicy(
        IReadOnlyList<RouteEndpoint> endpoints,
        string endpointName,
        string expectedPolicy)
    {
        var endpoint = endpoints.SingleOrDefault(
            candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);

        endpoint.ShouldNotBeNull($"The endpoint named '{endpointName}' must be registered.");
        endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(metadata => metadata.Policy == expectedPolicy);
    }
}
