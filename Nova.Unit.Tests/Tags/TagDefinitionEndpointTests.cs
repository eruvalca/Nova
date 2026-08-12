using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Tags;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Security;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Tags;

/// <summary>
/// Verifies tag-definition endpoint registration and authorization metadata.
/// </summary>
public sealed class TagDefinitionEndpointTests
{
    private sealed class FakeTagDefinitionService : ITagDefinitionService
    {
        public Task<ServiceResult<TagDefinitionMutationSuccess>> CreateAsync(
            CreateTagDefinitionInput input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceResult<TagDefinitionMutationSuccess>(new TagDefinitionMutationSuccess { TagDefinitionId = 7 }));

        public Task<ServiceResult<TagDefinitionMutationSuccess>> UpdateAsync(
            UpdateTagDefinitionInput input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceResult<TagDefinitionMutationSuccess>(new TagDefinitionMutationSuccess { TagDefinitionId = 7 }));

        public Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetActiveAsync(
            GetTagDefinitionsInput? input = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(Array.Empty<TagDefinitionSummary>()));

        public Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetArchivedAsync(
            GetTagDefinitionsInput? input = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionSummary>>(Array.Empty<TagDefinitionSummary>()));

        public Task<ServiceResult<TagDefinitionMutationSuccess>> ArchiveAsync(
            long tagDefinitionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceResult<TagDefinitionMutationSuccess>(new TagDefinitionMutationSuccess { TagDefinitionId = tagDefinitionId }));

        public Task<ServiceResult<TagDefinitionMutationSuccess>> RestoreAsync(
            long tagDefinitionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ServiceResult<TagDefinitionMutationSuccess>(new TagDefinitionMutationSuccess { TagDefinitionId = tagDefinitionId }));
    }

    [Fact]
    public async Task TagDefinitionEndpoints_RequireExpectedAuthorizationPolicies()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ITagDefinitionService, FakeTagDefinitionService>();
        await using var app = builder.Build();

        app.MapTagDefinitionEndpoints();

        var expectedRoutes = new[]
        {
            TagDefinitionEndpoints.Create.TrimEnd('/'),
            TagDefinitionEndpoints.UpdateTemplate.TrimEnd('/'),
            TagDefinitionEndpoints.ArchiveTemplate.TrimEnd('/'),
            TagDefinitionEndpoints.RestoreTemplate.TrimEnd('/'),
            TagDefinitionEndpoints.ListActive.TrimEnd('/'),
            TagDefinitionEndpoints.ListArchived.TrimEnd('/')
        };

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is not null &&
                expectedRoutes.Contains(endpoint.RoutePattern.RawText.TrimEnd('/')))
            .ToList();

        routeEndpoints.Count.ShouldBe(6);

        foreach (var endpoint in routeEndpoints
                     .Where(ep => ep.RoutePattern.RawText?.TrimEnd('/') != TagDefinitionEndpoints.ListActive.TrimEnd('/')))
        {
            endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .ShouldContain(metadata => metadata.Policy == Policies.RequireClubAdmin);
        }

        var activeListEndpoint = routeEndpoints.Single(ep => ep.RoutePattern.RawText?.TrimEnd('/') == TagDefinitionEndpoints.ListActive.TrimEnd('/'));
        activeListEndpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .ShouldContain(metadata => metadata.Policy == Policies.RequireClubMember);
    }
}
