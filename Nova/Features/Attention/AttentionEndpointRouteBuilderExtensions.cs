using Nova.Features.Shared;
using Nova.Shared.Features.Attention;
using Nova.Shared.Security;

namespace Nova.Features.Attention;

/// <summary>
/// Maps the club attention projection query endpoint.
/// </summary>
internal static class AttentionEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the administrator-only club attention projection endpoint.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapAttentionEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGroup(AttentionEndpoints.GroupPrefix)
                .MapGet(AttentionEndpoints.GetClubAttentionRelative, GetClubAttentionHandler)
                .Produces<ClubAttentionResult>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName(AttentionEndpoints.GetClubAttentionRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Handles GET /api/attention.
    /// </summary>
    /// <param name="clubAttentionQueryService">The club attention query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The attention projection or a ProblemDetails response.</returns>
    private static async Task<IResult> GetClubAttentionHandler(
        IClubAttentionQueryService clubAttentionQueryService,
        CancellationToken cancellationToken)
    {
        var result = await clubAttentionQueryService.GetClubAttentionAsync(cancellationToken);
        return result.ToHttpResult();
    }
}
