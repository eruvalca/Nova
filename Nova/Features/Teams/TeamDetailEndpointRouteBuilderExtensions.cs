using Nova.Features.Shared;
using Nova.Shared.Security;
using Nova.Shared.Teams;

namespace Nova.Features.Teams;

/// <summary>
/// Maps the team detail query endpoint.
/// </summary>
internal static class TeamDetailEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the tenant-scoped team detail endpoint.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapTeamDetailEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGroup(TeamEndpoints.GroupPrefix)
                .MapGet(TeamEndpoints.GetDetailRelative, GetTeamDetailHandler)
                .Produces<TeamDetailDto>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName("GetTeamDetail");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles GET /api/teams/{teamId}.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="teamDetailService">The team detail service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The team detail result or a ProblemDetails response.</returns>
    private static async Task<IResult> GetTeamDetailHandler(
        long teamId,
        ITeamDetailService teamDetailService,
        CancellationToken cancellationToken)
    {
        var result = await teamDetailService.GetTeamDetailAsync(teamId, cancellationToken);
        return result.ToHttpResult();
    }
}
