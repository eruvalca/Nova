using Nova.Features.Shared;
using Nova.Shared.Features.Tags;
using Nova.Shared.Security;

namespace Nova.Features.Tags;

/// <summary>
/// Maps the minimal API endpoints for tag-definition management, lifecycle, and querying.
/// </summary>
internal static class TagDefinitionEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps tag-definition endpoints under the tags group, with club-administrator
        /// authorization for management/lifecycle and evaluator authorization for active choices.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapTagDefinitionEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var managementGroup = endpoints
                .MapGroup(TagEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            managementGroup.MapPost(TagEndpoints.CreateRelative, CreateTagDefinitionHandler)
                .Produces<TagDefinitionDto>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreateTagDefinition");

            managementGroup.MapPut(TagEndpoints.UpdateRelative, UpdateTagDefinitionHandler)
                .Produces<TagDefinitionDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdateTagDefinition");

            managementGroup.MapPost(TagEndpoints.ArchiveRelative, ArchiveTagDefinitionHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("ArchiveTagDefinition");

            managementGroup.MapPost(TagEndpoints.RestoreRelative, RestoreTagDefinitionHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("RestoreTagDefinition");

            managementGroup.MapGet(TagEndpoints.GetListRelative, GetTagDefinitionsHandler)
                .Produces<TagDefinitionListResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName("GetTagDefinitions");

            endpoints.MapGroup(TagEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireEvaluator)
                .MapGet(TagEndpoints.GetChoicesRelative, GetTagDefinitionChoicesHandler)
                .Produces<IReadOnlyList<TagDefinitionDto>>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName("GetTagDefinitionChoices");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles POST /api/tags.
    /// </summary>
    /// <param name="input">The requested tag-definition profile.</param>
    /// <param name="tagDefinitionService">The tag-definition management service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The created tag definition or a ProblemDetails response.</returns>
    private static async Task<IResult> CreateTagDefinitionHandler(
        CreateTagDefinitionInput input,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionService.CreateAsync(input, cancellationToken);
        return result.ToHttpResult(dto => TypedResults.Created((string?)null, dto));
    }

    /// <summary>
    /// Handles PUT /api/tags/{tagId}.
    /// </summary>
    /// <param name="tagId">The route tag-definition identifier.</param>
    /// <param name="input">The requested tag-definition profile.</param>
    /// <param name="tagDefinitionService">The tag-definition management service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The updated tag definition or a ProblemDetails response.</returns>
    private static async Task<IResult> UpdateTagDefinitionHandler(
        long tagId,
        UpdateTagDefinitionInput input,
        ITagDefinitionService tagDefinitionService,
        CancellationToken cancellationToken)
    {
        if (tagId != input.TagId)
        {
            return Nova.Shared.Results.ServiceProblem.BadRequest(
                    "The tag identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await tagDefinitionService.UpdateAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles POST /api/tags/{tagId}/archive.
    /// </summary>
    /// <param name="tagId">The tag-definition identifier.</param>
    /// <param name="tagDefinitionLifecycleService">The tag-definition lifecycle service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> ArchiveTagDefinitionHandler(
        long tagId,
        ITagDefinitionLifecycleService tagDefinitionLifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionLifecycleService.ArchiveAsync(tagId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles POST /api/tags/{tagId}/restore.
    /// </summary>
    /// <param name="tagId">The tag-definition identifier.</param>
    /// <param name="tagDefinitionLifecycleService">The tag-definition lifecycle service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> RestoreTagDefinitionHandler(
        long tagId,
        ITagDefinitionLifecycleService tagDefinitionLifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionLifecycleService.RestoreAsync(tagId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles GET /api/tags (management list).
    /// </summary>
    /// <param name="input">The bound management filters.</param>
    /// <param name="tagDefinitionQueryService">The tag-definition query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The matching tag definitions or a ProblemDetails response.</returns>
    private static async Task<IResult> GetTagDefinitionsHandler(
        [AsParameters] GetTagDefinitionsInput input,
        ITagDefinitionQueryService tagDefinitionQueryService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionQueryService.GetManagementListAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles GET /api/tags/choices.
    /// </summary>
    /// <param name="tagDefinitionQueryService">The tag-definition query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The active tag-definition choices or a ProblemDetails response.</returns>
    private static async Task<IResult> GetTagDefinitionChoicesHandler(
        ITagDefinitionQueryService tagDefinitionQueryService,
        CancellationToken cancellationToken)
    {
        var result = await tagDefinitionQueryService.GetChoicesAsync(cancellationToken);
        return result.ToHttpResult();
    }
}
