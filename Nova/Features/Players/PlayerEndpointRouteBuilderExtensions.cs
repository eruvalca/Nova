using Nova.Features.Shared;
using Nova.Shared.Players;
using Nova.Shared.Security;

namespace Nova.Features.Players;

/// <summary>
/// Maps the minimal API endpoint for player detail and campaign-history queries.
/// </summary>
internal static class PlayerEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps player query endpoints under a shared group prefix.
        /// </summary>
        /// <returns>The route builder for chaining.</returns>
        public IEndpointRouteBuilder MapPlayerEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup(PlayerEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(PlayerEndpoints.GetDetailRelative, GetPlayerDetailHandler)
                .Produces<PlayerDetailDto>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .WithName("GetPlayerDetail");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles player detail and campaign-history read requests.
    /// </summary>
    /// <param name="playerId">The requested player identifier.</param>
    /// <param name="playerDetailService">The service that loads the detail payload.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The HTTP result translated from the service result.</returns>
    private static async Task<IResult> GetPlayerDetailHandler(
        long playerId,
        IPlayerDetailService playerDetailService,
        CancellationToken cancellationToken)
    {
        var result = await playerDetailService.GetPlayerDetailAsync(playerId, cancellationToken);
        return result.ToHttpResult();
    }
}
