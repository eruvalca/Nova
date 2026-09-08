using Nova.Features.Common;
using Nova.SharedKernel.Features.Activity;
using Nova.SharedKernel.Security;

namespace Nova.Features.Activity;

/// <summary>
/// Maps the club activity feed query endpoint.
/// </summary>
internal static class ActivityEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the tenant-scoped club activity feed endpoint.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapActivityEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGroup(ActivityEndpoints.GroupPrefix)
                .MapGet(ActivityEndpoints.GetClubActivityRelative, GetClubActivityHandlerAsync)
                .Produces<ClubActivityResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName(ActivityEndpoints.GetClubActivityRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Handles GET /api/activity.
    /// </summary>
    /// <param name="input">The optional continuation cursor.</param>
    /// <param name="clubActivityQueryService">The club activity query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The activity page or a ProblemDetails response.</returns>
    private static async Task<IResult> GetClubActivityHandlerAsync(
        [AsParameters] GetClubActivityInput input,
        IClubActivityQueryService clubActivityQueryService,
        CancellationToken cancellationToken)
    {
        var result = await clubActivityQueryService.GetClubActivityAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
