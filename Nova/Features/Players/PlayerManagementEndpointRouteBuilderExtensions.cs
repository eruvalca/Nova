using Nova.Features.Common;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;

namespace Nova.Features.Players;

/// <summary>
/// Maps the minimal API endpoints for player creation and permanent-profile editing.
/// </summary>
internal static class PlayerManagementEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the player management endpoints under the players group with club-member authorization.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapPlayerManagementEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(PlayerEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            // Create or recover a player and their original optional Active-campaign enrollment.
            group.MapPost(PlayerEndpoints.CreateRelative, CreatePlayerHandlerAsync)
                .Produces<PlayerCreationCompletion>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreatePlayer");

            // Update a player's permanent profile fields.
            group.MapPut(PlayerEndpoints.UpdateRelative, UpdatePlayerHandlerAsync)
                .Produces<PlayerDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdatePlayer");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles POST /api/players — creates or recovers a player and their original optional Active-campaign enrollment.
    /// </summary>
    private static async Task<IResult> CreatePlayerHandlerAsync(
        CreatePlayerInput input,
        IPlayerManagementService playerManagementService,
        CancellationToken cancellationToken)
    {
        var result = await playerManagementService.CreateAsync(input, cancellationToken);
        return result.ToHttpResult(player => TypedResults.Created((string?)null, player));
    }

    /// <summary>
    /// Handles PUT /api/players/{playerId} — updates a player's permanent profile fields.
    /// </summary>
    private static async Task<IResult> UpdatePlayerHandlerAsync(
        long playerId,
        UpdatePlayerInput input,
        IPlayerManagementService playerManagementService,
        CancellationToken cancellationToken)
    {
        // Ensure the route parameter and body agree on the target player.
        if (playerId != input.PlayerId)
        {
            return ServiceProblem.BadRequest("The player identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await playerManagementService.UpdateAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
