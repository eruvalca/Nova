using Nova.Features.Shared;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Tags;

/// <summary>
/// Maps the tag-definition minimal API endpoints.
/// </summary>
internal static class TagDefinitionEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapTagDefinitionEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var adminGroup = endpoints
                .MapGroup(TagDefinitionEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            adminGroup.MapPost(TagDefinitionEndpoints.CreateRelative, CreateTagDefinitionHandler)
                .Produces<TagDefinitionMutationSuccess>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreateTagDefinition");

            adminGroup.MapPut(TagDefinitionEndpoints.UpdateRelative, UpdateTagDefinitionHandler)
                .Produces<TagDefinitionMutationSuccess>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdateTagDefinition");

            adminGroup.MapPost(TagDefinitionEndpoints.ArchiveRelative, ArchiveTagDefinitionHandler)
                .Produces<TagDefinitionMutationSuccess>()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .WithName("ArchiveTagDefinition");

            adminGroup.MapPost(TagDefinitionEndpoints.RestoreRelative, RestoreTagDefinitionHandler)
                .Produces<TagDefinitionMutationSuccess>()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .WithName("RestoreTagDefinition");

            var readGroup = endpoints
                .MapGroup(TagDefinitionEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            readGroup.MapGet(TagDefinitionEndpoints.ListActiveRelative, GetActiveTagDefinitionsHandler)
                .Produces<IReadOnlyList<TagDefinitionSummary>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .WithName("GetActiveTagDefinitions");

            adminGroup.MapGet(TagDefinitionEndpoints.ListArchivedRelative, GetArchivedTagDefinitionsHandler)
                .Produces<IReadOnlyList<TagDefinitionSummary>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .WithName("GetArchivedTagDefinitions");

            return endpoints;
        }
    }

    private static async Task<IResult> CreateTagDefinitionHandler(
        CreateTagDefinitionInput input,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionService.CreateAsync(input, cancellationToken);
        return result.ToHttpResult(definition => TypedResults.Created((string?)null, definition));
    }

    private static async Task<IResult> UpdateTagDefinitionHandler(
        long tagDefinitionId,
        UpdateTagDefinitionInput input,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        if (tagDefinitionId != input.TagDefinitionId)
        {
            return ServiceProblem.BadRequest("The tag-definition identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await tagDefinitionService.UpdateAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ArchiveTagDefinitionHandler(
        long tagDefinitionId,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionService.ArchiveAsync(tagDefinitionId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RestoreTagDefinitionHandler(
        long tagDefinitionId,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionService.RestoreAsync(tagDefinitionId, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetActiveTagDefinitionsHandler(
        [AsParameters] GetTagDefinitionsInput input,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionService.GetActiveAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetArchivedTagDefinitionsHandler(
        [AsParameters] GetTagDefinitionsInput input,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionService.GetArchivedAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
